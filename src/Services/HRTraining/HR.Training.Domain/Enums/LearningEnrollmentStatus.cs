using System.ComponentModel.DataAnnotations;

namespace HR.Training.Domain.Enums;

public enum LearningEnrollmentStatus
{
    [Display(Name = "ملتحق")]
    Enrolled = 0,

    [Display(Name = "قيد التعلّم")]
    InProgress = 1,

    [Display(Name = "مكتمل")]
    Completed = 2
}
