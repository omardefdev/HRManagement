namespace HRManagement.MessageContracts.Events;

public interface IAdvanceRequested
{
    Guid RequestId { get; }
    Guid EmployeeId { get; }
    decimal Amount { get; }
    string? Reason { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }
    Guid? OrgUnitId => null;

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
