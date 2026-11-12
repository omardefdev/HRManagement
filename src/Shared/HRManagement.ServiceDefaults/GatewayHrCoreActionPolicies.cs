namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-50 (§3, P-4): the one source of HR.Core's gateway exceptions for approval actions. A mutating request on a row's path gets the
/// row's policy instead of <see cref="HrManagementPolicies.HrCoreWrite"/>. A row matches the conventional path
/// <c>/ui/hr-core/{Controller}/{Action}[/{guid}]</c>, or its explicit API path after the module prefix, and nothing wider.
/// Rules older than PLAN-50 moved in unchanged in what they admit (their source names the plan); where an old rule matched by
/// <c>Contains</c> or a prefix, the row is exact, so it is narrower or equal.
/// </summary>
public static class GatewayHrCoreActionPolicies
{
    public const string Module = "hr-core";

    /// <summary>A conventional MVC action of HR.Core.</summary>
    public sealed record MvcRow(string Controller, string Action, string Policy, string Source);

    /// <summary>An explicit API route of HR.Core: its segments after the module prefix, <c>{guid}</c> standing for one GUID.</summary>
    public sealed record ApiRow(string Template, string Policy, string Source);

    public static readonly IReadOnlyList<MvcRow> MvcRows =
    [
        new("EmploymentRecords", "Approve", HrManagementPolicies.HrCoreApprove, "PLAN-50"),
        new("EmployeeCorrections", "Approve", HrManagementPolicies.HrCoreApprove, "PLAN-50"),
        new("Competitions", "Finalize", HrManagementPolicies.HrCoreApprove, "PLAN-50"),
        new("Competitions", "Cancel", HrManagementPolicies.HrCoreApprove, "PLAN-50"),
        new("Approvals", "Approve", HrManagementPolicies.HrCoreApprove, "PLAN-44"),
        new("Approvals", "Reject", HrManagementPolicies.HrCoreApprove, "PLAN-44"),
        new("Delegations", "Create", HrManagementPolicies.ManageDelegations, "PLAN-50"),
        new("Delegations", "Revoke", HrManagementPolicies.ManageDelegations, "PLAN-50"),
        new("PolicyTemplates", "PublishFromUi", HrManagementPolicies.PolicyApprover, "PLAN-47"),
    ];

    public static readonly IReadOnlyList<ApiRow> ApiRows =
    [
        new("api/employee-corrections/{guid}/approve", HrManagementPolicies.HrCoreApprove, "PLAN-50"),
        // PLAN-51 (§5): the platform administrator's alone; apply-file is gone.
        new("api/onboarding/agencies/apply", HrManagementPolicies.PlatformAgencyOnboarding, "PLAN-51"),
        new("api/policies/templates/{guid}/publish", HrManagementPolicies.PolicyApprover, "PLAN-47"),
    ];

    /// <summary>The row's policy for a mutating request (POST, PUT, DELETE) on its path.</summary>
    public static bool TryGetPolicy(string httpMethod, string pathValue, out string policy)
    {
        policy = string.Empty;
        if (!IsMutating(httpMethod))
            return false;

        if (GatewayApprovalPaths.TrySplitModulePath(pathValue, Module, out var rest)
            && rest.Count is 2 or 3
            && (rest.Count == 2 || Guid.TryParse(rest[2], out _)))
        {
            var mvc = MvcRows.FirstOrDefault(r =>
                string.Equals(r.Controller, rest[0], StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Action, rest[1], StringComparison.OrdinalIgnoreCase));
            if (mvc is not null)
            {
                policy = mvc.Policy;
                return true;
            }
        }

        if (TrySplitApiPath(pathValue, out var apiRest))
        {
            var api = ApiRows.FirstOrDefault(r => GatewayApprovalPaths.MatchesTemplate(r.Template, apiRest));
            if (api is not null)
            {
                policy = api.Policy;
                return true;
            }
        }

        return false;
    }

    /// <summary>The segments after <c>/api/hr-core</c> or <c>/ui/hr-core</c> (the gateway strips either prefix before HR.Core).</summary>
    internal static bool TrySplitApiPath(string pathValue, out IReadOnlyList<string> rest)
    {
        if (GatewayApprovalPaths.TrySplitModulePath(pathValue, Module, out rest, "api"))
            return true;
        return GatewayApprovalPaths.TrySplitModulePath(pathValue, Module, out rest);
    }

    private static bool IsMutating(string httpMethod) =>
        string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, "PUT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, "DELETE", StringComparison.OrdinalIgnoreCase);
}
