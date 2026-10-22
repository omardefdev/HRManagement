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
    OfferRevoked = 5
}
