using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-026: one assessor's score for one criteria item of an application.</summary>
public class RecruitmentAssessmentScore : AuditableEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Display(Name = "إصدار المعايير")]
    public Guid CriteriaVersionId { get; set; }

    public virtual RecruitmentAssessmentCriteria? CriteriaVersion { get; set; }

    [Required, StringLength(64)]
    [Display(Name = "البند")]
    public string ItemKey { get; set; } = string.Empty;

    [Display(Name = "المقيّم")]
    public Guid AssessorUserId { get; set; }

    [Display(Name = "الموظف المقيّم")]
    public Guid? AssessorEmployeeId { get; set; }

    [Display(Name = "الدرجة")]
    public decimal Score { get; set; }

    [StringLength(1000)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "إقرار عدم تعارض المصالح")]
    public bool NoConflictDeclared { get; set; }

    [Display(Name = "تاريخ الإرسال")]
    public DateTime SubmittedAt { get; set; }
}
