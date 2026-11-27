using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum ArrearsLedgerStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "مدفوع جزئياً")]
    PartiallyPaid = 1,
    [Display(Name = "مدفوع بالكامل")]
    FullyPaid = 2,

    /// <summary>
    /// PLAN-58 (P-5): the run was reversed while its funded batch had not left. Nothing of the arrears was paid and nothing is owed under
    /// that run any more: the row is closed, with the reversal as its reason. Stored by name like the other states.
    /// </summary>
    [Display(Name = "ملغى بالعكس")]
    Voided = 3
}
