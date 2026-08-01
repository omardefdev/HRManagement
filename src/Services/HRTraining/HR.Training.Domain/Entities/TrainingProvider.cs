using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "مقدم التدريب")]
public class TrainingProvider : AuditableEntity
{
    [Required(ErrorMessage = "اسم المزود مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = default!;

    [Display(Name = "معلومات الاتصال")]
    [Column(TypeName = "jsonb")]
    public string? ContactInfo { get; set; }

    [Display(Name = "الدولة")]
    public string? Country { get; set; }
}
