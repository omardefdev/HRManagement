using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Training.Domain.Entities;

[Display(Name = "ترشيح للتدريب")]
public class TrainingNomination : BaseEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "الدورة")]
    public Guid CourseId { get; set; }

    [Display(Name = "رشّح بواسطة")]
    public Guid? NominatedBy { get; set; }

    [Display(Name = "تاريخ الترشيح")]
    public DateTime NominatedAt { get; set; }

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; }

    [Display(Name = "خطة التدريب")]
    public Guid? TrainingPlanId { get; set; }

    [ForeignKey(nameof(CourseId))]
    public virtual TrainingCourse Course { get; set; } = default!;

    [ForeignKey(nameof(TrainingPlanId))]
    public virtual TrainingPlan? TrainingPlan { get; set; }
}
