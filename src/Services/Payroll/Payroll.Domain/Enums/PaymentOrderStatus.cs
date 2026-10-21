using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>SYR-011 per-beneficiary payment order lifecycle.</summary>
public enum PaymentOrderStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "مُرسَل")]
    Submitted = 1,
    [Display(Name = "مُقرّ")]
    Acknowledged = 2,
    [Display(Name = "مرفوض")]
    Rejected = 3,
    [Display(Name = "مسدّد")]
    Settled = 4,
    [Display(Name = "مسدّد جزئياً")]
    PartiallySettled = 5
}
