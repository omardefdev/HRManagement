using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

public enum Severity
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