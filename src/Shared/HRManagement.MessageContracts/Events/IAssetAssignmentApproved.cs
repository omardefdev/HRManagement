namespace HRManagement.MessageContracts.Events;

public interface IAssetAssignmentApproved
{
    Guid AssignmentId { get; }
    Guid AssetId { get; }
    Guid EmployeeId { get; }
    DateTime OccurredAt { get; }
    string SourceSystem { get; }
    Guid? TenantId => null;
    int SchemaVersion => 1;
}
