using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "دورة تعلّم إلكتروني")]
public class ELearningCourse : AuditableEntity
{
    [Required(ErrorMessage = "كود الدورة مطلوب")]
    [Display(Name = "كود الدورة")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الدورة مطلوب")]
    [Display(Name = "اسم الدورة")]
    [StringLength(200)]
    public string TitleAr { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "التصنيف")]
    [StringLength(100)]
    public string? Category { get; set; }

    [Display(Name = "الساعات التقديرية")]
    [Range(0, 1000)]
    public int EstimatedHours { get; set; }

    [Display(Name = "منشورة للموظفين")]
    public bool IsPublished { get; set; }

    public virtual ICollection<CourseModule> Modules { get; set; } = new List<CourseModule>();
    public virtual ICollection<LearningEnrollment> Enrollments { get; set; } = new List<LearningEnrollment>();
}
