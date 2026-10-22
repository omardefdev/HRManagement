using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-028 (H3): the independent reviewer's explicit appeal outcome (stored as string).</summary>
public enum RecruitmentAppealOutcome
{
    [Display(Name = "قبول — إعادة التقييم")]
    Upheld = 0,
    [Display(Name = "قبول — تصحيح الترتيب")]
    UpheldRankCorrection = 1,
    [Display(Name = "رفض الاستئناف")]
    Dismissed = 2,
    [Display(Name = "قبول دون أثر على الشاغر")]
    UpheldNoRemedy = 3,
    /// <summary>History of the removed direct-reopen path (migration HCM028_AppealGovernance); never chosen.</summary>
    [Display(Name = "مسار قديم — إعادة فتح مباشرة")]
    LegacyReopen = 4
}
