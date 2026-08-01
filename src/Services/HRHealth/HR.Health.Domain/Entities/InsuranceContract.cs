using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "عقد التأمين الصحي")]
public class InsuranceContract : AuditableEntity
{
    [Required(ErrorMessage = "اسم المزود مطلوب")]
    [Display(Name = "المزود")]
    public string Provider { get; set; } = null!;

    [Display(Name = "تفاصيل التغطية")]
    public string? CoverageDetails { get; set; }

    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    public DateOnly? EndDate { get; set; }
}
