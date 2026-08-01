namespace HRManagement.SharedKernel.Entities;

public class EntityAuditLog : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty; // Created, Updated, Deleted
    public string? ChangedFields { get; set; } // JSON
    public Guid? UserId { get; set; }
}
