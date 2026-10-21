using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Forwards the caller's OIDC access token as Bearer for server-side File API calls.
/// Cookie forwarding is intentionally omitted — gateway cookies are host-bound and trigger invalid OIDC redirects.
/// </summary>
public sealed class ForwardAuthHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is not null)
        {
            if (request.Headers.Authorization is null)
            {
                if (TryGetBearerFromAuthorizationHeader(context, out var bearer))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }
                else if (context.RequestServices?.GetService(typeof(IAuthenticationService)) is not null)
                {
                    var accessToken = await context.GetTokenAsync("access_token");
                    if (!string.IsNullOrWhiteSpace(accessToken))
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }
            }

            var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
                ?? CorrelationIdFlow.Get();
            if (!string.IsNullOrWhiteSpace(correlationId)
                && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            {
                request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);
            }

            var tenantId = context.Items[TenantIdMiddleware.ItemKey] as Guid?
                ?? TenantIdFlow.Get();
            if (tenantId.HasValue && !request.Headers.Contains(TenantIdMiddleware.HeaderName))
            {
                request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenantId.Value.ToString("D"));
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool TryGetBearerFromAuthorizationHeader(HttpContext context, out string token)
    {
        token = string.Empty;
        if (!context.Request.Headers.TryGetValue("Authorization", out var values))
            return false;

        var header = values.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = header["Bearer ".Length..].Trim();
        return !string.IsNullOrEmpty(token);
    }
}
