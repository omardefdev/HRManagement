using System.ComponentModel.DataAnnotations;

namespace HR.Training.Domain.Enums;

public enum EnrollmentStatus
{
    [Display(Name = "مسجّل")]
    Enrolled,
    [Display(Name = "حاضر")]
    Attended,
    [Display(Name = "مكتمل")]
    Completed,
    [Display(Name = "ملغى")]
    Cancelled,
}