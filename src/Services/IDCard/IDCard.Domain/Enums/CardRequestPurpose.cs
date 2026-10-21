using System.ComponentModel.DataAnnotations;

namespace IDCard.Domain.Enums;

public enum CardRequestPurpose
{
    [Display(Name = "جديد")]
    New,
    [Display(Name = "بديل")]
    Replacement,
    [Display(Name = "تحديث")]
    Update,
}