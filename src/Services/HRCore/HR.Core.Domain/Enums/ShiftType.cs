using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum ShiftType
{
    [Display(Name = "نهارية")]
    Day = 0,

    [Display(Name = "مسائية")]
    Evening = 1,

    [Display(Name = "ليلية")]
    Night = 2
}
