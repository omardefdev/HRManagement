using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "رمز التأهيل المهني")]
public class QualificationCode : AuditableEntity
{
    [Required(ErrorMessage = "الكود مطلوب")]
    [Display(Name = "الكود")]
    public string Code { get; set; } = default!;

    [Required]
    [Display(Name = "الاسم بالعربي")]
    public string TitleAr { get; set; } = default!;

    [Display(Name = "الاسم بالإنكليزي")]
    public string? TitleEn { get; set; }

    [Display(Name = "الدورات المطلوبة")]
    [Column(TypeName = "jsonb")]
    public string? RequiredCourses { get; set; }
}
