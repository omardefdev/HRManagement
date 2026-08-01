using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "مكون بند الراتب")]
public class SalaryLineComponent : BaseEntity
{
    [Required]
    [ForeignKey(nameof(SalaryLine))]
    public Guid SalaryLineId { get; set; }

    [Display(Name = "نوع المكون")]
    public SalaryComponentType ComponentType { get; set; }

    [Required, MaxLength(200)]
    [Display(Name = "اسم المكون")]
    public string ComponentName { get; set; } = null!;

    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Display(Name = "مرجع")]
    public Guid? ReferenceId { get; set; }

    public SalaryLine SalaryLine { get; set; } = null!;
}
