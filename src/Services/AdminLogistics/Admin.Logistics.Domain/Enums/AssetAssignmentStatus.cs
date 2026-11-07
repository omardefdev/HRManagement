using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

/// <summary>Stored as an integer: a new value goes at the end only.</summary>
public enum AssetAssignmentStatus
{
    [Display(Name = "بانتظار الموافقة")]
    PendingApproval,

    [Display(Name = "نشطة")]
    Active,

    [Display(Name = "مرتجعة")]
    Returned,

    [Display(Name = "مرفوضة")]
    Rejected,

    /// <summary>PLAN-45: saved, with its approval not started yet. Holds the asset as <see cref="PendingApproval"/> does.</summary>
    [Display(Name = "بانتظار بدء الاعتماد")]
    ApprovalStarting,

    /// <summary>PLAN-45: withdrawn by the requester or a logistics officer. The asset is free again.</summary>
    [Display(Name = "مسحوبة")]
    Withdrawn
}
