using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum AssetStatus
{
    [Display(Name = "قيد الاستخدام")]
    InUse,
    [Display(Name = "في المخزن")]
    InStorage,
    [Display(Name = "صيانة")]
    Maintenance,
    [Display(Name = "مُستَبعَد")]
    Disposed,
    [Display(Name = "مفقود")]
    Lost,
}
