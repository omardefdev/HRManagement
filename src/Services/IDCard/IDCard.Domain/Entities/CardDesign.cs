using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IDCard.Domain.Entities;

[Display(Name = "تصميم البطاقة")]
public class CardDesign : AuditableEntity
{
    [Required(ErrorMessage = "اسم التصميم مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = null!;

    [Display(Name = "القالب")]
    public string? Template { get; set; }

    [Display(Name = "مدة الصلاحية بالأيام")]
    public int ValidityDays { get; set; }
}
