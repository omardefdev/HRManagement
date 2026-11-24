using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>
/// PLAN-54 (D-PAY-6): whether an allowance, a deduction or an adjustment counts in a calculation. Only <see cref="Approved"/> does.
/// The first member is the default: every row that existed before PLAN-54, and every row the system writes, is approved.
/// </summary>
public enum ManualInputApprovalStatus
{
    [Display(Name = "معتمد")]
    Approved = 0,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 1,
    [Display(Name = "مرفوض")]
    Rejected = 2
}

/// <summary>PLAN-54: who wrote the input: the system (an event consumer, a seeder, a row older than PLAN-54) or a person by hand.</summary>
public enum ManualInputOrigin
{
    [Display(Name = "آلي")]
    System = 0,
    [Display(Name = "يدوي")]
    Manual = 1
}
