using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum AssetLossReportStatus
{
    [Display(Name = "بانتظار الموافقة")]
    PendingApproval,

    [Display(Name = "مؤكد")]
    Confirmed,

    [Display(Name = "مرفوض")]
    Rejected,

    [Display(Name = "مُسترد")]
    Recovered,
}
