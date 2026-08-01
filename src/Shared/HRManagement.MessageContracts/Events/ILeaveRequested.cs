namespace HRManagement.MessageContracts.Events;

public interface ILeaveRequested
{
    Guid RequestId { get; }
    Guid EmployeeId { get; }
    string LeaveType { get; }
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    int DurationDays { get; }
    string? Notes { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }
    Guid? OrgUnitId => null;

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
