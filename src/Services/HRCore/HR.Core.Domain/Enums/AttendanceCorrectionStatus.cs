using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>PLAN-57 (§3): a correction of a locked attendance day waits, or was decided once.</summary>
public enum AttendanceCorrectionStatus
{
    [Display(Name = "بانتظار القرار")]
    Pending = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "مرفوض")]
    Rejected = 2
}

/// <summary>PLAN-57 (§3): a person asked for the correction, or a leave decision made it (D-LVA-12).</summary>
public enum AttendanceCorrectionSource
{
    [Display(Name = "طلب من مستخدم")]
    Manual = 0,
    [Display(Name = "قرار إجازة")]
    LeaveEvent = 1
}

/// <summary>PLAN-57 (§4): why a version of an employee's attendance summary was written. The event carries its name.</summary>
public enum AttendanceSummaryReason
{
    [Display(Name = "إغلاق الفترة")]
    Close = 0,
    [Display(Name = "تصحيح معتمد")]
    Correction = 1,
    [Display(Name = "قرار إجازة")]
    LeaveEvent = 2
}

/// <summary>PLAN-57 (D-LVA-9): what a close does with a working day that has no record.</summary>
public enum AttendanceUnrecordedDaysMode
{
    /// <summary>The close is refused with the list of what is missing.</summary>
    RequireComplete = 0,
    /// <summary>The missing working days are counted as absence.</summary>
    CountAsAbsent = 1,
    /// <summary>The missing working days are shown and counted nowhere.</summary>
    Ignore = 2
}
