using System.ComponentModel.DataAnnotations;

namespace HR.Health.Domain.Enums;

public enum AccidentSeverity
{
    [Display(Name = "طفيفة")]
    Minor = 0,

    [Display(Name = "متوسطة")]
    Moderate = 1,

    [Display(Name = "بليغة")]
    Severe = 2,

    [Display(Name = "مميتة")]
    Fatal = 3
}
