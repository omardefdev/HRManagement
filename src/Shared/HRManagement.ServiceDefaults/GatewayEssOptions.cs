namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-51 (§1, D-51-1): <c>Gateway:Ess</c>. The self-service controllers the gateway exposes per path segment, matched by the first segment
/// after <c>/ui/{segment}/</c> or <c>/api/{segment}/</c> exactly (case-insensitive, as MVC routes), never by a substring. On the <c>ess</c>
/// segment, anything else that is not a static file (GET) answers 404 at the gateway.
/// </summary>
public sealed class GatewayEssOptions
{
    public const string SectionName = "Gateway:Ess";

    /// <summary>The gateway's own self-service segment: <c>/ui/ess/{**catch-all}</c> goes to HR.Core.</summary>
    public const string EssSegment = "ess";

    /// <summary>The gateway's audit decision for a path under the <c>ess</c> segment that is not exposed.</summary>
    public const string NotExposedDecision = "Denied:EssNotExposed";

    /// <summary>The <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> key that hands an explicit decision to the gateway's audit.</summary>
    public const string AuditDecisionItemKey = "HRManagement.Gateway.AuditDecision";

    public static readonly IReadOnlyDictionary<string, string[]> DefaultAllowedControllers =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [EssSegment] = ["EssPortal", "EssTalent"],
            ["hr-core"] = ["EssPortal", "EssTalent"],
            ["payroll"] = ["Ess"],
        };

    public static readonly IReadOnlyList<string> DefaultStaticPrefixes = ["css/", "js/", "lib/", "fonts/", "images/", "_content/", "favicon.ico"];

    /// <summary>Segment → controller names. Empty in configuration means the defaults (D-51-1).</summary>
    public Dictionary<string, string[]> AllowedControllers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The static file prefixes (after the segment) the <c>ess</c> segment serves on GET. Empty means the defaults.</summary>
    public List<string> StaticPrefixes { get; set; } = [];

    public IReadOnlyDictionary<string, string[]> EffectiveAllowedControllers =>
        AllowedControllers.Count > 0
            ? new Dictionary<string, string[]>(AllowedControllers, StringComparer.OrdinalIgnoreCase)
            : DefaultAllowedControllers;

    public IReadOnlyList<string> EffectiveStaticPrefixes => StaticPrefixes.Count > 0 ? StaticPrefixes : DefaultStaticPrefixes;
}

/// <summary>What the gateway decides for a self-service path.</summary>
public enum GatewayEssDecision
{
    /// <summary>Not a self-service path: the usual rules apply.</summary>
    NotEss,

    /// <summary>A named self-service controller: <see cref="HrManagementPolicies.ModuleEss"/>.</summary>
    Ess,

    /// <summary>A static file under the <c>ess</c> segment (GET): <see cref="HrManagementPolicies.GatewayAuthenticated"/>.</summary>
    StaticFile,

    /// <summary>Anything else under the <c>ess</c> segment: 404 before forwarding.</summary>
    NotExposed,
}

/// <summary>PLAN-51 (§1, P-3): the gateway's self-service rule, by exact segment.</summary>
public static class GatewayEssRouting
{
    public static GatewayEssDecision Decide(string httpMethod, string pathValue, GatewayEssOptions options)
    {
        if (!TrySplit(pathValue, out var segment, out var rest))
            return GatewayEssDecision.NotEss;

        var isEssSegment = string.Equals(segment, GatewayEssOptions.EssSegment, StringComparison.OrdinalIgnoreCase);
        if (!options.EffectiveAllowedControllers.TryGetValue(segment, out var controllers))
        {
            // The ess segment is always decided here: a configuration without its entry exposes no controller, it never falls back
            // to the segment's module policy (the old catch-all).
            if (!isEssSegment)
                return GatewayEssDecision.NotEss;
            controllers = [];
        }

        if (rest is not null && !rest.Contains("//", StringComparison.Ordinal))
        {
            var first = rest.Split('/', 2)[0];
            if (controllers.Any(c => string.Equals(c, first, StringComparison.OrdinalIgnoreCase)))
                return GatewayEssDecision.Ess;
        }

        if (!isEssSegment)
            return GatewayEssDecision.NotEss;

        return IsStaticFile(httpMethod, rest, options) ? GatewayEssDecision.StaticFile : GatewayEssDecision.NotExposed;
    }

    private static bool IsStaticFile(string httpMethod, string? rest, GatewayEssOptions options)
    {
        if (rest is null
            || !(string.Equals(httpMethod, "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(httpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            || rest.Contains("//", StringComparison.Ordinal)
            || rest.Split('/').Any(s => s is ".." or "."))
            return false;

        // The scoped CSS bundle of the application ({assembly}.styles.css) sits at the root.
        if (rest.IndexOf('/') < 0 && rest.EndsWith(".styles.css", StringComparison.OrdinalIgnoreCase))
            return true;

        return options.EffectiveStaticPrefixes.Any(p => p.EndsWith('/')
            ? rest.StartsWith(p, StringComparison.OrdinalIgnoreCase) && rest.Length > p.Length
            : string.Equals(rest, p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><c>/ui/{segment}[/rest]</c> or <c>/api/{segment}[/rest]</c>; <paramref name="rest"/> is null when nothing follows the segment.</summary>
    private static bool TrySplit(string pathValue, out string segment, out string? rest)
    {
        segment = string.Empty;
        rest = null;
        string tail;
        if (pathValue.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase))
            tail = pathValue[4..];
        else if (pathValue.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            tail = pathValue[5..];
        else
            return false;

        var slash = tail.IndexOf('/');
        segment = slash < 0 ? tail : tail[..slash];
        if (segment.Length == 0)
            return false;
        var after = slash < 0 ? string.Empty : tail[(slash + 1)..];
        rest = after.Length == 0 ? null : after;
        return true;
    }
}
