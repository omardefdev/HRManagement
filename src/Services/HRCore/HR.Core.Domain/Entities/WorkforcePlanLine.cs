using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-004: planned headcount target line on a workforce plan.</summary>
public class WorkforcePlanLine : AuditableEntity
{
    public Guid WorkforcePlanId { get; set; }
    public virtual WorkforcePlan? WorkforcePlan { get; set; }

    [StringLength(20)]
    public string? GradeCode { get; set; }

    public Guid? JobCatalogEntryId { get; set; }
    public virtual JobCatalogEntry? JobCatalogEntry { get; set; }

    public int PlannedCount { get; set; }

    public decimal PlannedFTE { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
