using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>
/// HCM-032 (PLAN-32, decision 4): category of an offer decline (stored as string(20)); required for new declines on the HR
/// and candidate-portal paths, <see cref="Other"/> for declines caused by the candidate's withdrawal. Declines recorded
/// before PLAN-32 have none and are reported «غير مصنف». The free-text reason is kept.
/// </summary>
public enum OfferDeclineReasonCategory
{
    [Display(Name = "الراتب")]
    Salary = 0,
    [Display(Name = "مكان العمل")]
    Location = 1,
    [Display(Name = "عرض آخر")]
    OtherOffer = 2,
    [Display(Name = "أسباب شخصية")]
    Personal = 3,
    [Display(Name = "طبيعة العمل")]
    JobContent = 4,
    [Display(Name = "أخرى")]
    Other = 5
}

public static class OfferDeclineReasonCategories
{
    public const string UncategorizedAr = "غير مصنف";

    public static string LabelAr(OfferDeclineReasonCategory? category) => category switch
    {
        OfferDeclineReasonCategory.Salary => "الراتب",
        OfferDeclineReasonCategory.Location => "مكان العمل",
        OfferDeclineReasonCategory.OtherOffer => "عرض آخر",
        OfferDeclineReasonCategory.Personal => "أسباب شخصية",
        OfferDeclineReasonCategory.JobContent => "طبيعة العمل",
        OfferDeclineReasonCategory.Other => "أخرى",
        _ => UncategorizedAr
    };
}
