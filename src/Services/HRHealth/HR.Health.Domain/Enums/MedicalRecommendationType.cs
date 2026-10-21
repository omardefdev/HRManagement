using System.ComponentModel.DataAnnotations;

namespace HR.Health.Domain.Enums;

public enum MedicalRecommendationType
{
    [Display(Name = "لياقة للعمل")]
    FitnessForDuty,
    [Display(Name = "مهام محدودة")]
    LimitedDuties,
    [Display(Name = "تقاعد")]
    Retirement,
    [Display(Name = "تعويض")]
    Compensation,
    [Display(Name = "أخرى")]
    Other
}
