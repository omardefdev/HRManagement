using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-028 (G): why a recruitment application was rejected (stored as string).</summary>
public enum RecruitmentRejectionKind
{
    [Display(Name = "قرار الاختيار")]
    SelectionDecision = 0,
    [Display(Name = "إلغاء الشاغر")]
    RequisitionCancelled = 1,
    [Display(Name = "إغلاق الشاغر")]
    RequisitionClosed = 2,
    [Display(Name = "عدم الاختيار عند التعيين")]
    NotSelectedOnHire = 3,
    [Display(Name = "انتهاء صلاحية قائمة الاحتياط")]
    ReserveExpired = 4,
    [Display(Name = "إلغاء خطاب العرض")]
    OfferRevoked = 5,
    /// <summary>
    /// HCM-031 (PLAN-31): not successful in a competition (failed, absent, or passed with no line and no reserve).
    /// Not appealable by default — the result objection window is the remedy.
    /// </summary>
    [Display(Name = "نتيجة المسابقة")]
    CompetitionResult = 6
}
