using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "المورد")]
public class Vendor : AuditableEntity
{
    [Required(ErrorMessage = "اسم المورد مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = default!;

    [Display(Name = "معلومات الاتصال")]
    public string? ContactInfo { get; set; }

    [Display(Name = "الدولة")]
    public string? Country { get; set; }
}
