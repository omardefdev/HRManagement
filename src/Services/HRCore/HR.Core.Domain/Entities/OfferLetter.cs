using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-004: persisted offer letter linked to recruitment application.</summary>
public class OfferLetter : AuditableEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Display(Name = "المنصب الممول")]
    public Guid FundedPositionId { get; set; }

    public virtual FundedPosition? FundedPosition { get; set; }

    [Required, StringLength(50)]
    [Display(Name = "رقم العرض")]
    public string OfferNumber { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    [Display(Name = "تاريخ الإصدار")]
    public DateTime? IssuedDate { get; set; }

    [Display(Name = "تاريخ الانتهاء")]
    public DateTime? ExpiryDate { get; set; }

    [Display(Name = "تاريخ القبول")]
    public DateTime? AcceptedDate { get; set; }

    /// <summary>
    /// HCM-030 (PLAN-30 I/J): <c>Candidate</c> (accepted on <c>/careers/my</c>) or <c>HrOnBehalf</c> (see
    /// <see cref="OfferAcceptedBy"/>); null for offers accepted before HCM-030.
    /// </summary>
    [StringLength(20)]
    [Display(Name = "قبول بواسطة")]
    public string? AcceptedBy { get; set; }

    /// <summary>HCM-030 (J): reason recorded when HR accepts on the candidate's behalf.</summary>
    [StringLength(1000)]
    [Display(Name = "ملاحظة القبول")]
    public string? AcceptanceNote { get; set; }

    /// <summary>HCM-030 (J): File.Storage id of the signed paper acceptance (HR on behalf).</summary>
    [Display(Name = "مستند إثبات القبول")]
    public Guid? AcceptanceEvidenceFileId { get; set; }

    [Display(Name = "الراتب المعروض")]
    public decimal OfferedSalary { get; set; }

    [StringLength(50)]
    [Display(Name = "الدرجة")]
    public string? GradeCode { get; set; }

    [Display(Name = "تاريخ المباشرة المقترح")]
    public DateTime ProposedStartDate { get; set; }

    [StringLength(100)]
    [Display(Name = "مرجع قانوني")]
    public string? LegalSourceId { get; set; }

    public virtual LegislationSource? LegalSource { get; set; }

    [Display(Name = "تحوّل إلى موظف")]
    public bool IsConvertedToEmployee { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }

    /// <summary>HCM-027: offer skips merit order — approved through RECRUITMENT-OFFER-OVERRIDE-APPROVAL.</summary>
    [Display(Name = "عرض استثنائي — تجاوز الجدارة")]
    public bool IsMeritOverride { get; set; }

    [StringLength(1000)]
    [Display(Name = "مبرر تجاوز الجدارة")]
    public string? MeritOverrideReason { get; set; }

    /// <summary>HCM-027: days the offer stays open once issued (ExpiryDate = IssuedDate + ValidityDays).</summary>
    [Display(Name = "مدة صلاحية العرض (أيام)")]
    public int ValidityDays { get; set; } = 14;

    [StringLength(1000)]
    [Display(Name = "سبب الرفض")]
    public string? DeclineReason { get; set; }

    /// <summary>HCM-032 (PLAN-32): required on new declines; null on declines recorded before PLAN-32 («غير مصنف»).</summary>
    [Display(Name = "فئة سبب الرفض")]
    public OfferDeclineReasonCategory? DeclineReasonCategory { get; set; }

    [Display(Name = "تاريخ الرفض")]
    public DateTime? DeclinedAt { get; set; }

    /// <summary>HCM-027: <c>Candidate</c> or <c>HrOnBehalf</c> (see <see cref="OfferDeclinedBy"/>).</summary>
    [StringLength(20)]
    [Display(Name = "رفض بواسطة")]
    public string? DeclinedBy { get; set; }

    /// <summary>HCM-030 (J): optional File.Storage id of the signed paper decline (HR on behalf).</summary>
    [Display(Name = "مستند إثبات الرفض")]
    public Guid? DeclineEvidenceFileId { get; set; }

    [StringLength(1000)]
    [Display(Name = "سبب الإلغاء")]
    public string? RevokeReason { get; set; }

    [Display(Name = "تاريخ الإلغاء")]
    public DateTime? RevokedAt { get; set; }

    [Display(Name = "تاريخ انتهاء الصلاحية الفعلي")]
    public DateTime? ExpiredAt { get; set; }

    /// <summary>HCM-027 (E1): set when the offered grade differs from the funded position's grade.</summary>
    [StringLength(500)]
    [Display(Name = "مبرر اختلاف الدرجة")]
    public string? GradeDeviationReason { get; set; }

    /// <summary>HCM-027 (E3): the Payroll grade lookup was unavailable when the draft was created.</summary>
    [Display(Name = "راتب غير متحقق منه")]
    public bool SalaryUnverified { get; set; }

    [StringLength(1000)]
    [Display(Name = "مبرر الراتب غير المتحقق منه")]
    public string? SalaryJustification { get; set; }

    /// <summary>HCM-027 (E3): Payroll grade bounds at the proposed start date (snapshot).</summary>
    [Display(Name = "الحد الأدنى للدرجة")]
    public decimal? GradeMinSalary { get; set; }

    [Display(Name = "الحد الأقصى للدرجة")]
    public decimal? GradeMaxSalary { get; set; }

    [Display(Name = "تاريخ التحقق من الراتب")]
    public DateTime? SalaryCheckedAt { get; set; }

    /// <summary>
    /// HCM-029 (D2): SHA-256 (lowercase hex) of the canonical JSON of the offer letter's rendered fields, stored when the
    /// offer is issued. The PDF prints its first 12 characters as the verification code.
    /// </summary>
    [StringLength(64)]
    [Display(Name = "بصمة خطاب العرض")]
    public string? DocumentSha256 { get; set; }

    [Display(Name = "تاريخ توليد خطاب العرض")]
    public DateTime? DocumentGeneratedAt { get; set; }

    /// <summary>HCM-027 (E1): grade differs from the funded position (a reason was recorded).</summary>
    [NotMapped]
    public bool IsGradeDeviation => !string.IsNullOrWhiteSpace(GradeDeviationReason);

    /// <summary>
    /// HCM-027 (E1): merit override, grade deviation or unverified salary — approved through
    /// RECRUITMENT-OFFER-OVERRIDE-APPROVAL (three distinct approvers).
    /// </summary>
    [NotMapped]
    public bool IsExceptionOffer => IsMeritOverride || IsGradeDeviation || SalaryUnverified;
}

/// <summary>HCM-027: values of <see cref="OfferLetter.DeclinedBy"/>.</summary>
public static class OfferDeclinedBy
{
    /// <summary>HCM-030: the candidate declined (or withdrew) on <c>/careers/my</c>.</summary>
    public const string Candidate = "Candidate";
    public const string HrOnBehalf = "HrOnBehalf";

    public static bool IsValid(string? value) => value is Candidate or HrOnBehalf;
}

/// <summary>HCM-030 (PLAN-30 I/J): values of <see cref="OfferLetter.AcceptedBy"/>.</summary>
public static class OfferAcceptedBy
{
    /// <summary>The candidate accepted on <c>/careers/my</c> (typed acknowledgement).</summary>
    public const string Candidate = "Candidate";

    /// <summary>HR recorded a paper acceptance (reason and evidence required).</summary>
    public const string HrOnBehalf = "HrOnBehalf";

    public static bool IsValid(string? value) => value is Candidate or HrOnBehalf;

    /// <summary>HCM-030 (J): «قبول بواسطة» on OfferLetters/Details; offers accepted before HCM-030 have no channel.</summary>
    public static string LabelAr(string? value) => value switch
    {
        Candidate => "المرشح عبر بوابة التوظيف",
        HrOnBehalf => "HR نيابة عن المرشح",
        _ => "غير مسجَّل (قبول سابق لتسجيل جهة القبول)"
    };
}
