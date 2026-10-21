using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-003: dated version of a job catalog entry (as-of queries).</summary>
public class JobCatalogVersion : AuditableEntity
{
    public Guid JobCatalogEntryId { get; set; }
    public virtual JobCatalogEntry? JobCatalogEntry { get; set; }

    [Display(Name = "صالح من")]
    public DateTime ValidFrom { get; set; }

    [Display(Name = "صالح حتى")]
    public DateTime? ValidTo { get; set; }

    [Required]
    [StringLength(20)]
    public string JobCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string TitleAr { get; set; } = string.Empty;

    [StringLength(200)]
    public string? TitleEn { get; set; }

    [Required]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? OccupationalGroup { get; set; }

    public bool IsActive { get; set; } = true;

    public EstablishmentOperationType OperationType { get; set; }

    public Guid? ChangeRequestId { get; set; }

    [StringLength(100)]
    public string? LegalSourceId { get; set; }

    [StringLength(100)]
    public string? DecisionReference { get; set; }
}
