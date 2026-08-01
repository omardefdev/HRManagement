namespace HRManagement.ServiceDefaults;

/// <summary>
/// Selectively forwards antiforgery cookies to upstream module services behind the gateway.
/// Gateway OIDC/session cookies are stripped to avoid conflicting with JwtBearer upstream auth.
/// </summary>
public static class GatewayUpstreamCookieForwarding
{
    public const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";

    public static string? BuildAntiforgeryCookieHeader(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return null;

        var parts = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var antiforgeryParts = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            var equalsIndex = trimmed.IndexOf('=');
            var name = equalsIndex >= 0 ? trimmed[..equalsIndex] : trimmed;
            if (name.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal))
                antiforgeryParts.Add(trimmed);
        }

        return antiforgeryParts.Count == 0 ? null : string.Join("; ", antiforgeryParts);
    }
}
