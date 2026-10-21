using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-003: dated version of a funded position (as-of queries).</summary>
public class FundedPositionVersion : AuditableEntity
{
    public Guid FundedPositionId { get; set; }
    public virtual FundedPosition? FundedPosition { get; set; }

    [Display(Name = "صالح من")]
    public DateTime ValidFrom { get; set; }

    [Display(Name = "صالح حتى")]
    public DateTime? ValidTo { get; set; }

    [Required]
    [StringLength(30)]
    public string PositionCode { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    public decimal FTE { get; set; } = 1.0m;

    public int BudgetYear { get; set; }

    public FundedPositionStatus Status { get; set; }

    public Guid OrgUnitId { get; set; }

    public Guid? JobCatalogEntryId { get; set; }

    public Guid? OccupantEmployeeId { get; set; }

    [StringLength(50)]
    public string? CostCenter { get; set; }

    [StringLength(500)]
    public string? FreezeReason { get; set; }

    public EstablishmentOperationType OperationType { get; set; }

    public Guid? ChangeRequestId { get; set; }

    [StringLength(100)]
    public string? LegalSourceId { get; set; }

    [StringLength(100)]
    public string? DecisionReference { get; set; }
}
