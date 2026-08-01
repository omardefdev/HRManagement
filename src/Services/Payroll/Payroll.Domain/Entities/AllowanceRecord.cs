using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "استحقاق البدلات")]
public class AllowanceRecord : AuditableEntity
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "نوع البدل مطلوب")]
    [Display(Name = "نوع البدل")]
    public Guid AllowanceTypeId { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "تاريخ السريان مطلوب")]
    [Display(Name = "ساري من")]
    public DateTime EffectiveFrom { get; set; }

    [Display(Name = "ساري إلى")]
    public DateTime? EffectiveTo { get; set; }

    public CompensationType AllowanceType { get; set; } = null!;
}
