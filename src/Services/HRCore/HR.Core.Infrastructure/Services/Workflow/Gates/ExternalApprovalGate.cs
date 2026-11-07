using HR.Core.Infrastructure.Services.Workflow.External;

namespace HR.Core.Infrastructure.Services.Workflow.Gates;

/// <summary>
/// PLAN-45 (G-WF-02): the entity rule of a type started from outside HR.Core, one gate per entry of <see cref="ExternalApprovalTypes"/>.
/// The entity lives in another service, so the unit is the one the instance recorded at the start. A branch-level actor acts only inside
/// it, and an instance with no unit is for central roles. The requester, the subject and the distinct approvers are
/// <c>WorkflowSodRules</c>, which the evaluator runs for every type before any gate.
/// </summary>
public sealed class ExternalApprovalGate(ExternalApprovalType type, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public string EntityType => type.EntityType;

    public Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default) =>
        scope.GetBlockReasonAsync(context.Actor, context.Instance.ScopeOrgUnitId, ct);
}
