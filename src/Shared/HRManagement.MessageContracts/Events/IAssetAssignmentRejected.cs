namespace HRManagement.MessageContracts.Events;

public interface IAssetAssignmentRejected
{
    Guid AssignmentId { get; }
    Guid AssetId { get; }
    string? Reason { get; }
    DateTime OccurredAt { get; }
    string SourceSystem { get; }
    Guid? TenantId => null;
    int SchemaVersion => 1;
}
