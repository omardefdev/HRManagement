namespace HRManagement.MessageContracts.Events;

public interface IComplaintResolved
{
    Guid ComplaintId { get; }
    string Subject { get; }
    string Resolution { get; }
    DateTime ResolvedAt { get; }
    Guid? CorrelationId { get; }
    string SourceSystem { get; }

    /// <summary>Event contract version; bump when adding required fields (consumers use EventSchemaVersion).</summary>
    Guid? TenantId => null;

    int SchemaVersion => 1;
}
