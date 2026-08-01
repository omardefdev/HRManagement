using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "مرشّح التعاقب")]
public class SuccessorCandidate : AuditableEntity
{
    [Display(Name = "خطة التعاقب")]
    public Guid SuccessionPlanId { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "آخر درجة تقييم")]
    public decimal LatestEvaluationScore { get; set; }

    [Display(Name = "سنوات الخدمة")]
    public int YearsInService { get; set; }

    [Display(Name = "يستوفي المؤهل")]
    public bool MeetsQualification { get; set; } = true;

    [Display(Name = "مستوى الجاهزية")]
    public ReadinessLevel Readiness { get; set; } = ReadinessLevel.NeedsDevelopment;

    public virtual SuccessionPlan? SuccessionPlan { get; set; }
    public virtual Employee? Employee { get; set; }
}
