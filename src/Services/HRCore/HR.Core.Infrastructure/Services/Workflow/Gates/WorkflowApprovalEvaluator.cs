using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Core.Infrastructure.Services.Workflow.Gates;

/// <summary>
/// PLAN-50 (§6): the evaluator's answer. <see cref="OnBehalfOf"/> is the delegation through which the actor may act when he does not hold
/// the step's role himself; null for a direct actor (the direct role wins) and for a refusal.
/// </summary>
public sealed record WorkflowApprovalDecision(string? BlockReason, ActiveDelegation? OnBehalfOf)
{
    public bool Allowed => BlockReason is null;
}

/// <summary>
/// PLAN-25 (G-04, decision 5): the single decision for "may this actor approve / reject the current step" — the stage guard
/// (status, no skipping, initiator on later steps, step role) and then the entity gate. The server
/// (<see cref="WorkflowInstanceActionService"/>) and the detail panels both ask it. PLAN-50 (§6): when the actor lacks the step's role, a
/// delegation of that role covering the instance's type stands in for it, and then every identity rule after the role (the SoD rules,
/// the entity gate, the initiator rule) runs twice, as the delegate and as the principal (with the principal's stored scope); any refusal
/// wins.
/// </summary>
public sealed class WorkflowApprovalEvaluator(
    IEnumerable<IWorkflowApprovalGate> gates,
    WorkflowStepRoleOptions options,
    IApprovalDelegationLookup? delegationLookup = null,
    ILogger<WorkflowApprovalEvaluator>? logger = null)
{
    private readonly IReadOnlyList<IWorkflowApprovalGate> _gates = gates.ToList();

    public WorkflowStepRoleOptions Options => options;

    public async Task<string?> GetBlockReasonAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowActionActor actor,
        WorkflowActionKind kind,
        int? targetStepLevel = null,
        CancellationToken ct = default) =>
        (await GetDecisionAsync(instance, definition, actor, kind, targetStepLevel, ct)).BlockReason;

    public async Task<WorkflowApprovalDecision> GetDecisionAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowActionActor actor,
        WorkflowActionKind kind,
        int? targetStepLevel = null,
        CancellationToken ct = default)
    {
        // PLAN-43 (G-WF-05, rule 9): behind the route policies, so no future route can let a service account act.
        if (actor.IsServiceAccount)
            return Refused(WorkflowStageGuard.ServiceAccountMessage);

        var snapshot = WorkflowDefinitionMapper.ToSnapshot(definition);
        var state = WorkflowDefinitionMapper.ToState(instance);

        // Order: structure (status, no skipping, step exists) → step role (direct, then delegated) → SoD rules → entity gate → initiator rule
        // (approve only), so each refusal names the most specific rule: a user without the role sees the role, a creator sees the SoD rule.
        var structureError = WorkflowStageGuard.ValidateStructure(
            state, snapshot, kind == WorkflowActionKind.Approve ? targetStepLevel : null);
        if (structureError is not null)
            return Refused(structureError);

        var step = snapshot.Steps.First(s => s.Level == instance.CurrentStepLevel);
        ActiveDelegation? onBehalfOf = null;
        var roleError = WorkflowStageGuard.ValidateStepRole(step, instance.EntityType, actor.Roles, options);
        if (roleError is not null)
        {
            onBehalfOf = await DelegationForAsync(actor, step.RoleRequired, instance.EntityType, ct);
            if (onBehalfOf is null)
                return Refused(roleError);
        }

        // PLAN-50 (P-6): the delegate, then (on behalf) the principal with his stored scope.
        WorkflowActionActor[] identities = onBehalfOf is null ? [actor] : [actor, WorkflowActionActor.PrincipalOf(onBehalfOf)];

        // PLAN-43 (G-WF-06): for every type, after the role and before the entity's own rules; AdminBypass has no say here.
        var sodFacts = WorkflowDefinitionMapper.ToSodFacts(instance, definition);
        foreach (var identity in identities)
        {
            var sodError = WorkflowSodRules.Evaluate(sodFacts, identity.UserId, identity.EmployeeId, kind == WorkflowActionKind.Approve);
            if (sodError is not null)
                return Refused(sodError);
        }

        var gate = _gates.FirstOrDefault(g => string.Equals(g.EntityType, instance.EntityType, StringComparison.OrdinalIgnoreCase));
        if (gate is not null)
        {
            foreach (var identity in identities)
            {
                var gateError = await gate.GetBlockReasonAsync(
                    new WorkflowGateContext(instance, definition, step, identity, kind, options.Maker.IncludeSubmitter), ct);
                if (gateError is not null)
                    return Refused(gateError);
            }
        }

        if (kind == WorkflowActionKind.Approve)
        {
            foreach (var identity in identities)
            {
                var initiatorError = WorkflowStageGuard.ValidateInitiator(state, identity.UserId, instance.InitiatedBy);
                if (initiatorError is not null)
                    return Refused(initiatorError);
            }
        }

        return new WorkflowApprovalDecision(null, onBehalfOf);
    }

    /// <summary>
    /// A delegation of the step's role that covers the type, from the actor's loaded delegations or the lookup. Only for an enforced step
    /// with a role. Any failure of the lookup means none (fail-closed).
    /// </summary>
    private async Task<ActiveDelegation?> DelegationForAsync(
        WorkflowActionActor actor, string? stepRole, string? entityType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stepRole) || !options.IsEnforced(entityType))
            return null;

        var delegations = actor.Delegations;
        if (delegations is null)
        {
            if (delegationLookup is null || (actor.UserId is null && actor.EmployeeId is null))
                return null;
            try
            {
                delegations = await delegationLookup.GetActiveForAsync(actor.ToPrincipal(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (logger ?? NullLogger<WorkflowApprovalEvaluator>.Instance)
                    .LogWarning(ex, "approval delegation lookup failed; the step role decides alone");
                return null;
            }
        }

        return ApprovalDelegationMatcher.Match(delegations, stepRole, entityType, DateTime.UtcNow);
    }

    private static WorkflowApprovalDecision Refused(string reason) => new(reason, null);

    /// <summary>Every recruitment and establishment gate over one context (DI registers the same set).</summary>
    public static IReadOnlyList<IWorkflowApprovalGate> CreateGates(HRCoreDbContext db, WorkflowGateOrgScope scope) =>
    [
        .. ExternalApprovalTypes.All.Select(type => new ExternalApprovalGate(type, scope)),
        new RequisitionApprovalGate(db, scope),
        new OfferApprovalGate(db, scope),
        new CommitteeApprovalGate(db, scope),
        new AppealApprovalGate(db),
        new CompetitionApprovalGate(db, scope),
        new ResultsApprovalGate(db, scope),
        new EstablishmentChangeApprovalGate(db, scope)
    ];

    /// <summary>For manual constructions (tests): default options (no admin bypass, recruitment + establishment enforced).</summary>
    public static WorkflowApprovalEvaluator CreateDefault(
        HRCoreDbContext db, WorkflowStepRoleOptions? options = null, IApprovalDelegationLookup? delegationLookup = null) =>
        new(CreateGates(db, new WorkflowGateOrgScope(
                new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()))))),
            options ?? new WorkflowStepRoleOptions(),
            delegationLookup);
}
