using HR.Core.Domain.Enums;

namespace HR.Core.Infrastructure.Services.Attendance;

/// <summary>PLAN-57 (D-LVA-9, D-LVA-11, D-LVA-12, D-LVA-13): the attendance settings, section <c>Attendance</c>.</summary>
public sealed class AttendanceOptions
{
    public const string SectionName = "Attendance";

    public AttendancePeriodsOptions Periods { get; set; } = new();

    public AttendanceCloseOptions Close { get; set; } = new();

    public AttendanceCorrectionsOptions Corrections { get; set; } = new();

    public AttendanceCalendarOptions Calendar { get; set; } = new();
}

/// <summary>D-LVA-11: the period is a calendar month; periods of other bounds that do not overlap stay behind <c>Custom</c>.</summary>
public enum AttendancePeriodMode
{
    CalendarMonth = 0,
    Custom = 1
}

/// <summary>D-LVA-12: what an approved or cancelled leave does to a locked day.</summary>
public enum AttendanceLeaveEventMode
{
    /// <summary>A system correction, approved at once: the leave decision is the authority.</summary>
    AutoApply = 0,
    /// <summary>A system correction that waits for a central person.</summary>
    Pending = 1
}

public sealed class AttendancePeriodsOptions
{
    public AttendancePeriodMode Mode { get; set; } = AttendancePeriodMode.CalendarMonth;
}

public sealed class AttendanceCloseOptions
{
    public AttendanceUnrecordedDaysMode UnrecordedWorkingDays { get; set; } = AttendanceUnrecordedDaysMode.RequireComplete;
}

public sealed class AttendanceCorrectionsOptions
{
    public AttendanceLeaveEventMode LeaveEventsAfterClose { get; set; } = AttendanceLeaveEventMode.AutoApply;
}

/// <summary>
/// D-LVA-13: the days that are not working days, until the central dated calendar exists (LVA-G). The weekly rest days are day
/// names separated by commas; the holidays are dates (<c>yyyy-MM-dd</c>) and are empty by default: no holiday lives in code.
/// </summary>
public sealed class AttendanceCalendarOptions
{
    public string WeeklyRestDays { get; set; } = "Friday,Saturday";

    public List<string> Holidays { get; set; } = [];
}
