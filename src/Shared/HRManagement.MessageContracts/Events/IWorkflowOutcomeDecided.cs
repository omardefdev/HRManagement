namespace HRManagement.MessageContracts.Events;

/// <summary>
/// PLAN-46 (G-WF-01): the outcome of an approval another service owns. HR.Core publishes it through its outbox, in the transaction of the
/// decision, so delivery is at least once. The owner applies it only to the record linked to <see cref="InstanceId"/> while that record is
/// pending, with <c>WorkflowOutcomeGuard</c>.
/// </summary>
public interface IWorkflowOutcomeDecided
{
    Guid? TenantId { get; }

    string EntityType { get; }

    Guid EntityId { get; }

    Guid InstanceId { get; }

    string WorkflowCode { get; }

    /// <summary>Approved, Rejected or Cancelled.</summary>
    string Outcome { get; }

    /// <summary>Who took the last step, or who withdrew.</summary>
    Guid? DecidedByUserId { get; }

    DateTime DecidedAt { get; }

    /// <summary>The reason of a rejection or a withdrawal.</summary>
    string? Reason { get; }

    IReadOnlyList<WorkflowStepDecisionDto> Decisions { get; }

    /// <summary>The idempotency key that started the instance.</summary>
    string? CorrelationId { get; }

    int SchemaVersion => 1;
}
