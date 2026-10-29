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
    LegacyReopen = 4,
    /// <summary>HCM-031 (PLAN-31, decision 5): result objection upheld — a corrected exam mark, then a new results version.</summary>
    [Display(Name = "قبول الاعتراض — تصحيح العلامة")]
    ObjectionUpheld = 5,
    /// <summary>HCM-031: result objection dismissed — no change.</summary>
    [Display(Name = "رفض الاعتراض")]
    ObjectionDismissed = 6
}

/// <summary>HCM-031 (PLAN-31): what an appeal is about (stored as string; existing rows are rejection appeals).</summary>
public enum RecruitmentAppealKind
{
    [Display(Name = "استئناف رفض")]
    Rejection = 0,
    [Display(Name = "اعتراض على نتيجة مسابقة")]
    ResultObjection = 1
}
