using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum AssetAssignmentStatus
{
    [Display(Name = "بانتظار الموافقة")]
    PendingApproval,

    [Display(Name = "نشطة")]
    Active,

    [Display(Name = "مرتجعة")]
    Returned,

    [Display(Name = "مرفوضة")]
    Rejected
}
