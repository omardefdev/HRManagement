using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>PLAN-56 (G-PAY-06): where a request to reverse a posted run stands. A decision is final.</summary>
public enum PayrollReversalRequestStatus
{
    [Display(Name = "بانتظار القرار")]
    Pending = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "مرفوض")]
    Rejected = 2,
}
