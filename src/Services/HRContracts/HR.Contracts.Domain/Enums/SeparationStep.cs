using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

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
    Archived
}
