using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-004: imported budget line keyed by cost center.</summary>
public class FinanceBudgetLine : AuditableEntity
{
    public Guid BatchId { get; set; }
    public virtual FinanceBudgetImportBatch? Batch { get; set; }

    [Required]
    [StringLength(50)]
    public string CostCenter { get; set; } = string.Empty;

    [StringLength(20)]
    public string? GradeCode { get; set; }

    public int ApprovedCount { get; set; }

    public decimal ApprovedFTE { get; set; }

    public decimal? BudgetAmount { get; set; }

    [StringLength(100)]
    public string? ExternalReference { get; set; }
}
