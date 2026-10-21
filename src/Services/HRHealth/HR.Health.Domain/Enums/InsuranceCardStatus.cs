using System.ComponentModel.DataAnnotations;

namespace HR.Health.Domain.Enums;

public enum InsuranceCardStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "نشطة")]
    Active = 1,
    [Display(Name = "موقوف")]
    Suspended = 2,
    [Display(Name = "منتهي الصلاحية")]
    Expired = 3,
    [Display(Name = "ملغى")]
    Revoked = 4
}
