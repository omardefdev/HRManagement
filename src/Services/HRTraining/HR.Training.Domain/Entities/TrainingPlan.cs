using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "خطة التدريب السنوية")]
public class TrainingPlan : AuditableEntity
{
    [Required]
    [Display(Name = "السنة")]
    public int Year { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "الدورات المخطط لها")]
    [Column(TypeName = "jsonb")]
    public string? PlannedCourses { get; set; }

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "تاريخ الاعتماد")]
    public DateTime? ApprovedAt { get; set; }
}
