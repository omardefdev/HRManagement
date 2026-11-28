using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

/// <summary>
/// The clearance items of a separation, stored as text. PLAN-60 adds four at the end (never in the middle: the stored text keeps every
/// older value).
/// </summary>
public enum SeparationStep
{
    [Display(Name = "إنهاء العقد")]
    ContractTerminated,
    [Display(Name = "تسوية الراتب")]
    PayrollSettled,
    [Display(Name = "إلغاء البطاقة")]
    CardRevoked,
    [Display(Name = "تعطيل الحساب")]
    AccountDisabled,
    [Display(Name = "إرجاع العهدة")]
    AssetsReturned,
    [Display(Name = "مؤرشف")]
    Archived,
    [Display(Name = "إصدار قرار إنهاء الخدمة")]
    DecisionIssued,
    [Display(Name = "تبليغ الموظف")]
    EmployeeNotified,
    [Display(Name = "سحب صلاحيات الأنظمة التقنية")]
    ItAccessRevoked,
    [Display(Name = "تحديث السجل المركزي")]
    HrCoreUpdated
}
