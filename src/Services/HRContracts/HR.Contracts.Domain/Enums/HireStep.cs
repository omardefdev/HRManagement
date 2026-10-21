using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

/// <summary>خطوات معاملة التعيين (DFD Level 2 — موظف جديد).</summary>
public enum HireStep
{
    [Display(Name = "استلام المرشح")]
    CandidateReceived = 0,
    [Display(Name = "تصنيف العقد")]
    ContractClassified = 1,
    [Display(Name = "فتح الملف الشخصي")]
    PersonalFileOpened = 2,
    [Display(Name = "ربط الرواتب")]
    PayrollLinked = 3,
    [Display(Name = "اعتماد السجل الوظيفي")]
    CareerRecordApproved = 4,
    [Display(Name = "إصدار الهوية")]
    IdentityIssued = 5,
    [Display(Name = "أرشفة التعيين")]
    HireArchived = 6
}
