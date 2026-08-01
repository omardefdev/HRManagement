namespace HRManagement.MessageContracts.Events;

/// <summary>MDM-001: published when a national reference catalog entry is created, updated, or deactivated.</summary>
public interface IReferenceDataChanged
{
    Guid EntryId { get; }
    Guid? TenantId { get; }
    string CatalogKind { get; }
    string Code { get; }
    string NameAr { get; }
    bool IsActive { get; }
    string ChangeType { get; }

    int SchemaVersion => 1;
}
