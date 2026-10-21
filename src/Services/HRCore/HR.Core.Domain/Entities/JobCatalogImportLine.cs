using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-006: parsed row from job catalog CSV import.</summary>
public class JobCatalogImportLine : AuditableEntity
{
    public Guid BatchId { get; set; }
    public virtual JobCatalogImportBatch? Batch { get; set; }

    public int LineNumber { get; set; }

    [Required]
    [StringLength(200)]
    public string TitleAr { get; set; } = string.Empty;

    [StringLength(200)]
    public string? TitleEn { get; set; }

    [StringLength(2000)]
    public string? DescriptionAr { get; set; }

    [Required]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string JobClassificationCode { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [StringLength(20)]
    public string? LegacyJobCode { get; set; }

    [StringLength(500)]
    public string? ValidationError { get; set; }
}
