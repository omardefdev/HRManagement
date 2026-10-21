using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum ProrationDaysBasis
{
    [Display(Name = "أيام عمل")]
    WorkingDays = 0,
    [Display(Name = "30 يوماً تقويمياً")]
    Calendar30 = 1,
}
