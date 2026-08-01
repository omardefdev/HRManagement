using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "تسوية الراتب")]
public class PayrollAdjustment : AuditableEntity
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تشغيل الرواتب")]
    public Guid? PayrollRunId { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "سبب التسوية مطلوب")]
    [Display(Name = "سبب التسوية")]
    public string? AdjustmentReason { get; set; }

    public PayrollRun? PayrollRun { get; set; }
}
