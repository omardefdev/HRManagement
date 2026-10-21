using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum Gender
{
    [Display(Name = "ذكر")]
    Male,
    [Display(Name = "أنثى")]
    Female,
    [Display(Name = "أخرى")]
    Other
}
