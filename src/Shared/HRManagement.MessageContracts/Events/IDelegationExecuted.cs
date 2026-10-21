namespace HRManagement.MessageContracts.Events;

public interface IDelegationExecuted
{
    Guid DelegationRequestId { get; }
    Guid EmployeeId { get; }
    string HostOrg { get; }
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    Guid CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;

    int SchemaVersion => 1;
}
