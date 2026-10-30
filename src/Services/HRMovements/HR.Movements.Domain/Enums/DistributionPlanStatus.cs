using System.ComponentModel.DataAnnotations;

namespace HR.Movements.Domain.Enums;

public enum DistributionPlanStatus
{
    [Display(Name = "مسودة")]
    Draft,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "نشط")]
    Active,
}
