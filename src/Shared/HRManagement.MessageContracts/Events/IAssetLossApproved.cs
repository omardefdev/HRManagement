namespace HRManagement.MessageContracts.Events;

public interface IAssetLossApproved
{
    Guid LossReportId { get; }
    Guid AssetId { get; }
    DateTime OccurredAt { get; }
    string SourceSystem { get; }
    Guid? TenantId => null;
    int SchemaVersion => 1;
}
