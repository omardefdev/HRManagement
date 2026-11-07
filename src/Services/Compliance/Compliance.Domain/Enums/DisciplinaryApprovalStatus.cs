using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

/// <summary>PLAN-46 (D-WF-1, D-ER-2): a disciplinary action reaches payroll only once it is Approved in HR.Core's engine.</summary>
public enum DisciplinaryApprovalStatus
{
    [Display(Name = "مسودة")]
    Draft,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مرفوض")]
    Rejected
}
