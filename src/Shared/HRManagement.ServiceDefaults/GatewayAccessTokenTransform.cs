using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Forwards the gateway OIDC access token to upstream module services (never on the anonymous careers route).
/// Strips gateway OIDC/session cookies but preserves module antiforgery cookies for form POSTs
/// (and, on the careers route only, the candidate portal <c>careers_session</c> cookie).
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

        // HCM-030 (H5): the candidate portal session cookie reaches HR.Core on the careers route only.
        var forwardCareersSession = GatewayUpstreamCookieForwarding.IsCareersRoute(context.Route.RouteId, pathPattern);

        context.AddRequestTransform(async transformContext =>
        {
            var httpContext = transformContext.HttpContext;
            // HCM-030 (H5): the public candidate portal never receives staff credentials. A signed-in staff browser
            // (or a client Bearer header) would otherwise authenticate upstream, and HR.Core's TenantContext prefers the
            // token's tenant_id claim over the portal session tenant (DECISIONS H.1).
            var accessToken = forwardCareersSession ? null : await httpContext.GetTokenAsync("access_token");
            if (forwardCareersSession)
            {
                transformContext.ProxyRequest.Headers.Authorization = null;
            }
            else if (string.IsNullOrEmpty(accessToken)
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
                var upstreamCookies = GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(
                    incomingCookies.ToString(),
                    forwardCareersSession);
                if (!string.IsNullOrEmpty(upstreamCookies))
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("Cookie", upstreamCookies);
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
