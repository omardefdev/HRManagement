using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// Ambiguous or unresolvable rows held pending human decision during tenant migration (TEN-003).
/// </summary>
public class TenantQuarantineEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(64)]
    public string ServiceName { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string TableName { get; set; } = string.Empty;

    public Guid RecordId { get; set; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Resolution { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public Guid? AssignedTenantId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
}
