using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>PLAN-57 (§2): what wrote an attendance day. Only <c>AttendanceDayWriter</c> sets it.</summary>
public enum AttendanceRecordSource
{
    [Display(Name = "إدخال يدوي")]
    Manual = 0,
    [Display(Name = "إدخال جماعي")]
    Bulk = 1,
    [Display(Name = "إجازة معتمدة")]
    Leave = 2,
    [Display(Name = "تصحيح معتمد")]
    Correction = 3
}
