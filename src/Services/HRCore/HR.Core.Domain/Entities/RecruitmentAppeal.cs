using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-006: independent appeal review for rejected recruitment applications.</summary>
public class RecruitmentAppeal : AuditableEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Display(Name = "حالة الاستئناف")]
    public RecruitmentAppealStatus Status { get; set; } = RecruitmentAppealStatus.Pending;

    [Required, StringLength(2000)]
    [Display(Name = "السبب")]
    public string Reason { get; set; } = string.Empty;

    [Display(Name = "تاريخ القرار")]
    public DateTime? DecidedAt { get; set; }

    [StringLength(2000)]
    [Display(Name = "القرار")]
    public string? Decision { get; set; }

    [Display(Name = "المراجع المستقل")]
    public Guid? IndependentReviewerId { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }
}
