using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>PLAN-57 (§1, D-LVA-13): which days count. The attendance calendar answers it from configuration, never from a list in code.</summary>
public interface IAttendanceWorkingDays
{
    bool IsWorkingDay(DateOnly date);
}

/// <summary>PLAN-57 (§1): an employee's attendance in a period, counted on working days only. The values are absolute.</summary>
public readonly record struct AttendanceSummaryFigures(
    int WorkingDays,
    int PresentDays,
    int AbsentDays,
    int OnLeaveDays,
    int RemoteDays,
    int UnrecordedWorkingDays);

/// <summary>
/// PLAN-57 (§1, rules 4 and 5): the one computation of an employee's attendance summary: the close writes version 1 with it and an
/// approved correction writes the next version with it. A pure function of the employee's records, the period, the calendar, the
/// hire date and the end of service.
/// <list type="bullet">
/// <item>The working days are the period's days within [max(start, hire), min(end, end of service)] that the calendar calls working.</item>
/// <item>A record on a day that is not a working day stays where it is and enters no count.</item>
/// <item>A working day with no record is unrecorded; <see cref="AttendanceUnrecordedDaysMode.CountAsAbsent"/> adds those days to
/// the absence, the other modes leave the absence as recorded.</item>
/// </list>
/// </summary>
public static class AttendanceSummaryCalculator
{
    public static AttendanceSummaryFigures Calculate(
        IReadOnlyDictionary<DateOnly, DailyAttendanceStatus> recordsByDay,
        DateOnly periodStart,
        DateOnly periodEnd,
        IAttendanceWorkingDays calendar,
        DateOnly? hireDate,
        DateOnly? endOfService,
        AttendanceUnrecordedDaysMode unrecordedMode)
    {
        ArgumentNullException.ThrowIfNull(recordsByDay);
        ArgumentNullException.ThrowIfNull(calendar);

        int working = 0, present = 0, absent = 0, onLeave = 0, remote = 0, unrecorded = 0;
        foreach (var day in WorkingDays(periodStart, periodEnd, calendar, hireDate, endOfService))
        {
            working++;
            if (!recordsByDay.TryGetValue(day, out var status))
            {
                unrecorded++;
                continue;
            }

            switch (status)
            {
                case DailyAttendanceStatus.Present: present++; break;
                case DailyAttendanceStatus.Absent: absent++; break;
                case DailyAttendanceStatus.OnLeave: onLeave++; break;
                case DailyAttendanceStatus.Remote: remote++; break;
            }
        }

        if (unrecordedMode == AttendanceUnrecordedDaysMode.CountAsAbsent)
            absent += unrecorded;

        return new AttendanceSummaryFigures(working, present, absent, onLeave, remote, unrecorded);
    }

    /// <summary>The employee's working days in the period, in order (rule 4).</summary>
    public static IEnumerable<DateOnly> WorkingDays(
        DateOnly periodStart,
        DateOnly periodEnd,
        IAttendanceWorkingDays calendar,
        DateOnly? hireDate,
        DateOnly? endOfService)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var from = hireDate is { } hired && hired > periodStart ? hired : periodStart;
        var to = endOfService is { } ended && ended < periodEnd ? ended : periodEnd;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (calendar.IsWorkingDay(day))
                yield return day;
        }
    }
}
