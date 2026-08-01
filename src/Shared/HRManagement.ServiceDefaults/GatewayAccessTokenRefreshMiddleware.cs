using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Refreshes the OIDC access token before proxying to upstream modules so JwtBearer validation does not fail mid-session.
/// </summary>
public sealed class GatewayAccessTokenRefreshMiddleware(
    RequestDelegate next,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GatewayAccessTokenRefreshMiddleware> logger)
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsModulePath(context.Request.Path) && context.User.Identity?.IsAuthenticated == true)
        {
            if (await TryRefreshAccessTokenAsync(context))
                return;
        }

        await next(context);
    }

    private async Task<bool> TryRefreshAccessTokenAsync(HttpContext context)
    {
        var expiresAtRaw = await context.GetTokenAsync("expires_at");
        if (TryParseExpiresAt(expiresAtRaw, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow.Add(RefreshSkew))
            return false;

        var refreshToken = await context.GetTokenAsync("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            // #region agent log
            logger.LogWarning(
                "[DEBUG-8b0e6a] H4 missing-refresh-token path={Path} expiresAt={ExpiresAt} hypothesisId=H4",
                context.Request.Path.Value,
                expiresAtRaw);
            // #endregion
            return false;
        }

        var authority = configuration["Keycloak:Authority"];
        var publicAuthority = configuration["Keycloak:PublicAuthority"];
        var clientId = configuration["Keycloak:ClientId"];
        var clientSecret = configuration["Keycloak:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("Keycloak__ClientSecret");

        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return false;

        // Prefer internal authority for container-to-container calls; PublicAuthority is browser-only.
        var tokenAuthority = authority;
        var tokenEndpoint = $"{tokenAuthority.TrimEnd('/')}/protocol/openid-connect/token";
        var client = httpClientFactory.CreateClient(nameof(GatewayAccessTokenRefreshMiddleware));

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            })
        };

        using var response = await client.SendAsync(request, context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OIDC refresh failed status={Status} path={Path} authority={Authority} publicAuthority={PublicAuthority}",
                (int)response.StatusCode,
                context.Request.Path.Value,
                authority,
                publicAuthority);

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect(BuildLoginRedirect(context));
            return true;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: context.RequestAborted);
        var root = document.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenElement))
            return false;

        var newAccessToken = accessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(newAccessToken))
            return false;

        var authenticateResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null || authenticateResult.Properties is null)
            return false;

        var props = authenticateResult.Properties;
        SetToken(props, "access_token", newAccessToken);

        if (root.TryGetProperty("refresh_token", out var refreshTokenElement)
            && !string.IsNullOrWhiteSpace(refreshTokenElement.GetString()))
        {
            SetToken(props, "refresh_token", refreshTokenElement.GetString()!);
        }

        if (root.TryGetProperty("expires_in", out var expiresInElement)
            && expiresInElement.TryGetInt32(out var expiresInSeconds))
        {
            SetToken(
                props,
                "expires_at",
                DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds).ToString("o", CultureInfo.InvariantCulture));
        }

        if (root.TryGetProperty("id_token", out var idTokenElement)
            && !string.IsNullOrWhiteSpace(idTokenElement.GetString()))
        {
            SetToken(props, "id_token", idTokenElement.GetString()!);
        }

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            authenticateResult.Principal,
            props);

        logger.LogInformation("OIDC token refreshed for path={Path}", context.Request.Path.Value);
        return false;
    }

    private static string BuildLoginRedirect(HttpContext context)
    {
        var returnUrl = context.Request.Path + context.Request.QueryString;
        return $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    private static bool IsModulePath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return false;

        return value.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseExpiresAt(string? raw, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expiresAt)
            || DateTimeOffset.TryParse(raw, out expiresAt);
    }

    private static void SetToken(AuthenticationProperties properties, string name, string value)
    {
        var tokens = properties.GetTokens().Where(t => t.Name != name).ToList();
        tokens.Add(new AuthenticationToken { Name = name, Value = value });
        properties.StoreTokens(tokens);
    }
}

public static class GatewayAccessTokenRefreshMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayAccessTokenRefresh(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayAccessTokenRefreshMiddleware>();
}
