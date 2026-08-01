namespace HRManagement.MessageContracts.Events;

public interface IEmployeeStatusChanged
{
    Guid EmployeeId { get; }
    string OldStatus { get; }
    string NewStatus { get; }
    string Reason { get; }
    DateTime ChangedAt { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
