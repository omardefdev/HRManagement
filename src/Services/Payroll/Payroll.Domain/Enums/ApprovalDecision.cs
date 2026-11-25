using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum ApprovalDecision
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "مرفوض")]
    Rejected = 2,

    /// <summary>PLAN-56 (D-PAY-18): the run was cancelled before its posting; the row carries who cancelled it and why.</summary>
    [Display(Name = "ملغى")]
    Cancelled = 3,
}
