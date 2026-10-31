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
/// are exempt for signed-in users so that page loads do not use up their window. Runs after authentication, so the subject comes from the
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
        var subject = SubjectOf(context.User);
        if (subject is null)
        {
            // Anonymous callers are never exempt: an asset-looking path still triggers the OIDC challenge (a pushed
            // authorization request to Keycloak), so exempting it would let anyone flood Keycloak through the gateway.
            return new GatewayRateLimitPartition(
                GatewayRateLimitPartitionKind.Ip,
                "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
        }

        if (exemptStaticAssets && IsStaticAsset(context.Request))
            return new GatewayRateLimitPartition(GatewayRateLimitPartitionKind.Exempt, "exempt");

        return new GatewayRateLimitPartition(GatewayRateLimitPartitionKind.User, "user:" + subject);
    }

    /// <summary>
    /// A GET or HEAD for a file with an asset extension under a module's UI (<c>/ui/{module}/…</c>). Nothing else is an
    /// asset: the gateway's own static files are served by <c>UseStaticFiles</c> before the limiter, and an API path is
    /// an API call whatever it ends with (<c>/api/x.css</c>). Applies only to signed-in callers (<see cref="Resolve"/>).
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

        if (!path.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase))
            return false;

        // /ui/{module}/{file...}: a module slug and at least one more segment.
        var rest = path.AsSpan(4);
        var slash = rest.IndexOf('/');
        return slash > 0 && slash < rest.Length - 1;
    }

    private static string? SubjectOf(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(subject) ? null : subject;
    }
}
