using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum FindingStatus
{
    [Display(Name = "مفتوح")]
    Open,
    [Display(Name = "مُخفَّف")]
    Mitigated,
    [Display(Name = "مغلق")]
    Closed,
}