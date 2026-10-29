using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    /// <summary>Legacy (before HCM-028): the reviewer's Keycloak user id typed as free text.</summary>
    [Display(Name = "المراجع المستقل (معرّف مستخدم قديم)")]
    public Guid? IndependentReviewerId { get; set; }

    /// <summary>HCM-028 (H2): the reviewer, chosen from the employee picker; matched against the <c>employee_id</c> claim.</summary>
    [Display(Name = "المراجع المستقل")]
    public Guid? IndependentReviewerEmployeeId { get; set; }

    [Display(Name = "تاريخ تعيين المراجع")]
    public DateTime? AssignedAt { get; set; }

    [Display(Name = "عيّن المراجع")]
    public Guid? AssignedBy { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }

    [Display(Name = "نتيجة الاستئناف")]
    public RecruitmentAppealOutcome? Outcome { get; set; }

    [Display(Name = "الترتيب المصحّح")]
    public int? CorrectedRank { get; set; }

    /// <summary>HCM-028 (H1): <see cref="RecruitmentAppealFiledBy"/>.</summary>
    [StringLength(20)]
    [Display(Name = "مقدّم الاستئناف")]
    public string? FiledBy { get; set; }

    /// <summary>HCM-028 (H2): the filer's employee (<c>employee_id</c> claim), excluded as reviewer.</summary>
    [Display(Name = "موظف مقدّم الاستئناف")]
    public Guid? FiledByEmployeeId { get; set; }

    [StringLength(2000)]
    [Display(Name = "طلب معلومات إضافية")]
    public string? InfoRequest { get; set; }

    [Display(Name = "تاريخ طلب المعلومات")]
    public DateTime? InfoRequestedAt { get; set; }

    [StringLength(4000)]
    [Display(Name = "المعلومات الإضافية")]
    public string? AdditionalInfo { get; set; }

    [Display(Name = "تاريخ استيفاء المعلومات")]
    public DateTime? AdditionalInfoAt { get; set; }

    /// <summary>HCM-028 (H3): the reviewer asked for information that HR has not supplied yet.</summary>
    [NotMapped]
    public bool IsAwaitingInfo => InfoRequestedAt is not null && (AdditionalInfoAt is null || AdditionalInfoAt < InfoRequestedAt);
}

/// <summary>HCM-028 (H1): values of <see cref="RecruitmentAppeal.FiledBy"/>.</summary>
public static class RecruitmentAppealFiledBy
{
    /// <summary>HCM-030: the candidate filed on <c>/careers/my</c>.</summary>
    public const string Candidate = "Candidate";
    public const string HrOnBehalf = "HrOnBehalf";

    public static bool IsValid(string? value) => value is Candidate or HrOnBehalf;
}
