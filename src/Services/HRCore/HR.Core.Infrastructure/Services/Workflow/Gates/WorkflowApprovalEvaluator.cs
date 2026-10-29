using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.Extensions.Caching.Memory;

namespace HR.Core.Infrastructure.Services.Workflow.Gates;

/// <summary>
/// PLAN-25 (G-04, decision 5): the single decision for "may this actor approve / reject the current step" — the stage guard
/// (status, no skipping, initiator on later steps, step role) and then the entity gate. The server
/// (<see cref="WorkflowInstanceActionService"/>) and the detail panels both ask it.
/// </summary>
public sealed class WorkflowApprovalEvaluator(IEnumerable<IWorkflowApprovalGate> gates, WorkflowStepRoleOptions options)
{
    private readonly IReadOnlyList<IWorkflowApprovalGate> _gates = gates.ToList();

    public WorkflowStepRoleOptions Options => options;

    public async Task<string?> GetBlockReasonAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowActionActor actor,
        WorkflowActionKind kind,
        int? targetStepLevel = null,
        CancellationToken ct = default)
    {
        var snapshot = WorkflowDefinitionMapper.ToSnapshot(definition);
        var state = WorkflowDefinitionMapper.ToState(instance);

        // Order: structure (status, no skipping, step exists) → step role → entity gate → initiator rule (approve only), so
        // each refusal names the most specific rule: a user without the role sees the role, a creator sees the SoD rule.
        var structureError = WorkflowStageGuard.ValidateStructure(
            state, snapshot, kind == WorkflowActionKind.Approve ? targetStepLevel : null);
        if (structureError is not null)
            return structureError;

        var step = snapshot.Steps.First(s => s.Level == instance.CurrentStepLevel);
        var roleError = WorkflowStageGuard.ValidateStepRole(step, instance.EntityType, actor.Roles, options);
        if (roleError is not null)
            return roleError;

        var gate = _gates.FirstOrDefault(g => string.Equals(g.EntityType, instance.EntityType, StringComparison.OrdinalIgnoreCase));
        if (gate is not null)
        {
            var gateError = await gate.GetBlockReasonAsync(new WorkflowGateContext(instance, definition, step, actor, kind), ct);
            if (gateError is not null)
                return gateError;
        }

        return kind == WorkflowActionKind.Approve
            ? WorkflowStageGuard.ValidateInitiator(state, actor.UserId, instance.InitiatedBy)
            : null;
    }

    /// <summary>Every recruitment and establishment gate over one context (DI registers the same set).</summary>
    public static IReadOnlyList<IWorkflowApprovalGate> CreateGates(HRCoreDbContext db, WorkflowGateOrgScope scope) =>
    [
        new RequisitionApprovalGate(db, scope),
        new OfferApprovalGate(db, scope),
        new CommitteeApprovalGate(db, scope),
        new AppealApprovalGate(db),
        new CompetitionApprovalGate(db, scope),
        new ResultsApprovalGate(db, scope),
        new EstablishmentChangeApprovalGate(db, scope)
    ];

    /// <summary>For manual constructions (tests): default options (no admin bypass, recruitment + establishment enforced).</summary>
    public static WorkflowApprovalEvaluator CreateDefault(HRCoreDbContext db, WorkflowStepRoleOptions? options = null) =>
        new(CreateGates(db, new WorkflowGateOrgScope(
                new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()))))),
            options ?? new WorkflowStepRoleOptions());
}
