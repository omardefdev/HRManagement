using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

/// <summary>Stored as an integer: a new value goes at the end only.</summary>
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

    /// <summary>PLAN-45: saved, with its approval not started yet. Blocks a second report as <see cref="PendingApproval"/> does.</summary>
    [Display(Name = "بانتظار بدء الاعتماد")]
    ApprovalStarting,

    /// <summary>PLAN-45: withdrawn by the requester or a logistics officer.</summary>
    [Display(Name = "مسحوب")]
    Withdrawn
}
