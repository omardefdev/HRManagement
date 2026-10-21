using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Forwards the gateway OIDC access token to upstream module services.
/// Strips gateway OIDC/session cookies but preserves module antiforgery cookies for form POSTs.
/// </summary>
public sealed class GatewayAccessTokenTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var pathPattern = context.Route.Match.Path;
        var routePrefix = string.Empty;
        if (!string.IsNullOrEmpty(pathPattern)
            && pathPattern.EndsWith("{**catch-all}", StringComparison.Ordinal))
        {
            var prefix = pathPattern[..^"{**catch-all}".Length].TrimEnd('/');
            if (prefix.Length > 0
                && (prefix.StartsWith("/api/", StringComparison.Ordinal)
                    || prefix.StartsWith("/ui/", StringComparison.Ordinal))
                && !ShouldPreserveApiPrefix(prefix))
            {
                routePrefix = prefix;
                context.AddPathRemovePrefix(prefix);
            }
        }

        context.AddRequestTransform(async transformContext =>
        {
            var httpContext = transformContext.HttpContext;
            var accessToken = await httpContext.GetTokenAsync("access_token");
            if (string.IsNullOrEmpty(accessToken)
                && AuthenticationHeaderValue.TryParse(httpContext.Request.Headers.Authorization, out var incoming)
                && string.Equals(incoming.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                accessToken = incoming.Parameter;
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                transformContext.ProxyRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
            }

            transformContext.ProxyRequest.Headers.Remove("Cookie");
            if (httpContext.Request.Headers.TryGetValue("Cookie", out var incomingCookies))
            {
                var antiforgeryCookies = GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(
                    incomingCookies.ToString());
                if (!string.IsNullOrEmpty(antiforgeryCookies))
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("Cookie", antiforgeryCookies);
                }
            }

            if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId)
                && correlationId is string correlationIdValue
                && !string.IsNullOrWhiteSpace(correlationIdValue))
            {
                transformContext.ProxyRequest.Headers.Remove(CorrelationIdMiddleware.HeaderName);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                    CorrelationIdMiddleware.HeaderName,
                    correlationIdValue);
            }

            if (httpContext.Items.TryGetValue(TenantIdMiddleware.ItemKey, out var tenantIdItem)
                && tenantIdItem is Guid tenantIdValue)
            {
                transformContext.ProxyRequest.Headers.Remove(TenantIdMiddleware.HeaderName);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                    TenantIdMiddleware.HeaderName,
                    tenantIdValue.ToString("D"));
            }

            var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
            var proofPath = transformContext.Path;
            if (!string.IsNullOrEmpty(routePrefix)
                && httpContext.Request.Path.StartsWithSegments(new PathString(routePrefix), out var remainingPath))
            {
                proofPath = remainingPath.HasValue ? remainingPath : new PathString("/");
            }
            GatewayCallerProof.Sign(
                transformContext.ProxyRequest,
                configuration,
                $"{proofPath}{httpContext.Request.QueryString}");
        });
    }

    internal static bool ShouldPreserveApiPrefix(string prefix) =>
        string.Equals(prefix, "/api/payroll", StringComparison.OrdinalIgnoreCase)
        || string.Equals(prefix, "/api/files", StringComparison.OrdinalIgnoreCase);
}
