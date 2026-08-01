using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "وحدة الدورة")]
public class CourseModule : AuditableEntity
{
    [Display(Name = "الدورة")]
    public Guid ELearningCourseId { get; set; }

    [Display(Name = "الترتيب")]
    public int Order { get; set; }

    [Required(ErrorMessage = "عنوان الوحدة مطلوب")]
    [Display(Name = "عنوان الوحدة")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public virtual ELearningCourse? ELearningCourse { get; set; }
    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}
