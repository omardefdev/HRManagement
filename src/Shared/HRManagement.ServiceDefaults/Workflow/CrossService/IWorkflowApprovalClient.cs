namespace HRManagement.ServiceDefaults.Workflow.CrossService;

/// <summary>
/// PLAN-46 (G-WF-01): how a service asks HR.Core's engine to approve a decision it owns. Nothing here throws except on the caller's own
/// cancellation: a failure is an answer. Every call runs under the record's tenant (<see cref="TenantIdFlow"/>); a call with no tenant is
/// refused here, because HR.Core would otherwise put the instance in the national tenant.
/// </summary>
public interface IWorkflowApprovalClient
{
    /// <summary>
    /// The same key answers with the same instance in any state. When it has finished, <see cref="WorkflowApprovalStartResult.AlreadyActive"/>
    /// is false, and the owner applies its outcome through <see cref="GetStatusAsync"/>.
    /// </summary>
    Task<WorkflowApprovalStartResult> StartAsync(WorkflowApprovalStartRequest request, CancellationToken ct);

    /// <summary>
    /// A finished instance answers <see cref="WorkflowApprovalCancelResult.AlreadyTerminal"/> with its outcome: the owner then applies that
    /// outcome and never returns the record to draft.
    /// </summary>
    Task<WorkflowApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct);

    /// <summary>Null when the instance is not this service's, or the engine did not answer.</summary>
    Task<WorkflowApprovalStatusDto?> GetStatusAsync(Guid instanceId, CancellationToken ct);
}
