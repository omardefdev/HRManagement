using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum AssetHandoverCondition
{
    [Display(Name = "ممتازة")]
    Excellent,

    [Display(Name = "جيدة")]
    Good,

    [Display(Name = "مقبولة")]
    Acceptable,

    [Display(Name = "تحتاج صيانة")]
    NeedsMaintenance,

    [Display(Name = "تالفة جزئياً")]
    PartiallyDamaged,

    [Display(Name = "تالفة")]
    Damaged,
}
