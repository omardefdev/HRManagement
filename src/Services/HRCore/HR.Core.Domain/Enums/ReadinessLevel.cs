using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum ReadinessLevel
{
    [Display(Name = "يحتاج تطويراً")]
    NeedsDevelopment = 0,

    [Display(Name = "جاهز خلال سنة")]
    ReadyWithinYear = 1,

    [Display(Name = "جاهز الآن")]
    ReadyNow = 2
}
