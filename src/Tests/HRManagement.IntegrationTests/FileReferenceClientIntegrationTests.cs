using System.Net;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

public class FileReferenceClientIntegrationTests
{
    [Fact]
    public async Task RegisterReferenceAsync_posts_with_service_bearer_token()
    {
        var recordingHandler = new RecordingHttpMessageHandler();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [HrGatewayConfigurationExtensions.InternalBaseUrlKey] = "http://hr-gateway:8080",
                ["ServiceIdentity:ClientId"] = "svc-hr-core",
                ["ServiceIdentity:ClientSecret"] = "test-secret",
                ["ServiceIdentity:TokenEndpoint"] = "http://keycloak/token",
                ["ServiceIdentity:Scope"] = ServiceScopes.FileStorageReferences
            })
            .Build();

        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("sub", "acting-user-1")], "Cookies"));
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        services.AddLogging();
        services.AddSingleton<IServiceTokenProvider>(new FixedServiceTokenProvider("service-jwt"));
        services.AddTransient<ServiceAuthHandler>();
        services.AddTransient<ForwardActingUserHandler>();

        var baseUrl = HrGatewayConfigurationExtensions.RequireInternalBaseUrl(configuration);
        services.AddHttpClient("FileStorage", client =>
            {
                client.BaseAddress = new Uri(baseUrl + "/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => recordingHandler)
            .AddHttpMessageHandler<ServiceAuthHandler>()
            .AddHttpMessageHandler<ForwardActingUserHandler>();
        services.AddScoped<IFileReferenceClient, HttpFileReferenceClient>();

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IFileReferenceClient>();
        var fileId = Guid.Parse("6c6d8e2f-3c21-45d6-90ee-756bf8b1a732");
        const string holderKey = "hr-core:Qualification:15745c08-f4df-4e49-b83e-81b1c1c4c9d8:FileId";

        await client.RegisterReferenceAsync(fileId, holderKey);

        var request = Assert.Single(recordingHandler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"http://hr-gateway:8080/api/files/{fileId:D}/references",
            request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("service-jwt", request.Headers.Authorization?.Parameter);
        Assert.True(request.Headers.TryGetValues(ForwardActingUserHandler.HeaderName, out var acting));
        Assert.Equal("acting-user-1", Assert.Single(acting));
    }

    private sealed class FixedServiceTokenProvider(string token) : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"referenceCount\":1}")
            });
        }
    }
}
