using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>PLAN-53: what a salary change request changes. A bank account is not a kind here: it is its own maker-checker entity (PLAN-55).</summary>
public enum PayProfileChangeKind
{
    [Display(Name = "الراتب الأساسي")]
    BaseSalary = 0,
    [Display(Name = "الدرجة")]
    PayGrade = 1,
    [Display(Name = "الراتب الأساسي والدرجة")]
    BaseSalaryAndGrade = 2,
}

/// <summary>
/// PLAN-53 (D-PAY-6): where a salary change comes from. <see cref="Manual"/> (the form) and <see cref="Api"/> are requests a second person
/// approves; the others are administrative decisions recorded as applied at once (<c>ApplySystemChangeAsync</c>).
/// </summary>
public enum PayProfileChangeSource
{
    [Display(Name = "طلب يدوي")]
    Manual = 0,
    [Display(Name = "واجهة برمجية")]
    Api = 1,
    [Display(Name = "تفعيل عقد")]
    ContractActivated = 2,
    [Display(Name = "ترفيع")]
    Promotion = 3,
    [Display(Name = "نقل")]
    Transfer = 4,
    [Display(Name = "تصحيح")]
    Correction = 5,
}

/// <summary>PLAN-53: the states of a salary change request. Open = <see cref="PendingApproval"/> or <see cref="Scheduled"/> (D-53-3).</summary>
public enum PayProfileChangeState
{
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 0,
    [Display(Name = "مجدول")]
    Scheduled = 1,
    [Display(Name = "مطبَّق")]
    Applied = 2,
    [Display(Name = "مرفوض")]
    Rejected = 3,
    [Display(Name = "ملغى")]
    Cancelled = 4,
    [Display(Name = "أُبطل بتغيير آلي")]
    Superseded = 5,
}

/// <summary>PLAN-53: the decision of an approver on a request.</summary>
public enum PayProfileChangeDecision
{
    [Display(Name = "اعتماد")]
    Approved = 0,
    [Display(Name = "رفض")]
    Rejected = 1,
}
