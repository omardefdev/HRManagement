using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum FindingSeverity
{
    [Display(Name = "منخفض")]
    Low,
    [Display(Name = "متوسط")]
    Medium,
    [Display(Name = "عالي")]
    High,
    [Display(Name = "حرج")]
    Critical,
}