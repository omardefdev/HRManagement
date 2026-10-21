using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum ServiceTransactionType
{
    [Display(Name = "تعيين")]
    Appointment = 0,
    [Display(Name = "تثبيت")]
    Confirmation = 1,
    [Display(Name = "سلفة إجازة")]
    LeaveAdvance = 2,
    [Display(Name = "تحديث وظيفة")]
    JobUpdate = 3,
    [Display(Name = "إنهاء خدمة")]
    Separation = 4
}
