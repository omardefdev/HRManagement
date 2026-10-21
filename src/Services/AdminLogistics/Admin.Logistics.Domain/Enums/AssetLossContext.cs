using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum AssetLossContext
{
    [Display(Name = "مع موظف (عهدة مفتوحة)")]
    WithOpenAssignment,

    [Display(Name = "في المخزن")]
    InStorage,

    [Display(Name = "أثناء صيانة")]
    InMaintenance,
}
