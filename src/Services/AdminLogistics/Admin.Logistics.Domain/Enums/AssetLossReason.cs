using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum AssetLossReason
{
    [Display(Name = "سرقة")]
    Theft,

    [Display(Name = "ضياع")]
    Misplacement,

    [Display(Name = "تلف كلي")]
    DamagedBeyondRepair,

    [Display(Name = "نقص مخزني")]
    InventoryShortage,

    [Display(Name = "أخرى")]
    Other,
}
