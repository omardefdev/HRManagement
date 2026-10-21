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
}
