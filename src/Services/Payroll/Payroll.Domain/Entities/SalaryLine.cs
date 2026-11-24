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

    /// <summary>
    /// PLAN-55 (D-PAY-15): the employee's bank account in force at the run's snapshot, copied from
    /// <see cref="PayrollRunInputSnapshot.BankAccountId"/> when the line is built. Payment orders, the treasury file and FR-04 read this,
    /// never the profile and never «the current account»: a change of account after the calculation does not move this run's money.
    /// Null when the employee had no verified account then. It is not an input of the amount.
    /// </summary>
    [Display(Name = "الحساب المصرفي المثبَّت")]
    public Guid? BankAccountId { get; set; }

    public PayrollRun PayrollRun { get; set; } = null!;

    public EmployeeBankAccount? BankAccount { get; set; }
}
