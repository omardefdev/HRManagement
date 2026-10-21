using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum PayrollRunStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "محسوب")]
    Calculated = 1,
    [Display(Name = "مرحّل")]
    Posted = 2,
    [Display(Name = "قيد المراجعة")]
    UnderReview = 3,
    [Display(Name = "معتمد")]
    Approved = 4,
    [Display(Name = "معكوس")]
    Reversed = 5,
    /// <summary>PAY-003: calculation in progress (resumable).</summary>
    [Display(Name = "جاري الحساب")]
    Calculating = 6,
    /// <summary>PAY-003: totals validation in progress.</summary>
    [Display(Name = "جاري التحقق")]
    Validating = 7,
    /// <summary>PAY-003: job cancelled before post.</summary>
    [Display(Name = "ملغى")]
    Cancelled = 8,

    /// <summary>Legacy alias for <see cref="Draft"/>.</summary>
    [Display(Name = "مسودة")]
    Pending = Draft,

    /// <summary>Legacy alias for <see cref="Calculated"/>.</summary>
    [Display(Name = "محسوب")]
    Processed = Calculated,
}
