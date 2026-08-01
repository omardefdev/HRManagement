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
}
