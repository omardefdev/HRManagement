using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>PAY-004: treasury funding decision for a payroll run (may be partial).</summary>
[Display(Name = "قرار التمويل")]
public class FundingDecision : AuditableEntity
{
    [Required]
    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    [Display(Name = "المبلغ المعتمد")]
    public decimal ApprovedAmount { get; set; }

    [Display(Name = "المبلغ المطلوب")]
    public decimal RequestedAmount { get; set; }

    /// <summary>Ratio of approved to requested funding in range [0, 1].</summary>
    [Display(Name = "نسبة التمويل")]
    public decimal FundingRatio { get; set; }

    [Display(Name = "مجموعة قواعد الأولوية")]
    public Guid? PriorityRuleSetId { get; set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime DecisionDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "المعتمد")]
    public Guid ApprovedBy { get; set; }

    [MaxLength(2000)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public PayrollRun PayrollRun { get; set; } = null!;
}
