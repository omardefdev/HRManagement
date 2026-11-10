namespace HR.Contracts.Application.Features.LeaveRequests;

/// <summary>
/// PLAN-49 (G-POL-04): the actions the leave channels send the policy engine for Contracts.Leave, as PolicyDomainCatalog lists
/// them for the type LeaveRequest. Create: a request recorded by HR, and one that arrives from the self-service portal.
/// Update: an edit of a pending request. Approve: the approval.
/// </summary>
public static class LeavePolicyActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Approve = "Approve";

    /// <summary>What a refusal says when the policy gives no reason of its own.</summary>
    public const string DeniedMessage = "مرفوض بسياسة الإجازات";
}
