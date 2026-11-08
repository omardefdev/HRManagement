using HRManagement.MessageContracts.Events;

namespace HRManagement.ServiceDefaults.Workflow.CrossService;

/// <param name="AlreadyActive">The instance is running. False on a success means the key found a finished instance.</param>
public sealed record WorkflowApprovalStartResult(bool Success, Guid? InstanceId, bool AlreadyActive, string? ErrorCode, string? Error);

/// <param name="Success">The instance was withdrawn by this call. False when it had already finished.</param>
/// <param name="AlreadyTerminal">The instance finished first; <paramref name="FinalOutcome"/> says how.</param>
/// <param name="FinalOutcome">Approved, Rejected or Cancelled.</param>
public sealed record WorkflowApprovalCancelResult(bool Success, bool AlreadyTerminal, string? FinalOutcome, string? ErrorCode, string? Error);

/// <param name="Status">InProgress, Approved, Rejected or Cancelled.</param>
/// <param name="ApprovalUrl">The generic approval page: /ui/hr-core/WorkflowApprovals/Details/{id}.</param>
/// <param name="Reason">PLAN-46's addition to the contract: the reason of a rejection or a withdrawal, for the owner-side sweep.</param>
/// <param name="CorrelationId">The idempotency key that started the instance.</param>
public sealed record WorkflowApprovalStatusDto(
    Guid InstanceId,
    string EntityType,
    Guid EntityId,
    string WorkflowCode,
    int DefinitionVersion,
    string Status,
    int? CurrentStepLevel,
    string? CurrentStepName,
    string? CurrentStepRole,
    IReadOnlyList<WorkflowStepDecisionDto> Decisions,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string ApprovalUrl,
    string? Reason = null,
    string? CorrelationId = null);

/// <summary>The words of the contract, and the engine's statuses in those words.</summary>
public static class WorkflowApprovalOutcomes
{
    public const string InProgress = "InProgress";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public const string ActionApproved = "Approved";
    public const string ActionRejected = "Rejected";
    public const string ActionWithdrawn = "Withdrawn";

    public static bool IsFinal(string? outcome) => outcome is Approved or Rejected or Cancelled;

    /// <summary>The engine calls an approved instance Completed. Null for a status that is not final.</summary>
    public static string? FromEngineStatus(string? engineStatus) => engineStatus switch
    {
        "Completed" => Approved,
        Rejected => Rejected,
        Cancelled => Cancelled,
        _ => null
    };
}

/// <summary>
/// An outcome in the shape of the event, so the consumer and the sweep run the same <see cref="IWorkflowApprovalOwnerStore.ApplyOutcomeAsync"/>.
/// </summary>
public sealed record WorkflowOutcomeDecision(
    Guid? TenantId,
    string EntityType,
    Guid EntityId,
    Guid InstanceId,
    string WorkflowCode,
    string Outcome,
    Guid? DecidedByUserId,
    DateTime DecidedAt,
    string? Reason,
    IReadOnlyList<WorkflowStepDecisionDto> Decisions,
    string? CorrelationId) : IWorkflowOutcomeDecided
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>What a finished status says. The decider is whoever acted last.</summary>
    public static WorkflowOutcomeDecision From(WorkflowApprovalStatusDto status, Guid? tenantId)
    {
        var last = status.Decisions.Count == 0 ? null : status.Decisions[^1];
        return new WorkflowOutcomeDecision(
            tenantId,
            status.EntityType,
            status.EntityId,
            status.InstanceId,
            status.WorkflowCode,
            status.Status,
            last?.UserId,
            status.CompletedAt ?? last?.At ?? DateTime.UtcNow,
            status.Reason,
            status.Decisions,
            status.CorrelationId);
    }
}
