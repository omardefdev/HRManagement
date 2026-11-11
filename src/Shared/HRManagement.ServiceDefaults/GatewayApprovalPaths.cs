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
    /// The workflow approval actions of HR.Core's own entities (POST): a delegate may hold no operational role (D-50-3), and the holders of
    /// admin-logistics, idcard or compliance hold no HR.Core read role (PLAN-45/46), so the gateway admits any staff user and the service's
    /// <see cref="HrManagementPolicies.WorkflowStepAction"/> and the evaluator decide.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> HrCoreWorkflowActions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["RecruitmentRequisitions"] = ["ApproveWorkflow", "RejectWorkflow"],
            ["OfferLetters"] = ["ApproveWorkflow", "RejectWorkflow"],
            ["SelectionCommittees"] = ["ApproveWorkflow", "RejectWorkflow"],
            ["EstablishmentChanges"] = ["ApproveWorkflow", "RejectWorkflow"],
            ["Competitions"] = ["ApproveWorkflow", "RejectWorkflow", "ApproveResultsWorkflow", "RejectResultsWorkflow"],
            // PLAN-45: the generic approval page of the external types.
            ["WorkflowApprovals"] = ["ApproveWorkflow", "RejectWorkflow"],
        };

    /// <summary>The step actions of the workflow API (PLAN-43): <c>api/workflow-instances/{guid}/approve|reject</c>.</summary>
    public static readonly IReadOnlyList<string> HrCoreWorkflowApiTemplates =
        ["api/workflow-instances/{guid}/approve", "api/workflow-instances/{guid}/reject"];

    /// <summary>
    /// A workflow step action of HR.Core on POST: <c>/ui/hr-core/{Controller}/{Action}[/{guid}]</c> for the actions above, or the API
    /// template after <c>/api/hr-core</c> or <c>/ui/hr-core</c>.
    /// </summary>
    public static bool IsHrCoreWorkflowApprovalPath(string httpMethod, string pathValue)
    {
        if (!string.Equals(httpMethod, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
            return false;

        if (TrySplitModulePath(pathValue, GatewayHrCoreActionPolicies.Module, out var rest)
            && rest.Count is 2 or 3
            && (rest.Count == 2 || Guid.TryParse(rest[2], out _))
            && HrCoreWorkflowActions.TryGetValue(rest[0], out var actions)
            && actions.Contains(rest[1], StringComparer.OrdinalIgnoreCase))
            return true;

        return GatewayHrCoreActionPolicies.TrySplitApiPath(pathValue, out var apiRest)
               && HrCoreWorkflowApiTemplates.Any(t => MatchesTemplate(t, apiRest));
    }

    /// <summary>Segments equal to the template's, case-insensitive, where <c>{guid}</c> takes one GUID.</summary>
    public static bool MatchesTemplate(string template, IReadOnlyList<string> segments)
    {
        var parts = template.Split('/');
        if (parts.Length != segments.Count)
            return false;

        for (var i = 0; i < parts.Length; i++)
        {
            var ok = parts[i] == "{guid}"
                ? Guid.TryParse(segments[i], out _)
                : string.Equals(parts[i], segments[i], StringComparison.OrdinalIgnoreCase);
            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The segments after <c>/{root}/{module}</c> (root <c>ui</c> by default). False when the path is elsewhere or has an empty segment
    /// (a single trailing slash is allowed).
    /// </summary>
    internal static bool TrySplitModulePath(string pathValue, string module, out IReadOnlyList<string> rest, string root = "ui")
    {
        rest = [];
        if (string.IsNullOrEmpty(pathValue))
            return false;

        var value = pathValue.EndsWith('/') ? pathValue[..^1] : pathValue;
        var segments = value.Split('/');
        // "", root, module, ...
        if (segments.Length < 3
            || segments[0].Length != 0
            || !string.Equals(segments[1], root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], module, StringComparison.OrdinalIgnoreCase))
            return false;

        var tail = segments[3..];
        if (tail.Any(s => s.Length == 0))
            return false;

        rest = tail;
        return true;
    }
}
