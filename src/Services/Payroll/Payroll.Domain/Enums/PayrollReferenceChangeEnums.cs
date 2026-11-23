using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>PLAN-53 (D-53-4): the reference records that change by a request another person approves.</summary>
public enum PayrollReferenceEntityType
{
    [Display(Name = "جدول الرواتب")]
    SalaryScale = 0,
    [Display(Name = "الدرجة الوظيفية")]
    PayGrade = 1,
}

public enum PayrollReferenceOperation
{
    [Display(Name = "إنشاء")]
    Create = 0,
    [Display(Name = "تعديل")]
    Update = 1,
    [Display(Name = "حذف")]
    Delete = 2,
}

public enum PayrollReferenceChangeState
{
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 0,
    [Display(Name = "مطبَّق")]
    Applied = 1,
    [Display(Name = "مرفوض")]
    Rejected = 2,
    [Display(Name = "ملغى")]
    Cancelled = 3,
}
