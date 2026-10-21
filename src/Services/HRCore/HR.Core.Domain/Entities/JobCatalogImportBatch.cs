using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-006: governed bulk job catalog CSV import batch.</summary>
public class JobCatalogImportBatch : AuditableEntity
{
    [Required]
    [StringLength(260)]
    [Display(Name = "اسم الملف")]
    public string SourceFileName { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public JobCatalogImportStatus Status { get; set; } = JobCatalogImportStatus.Pending;

    public JobCatalogImportMode ImportMode { get; set; } = JobCatalogImportMode.Standard;

    public int RowCount { get; set; }

    [StringLength(2000)]
    public string? ErrorSummary { get; set; }

    public Guid? ImportedBy { get; set; }

    public Guid? EstablishmentChangeRequestId { get; set; }

    public virtual EstablishmentChangeRequest? EstablishmentChangeRequest { get; set; }

    public virtual ICollection<JobCatalogImportLine> Lines { get; set; } = new List<JobCatalogImportLine>();
}
