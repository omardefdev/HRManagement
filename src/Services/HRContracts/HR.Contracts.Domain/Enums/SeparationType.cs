using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum SeparationType
{
    [Display(Name = "استقالة")]
    Resignation,
    [Display(Name = "إنهاء")]
    Termination,
    [Display(Name = "انتهاء عقد")]
    EndOfContract,
    [Display(Name = "تقاعد")]
    Retirement,
    [Display(Name = "طبية")]
    Medical,
    [Display(Name = "قضائية")]
    Judicial,
    [Display(Name = "تأديبية")]
    Disciplinary
}
