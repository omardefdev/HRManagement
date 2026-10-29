using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-031 (PLAN-31): one ordered line preference of a competition application (1 = first choice).</summary>
public class CompetitionPreference : AuditableEntity
{
    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    public Guid CompetitionLineId { get; set; }

    public virtual CompetitionLine? CompetitionLine { get; set; }

    [Display(Name = "ترتيب التفضيل")]
    public int Order { get; set; }

    /// <summary>
    /// HCM-030 eligibility flag codes evaluated against the requisitions of this line (comma-separated; null = none).
    /// The application's own <c>EligibilityFlags</c> is the union over its preferences.
    /// </summary>
    [StringLength(200)]
    [Display(Name = "ملاحظات الأهلية للسطر")]
    public string? EligibilityFlags { get; set; }
}
