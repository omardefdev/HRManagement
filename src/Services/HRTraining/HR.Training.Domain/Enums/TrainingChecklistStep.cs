using System.ComponentModel.DataAnnotations;

namespace HR.Training.Domain.Enums;

public enum TrainingChecklistStep
{
    [Display(Name = "مسجّل")]
    Enrolled = 0,
    [Display(Name = "مكتمل")]
    Completed = 1,
    [Display(Name = "إصدار الشهادة")]
    CertificateIssued = 2,
    [Display(Name = "مُتحقَّق")]
    Verified = 3
}
