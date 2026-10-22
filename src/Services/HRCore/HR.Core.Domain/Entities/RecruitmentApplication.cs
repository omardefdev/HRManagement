using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class RecruitmentApplication : AuditableEntity
{
    [Required]
    [Display(Name = "اسم المتقدم")]
    public string ApplicantFullName { get; set; } = string.Empty;

    [Display(Name = "الرقم الوطني")]
    [AuditRedact]
    public string? NationalId { get; set; }

    [Display(Name = "تجزئة الرقم الوطني")]
    [StringLength(64)]
    public string? NationalIdHash { get; set; }

    [Display(Name = "حجز قانوني")]
    public bool LegalHold { get; set; }

    [Display(Name = "تاريخ إخفاء الهوية")]
    public DateTime? AnonymizedAt { get; set; }

    [Display(Name = "حالة الاستئناف")]
    public RecruitmentAppealStatus AppealStatus { get; set; } = RecruitmentAppealStatus.None;

    [Required]
    [Display(Name = "الوظيفة المتقدم لها")]
    public string AppliedPosition { get; set; } = string.Empty;

    [Display(Name = "طلب الشاغر")]
    public Guid? RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    [Display(Name = "ترشّح داخلي مصدر")]
    public Guid? SourceInternalApplicationId { get; set; }

    public virtual InternalApplication? SourceInternalApplication { get; set; }

    [Display(Name = "تاريخ التقديم")]
    public DateTime AppliedAt { get; set; }

    [Display(Name = "الحالة")]
    public RecruitmentApplicationStatus Status { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "ملاحظات الاستئناف")]
    public string? AppealNotes { get; set; }

    [Display(Name = "طلب قديم غير مرتبط")]
    public bool IsLegacyUnlinked { get; set; }

    [Display(Name = "موظف بعد التعيين")]
    public Guid? HiredEmployeeId { get; set; }

    [Display(Name = "تاريخ المراجعة")]
    public DateTime? ReviewedAt { get; set; }

    [Display(Name = "تاريخ الفرز")]
    public DateTime? ScreenedAt { get; set; }

    [Display(Name = "درجة التقييم")]
    public decimal? AssessmentScore { get; set; }

    [Display(Name = "الترتيب")]
    public int? Rank { get; set; }

    [Display(Name = "إصدار المعايير")]
    public Guid? CriteriaVersionId { get; set; }

    public virtual RecruitmentAssessmentCriteria? CriteriaVersion { get; set; }

    [Display(Name = "إقرار تعارض المصالح")]
    public bool ConflictOfInterestDeclared { get; set; }

    [Display(Name = "تاريخ إقرار COI")]
    public DateTime? ConflictOfInterestDeclaredAt { get; set; }

    [Display(Name = "تاريخ الترتيب")]
    public DateTime? RankedAt { get; set; }

    [Display(Name = "خطاب العرض النشط")]
    public Guid? ActiveOfferLetterId { get; set; }

    public virtual OfferLetter? ActiveOfferLetter { get; set; }

    /// <summary>HCM-027 (F2, D-7): last day the application stays on the reserve list.</summary>
    [Display(Name = "الاحتياط حتى")]
    public DateOnly? ReserveUntil { get; set; }

    /// <summary>HCM-027 (F2): when the application was moved to the reserve list (hire of a sibling).</summary>
    [Display(Name = "تاريخ الإدراج في الاحتياط")]
    public DateTime? ReserveListedAt { get; set; }

    /// <summary>HCM-028 (C): why the rank differs from the approved committee's proposed rank.</summary>
    [StringLength(1000)]
    [Display(Name = "سبب مخالفة الترتيب المقترح")]
    public string? RankOverrideReason { get; set; }

    [Display(Name = "خالف الترتيب المقترح")]
    public Guid? RankOverriddenBy { get; set; }

    [Display(Name = "تاريخ مخالفة الترتيب المقترح")]
    public DateTime? RankOverriddenAt { get; set; }

    public virtual Employee? HiredEmployee { get; set; }

    public virtual ICollection<OfferLetter> OfferLetters { get; set; } = [];
}
