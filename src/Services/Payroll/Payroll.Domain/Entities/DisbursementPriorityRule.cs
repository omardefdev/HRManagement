using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>PAY-004: legally sourced priority rule for partial disbursement.</summary>
[Display(Name = "قاعدة أولوية الصرف")]
public class DisbursementPriorityRule : BaseEntity, ILegallySourcedValue
{
    [Required]
    [Display(Name = "مجموعة القواعد")]
    public Guid RuleSetId { get; set; }

    [Required]
    [MaxLength(100)]
    [Display(Name = "الفئة")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Lower value = higher priority (paid first).</summary>
    [Display(Name = "الأولوية")]
    public int Priority { get; set; }

    [Display(Name = "الحد الأدنى")]
    public decimal MinimumAmount { get; set; }

    [Required]
    [MaxLength(512)]
    [Display(Name = "المصدر القانوني")]
    public string LegalSource { get; set; } = string.Empty;

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// PLAN-54: the framework version the rule belongs to. A partial disbursement of a run pinned to a v2 version reads that version's
    /// rules only. Null for a general rule (older than PLAN-54), which only a legacy run reads.
    /// </summary>
    [Display(Name = "نسخة الإطار")]
    public Guid? PayrollConfigurationId { get; set; }

    public PayrollConfiguration? PayrollConfiguration { get; set; }

    [Display(Name = "الحالة القانونية")]
    public PayrollLegalValueStatus LegalStatus { get; set; } = PayrollLegalValueStatus.Interim;

    [MaxLength(LegalValueRules.InterimExceptionIdMaxLength)]
    [Display(Name = "الاستثناء المؤقت")]
    public string? InterimExceptionId { get; set; }

    /// <summary>PLAN-54: <see cref="LegalSource"/> is the rule's legal reference (the same meaning, so no second field).</summary>
    string? ILegallySourcedValue.LegalReference => LegalSource;

    public string DescribeAr() => $"قاعدة الأولوية {Category}";
}
