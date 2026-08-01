using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// Records how rows in a target table are mapped from the default tenant to a real agency (TEN-003).
/// </summary>
public class TenantAssignmentRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(128)]
    public string TargetTable { get; set; } = string.Empty;

    /// <summary>OrgUnit, Explicit, or Quarantine.</summary>
    [Required]
    [StringLength(64)]
    public string DiscoveryMethod { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? SqlCondition { get; set; }

    public Guid? TargetTenantId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
