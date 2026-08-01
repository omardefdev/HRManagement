namespace HRManagement.MessageContracts.Events;

public interface IMissionApproved
{
    Guid MissionFileId { get; }
    Guid EmployeeId { get; }
    string Destination { get; }
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    Guid CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
