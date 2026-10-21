using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum MaritalStatus
{
    [Display(Name = "أعزب")]
    Single,
    [Display(Name = "متزوج")]
    Married,
    [Display(Name = "مطلق")]
    Divorced,
    [Display(Name = "أرمل")]
    Widowed
}
