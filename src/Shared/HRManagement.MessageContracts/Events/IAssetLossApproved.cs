namespace HRManagement.MessageContracts.Events;

public interface IAssetLossApproved
{
    Guid LossReportId { get; }
    Guid AssetId { get; }
    DateTime OccurredAt { get; }
    string SourceSystem { get; }
    Guid? TenantId => null;
    /// <summary>PLAN-43: the instance that decided this outcome; the consumer applies it only to the record linked to it.</summary>
    Guid? WorkflowInstanceId => null;
    int SchemaVersion => 2;
}
