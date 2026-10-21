using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum PayrollRunType
{
    [Display(Name = "اعتيادي")]
    Regular = 0,
    [Display(Name = "تصحيح")]
    Correction = 1,
    [Display(Name = "تسوية نهائية")]
    FinalSettlement = 2,
}
