using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum SalaryComponentType
{
    [Display(Name = "راتب أساسي")]
    BaseSalary,
    [Display(Name = "علاوة")]
    Allowance,
    [Display(Name = "اقتطاع")]
    Deduction,
    [Display(Name = "تسوية")]
    Adjustment,
    [Display(Name = "ضريبة")]
    Tax
}
