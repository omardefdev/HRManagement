using System.ComponentModel.DataAnnotations;

namespace IDCard.Domain.Enums;

/// <summary>PLAN-46: the two approval states go at the end, so the stored numbers of the first three never change.</summary>
public enum CardRequestStatus
{
    [Display(Name = "مسودة")]
    Pending,
    [Display(Name = "معالج")]
    Processed,
    [Display(Name = "مرفوض")]
    Rejected,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval,
    [Display(Name = "معتمد")]
    Approved,
}
