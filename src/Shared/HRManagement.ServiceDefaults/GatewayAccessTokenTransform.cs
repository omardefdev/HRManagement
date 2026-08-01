using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
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
        context.AddRequestTransform(async transformContext =>
        {
            var httpContext = transformContext.HttpContext;
            var accessToken = await httpContext.GetTokenAsync("access_token");

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
            GatewayCallerProof.Sign(transformContext.ProxyRequest, configuration);
        });
    }
}
