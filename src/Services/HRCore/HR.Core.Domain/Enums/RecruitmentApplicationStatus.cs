using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum RecruitmentApplicationStatus
{
    [Display(Name = "مُرسَل")]
    Submitted = 0,
    [Display(Name = "قيد الفرز")]
    UnderReview = 1,
    [Display(Name = "في القائمة المختصرة")]
    Shortlisted = 2,
    [Display(Name = "مرفوض")]
    Rejected = 3,
    [Display(Name = "قيد استكمال التعيين")]
    Hired = 4,
    [Display(Name = "مقابلة مجدولة")]
    InterviewScheduled = 5,
    [Display(Name = "مُقيَّم")]
    Assessed = 6,
    [Display(Name = "مُرتَّب")]
    Ranked = 7,
    [Display(Name = "عرض مُمدَّد")]
    OfferExtended = 8,
    [Display(Name = "عرض مقبول")]
    OfferAccepted = 9,
    [Display(Name = "منسحب")]
    Withdrawn = 10,
    /// <summary>HCM-027: the offer was declined or expired (terminal).</summary>
    [Display(Name = "عرض مرفوض/منتهٍ")]
    OfferDeclined = 11,
    /// <summary>HCM-027: kept on the requisition's reserve list.</summary>
    [Display(Name = "قائمة احتياط")]
    ReserveListed = 12
}
