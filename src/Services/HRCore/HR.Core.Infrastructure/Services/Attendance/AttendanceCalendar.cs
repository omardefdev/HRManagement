using System.Globalization;
using HR.Core.Domain.Services;
using HRManagement.ServiceDefaults.Localization;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Attendance;

/// <summary>
/// PLAN-57 (§1, D-LVA-13): the attendance calendar, read from <see cref="AttendanceCalendarOptions"/>. A working day is a day that is
/// neither a weekly rest day nor a configured holiday. «Today» is the date in the application's zone (Damascus), never the UTC date.
/// A value that cannot be read (a day name, a date) stops the service at its first use rather than being skipped.
/// </summary>
public sealed class AttendanceCalendar : IAttendanceWorkingDays
{
    private readonly HashSet<DayOfWeek> _restDays;
    private readonly HashSet<DateOnly> _holidays;
    private readonly TimeProvider _clock;

    public AttendanceCalendar(IOptions<AttendanceOptions> options, TimeProvider? clock = null)
        : this(options.Value.Calendar, clock)
    {
    }

    public AttendanceCalendar(AttendanceCalendarOptions options, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _clock = clock ?? TimeProvider.System;
        _restDays = ParseRestDays(options.WeeklyRestDays);
        _holidays = ParseHolidays(options.Holidays);
    }

    public IReadOnlyCollection<DayOfWeek> WeeklyRestDays => _restDays;

    public IReadOnlyCollection<DateOnly> Holidays => _holidays;

    /// <summary>Today in the application's zone (Asia/Damascus by default).</summary>
    public DateOnly Today => DateOnly.FromDateTime(HrLocalTime.ToLocal(_clock.GetUtcNow(), HrLocalTime.Zone));

    /// <summary>The local date of a stored UTC instant (an end of service, for one).</summary>
    public static DateOnly LocalDate(DateTime storedUtc) =>
        DateOnly.FromDateTime(HrLocalTime.ToLocal(storedUtc, HrLocalTime.Zone));

    public bool IsWorkingDay(DateOnly date) => !_restDays.Contains(date.DayOfWeek) && !_holidays.Contains(date);

    /// <summary>Whether any holiday is configured inside the period: the close page warns when none is.</summary>
    public bool HasHolidayIn(DateOnly from, DateOnly to) => _holidays.Any(h => h >= from && h <= to);

    private static HashSet<DayOfWeek> ParseRestDays(string? text)
    {
        var days = new HashSet<DayOfWeek>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<DayOfWeek>(part, ignoreCase: true, out var day) || !Enum.IsDefined(day))
                throw new InvalidOperationException($"Attendance:Calendar:WeeklyRestDays holds '{part}', which is not a day of the week.");
            days.Add(day);
        }

        if (days.Count >= 7)
            throw new InvalidOperationException("Attendance:Calendar:WeeklyRestDays leaves no working day.");
        return days;
    }

    private static HashSet<DateOnly> ParseHolidays(IEnumerable<string>? values)
    {
        var holidays = new HashSet<DateOnly>();
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException($"Attendance:Calendar:Holidays holds '{value}', which is not a date (yyyy-MM-dd).");
            holidays.Add(date);
        }

        return holidays;
    }
}
