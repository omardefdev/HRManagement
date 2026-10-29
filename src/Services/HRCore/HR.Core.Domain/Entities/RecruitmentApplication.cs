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

    /// <summary>
    /// HCM-030 (A5): intake channel. A public application is linked from
    /// <see cref="CitizenApplication.PromotedRecruitmentApplicationId"/>, an internal one from
    /// <see cref="InternalApplication.PromotedRecruitmentApplicationId"/> (and <see cref="SourceInternalApplicationId"/>).
    /// </summary>
    [Display(Name = "قناة التقديم")]
    public RecruitmentIntakeChannel IntakeChannel { get; set; } = RecruitmentIntakeChannel.HrEntered;

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

    /// <summary>HCM-028 (G1): set by <see cref="Services.ApplicationRejection"/>; the appeal window starts here.</summary>
    [Display(Name = "تاريخ الرفض")]
    public DateTime? RejectedAt { get; set; }

    [Display(Name = "نوع الرفض")]
    public RecruitmentRejectionKind? RejectionKind { get; set; }

    [StringLength(1000)]
    [Display(Name = "ملاحظة الرفض")]
    public string? RejectionNote { get; set; }

    /// <summary>HCM-028 (H3): the rank held when rejected — an appeal may correct it.</summary>
    [Display(Name = "الترتيب قبل الرفض")]
    public int? RankBeforeRejection { get; set; }


    // HCM-030 (D1, G-17): application profile snapshot (from the citizen row or the employee). BirthDate is personal data — never logged (audit-redacted), shown to
    // HR on the application details only. Cleared by candidate anonymization (HCM-030 G2, CandidateApplicationLegalLifecycleService.TryAnonymize).

    /// <summary>MDM <c>CatalogKind.Qualification</c> code (DIP/BACH/MASTER/PHD …).</summary>
    [StringLength(20)]
    [Display(Name = "المؤهل العلمي")]
    public string? EducationLevelCode { get; set; }

    [StringLength(200)]
    [Display(Name = "التخصص")]
    public string? Specialization { get; set; }

    [Display(Name = "سنة التخرج")]
    public int? GraduationYear { get; set; }

    [Display(Name = "سنوات الخبرة")]
    public decimal? YearsOfExperience { get; set; }

    [AuditRedact]
    [Display(Name = "تاريخ الميلاد")]
    public DateOnly? BirthDate { get; set; }

    /// <summary>
    /// HCM-030 (D4): <see cref="Services.RecruitmentEligibilityRules"/> codes, comma-separated, evaluated at intake
    /// (null: no flag, or not evaluated — see <see cref="EligibilityEvaluatedAt"/>).
    /// </summary>
    [StringLength(200)]
    [Display(Name = "ملاحظات الأهلية")]
    public string? EligibilityFlags { get; set; }

    [Display(Name = "تاريخ تقييم الأهلية")]
    public DateTime? EligibilityEvaluatedAt { get; set; }

    /// <summary>HCM-030 (D4): why a flagged application was shortlisted (audited as <c>EligibilityOverride</c>).</summary>
    [StringLength(1000)]
    [Display(Name = "سبب تجاوز ملاحظات الأهلية")]
    public string? EligibilityOverrideReason { get; set; }

    public virtual Employee? HiredEmployee { get; set; }

    public virtual ICollection<OfferLetter> OfferLetters { get; set; } = [];
}
