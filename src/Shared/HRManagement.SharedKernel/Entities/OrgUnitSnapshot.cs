namespace HRManagement.SharedKernel.Entities;

/// <summary>
/// Local replica of the HR.Core org-unit tree for branch-scope filtering in satellite services.
/// TenantId isolates trees per agency (TEN-001/002).
/// </summary>
public class OrgUnitSnapshot
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ParentId { get; set; }
    public string MaterializedPath { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
}
