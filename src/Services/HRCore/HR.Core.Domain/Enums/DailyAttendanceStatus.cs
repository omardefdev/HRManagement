using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum DailyAttendanceStatus
{
    [Display(Name = "حاضر")]
    Present = 0,
    [Display(Name = "غائب")]
    Absent = 1,
    [Display(Name = "في إجازة")]
    OnLeave = 2,
    [Display(Name = "عن بُعد")]
    Remote = 3
}
