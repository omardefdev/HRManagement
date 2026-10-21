using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum LeaveType
{
    [Display(Name = "سنوية")]
    Annual,
    [Display(Name = "مرضية")]
    Sick,
    [Display(Name = "بدون أجر")]
    Unpaid,
    [Display(Name = "أمومة")]
    Maternity,
    [Display(Name = "أخرى")]
    Other
}
