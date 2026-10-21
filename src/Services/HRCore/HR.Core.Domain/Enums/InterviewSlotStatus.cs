using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum InterviewSlotStatus
{
    [Display(Name = "مجدولة")]
    Scheduled = 0,
    [Display(Name = "مكتملة")]
    Completed = 1,
    [Display(Name = "ملغاة")]
    Cancelled = 2
}
