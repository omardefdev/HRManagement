using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "طلب توظيف مواطن")]
public class CitizenApplication : AuditableEntity
{
    [Display(Name = "الإعلان الوظيفي")]
    public Guid PublicJobPostingId { get; set; }

    [Display(Name = "طلب الشاغر")]
    public Guid? RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    [Display(Name = "طلب التوظيف الداخلي المرتبط")]
    public Guid? PromotedRecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? PromotedRecruitmentApplication { get; set; }

    [Required]
    [Display(Name = "الرقم المرجعي")]
    [StringLength(40)]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [Display(Name = "الاسم الكامل")]
    [StringLength(300)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "الرقم الوطني مطلوب")]
    [Display(Name = "الرقم الوطني")]
    [StringLength(20)]
    [AuditRedact]
    public string NationalId { get; set; } = string.Empty;

    [Display(Name = "تجزئة الرقم الوطني")]
    [StringLength(64)]
    public string? NationalIdHash { get; set; }

    [Display(Name = "حجز قانوني")]
    public bool LegalHold { get; set; }

    [Display(Name = "تاريخ إخفاء الهوية")]
    public DateTime? AnonymizedAt { get; set; }

    [Display(Name = "رقم الهاتف")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [Display(Name = "البريد الإلكتروني")]
    [StringLength(150)]
    public string? Email { get; set; }

    [Display(Name = "نبذة/مسوّغات")]
    public string? CoverNote { get; set; }

    [Display(Name = "تاريخ التقديم")]
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// HCM-030: authoritative only for legacy rows (no requisition, or not yet linked). Once
    /// <see cref="PromotedRecruitmentApplicationId"/> is set the status lives on the recruitment application and this
    /// column is no longer written.
    /// </summary>
    [Display(Name = "الحالة")]
    public CitizenApplicationStatus Status { get; set; } = CitizenApplicationStatus.Received;

    [Display(Name = "إقرار تعارض المصالح")]
    public bool ConflictOfInterestDeclared { get; set; }

    [Display(Name = "تاريخ إقرار COI")]
    public DateTime? ConflictOfInterestDeclaredAt { get; set; }

    // HCM-030 (D1, G-17): application profile. BirthDate is personal data — never logged (audit-redacted), shown to
    // HR on the application details only. PLAN-30 S2: include in candidate anonymization.

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

    public virtual PublicJobPosting? PublicJobPosting { get; set; }
}
