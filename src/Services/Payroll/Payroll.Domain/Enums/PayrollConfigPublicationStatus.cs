using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum PayrollConfigPublicationStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "منشور")]
    Published = 2,
    [Display(Name = "مُستبدَل")]
    Superseded = 3
}
