using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.ServiceDefaults;

/// <summary>SEC-001: blocks open redirects via user-supplied paths.</summary>
public static class SafeRedirect
{
    /// <summary>
    /// Returns a safe relative module path. Only paths under <paramref name="allowedPrefix"/> are accepted
    /// (typically <c>/ui/{slug}/</c>). External URLs and protocol-relative paths are rejected.
    /// </summary>
    public static bool TryResolveModuleUiPath(
        IUrlHelper urlHelper,
        string allowedPrefix,
        string? candidate,
        out string target)
    {
        var fallback = allowedPrefix.TrimEnd('/') + "/";
        target = fallback;

        if (string.IsNullOrWhiteSpace(candidate))
            return true;

        var path = candidate.Trim();
        if (!path.StartsWith('/'))
            return false;
        if (path.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (path.Contains("://", StringComparison.Ordinal))
            return false;
        if (!urlHelper.IsLocalUrl(path))
            return false;

        var normalizedPrefix = allowedPrefix.TrimEnd('/') + "/";
        if (!path.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            && !path.Equals(normalizedPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return false;

        target = path;
        return true;
    }

    /// <summary>SEC-001: same-host referer redirect for MVC conflict recovery.</summary>
    public static bool TryGetSafeRefererRedirect(HttpContext context, out string target)
    {
        target = "/";
        var referer = context.Request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(referer))
            return false;

        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.PathAndQuery;
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
            return false;
        if (path.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (path.Contains("://", StringComparison.Ordinal))
            return false;

        target = path;
        return true;
    }
}
