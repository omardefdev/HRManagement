using HRManagement.SharedKernel.Policy;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>
/// PLAN-42 (G-POL-08): the only (domain, subject type, workflow code) triples a service may start through the internal enforce
/// endpoint. Callers inside HR.Core do not pass here. PLAN-43: the list is empty: enforce only says an approval is needed, and the
/// owner starts it through <c>ExternalApprovalService</c>. PLAN-49: the list is read from <see cref="PolicyDomainCatalog"/>: the
/// triggers of a governed, service-owned domain that the catalog marks <see cref="PolicyWorkflowStartPath.InternalEndpoint"/>.
/// The catalog marks none, so the endpoint still starts nothing, and a triple cannot start without being written there.
/// </summary>
public static class PolicyWorkflowTriggerAllowlist
{
    public const string NotAllowedExplanation = "أثر TriggerWorkflow غير مسموح لهذا الكيان";

    /// <summary>Every triple the internal endpoint may start, as the catalog says.</summary>
    public static IReadOnlyList<(string Domain, string SubjectType, string WorkflowCode)> Triples =>
        PolicyDomainCatalog.Governed
            .Where(d => d.OwnerClientId is not null)
            .SelectMany(d => d.WorkflowTriggers.SelectMany(kv => kv.Value
                .Where(t => t.StartedBy == PolicyWorkflowStartPath.InternalEndpoint)
                .Select(t => (d.Domain, SubjectType: kv.Key, t.WorkflowCode))))
            .ToList();

    public static bool Allows(string domain, string subjectType, string? workflowCode) =>
        workflowCode is not null && Triples.Contains((domain, subjectType, workflowCode));
}
