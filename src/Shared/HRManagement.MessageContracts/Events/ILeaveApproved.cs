namespace HRManagement.MessageContracts.Events;

public interface ILeaveApproved
{
    Guid TransactionId { get; }
    Guid LeaveRequestId { get; }
    Guid EmployeeId { get; }
    Guid ApprovedBy { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }
    string LeaveType { get; }
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    int DurationDays { get; }

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
