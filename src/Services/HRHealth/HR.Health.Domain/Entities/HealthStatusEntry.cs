using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "تسجيل الحالة الصحية")]
public class HealthStatusEntry : BaseEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ التسجيل")]
    public DateTime EntryDate { get; set; }

    [Display(Name = "كود الحالة")]
    public string? StatusCode { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public Guid? CreatedBy { get; set; }
}
