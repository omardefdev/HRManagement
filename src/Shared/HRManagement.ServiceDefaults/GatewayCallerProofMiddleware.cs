using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Rejects direct module access when the gateway proof is required. PLAN-42: a valid service token passes on its own; every other
/// caller needs a v2 proof. <c>api/internal/*</c> stays exempt because <c>InternalRoutesAreServiceOnlyTests</c> (HRManagement.ArchitectureTests) keeps
/// those routes for service accounts alone.
/// </summary>
public sealed class GatewayCallerProofMiddleware(
    RequestDelegate next,
    ILogger<GatewayCallerProofMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration, IHostEnvironment env)
    {
        var behindGateway = configuration.GetValue("HrAuth:BehindGateway", true);
        var enforce = configuration.GetValue("GatewayCallerProof:Enforce", !env.IsDevelopment()) && behindGateway;

        if (enforce && !IsExemptPath(context.Request.Path) && !await IsServiceAccountAsync(context))
        {
            var keys = context.RequestServices.GetRequiredService<GatewayProofKeys>();
            var result = GatewayCallerProof.ValidateV2(
                context,
                keys.PublicKey,
                context.RequestServices.GetRequiredService<IGatewayProofReplayCache>(),
                keys.MaxSkew);

            var acceptedV1 = result != GatewayProofResult.Valid
                             && env.IsDevelopment()
                             && keys.AllowV1InDevelopment
                             && GatewayCallerProof.ValidateV1(context, configuration);

            if (result != GatewayProofResult.Valid && !acceptedV1)
            {
                logger.LogWarning("Gateway caller proof rejected: {Result} for {ActualPath}", result, context.Request.Path.Value);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Direct module access is not permitted.");
                return;
            }
        }

        await next(context);
    }

    /// <summary>The scheme caches its result for the request, so authentication later in the pipeline does not validate twice.</summary>
    private static async Task<bool> IsServiceAccountAsync(HttpContext context)
    {
        if (!KeycloakExtensions.HasBearerAuthorization(context.Request))
            return false;

        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null || await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme) is null)
            return false;

        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        return result.Succeeded && ServiceCallerPrincipal.IsServiceAccount(result.Principal);
    }

    /// <summary>Whole path segments only: /HealthStatusEntries is a page of hr-health-web, not /health.</summary>
    public static bool IsExemptPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return IsPathOrChild(value, "/health")
               || IsPathOrChild(value, "/alive")
               || IsPathOrChild(value, "/ready")
               || IsPathOrChild(value, "/metrics")
               || value.StartsWith("/api/internal/", StringComparison.OrdinalIgnoreCase)
               || (value.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase)
                   && value.EndsWith("/references", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPathOrChild(string value, string prefix)
    {
        return value.Equals(prefix, StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}

public static class GatewayCallerProofMiddlewareExtensions
{
    public static IApplicationBuilder UseHrGatewayCallerProof(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayCallerProofMiddleware>();
}

/// <summary>
/// PLAN-42: signs a gateway call to a module. Registered on the gateway's named clients, so a call the gateway makes on the user's
/// behalf carries the same proof as a proxied one. Without a private key the request stays unsigned and the module refuses it.
/// </summary>
public sealed class GatewayProofSigningHandler(
    GatewayProofKeys keys,
    IConfiguration configuration,
    IHostEnvironment environment) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sign(request, keys, configuration, environment, request.RequestUri?.PathAndQuery ?? "/");
        return base.SendAsync(request, cancellationToken);
    }

    public static void Sign(
        HttpRequestMessage request,
        GatewayProofKeys keys,
        IConfiguration configuration,
        IHostEnvironment environment,
        string pathAndQuery)
    {
        if (keys.PrivateKey is { } privateKey)
            GatewayCallerProof.SignV2(request, privateKey, pathAndQuery);
        else if (environment.IsDevelopment() && keys.AllowV1InDevelopment)
            GatewayCallerProof.SignV1(request, configuration, pathAndQuery);
    }
}
