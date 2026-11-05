namespace HRManagement.ServiceDefaults;

/// <summary>
/// Selectively forwards antiforgery cookies and the flash message cookie to upstream module services behind the gateway.
/// Gateway OIDC/session cookies are stripped to avoid conflicting with JwtBearer upstream auth.
/// HCM-030 (PLAN-30 H5): on the anonymous careers route only, the candidate portal cookie
/// <see cref="CareersSessionCookieName"/> (exact name) is forwarded as well.
/// </summary>
public static class GatewayUpstreamCookieForwarding
{
    public const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";

    /// <summary>
    /// The MVC flash message cookie. Without it a refusal reported through TempData (a denied handover, a failed legal-hold check) is
    /// lost and the page reloads with no reason. It carries no identity, and only the service that wrote it can read it.
    /// </summary>
    public const string TempDataCookieName = ".AspNetCore.Mvc.CookieTempDataProvider";

    /// <summary>The HR.Core candidate portal session cookie (<c>/careers/my</c>); issued by HR.Core, forwarded on the careers route only.</summary>
    public const string CareersSessionCookieName = "careers_session";

    /// <summary>Gateway route id of the anonymous citizen careers portal (gateway appsettings <c>ReverseProxy:Routes</c>).</summary>
    public const string CareersRouteId = "careers-public";

    public static string? BuildAntiforgeryCookieHeader(string? cookieHeader) =>
        BuildUpstreamCookieHeader(cookieHeader, forwardCareersSession: false);

    /// <summary>
    /// Keeps antiforgery cookies, plus the flash message cookie on staff routes or, when <paramref name="forwardCareersSession"/> is set (careers route), the cookie
    /// named exactly <see cref="CareersSessionCookieName"/>. Every other cookie is dropped.
    /// </summary>
    public static string? BuildUpstreamCookieHeader(string? cookieHeader, bool forwardCareersSession)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return null;

        var parts = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var forwardedParts = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            var equalsIndex = trimmed.IndexOf('=');
            var name = equalsIndex >= 0 ? trimmed[..equalsIndex] : trimmed;
            if (name.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal)
                || (!forwardCareersSession && string.Equals(name, TempDataCookieName, StringComparison.Ordinal))
                || (forwardCareersSession && string.Equals(name, CareersSessionCookieName, StringComparison.Ordinal)))
                forwardedParts.Add(trimmed);
        }

        return forwardedParts.Count == 0 ? null : string.Join("; ", forwardedParts);
    }

    /// <summary>
    /// True for the careers route: route id <see cref="CareersRouteId"/>, or a path pattern under
    /// <see cref="ProductionHostExtensions.PublicCareersGatewayPrefix"/> (segment match, so <c>/careersx</c> is not one).
    /// </summary>
    public static bool IsCareersRoute(string? routeId, string? pathPattern)
    {
        if (string.Equals(routeId, CareersRouteId, StringComparison.Ordinal))
            return true;

        if (string.IsNullOrEmpty(pathPattern))
            return false;

        var prefix = ProductionHostExtensions.PublicCareersGatewayPrefix;
        return string.Equals(pathPattern, prefix, StringComparison.OrdinalIgnoreCase)
            || pathPattern.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
