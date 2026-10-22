using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-028: a committee member's vote on the decision for one candidate.</summary>
public enum CommitteeVoteValue
{
    [Display(Name = "موافق")]
    Agree = 0,
    [Display(Name = "غير موافق")]
    Disagree = 1,
    [Display(Name = "ممتنع")]
    Abstain = 2
}
