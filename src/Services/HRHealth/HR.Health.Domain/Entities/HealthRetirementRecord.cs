using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "ملف التقاعد الصحي")]
public class HealthRetirementRecord : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "المستندات")]
    public string? Documents { get; set; }
}
