using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

public enum DeviceType
{
    [Display(Name = "حاسوب محمول")]
    Laptop,
    [Display(Name = "حاسوب مكتبي")]
    Desktop,
    [Display(Name = "هاتف")]
    Mobile,
}