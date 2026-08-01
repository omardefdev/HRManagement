using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "مستحقات نهاية الخدمة")]
public class EndOfServiceSettlement : BaseEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "سجل الفصل")]
    public Guid? SeparationRecordId { get; set; }

    [Display(Name = "سنوات الخدمة")]
    public decimal ServiceYears { get; set; }

    [Display(Name = "الراتب الأساسي")]
    public decimal BaseSalary { get; set; }

    [Display(Name = "رصيد الإجازات المستحقة")]
    public decimal LeaveBalancePayout { get; set; }

    [Display(Name = "مكافأة نهاية الخدمة")]
    public decimal GratuityAmount { get; set; }

    [Display(Name = "تعويضات أخرى")]
    public decimal OtherCompensation { get; set; }

    [Display(Name = "إجمالي الخصومات")]
    public decimal TotalDeductions { get; set; }

    [Display(Name = "صافي التسوية")]
    public decimal NetSettlement { get; set; }

    [Display(Name = "الحالة")]
    public EosStatus Status { get; set; } = EosStatus.Draft;

    [Display(Name = "تاريخ الحساب")]
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }
}
