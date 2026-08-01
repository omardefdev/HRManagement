using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "الدورة التدريبية")]
public class TrainingCourse : AuditableEntity
{
    [Required(ErrorMessage = "كود الدورة مطلوب")]
    [Display(Name = "كود الدورة")]
    public string Code { get; set; } = default!;

    [Required(ErrorMessage = "اسم الدورة بالعربي مطلوب")]
    [Display(Name = "الاسم بالعربي")]
    public string TitleAr { get; set; } = default!;

    [Display(Name = "الاسم بالإنكليزي")]
    public string? TitleEn { get; set; }

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "المدة بالأيام")]
    public int DurationDays { get; set; }

    [Display(Name = "مقدم التدريب")]
    public Guid? ProviderId { get; set; }

    [ForeignKey(nameof(ProviderId))]
    public virtual TrainingProvider? Provider { get; set; }

    public virtual ICollection<TrainingEnrollment> Enrollments { get; set; } = new List<TrainingEnrollment>();
    public virtual ICollection<TrainingNomination> Nominations { get; set; } = new List<TrainingNomination>();
}
