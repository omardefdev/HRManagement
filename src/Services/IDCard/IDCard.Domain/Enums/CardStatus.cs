using System.ComponentModel.DataAnnotations;

namespace IDCard.Domain.Enums;

public enum CardStatus
{
    [Display(Name = "نشطة")]
    Active,
    [Display(Name = "مفقود")]
    Lost,
    [Display(Name = "منتهي الصلاحية")]
    Expired,
    [Display(Name = "ملغى")]
    Revoked,
}
