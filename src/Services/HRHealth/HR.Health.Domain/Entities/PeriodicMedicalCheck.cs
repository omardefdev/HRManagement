using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "الفحص الطبي الدوري")]
public class PeriodicMedicalCheck : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "تاريخ الفحص")]
    public DateOnly CheckDate { get; set; }

    [Display(Name = "الملخص")]
    public string? Summary { get; set; }

    [Display(Name = "القياسات")]
    public string? Vitals { get; set; }

    [Display(Name = "الفحص التالي")]
    public DateOnly? NextDue { get; set; }
}
