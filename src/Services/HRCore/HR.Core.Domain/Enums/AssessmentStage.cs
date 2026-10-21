using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-026: selection stage an assessment criteria item belongs to.</summary>
public enum AssessmentStage
{
    [Display(Name = "الفرز الأولي")]
    Screening = 0,
    [Display(Name = "الاختبار")]
    Exam = 1,
    [Display(Name = "المقابلة")]
    Interview = 2,
    [Display(Name = "الخبرة")]
    Experience = 3,
    [Display(Name = "أخرى")]
    Other = 4
}
