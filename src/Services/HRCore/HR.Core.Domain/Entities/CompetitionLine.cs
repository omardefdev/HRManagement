using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// HCM-031 (PLAN-31): one line of a competition — a grade (and optionally a job catalog entry / org unit) that groups
/// approved requisitions. <see cref="RequiredCount"/> is the number of attached requisitions (decision 2: a requisition
/// stays one funded position).
/// </summary>
public class CompetitionLine : AuditableEntity
{
    public Guid CompetitionId { get; set; }

    public virtual RecruitmentCompetition? Competition { get; set; }

    [Display(Name = "رقم السطر")]
    public int LineNo { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "عنوان السطر")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Display(Name = "الدرجة")]
    public string GradeCode { get; set; } = string.Empty;

    [Display(Name = "الوظيفة في الدليل")]
    public Guid? JobCatalogEntryId { get; set; }

    public virtual JobCatalogEntry? JobCatalogEntry { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }

    /// <summary>
    /// Criteria version the line is assessed with: the active version of its first attached requisition, frozen when
    /// the competition is submitted for approval (all lines must share one criteria shape — PLAN-31 J2).
    /// </summary>
    [Display(Name = "إصدار المعايير")]
    public Guid? CriteriaVersionId { get; set; }

    public virtual RecruitmentAssessmentCriteria? CriteriaVersion { get; set; }

    /// <summary>Number of attached requisitions (kept in sync by the lifecycle service).</summary>
    [Display(Name = "عدد الشواغر")]
    public int RequiredCount { get; set; }

    /// <summary>Overrides the competition pass mark for this line (a legal input; null = the competition value).</summary>
    [Display(Name = "درجة نجاح السطر")]
    public decimal? LinePassMark { get; set; }

    public virtual ICollection<RecruitmentRequisition> Requisitions { get; set; } = [];
}
