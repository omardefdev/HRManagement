using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-004: file-based finance budget import batch (stub — no live ERP).</summary>
public class FinanceBudgetImportBatch : AuditableEntity
{
    [Required]
    [StringLength(260)]
    public string SourceFileName { get; set; } = string.Empty;

    public int FiscalYear { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public FinanceImportStatus Status { get; set; } = FinanceImportStatus.Pending;

    public int RowCount { get; set; }

    [StringLength(2000)]
    public string? ErrorSummary { get; set; }

    public Guid? ImportedBy { get; set; }

    public virtual ICollection<FinanceBudgetLine> Lines { get; set; } = new List<FinanceBudgetLine>();
}
