using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Entities;

public abstract class BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Agency/ministry boundary (TEN-002: required for tenant-owned rows).</summary>
    public Guid TenantId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
