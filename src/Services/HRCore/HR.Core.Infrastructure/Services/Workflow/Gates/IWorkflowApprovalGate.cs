using HR.Core.Domain.Entities.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Infrastructure.Services.Workflow.Gates;

public enum WorkflowActionKind
{
    Approve,
    Reject
}

/// <summary>What a gate sees: the instance, its pinned definition, the current step, the actor and the action.</summary>
public sealed record WorkflowGateContext(
    WorkflowInstance Instance,
    WorkflowDefinition Definition,
    WorkflowStepSnapshot Step,
    WorkflowActionActor Actor,
    WorkflowActionKind Kind);

/// <summary>
/// PLAN-25 (G-04, decision 5): the entity rules of one workflow entity type (SoD, quorum, membership, assigned reviewer,
/// distinct approvers, org scope). Used by <see cref="WorkflowInstanceActionService"/> on approve and reject and by the
/// detail panels, so the UI never offers what the server refuses. Null = allowed.
/// </summary>
public interface IWorkflowApprovalGate
{
    string EntityType { get; }

    Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default);
}

/// <summary>IAM-002 org scope for approval gates (same messages as the work inbox).</summary>
public sealed class WorkflowGateOrgScope(OrgScopeService orgScope)
{
    public const string CentralOnlyMessage = "هذا الاعتماد للموارد البشرية المركزية فقط.";
    public const string OutOfBranchMessage = "هذا الطلب خارج نطاق فرعك.";
    public const string DeniedMessage = "لا يوجد نطاق تنظيمي مسموح.";

    /// <summary>Null when the actor is unrestricted or the org unit is inside their branch scope.</summary>
    public async Task<string?> GetBlockReasonAsync(WorkflowActionActor actor, Guid? entityOrgUnitId, CancellationToken ct = default)
    {
        var principal = actor.ToPrincipal();
        if (!HrOrgScopeVisibility.IsBranchScoped(principal))
            return null;

        return GetBlockReason(await orgScope.GetAllowedOrgUnitIdsAsync(principal, ct), entityOrgUnitId);
    }

    public static string? GetBlockReason(OrgAccessScope scope, Guid? entityOrgUnitId)
    {
        if (scope.IsDenied)
            return DeniedMessage;
        if (!scope.MustFilter)
            return null;
        if (entityOrgUnitId is null)
            return CentralOnlyMessage;
        return scope.CanAccess(entityOrgUnitId.Value) ? null : OutOfBranchMessage;
    }
}
