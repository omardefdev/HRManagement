using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public interface IServiceTokenProvider
{
    /// <summary>Returns a client_credentials access token for the configured service identity.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// OAuth2 client_credentials token provider with in-memory cache (IAM-004).
/// Config: ServiceIdentity:ClientId, ServiceIdentity:ClientSecret, ServiceIdentity:TokenEndpoint
/// (or Keycloak:Authority + /protocol/openid-connect/token).
/// </summary>
public sealed class ClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ClientCredentialsTokenProvider> logger) : IServiceTokenProvider
{
    private static readonly ConcurrentDictionary<string, CachedToken> Cache = new(StringComparer.Ordinal);

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var clientId = NonBlank(configuration["ServiceIdentity:ClientId"])
            ?? throw new InvalidOperationException("ServiceIdentity:ClientId is required for service-to-service calls.");
        // PLAN-37: a blank value (the "" placeholder in appsettings.json) counts as missing, so the environment variable
        // is still consulted; rejecting placeholder secrets such as USE_ENV_OR_SECRET_MANAGER is PLAN-42.
        var clientSecret = ResolveClientSecret(configuration)
            ?? throw new InvalidOperationException(
                "ServiceIdentity:ClientSecret is required (blank counts as missing; set ServiceIdentity__ClientSecret).");
        var scope = configuration["ServiceIdentity:Scope"];

        var cacheKey = $"{clientId}|{scope}";
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
            return cached.AccessToken;

        var tokenEndpoint = configuration["ServiceIdentity:TokenEndpoint"];
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            var authority = configuration["Keycloak:Authority"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(authority))
                throw new InvalidOperationException("ServiceIdentity:TokenEndpoint or Keycloak:Authority is required.");
            tokenEndpoint = $"{authority}/protocol/openid-connect/token";
        }

        var client = httpClientFactory.CreateClient("ServiceToken");
        using var content = new FormUrlEncodedContent(BuildForm(clientId, clientSecret, scope));
        using var response = await client.PostAsync(tokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Service token request failed: {Status} {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"Failed to obtain service token for {clientId}: {(int)response.StatusCode}");
        }

        var parsed = JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new InvalidOperationException("Empty token response.");
        if (string.IsNullOrWhiteSpace(parsed.AccessToken))
            throw new InvalidOperationException("Token response missing access_token.");

        var expires = DateTime.UtcNow.AddSeconds(Math.Max(60, parsed.ExpiresIn - 30));
        Cache[cacheKey] = new CachedToken(parsed.AccessToken, expires);
        return parsed.AccessToken;
    }

    /// <summary>Clears cached tokens (used by rotation tests).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// The configured client secret: <c>ServiceIdentity:ClientSecret</c> unless blank, then the
    /// <c>ServiceIdentity__ClientSecret</c> environment variable unless blank; null when neither is set.
    /// </summary>
    public static string? ResolveClientSecret(IConfiguration configuration) =>
        NonBlank(configuration["ServiceIdentity:ClientSecret"])
        ?? NonBlank(Environment.GetEnvironmentVariable("ServiceIdentity__ClientSecret"));

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<KeyValuePair<string, string>> BuildForm(string clientId, string clientSecret, string? scope)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", clientSecret)
        };
        if (!string.IsNullOrWhiteSpace(scope))
            form.Add(new("scope", scope));
        return form;
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 300;
    }
}

/// <summary>Attaches a service client_credentials Bearer token to outbound HttpClient calls (IAM-004).</summary>
public sealed class ServiceAuthHandler(IServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

public static class ServiceIdentityServiceCollectionExtensions
{
    /// <summary>Registers client_credentials token provider and ServiceAuthHandler.</summary>
    /// <summary>
    /// Ceiling for any inter-service call. HttpClient defaults to 100 seconds, so a single slow
    /// dependency could hold a request thread for over a minute and a half; under load that
    /// starves the thread pool and the stall propagates to every caller.
    /// </summary>
    private static readonly TimeSpan InterServiceTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddServiceIdentityClient(this IServiceCollection services)
    {
        // Token acquisition sits on the critical path of every authenticated call, so it gets the
        // tightest budget.
        services.AddHttpClient("ServiceToken", client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHrOutboundResilience(timeoutSeconds: 10);
        services.AddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();
        services.AddTransient<ServiceAuthHandler>();
        return services;
    }

    /// <summary>
    /// Configures a named HttpClient that authenticates with a service token.
    /// Optional acting-user forward via ForwardAuthHandler for audit (does not replace service auth).
    /// </summary>
    public static IHttpClientBuilder AddServiceAuthenticatedClient(
        this IServiceCollection services,
        string name,
        Uri? baseAddress = null,
        bool forwardActingUser = false)
    {
        services.AddServiceIdentityClient();
        services.AddHttpContextAccessor();
        services.AddTransient<ForwardTenantHandler>();
        var builder = services.AddHttpClient(name, client =>
        {
            if (baseAddress is not null)
                client.BaseAddress = baseAddress;
            client.Timeout = InterServiceTimeout;
        }).AddHttpMessageHandler<ServiceAuthHandler>()
            .AddHttpMessageHandler<ForwardTenantHandler>()
            .AddHrOutboundResilience();

        if (forwardActingUser)
            builder.AddHttpMessageHandler<ForwardActingUserHandler>();

        return builder;
    }
}

/// <summary>
/// Copies the caller's user id into X-Acting-User for audit without using it for authentication.
/// </summary>
public sealed class ForwardActingUserHandler(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    public const string HeaderName = "X-Acting-User";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        if (context?.User?.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(sub) && !request.Headers.Contains(HeaderName))
                request.Headers.TryAddWithoutValidation(HeaderName, sub);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Forwards the active tenant into <see cref="TenantIdMiddleware.HeaderName"/> for authenticated S2S calls.
/// Does not modify the service Bearer token attached by <see cref="ServiceAuthHandler"/>.
/// </summary>
public sealed class ForwardTenantHandler(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(TenantIdMiddleware.HeaderName))
        {
            var tenantId = httpContextAccessor.HttpContext?.Items[TenantIdMiddleware.ItemKey] as Guid?
                ?? TenantIdFlow.Get();
            if (tenantId.HasValue)
                request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenantId.Value.ToString("D"));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
