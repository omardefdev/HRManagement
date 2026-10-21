using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum EmploymentType
{
    [Display(Name = "دائم")]
    Permanent,
    [Display(Name = "إداري")]
    Administrative,
    [Display(Name = "مؤقت")]
    Temporary
}
