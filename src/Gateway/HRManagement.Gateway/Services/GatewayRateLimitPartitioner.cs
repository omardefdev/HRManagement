using System.Security.Claims;

namespace HRManagement.Gateway.Services;

/// <summary>Which window a request counts against.</summary>
public enum GatewayRateLimitPartitionKind
{
    /// <summary>A signed-in user: one window per <c>sub</c>, whatever address the user comes from.</summary>
    User,

    /// <summary>An anonymous caller: one window per client IP.</summary>
    Ip,

    /// <summary>A static asset (stylesheet, script, font, image): not limited.</summary>
    Exempt
}

public readonly record struct GatewayRateLimitPartition(GatewayRateLimitPartitionKind Kind, string Key)
{
    /// <summary>The kind as written in the 429 log line: never the user or the address.</summary>
    public string KindName => Kind switch
    {
        GatewayRateLimitPartitionKind.User => "user",
        GatewayRateLimitPartitionKind.Ip => "ip",
        _ => "exempt"
    };
}

/// <summary>
/// PLAN-36 §4 (G-OPS-10): a demo room behind one NAT shares one client IP, so signed-in users are partitioned by
/// their subject and only anonymous callers by IP. Static assets of the module pages (about 13 per page through YARP)
/// are exempt so that page loads do not use up the window. Runs after authentication, so the subject comes from the
/// validated session cookie and cannot be chosen by the client; the IP is <c>RemoteIpAddress</c>, rewritten only for
/// trusted proxies (<see cref="HRManagement.ServiceDefaults.ForwardedProxyTrust"/>).
/// </summary>
public static class GatewayRateLimitPartitioner
{
    /// <summary>Asset extensions; anything else (including a page or an API call) is limited.</summary>
    public static readonly string[] StaticAssetExtensions =
        [".css", ".js", ".woff", ".woff2", ".ttf", ".png", ".svg", ".ico", ".map"];

    public static GatewayRateLimitPartition Resolve(HttpContext context, bool exemptStaticAssets)
    {
        if (exemptStaticAssets && IsStaticAsset(context.Request))
            return new GatewayRateLimitPartition(GatewayRateLimitPartitionKind.Exempt, "exempt");

        var subject = SubjectOf(context.User);
        if (subject is not null)
            return new GatewayRateLimitPartition(GatewayRateLimitPartitionKind.User, "user:" + subject);

        return new GatewayRateLimitPartition(
            GatewayRateLimitPartitionKind.Ip,
            "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
    }

    /// <summary>
    /// A GET or HEAD for a file with an asset extension under a module's UI (<c>/ui/{module}/…</c>) or the gateway's
    /// own static files at the root. API and public-portal paths are never exempt, whatever they end with
    /// (<c>/api/x.css</c> is an API call).
    /// </summary>
    public static bool IsStaticAsset(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;

        var path = request.Path.Value;
        if (string.IsNullOrEmpty(path) || path.EndsWith('/'))
            return false;

        if (!StaticAssetExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (path.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase))
        {
            // /ui/{module}/{file...}: a module slug and at least one more segment.
            var rest = path.AsSpan(4);
            var slash = rest.IndexOf('/');
            return slash > 0 && slash < rest.Length - 1;
        }

        return !request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
               && !request.Path.StartsWithSegments("/careers", StringComparison.OrdinalIgnoreCase)
               && !request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
               && !request.Path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SubjectOf(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(subject) ? null : subject;
    }
}
