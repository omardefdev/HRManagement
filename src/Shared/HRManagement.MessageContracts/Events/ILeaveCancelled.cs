namespace HRManagement.MessageContracts.Events;

public interface ILeaveCancelled
{
    Guid TransactionId { get; }
    Guid LeaveRequestId { get; }
    Guid EmployeeId { get; }
    Guid CancelledBy { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }
    string LeaveType { get; }
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    int DurationDays { get; }

    Guid? TenantId => null;

    int SchemaVersion => 1;
}
