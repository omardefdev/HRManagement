using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-028: what a selection committee session decides on.</summary>
public enum CommitteePurpose
{
    [Display(Name = "مقابلة")]
    Interview = 0,
    [Display(Name = "ترتيب المرشحين")]
    Ranking = 1,
    [Display(Name = "قرار نهائي")]
    Final = 2
}
