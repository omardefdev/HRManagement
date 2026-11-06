using HRManagement.SharedKernel.Policy;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>
/// PLAN-42 (G-POL-08): the only (domain, subject type, workflow code) triples a service may start through the internal enforce
/// endpoint. Callers inside HR.Core do not pass here. PLAN-43: the list is empty: enforce only says an approval is needed, and the
/// owner starts it through <c>ExternalApprovalService</c>. The check stays, so a triple cannot start without being written here.
/// </summary>
public static class PolicyWorkflowTriggerAllowlist
{
    public const string NotAllowedExplanation = "أثر TriggerWorkflow غير مسموح لهذا الكيان";

    private static readonly HashSet<(string Domain, string SubjectType, string WorkflowCode)> Allowed =
    [];

    public static bool Allows(string domain, string subjectType, string? workflowCode) =>
        workflowCode is not null && Allowed.Contains((domain, subjectType, workflowCode));
}
