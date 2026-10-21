using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// EST-001: A funded position in the establishment table.
/// Each position is tied to a budget year, org unit, and job catalog entry.
/// Status tracks whether the position is Occupied, Vacant, or Frozen.
/// </summary>
public class FundedPosition : BaseEntity
{
    [Required]
    [StringLength(30)]
    public string PositionCode { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    /// <summary>Full-time equivalent capacity (1.0 = full position).</summary>
    public decimal FTE { get; set; } = 1.0m;

    public int BudgetYear { get; set; }

    public FundedPositionStatus Status { get; set; } = FundedPositionStatus.Vacant;

    public Guid OrgUnitId { get; set; }
    public virtual OrgUnit? OrgUnit { get; set; }

    public Guid? JobCatalogEntryId { get; set; }
    public virtual JobCatalogEntry? JobCatalogEntry { get; set; }

    /// <summary>The employee currently occupying this position (null if vacant/frozen).</summary>
    public Guid? OccupantEmployeeId { get; set; }
    public virtual Employee? OccupantEmployee { get; set; }

    [StringLength(50)]
    public string? CostCenter { get; set; }

    [StringLength(500)]
    public string? FreezeReason { get; set; }

    public DateTime? FreezeDate { get; set; }
}

public enum FundedPositionStatus
{
    [Display(Name = "شاغر")]
    Vacant,
    [Display(Name = "مشغول")]
    Occupied,
    [Display(Name = "مجمد")]
    Frozen
}
