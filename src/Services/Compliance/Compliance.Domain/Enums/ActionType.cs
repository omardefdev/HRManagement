using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum ActionType
{
    [Display(Name = "إنذار")]
    Warning,
    [Display(Name = "إيقاف")]
    Suspension,
    [Display(Name = "فصل")]
    Dismissal,
    [Display(Name = "أخرى")]
    Other,
}
