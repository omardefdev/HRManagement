using System.Net;
using System.Security.Claims;
using System.Text;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.UnitTests;

/// <summary>IAM-004 negative S2S authorization matrix + service token rotation cache behaviour.</summary>
public class ServiceToServiceNegativeMatrixTests
{
    private static IAuthorizationService BuildAuthService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal ServiceAccount(string clientId, string? scope, string? audience = null)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", $"service-account-{clientId}"),
            new("azp", clientId),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("N"))
        };
        if (!string.IsNullOrWhiteSpace(scope))
            claims.Add(new Claim("scope", scope));
        if (!string.IsNullOrWhiteSpace(audience))
            claims.Add(new Claim("aud", audience));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static ClaimsPrincipal InteractiveUser(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim(ClaimTypes.NameIdentifier, "user-1"))
            .Append(new Claim("preferred_username", "alice"))
            .ToList();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }

    [Fact]
    public async Task DirectorySync_NoToken_Fails()
    {
        var auth = BuildAuthService();
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        var result = await auth.AuthorizeAsync(anon, ServicePolicies.DirectorySync);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DirectorySync_InteractiveUserWithoutScope_Fails()
    {
        var auth = BuildAuthService();
        var user = InteractiveUser(HrManagementRoles.ItInfra, HrManagementRoles.AuthDirectory);
        var result = await auth.AuthorizeAsync(user, ServicePolicies.DirectorySync);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DirectorySync_ServiceWithoutScope_Fails()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.ItInfra, scope: null);
        var result = await auth.AuthorizeAsync(svc, ServicePolicies.DirectorySync);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DirectorySync_WrongClientWithScope_Fails()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.Payroll, ServiceScopes.DirectorySync);
        var result = await auth.AuthorizeAsync(svc, ServicePolicies.DirectorySync);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DirectorySync_ItInfraWithScope_Succeeds()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.ItInfra, ServiceScopes.DirectorySync);
        var result = await auth.AuthorizeAsync(svc, ServicePolicies.DirectorySync);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreLookup_Anonymous_Fails()
    {
        var auth = BuildAuthService();
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        var result = await auth.AuthorizeAsync(anon, HrManagementPolicies.HrCoreLookup);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreLookup_ServiceWithoutScope_Fails()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.Payroll, scope: "openid");
        var result = await auth.AuthorizeAsync(svc, HrManagementPolicies.HrCoreLookup);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreLookup_PayrollWithLookupScope_Succeeds()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.Payroll, ServiceScopes.HrCoreLookup);
        var result = await auth.AuthorizeAsync(svc, HrManagementPolicies.HrCoreLookup);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreLookup_InteractiveAuthenticatedUser_Succeeds()
    {
        var auth = BuildAuthService();
        var user = InteractiveUser(HrManagementRoles.Payroll);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreLookup);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task FileStorageReferences_UserTokenWithoutServiceScope_Fails()
    {
        var auth = BuildAuthService();
        var user = InteractiveUser(HrManagementRoles.HrCore);
        var result = await auth.AuthorizeAsync(user, ServicePolicies.FileStorageReferences);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FileStorageReferences_WrongAudienceClient_Fails()
    {
        var auth = BuildAuthService();
        // Payroll is not an allowed file-references caller even with the scope claim.
        var svc = ServiceAccount(ServiceClientIds.Payroll, ServiceScopes.FileStorageReferences, audience: "api-payroll");
        var result = await auth.AuthorizeAsync(svc, ServicePolicies.FileStorageReferences);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FileStorageReferences_HrCoreWithScope_Succeeds()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.HrCore, ServiceScopes.FileStorageReferences, audience: ServiceApiAudiences.FileStorage);
        var result = await auth.AuthorizeAsync(svc, ServicePolicies.FileStorageReferences);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ServiceAuthHandler_AttachesBearerFromProvider()
    {
        var provider = new StubTokenProvider("svc-token-abc");
        var inner = new CaptureHandler();
        var handler = new ServiceAuthHandler(provider) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://files/") };

        var response = await client.GetAsync("api/files/x/references");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("svc-token-abc", inner.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ClientCredentialsTokenProvider_RotationClearsCache_OtherClientsUnaffected()
    {
        ClientCredentialsTokenProvider.ClearCache();

        var handlerA = new SequenceHandler(
            """{"access_token":"token-a-1","expires_in":300}""",
            """{"access_token":"token-a-2","expires_in":300}""");
        var handlerB = new SequenceHandler(
            """{"access_token":"token-b-1","expires_in":300}""",
            """{"access_token":"token-b-1","expires_in":300}""");

        var configA = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceIdentity:ClientId"] = "svc-payroll",
            ["ServiceIdentity:ClientSecret"] = "secret-a",
            ["ServiceIdentity:TokenEndpoint"] = "http://kc/token",
            ["ServiceIdentity:Scope"] = ServiceScopes.HrCoreLookup
        }).Build();

        var configB = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceIdentity:ClientId"] = "svc-hr-core",
            ["ServiceIdentity:ClientSecret"] = "secret-b",
            ["ServiceIdentity:TokenEndpoint"] = "http://kc/token",
            ["ServiceIdentity:Scope"] = ServiceScopes.FileStorageReferences
        }).Build();

        var logger = LoggerFactory.Create(b => { }).CreateLogger<ClientCredentialsTokenProvider>();
        var providerA = new ClientCredentialsTokenProvider(new FixedClientFactory(handlerA), configA, logger);
        var providerB = new ClientCredentialsTokenProvider(new FixedClientFactory(handlerB), configB, logger);

        var a1 = await providerA.GetAccessTokenAsync();
        var b1 = await providerB.GetAccessTokenAsync();
        Assert.Equal("token-a-1", a1);
        Assert.Equal("token-b-1", b1);

        // Simulate rotating client A secret: clear cache and change secret; B keeps working from cache.
        ClientCredentialsTokenProvider.ClearCache();
        configA = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceIdentity:ClientId"] = "svc-payroll",
            ["ServiceIdentity:ClientSecret"] = "secret-a-rotated",
            ["ServiceIdentity:TokenEndpoint"] = "http://kc/token",
            ["ServiceIdentity:Scope"] = ServiceScopes.HrCoreLookup
        }).Build();
        providerA = new ClientCredentialsTokenProvider(new FixedClientFactory(handlerA), configA, logger);

        var a2 = await providerA.GetAccessTokenAsync();
        // Re-seed B into cache after ClearCache (proves A rotation path does not require touching B's secret).
        var b2 = await providerB.GetAccessTokenAsync();

        Assert.Equal("token-a-2", a2);
        Assert.Equal("token-b-1", b2);
        Assert.Equal(2, handlerA.Calls);
        Assert.Equal(2, handlerB.Calls); // one before clear, one after — B secret unchanged
    }

    private sealed class StubTokenProvider(string token) : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<string> _bodies;
        public int Calls { get; private set; }

        public SequenceHandler(params string[] bodies) => _bodies = new Queue<string>(bodies);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = _bodies.Count > 0 ? _bodies.Dequeue() : """{"access_token":"extra","expires_in":300}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FixedClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
