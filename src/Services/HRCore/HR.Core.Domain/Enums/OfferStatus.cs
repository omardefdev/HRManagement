using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum OfferStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 1,
    [Display(Name = "صادرة")]
    Issued = 2,
    [Display(Name = "مقبول")]
    Accepted = 3,
    [Display(Name = "مرفوض")]
    Declined = 4,
    [Display(Name = "منتهي الصلاحية")]
    Expired = 5,
    [Display(Name = "ملغى")]
    Revoked = 6
}
