using System.ComponentModel.DataAnnotations;

namespace HR.Health.Domain.Enums;

public enum MedicalCommitteeRecommendation
{
    [Display(Name = "عودة للعمل")]
    ReturnToWork,
    [Display(Name = "مهام محدودة")]
    LimitedDuties,
    [Display(Name = "تقاعد")]
    Retirement,
    [Display(Name = "أخرى")]
    Other
}
