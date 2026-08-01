namespace HRManagement.MessageContracts.Events;

public interface IOrgUnitChanged
{
    Guid OrgUnitId { get; }
    Guid? TenantId { get; }
    Guid? ParentId { get; }
    string Code { get; }
    string NameAr { get; }
    string MaterializedPath { get; }
    bool IsDeleted { get; }

    int SchemaVersion => 2;
}
