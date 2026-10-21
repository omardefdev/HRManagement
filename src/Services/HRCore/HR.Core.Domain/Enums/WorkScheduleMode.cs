using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum WorkScheduleMode
{
    [Display(Name = "دوام كامل")]
    FullTime = 0,
    [Display(Name = "دوام جزئي")]
    PartTime = 1,
    [Display(Name = "مرن")]
    Flexible = 2,
    [Display(Name = "عن بُعد")]
    Remote = 3
}
