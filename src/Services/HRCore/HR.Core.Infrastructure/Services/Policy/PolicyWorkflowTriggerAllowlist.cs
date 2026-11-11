using HRManagement.SharedKernel.Policy;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>
/// PLAN-42 (G-POL-08): the only starts a service may make through the internal enforce endpoint: a domain, an entity type, the
/// action the code starts at, and the workflow code (the action since the second review of PLAN-49). Callers inside HR.Core do
/// not pass here. PLAN-43: the list is empty: enforce only says an approval is needed, and the owner starts it through
/// <c>ExternalApprovalService</c>. PLAN-49: the list is read from <see cref="PolicyDomainCatalog"/>: the triggers of a governed,
/// service-owned domain that the catalog marks <see cref="PolicyWorkflowStartPath.InternalEndpoint"/>. The catalog marks none,
/// so the endpoint still starts nothing, and nothing can start without being written there.
/// </summary>
public static class PolicyWorkflowTriggerAllowlist
{
    public const string NotAllowedExplanation = "أثر TriggerWorkflow غير مسموح لهذا الكيان";

    /// <summary>
    /// Every start the internal endpoint may make, as the catalog says: the entity type, the action the code starts at, the code.
    /// </summary>
    public static IReadOnlyList<(string Domain, string SubjectType, string Action, string WorkflowCode)> Starts =>
        PolicyDomainCatalog.Governed
            .Where(d => d.OwnerClientId is not null)
            .SelectMany(d => d.WorkflowTriggers.SelectMany(kv => kv.Value
                .Where(t => t.StartedBy == PolicyWorkflowStartPath.InternalEndpoint)
                .Select(t => (d.Domain, SubjectType: kv.Key, t.Action, t.WorkflowCode))))
            .ToList();

    /// <summary>Every triple the internal endpoint may start, whatever the action.</summary>
    public static IReadOnlyList<(string Domain, string SubjectType, string WorkflowCode)> Triples =>
        Starts.Select(s => (s.Domain, s.SubjectType, s.WorkflowCode)).Distinct().ToList();

    /// <summary>Whether the endpoint may ever start the code for the entity type (at some action).</summary>
    public static bool Allows(string domain, string subjectType, string? workflowCode) =>
        workflowCode is not null && Triples.Contains((domain, subjectType, workflowCode));

    /// <summary>
    /// What the engine asks for a service's request (the second review of PLAN-49): the code starts only at the action the
    /// catalog gives it, on this path as inside HR.Core. A request for another action of the type starts nothing, whatever a
    /// rule in the data says.
    /// </summary>
    public static bool Allows(string domain, string subjectType, string? action, string? workflowCode) =>
        action is not null && workflowCode is not null && Starts.Contains((domain, subjectType, action, workflowCode));
}
