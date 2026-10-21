using System.ComponentModel.DataAnnotations;

namespace IDCard.Domain.Enums;

public enum CardChecklistStep
{
    [Display(Name = "مطلوب")]
    Requested = 0,
    [Display(Name = "تصميم")]
    Designed = 1,
    [Display(Name = "صادرة")]
    Issued = 2,
    [Display(Name = "مطبوعة")]
    Printed = 3,
    [Display(Name = "مُسلَّم")]
    Delivered = 4,
    [Display(Name = "ملغى")]
    Revoked = 5
}
