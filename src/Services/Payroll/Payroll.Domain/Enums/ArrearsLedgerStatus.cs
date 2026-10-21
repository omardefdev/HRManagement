using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum ArrearsLedgerStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "مدفوع جزئياً")]
    PartiallyPaid = 1,
    [Display(Name = "مدفوع بالكامل")]
    FullyPaid = 2
}
