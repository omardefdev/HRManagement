using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-50 (§2, §4, P-3, P-4): the named approval paths the gateway opens to <see cref="HrManagementPolicies.GatewayApprovalReach"/>. Every
/// match is by path segment and exact (never <c>Contains</c>): the module prefix, the controller, the action, and at most one GUID id.
/// A path that is not named here keeps the module's policy.
/// </summary>
public static class GatewayApprovalPaths
{
    public const string MovementsModule = "hr-movements";

    /// <summary>
    /// The four approval screens of the five movement types (D-50-3): the list and the details on GET, approve (and reject, which only
    /// transfers have) on POST. A controller's root is its <c>Index</c>, and the module's root is the default route
    /// (<c>TransferRequests/Index</c>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Action, string Method)>> MovementApprovalActions =
        new Dictionary<string, IReadOnlyList<(string Action, string Method)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["TransferRequests"] = [("Index", HttpMethods.Get), ("Details", HttpMethods.Get), ("Approve", HttpMethods.Post), ("Reject", HttpMethods.Post)],
            ["SecondmentRequests"] = [("Index", HttpMethods.Get), ("Details", HttpMethods.Get), ("Approve", HttpMethods.Post)],
            ["LoanAssignmentRequests"] = [("Index", HttpMethods.Get), ("Details", HttpMethods.Get), ("Approve", HttpMethods.Post)],
            ["PromotionRequests"] = [("Index", HttpMethods.Get), ("Details", HttpMethods.Get), ("Approve", HttpMethods.Post)],
            ["DelegationRequests"] = [("Index", HttpMethods.Get), ("Details", HttpMethods.Get), ("Approve", HttpMethods.Post)],
        };

    private const string MovementsDefaultController = "TransferRequests";

    private static readonly string[] StaticFolders = ["css", "js", "lib", "fonts", "images", "_content"];

    /// <summary>A movement approval screen: <c>/ui/hr-movements[/{Controller}[/{Action}[/{guid}]]]</c>, with the verb its action takes.</summary>
    public static bool IsMovementApprovalPath(string httpMethod, string pathValue)
    {
        if (!TrySplitModulePath(pathValue, MovementsModule, out var rest))
            return false;

        var (controller, action, hasId) = rest.Count switch
        {
            0 => (MovementsDefaultController, "Index", false),
            1 => (rest[0], "Index", false),
            2 => (rest[0], rest[1], false),
            3 => (rest[0], rest[1], true),
            _ => (string.Empty, string.Empty, false)
        };

        if (hasId && !Guid.TryParse(rest[2], out _))
            return false;

        if (!MovementApprovalActions.TryGetValue(controller, out var actions))
            return false;

        return actions.Any(a =>
            string.Equals(a.Action, action, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Method, httpMethod, StringComparison.OrdinalIgnoreCase)
            // The list takes no id; the details, approve and reject take one in the path or in the query or the form.
            && !(hasId && string.Equals(a.Action, "Index", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>A static asset of a module on GET (it carries no data and backs the module's pages).</summary>
    public static bool IsModuleStaticAsset(string module, string httpMethod, string pathValue)
    {
        if (!string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(httpMethod, HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TrySplitModulePath(pathValue, module, out var rest) || rest.Count == 0)
            return false;

        if (rest.Count == 1)
            return string.Equals(rest[0], "favicon.ico", StringComparison.OrdinalIgnoreCase)
                   || rest[0].EndsWith(".styles.css", StringComparison.OrdinalIgnoreCase);

        return StaticFolders.Contains(rest[0], StringComparer.OrdinalIgnoreCase) && !rest.Contains("..");
    }

    /// <summary>
    /// The segments after <c>/ui/{module}</c>. False when the path is elsewhere or has an empty segment (a single trailing slash is
    /// allowed).
    /// </summary>
    internal static bool TrySplitModulePath(string pathValue, string module, out IReadOnlyList<string> rest)
    {
        rest = [];
        if (string.IsNullOrEmpty(pathValue))
            return false;

        var value = pathValue.EndsWith('/') ? pathValue[..^1] : pathValue;
        var segments = value.Split('/');
        // "", "ui", module, ...
        if (segments.Length < 3
            || segments[0].Length != 0
            || !string.Equals(segments[1], "ui", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], module, StringComparison.OrdinalIgnoreCase))
            return false;

        var tail = segments[3..];
        if (tail.Any(s => s.Length == 0))
            return false;

        rest = tail;
        return true;
    }
}
