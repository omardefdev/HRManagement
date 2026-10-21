using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-001: persisted staffing requisition linked to a funded vacant position.</summary>
public class RecruitmentRequisition : AuditableEntity
{
    [Required]
    [StringLength(50)]
    [Display(Name = "رقم الطلب")]
    public string RequisitionNumber { get; set; } = string.Empty;

    [Display(Name = "المنصب الممول")]
    public Guid FundedPositionId { get; set; }

    public virtual FundedPosition? FundedPosition { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "المسمى الوظيفي")]
    public string JobTitle { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "تاريخ الاعتماد")]
    public DateTime? ApprovedDate { get; set; }

    [Display(Name = "تاريخ الإغلاق")]
    public DateTime? ClosedDate { get; set; }

    [StringLength(500)]
    [Display(Name = "مبررات الطلب")]
    public string? JustificationNotes { get; set; }

    [Display(Name = "عدد الشواغر")]
    public int VacancyCount { get; set; } = 1;

    [StringLength(100)]
    [Display(Name = "مرجع قانوني")]
    public string? LegalSourceId { get; set; }

    public virtual LegislationSource? LegalSource { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }

    [Display(Name = "تاريخ النشر")]
    public DateTime? PublishedAt { get; set; }

    [Display(Name = "الإعلان العام")]
    public Guid? PublicJobPostingId { get; set; }

    public virtual PublicJobPosting? PublicJobPosting { get; set; }

    [Display(Name = "قناة النشر")]
    public RecruitmentPublishingChannel PublishingChannel { get; set; } = RecruitmentPublishingChannel.PublicOnly;

    [Display(Name = "الشاغر الداخلي")]
    public Guid? InternalVacancyId { get; set; }

    public virtual InternalVacancy? InternalVacancy { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب الإلغاء")]
    public string? CancellationReason { get; set; }

    [Display(Name = "تاريخ الإلغاء")]
    public DateTime? CancelledAt { get; set; }

    [Display(Name = "ألغي بواسطة")]
    public Guid? CancelledBy { get; set; }

    /// <summary>HCM-026: last day applications are accepted (inclusive); required at first publish.</summary>
    [Display(Name = "تاريخ إغلاق التقديم")]
    [DataType(DataType.Date)]
    public DateOnly? ApplicationClosingDate { get; set; }

    /// <summary>HCM-026: set when intake closed (job or extension); the requisition stays Published for selection.</summary>
    [Display(Name = "تاريخ إغلاق التقديم الفعلي")]
    public DateTime? IntakeClosedAt { get; set; }
}
