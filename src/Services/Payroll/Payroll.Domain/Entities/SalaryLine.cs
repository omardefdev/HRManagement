using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "بند الراتب")]
public class SalaryLine : AuditableEntity
{
    [Required(ErrorMessage = "تشغيل الرواتب مطلوب")]
    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الراتب الإجمالي")]
    public decimal GrossPay { get; set; }

    [Display(Name = "إجمالي الخصومات")]
    public decimal TotalDeductions { get; set; }

    [Display(Name = "صافي الراتب")]
    public decimal NetPay { get; set; }

    [Display(Name = "مكونات الراتب")]
    public string? PayComponents { get; set; }

    public PayrollRun PayrollRun { get; set; } = null!;
}
