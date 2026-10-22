using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-028: the committee's recommendation for one candidate.</summary>
public enum CommitteeRecommendation
{
    [Display(Name = "توصية بالترشيح")]
    Recommend = 0,
    [Display(Name = "احتياط")]
    Reserve = 1,
    [Display(Name = "رفض")]
    Reject = 2
}
