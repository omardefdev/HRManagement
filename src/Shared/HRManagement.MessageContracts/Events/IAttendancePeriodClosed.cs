namespace HRManagement.MessageContracts.Events;

/// <summary>Published when a monthly attendance period is closed for an employee.</summary>
public interface IAttendancePeriodClosed
{
    Guid TransactionId { get; }
    Guid EmployeeId { get; }
    DateOnly PeriodStart { get; }
    DateOnly PeriodEnd { get; }
    int PresentDays { get; }
    int AbsentDays { get; }
    int OnLeaveDays { get; }
    int RemoteDays { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
