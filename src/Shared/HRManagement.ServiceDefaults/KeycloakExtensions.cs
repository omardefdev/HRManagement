using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HRManagement.ServiceDefaults;

public static class KeycloakExtensions
{
    public const string GatewayAuthenticationScheme = "Gateway";

    /// <summary>Gateway interactive OIDC (Cookie + OpenIdConnect) plus JwtBearer for server-to-server calls.</summary>
    public static IServiceCollection AddGatewayKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = GatewayAuthenticationScheme;
            options.DefaultAuthenticateScheme = GatewayAuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddPolicyScheme(GatewayAuthenticationScheme, null, options =>
        {
            options.ForwardDefaultSelector = context =>
                HasBearerAuthorization(context.Request)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(cookie => ConfigureCookieOptions(cookie, configuration))
        .AddKeycloakJwtBearer(configuration)
        .AddOpenIdConnect(options =>
        {
            ConfigureOpenIdConnect(options, configuration);
            options.SignedOutCallbackPath = "/signout-callback-oidc";

            var postLogoutUri = configuration["HrAuth:PostLogoutRedirectUri"];
            if (!string.IsNullOrWhiteSpace(postLogoutUri))
                options.SignedOutRedirectUri = postLogoutUri;

            var previousSignOut = options.Events.OnRedirectToIdentityProviderForSignOut;
            options.Events.OnRedirectToIdentityProviderForSignOut = async ctx =>
            {
                var idToken = await ctx.HttpContext.GetTokenAsync("id_token");
                if (!string.IsNullOrEmpty(idToken))
                    ctx.ProtocolMessage.IdTokenHint = idToken;

                if (previousSignOut != null)
                    await previousSignOut(ctx);
            };

            KeycloakRoleMapping.AttachTo(options);
        });

        return services;
    }

    public static AuthenticationBuilder AddKeycloakJwtBearer(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        return builder.AddJwtBearer(options =>
        {
            options.Authority = configuration["Keycloak:Authority"];
            options.RequireHttpsMetadata = configuration.GetValue("Keycloak:RequireHttpsMetadata", true);
            var validIssuers = new List<string>();
            var authority = configuration["Keycloak:Authority"];
            if (!string.IsNullOrWhiteSpace(authority))
                validIssuers.Add(authority);
            var additionalIssuers = configuration["Keycloak:AdditionalValidIssuers"];
            if (!string.IsNullOrWhiteSpace(additionalIssuers))
                validIssuers.AddRange(additionalIssuers.Split(';', StringSplitOptions.RemoveEmptyEntries));

            var validAudiences = GetValidAudiences(configuration);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Always on. Deriving this from "did we manage to build an audience list?" meant a
                // service with no audience configured silently accepted tokens minted for any other
                // service in the realm.
                ValidateAudience = true,
                ValidAudiences = validAudiences,
                ValidateIssuer = validIssuers.Count > 0,
                ValidIssuers = validIssuers.Count > 0 ? validIssuers : null,
                ClockSkew = TimeSpan.FromMinutes(2),
                NameClaimType = "preferred_username",
                RoleClaimType = System.Security.Claims.ClaimTypes.Role
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.SecurityToken is JwtSecurityToken jwt)
                        KeycloakRoleMapping.MapRealmRolesFromJwt(context.Principal, jwt);
                    else if (context.SecurityToken is JsonWebToken jsonJwt)
                        KeycloakRoleMapping.MapRealmRolesFromJsonWebToken(context.Principal, jsonJwt);
                    return Task.CompletedTask;
                }
            };
        });
    }

    public static bool HasBearerAuthorization(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var values))
            return false;

        var header = values.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && header.Length > "Bearer ".Length;
    }

    /// <summary>Development-only standalone module OIDC (Cookie + OpenIdConnect).</summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(cookie => ConfigureCookieOptions(cookie, configuration))
        .AddOpenIdConnect(options =>
        {
            ConfigureOpenIdConnect(options, configuration);
            KeycloakRoleMapping.AttachTo(options);
        });

        return services;
    }

    private static void ConfigureCookieOptions(CookieAuthenticationOptions cookie, IConfiguration configuration)
    {
        cookie.Cookie.SameSite = configuration.GetValue("HrAuth:CookieSameSite", SameSiteMode.Lax);
        if (configuration.GetValue("HrAuth:CookieSecureAlways", false))
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        var idleMinutes = configuration.GetValue("HrAuth:IdleTimeoutMinutes", 480);
        idleMinutes = Math.Clamp(idleMinutes, 15, 24 * 60);
        cookie.ExpireTimeSpan = TimeSpan.FromMinutes(idleMinutes);
        cookie.SlidingExpiration = configuration.GetValue("HrAuth:SlidingExpiration", true);
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions options, IConfiguration configuration)
    {
        var authority = configuration["Keycloak:Authority"];
        var publicAuthority = configuration["Keycloak:PublicAuthority"];

        options.Authority = authority;
        options.ClientId = configuration["Keycloak:ClientId"];
        var clientSecret = configuration["Keycloak:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
            clientSecret = Environment.GetEnvironmentVariable("Keycloak__ClientSecret");
        options.ClientSecret = clientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = configuration.GetValue("Keycloak:GetClaimsFromUserInfo", true);
        options.RequireHttpsMetadata = configuration.GetValue("Keycloak:RequireHttpsMetadata", true);
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("offline_access");

        if (!string.IsNullOrWhiteSpace(publicAuthority) && publicAuthority != authority)
        {
            options.Events.OnRedirectToIdentityProvider = ctx =>
            {
                ctx.ProtocolMessage.IssuerAddress = ctx.ProtocolMessage.IssuerAddress
                    .Replace(authority!, publicAuthority);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToIdentityProviderForSignOut = ctx =>
            {
                ctx.ProtocolMessage.IssuerAddress = ctx.ProtocolMessage.IssuerAddress
                    .Replace(authority!, publicAuthority);
                return Task.CompletedTask;
            };
            options.TokenValidationParameters.ValidIssuers = new[] { authority, publicAuthority };
        }

        ConfigureOidcCookies(options, configuration);
    }

    private static void ConfigureOidcCookies(OpenIdConnectOptions oidc, IConfiguration configuration)
    {
        var sameSite = configuration.GetValue("HrAuth:CookieSameSite", SameSiteMode.Lax);
        oidc.NonceCookie.SameSite = sameSite;
        oidc.CorrelationCookie.SameSite = sameSite;

        var secureAlways = configuration.GetValue("HrAuth:CookieSecureAlways", false);
        if (secureAlways)
        {
            oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        }
    }

    /// <summary>
    /// Builds accepted JWT audiences from ClientId + optional AdditionalValidAudiences + api-* resource audiences.
    /// <para>
    /// Keycloak's generic <c>account</c> audience is deliberately not included. Nearly every access
    /// token in the realm carries it, so accepting it made all services share one audience and
    /// erased the boundary between them: a token minted for one service authenticated against all
    /// of them. Realms must instead attach a per-service audience via an audience mapper.
    /// </para>
    /// </summary>
    private static IEnumerable<string>? GetValidAudiences(IConfiguration configuration)
    {
        var audiences = new List<string>();
        var clientId = configuration["Keycloak:ClientId"];
        if (!string.IsNullOrWhiteSpace(clientId))
            audiences.Add(clientId);

        var additional = configuration["Keycloak:AdditionalValidAudiences"];
        if (!string.IsNullOrWhiteSpace(additional))
            audiences.AddRange(additional.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // IAM-004: accept service API audiences when this service is a resource server.
        var apiAudience = configuration["ServiceIdentity:ApiAudience"];
        if (!string.IsNullOrWhiteSpace(apiAudience) && !audiences.Contains(apiAudience, StringComparer.Ordinal))
            audiences.Add(apiAudience);

        return audiences.Count > 0 ? audiences : null;
    }
}
