using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

public enum DeviceStatus
{
    [Display(Name = "قيد الاستخدام")]
    InUse,
    [Display(Name = "قيد الإصلاح")]
    InRepair,
    [Display(Name = "متقاعد")]
    Retired,
}