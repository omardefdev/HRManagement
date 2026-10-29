using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Domain.Entities;

/// <summary>
/// HCM-031 (PLAN-31, G-19): a multi-vacancy competition. Every line groups approved requisitions (one funded position
/// each); applicants apply once per competition with ordered line preferences. Every legal parameter is an input of the
/// competition taken from the approved legal reference (decision D-2) — the code ships no legal value.
/// </summary>
public class RecruitmentCompetition : AuditableEntity
{
    [Required]
    [StringLength(50)]
    [Display(Name = "رقم المسابقة")]
    public string Number { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "عنوان المسابقة")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "المرجع القانوني")]
    public string LegalSourceId { get; set; } = string.Empty;

    public virtual LegislationSource? LegalSource { get; set; }

    [StringLength(2000)]
    [Display(Name = "شروط التقديم")]
    public string? ApplicationConditions { get; set; }

    [Display(Name = "قناة النشر")]
    public RecruitmentPublishingChannel PublishingChannel { get; set; } = RecruitmentPublishingChannel.PublicOnly;

    [Display(Name = "الإعلان العام")]
    public Guid? PublicJobPostingId { get; set; }

    public virtual PublicJobPosting? PublicJobPosting { get; set; }

    [Display(Name = "الشاغر الداخلي")]
    public Guid? InternalVacancyId { get; set; }

    public virtual InternalVacancy? InternalVacancy { get; set; }

    [Display(Name = "الحالة")]
    public RecruitmentCompetitionStatus Status { get; set; } = RecruitmentCompetitionStatus.Draft;

    [Display(Name = "تاريخ الإعلان")]
    [DataType(DataType.Date)]
    public DateOnly? AnnouncementDate { get; set; }

    /// <summary>Last day applications are accepted (inclusive).</summary>
    [Display(Name = "تاريخ إغلاق التقديم")]
    [DataType(DataType.Date)]
    public DateOnly ApplicationClosingDate { get; set; }

    [Display(Name = "تاريخ الإعلان الفعلي")]
    public DateTime? AnnouncedAt { get; set; }

    [Display(Name = "تاريخ إغلاق التقديم الفعلي")]
    public DateTime? IntakeClosedAt { get; set; }

    // Legal inputs (D-2): taken from the approved reference for every competition; sanity ranges only in code.

    [Display(Name = "مدة الاعتراض (أيام)")]
    public int ObjectionWindowDays { get; set; }

    [Display(Name = "صلاحية الاحتياط (أشهر)")]
    public int ReserveValidityMonths { get; set; }

    [Display(Name = "الحد الأقصى للأسطر لكل متقدم")]
    public int MaxLinesPerCandidate { get; set; }

    /// <summary>Pass mark on the final score (0..100, weights sum to 100).</summary>
    [Display(Name = "درجة النجاح")]
    public decimal PassMark { get; set; }

    /// <summary>Largest allowed difference between the first and second marks before a reconciler marks the paper.</summary>
    [Display(Name = "عتبة التصحيح الثالث")]
    public decimal ReconcileThreshold { get; set; }

    [Display(Name = "قاعدة التسوية")]
    public CompetitionReconcileRule ReconcileRule { get; set; }

    /// <summary>Ordered, comma-separated <see cref="CompetitionTieBreak"/> names (see <c>CompetitionWorkflowRules.ParseTieBreakOrder</c>).</summary>
    [StringLength(100)]
    [Display(Name = "ترتيب كسر التعادل")]
    public string TieBreakOrder { get; set; } = string.Empty;

    [Display(Name = "مسار الاعتماد")]
    public Guid? ApprovalWorkflowInstanceId { get; set; }

    [Display(Name = "تاريخ الاعتماد")]
    public DateTime? ApprovedAt { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب الإرجاع")]
    public string? ReturnReason { get; set; }

    /// <summary>HCM-031 (P4): the published results version that currently applies.</summary>
    [Display(Name = "نسخة النتائج الحالية")]
    public Guid? CurrentResultVersionId { get; set; }

    /// <summary>HCM-031 (P4): first publication — the objection window starts here and is never reopened.</summary>
    [Display(Name = "تاريخ أول نشر للنتائج")]
    public DateTime? ResultsFirstPublishedAt { get; set; }

    [Display(Name = "تاريخ الإنهاء")]
    public DateTime? FinalizedAt { get; set; }

    [Display(Name = "تاريخ الإلغاء")]
    public DateTime? CancelledAt { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب الإلغاء")]
    public string? CancellationReason { get; set; }

    public virtual ICollection<CompetitionLine> Lines { get; set; } = [];
}
