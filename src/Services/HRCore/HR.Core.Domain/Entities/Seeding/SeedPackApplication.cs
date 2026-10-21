using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities.Seeding;

[Display(Name = "تطبيق حزمة بذور")]
public class SeedPackApplication : BaseEntity
{
    [Required, MaxLength(100)]
    public string PackId { get; set; } = default!;

    [MaxLength(128)]
    public string? ReleaseDigest { get; set; }

    [MaxLength(200)]
    public string? RollbackRef { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}
