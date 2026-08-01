namespace HRManagement.MessageContracts.Events;

public interface IJobUpdateApplied
{
    Guid TransactionId { get; }
    Guid DecisionId { get; }
    Guid EmployeeId { get; }
    string DecisionNumber { get; }
    string Subject { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
