using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// Agency/ministry boundary (TEN-001). Org units and branches live under a tenant.
/// </summary>
public class Tenant
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string NameAr { get; set; } = string.Empty;

    [StringLength(200)]
    public string? NameEn { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
