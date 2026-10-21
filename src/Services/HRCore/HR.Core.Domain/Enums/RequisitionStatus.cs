using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum RequisitionStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 1,
    [Display(Name = "معتمد")]
    Approved = 2,
    [Display(Name = "منشور")]
    Published = 3,
    [Display(Name = "مغلق")]
    Closed = 4,
    [Display(Name = "ملغى")]
    Cancelled = 5
}
