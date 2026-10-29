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

    /// <summary>
    /// HCM-028 (D-5): ranking requires an approved Ranking/Final committee session and follows its decisions.
    /// Initial value from <c>Recruitment:CommitteeApprovalDefault</c>; frozen once a rank is assigned.
    /// </summary>
    [Display(Name = "إلزامية اعتماد لجنة الاختيار")]
    public bool RequiresCommitteeApproval { get; set; }

    // HCM-030 (D2): application criteria, checked at intake as eligibility flags (never an automatic rejection).
    // Null = not checked. Editable while the requisition is editable (Draft).

    /// <summary>Minimum MDM qualification code; compared through <c>Recruitment:Eligibility:QualificationRanks</c>.</summary>
    [StringLength(20)]
    [Display(Name = "أدنى مؤهل علمي")]
    public string? MinQualificationCode { get; set; }

    [Display(Name = "أدنى سنوات خبرة")]
    public decimal? MinYearsOfExperience { get; set; }

    [Display(Name = "الحد الأدنى للعمر")]
    public int? MinAge { get; set; }

    [Display(Name = "الحد الأعلى للعمر")]
    public int? MaxAge { get; set; }

    /// <summary>
    /// HCM-030 (E4): document types the candidate is asked to attach (comma-separated
    /// <see cref="Services.CandidateDocumentTypes"/> codes; null = none). A missing one is the eligibility flag
    /// <c>MissingRequiredDocuments</c>, never a rejection. Editable while the requisition is editable (Draft).
    /// </summary>
    [StringLength(100)]
    [Display(Name = "المستندات المطلوبة")]
    public string? RequiredDocumentTypes { get; set; }

    /// <summary>
    /// HCM-031 (PLAN-31): the competition line this requisition is attached to. An attached requisition is never
    /// published on its own (no posting or vacancy of its own); announcing the competition moves it to Published.
    /// </summary>
    [Display(Name = "سطر المسابقة")]
    public Guid? CompetitionLineId { get; set; }

    public virtual CompetitionLine? CompetitionLine { get; set; }
}
