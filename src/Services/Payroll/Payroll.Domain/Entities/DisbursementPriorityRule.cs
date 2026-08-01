using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>PAY-004: legally sourced priority rule for partial disbursement.</summary>
[Display(Name = "قاعدة أولوية الصرف")]
public class DisbursementPriorityRule : BaseEntity
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
}
