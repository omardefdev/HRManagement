using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "لوحة المؤشرات")]
public class Dashboard : AuditableEntity
{
    [Display(Name = "الاسم")]
    [Required(ErrorMessage = "الاسم مطلوب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الأدوات")]
    public string? Widgets { get; set; }

    [Display(Name = "معرّف المالك")]
    public Guid? OwnerId { get; set; }
}
