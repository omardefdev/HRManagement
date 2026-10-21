using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum CorrespondenceDirection
{
    [Display(Name = "وارد")]
    Inbound = 0,
    [Display(Name = "صادر")]
    Outbound = 1
}
