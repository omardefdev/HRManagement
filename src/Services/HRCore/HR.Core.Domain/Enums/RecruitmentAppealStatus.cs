using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-006: recruitment appeal lifecycle (mirrors Compliance.AppealStatus).</summary>
public enum RecruitmentAppealStatus
{
    [Display(Name = "لا يوجد")]
    None = 0,
    [Display(Name = "معلق")]
    Pending = 1,
    [Display(Name = "مُبتَ فيه")]
    Decided = 2
}
