using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Attendance;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57 (§1, rules 4 and 5): an employee's attendance summary is counted on working days only. A rest day, a configured holiday,
/// a day before the hire or after the end of service is no working day; a record on such a day enters no count; a working day with
/// no record is unrecorded, and is absence only when the setting says so.
/// </summary>
public class Lva057AttendanceSummaryCalculatorTests
{
    // March 2025: 31 days, Fridays 7/14/21/28, Saturdays 1/8/15/22/29 -> 22 working days with no holiday.
    private static readonly DateOnly Start = new(2025, 3, 1);
    private static readonly DateOnly End = new(2025, 3, 31);

    private static AttendanceCalendar Calendar(params string[] holidays) =>
        new(new AttendanceCalendarOptions { WeeklyRestDays = "Friday,Saturday", Holidays = [.. holidays] });

    private static Dictionary<DateOnly, DailyAttendanceStatus> Fill(
        AttendanceCalendar calendar, DailyAttendanceStatus status, DateOnly? from = null, DateOnly? to = null)
    {
        var days = new Dictionary<DateOnly, DailyAttendanceStatus>();
        for (var d = from ?? Start; d <= (to ?? End); d = d.AddDays(1))
        {
            if (calendar.IsWorkingDay(d))
                days[d] = status;
        }

        return days;
    }

    private static AttendanceSummaryFigures Calculate(
        Dictionary<DateOnly, DailyAttendanceStatus> records,
        AttendanceCalendar calendar,
        DateOnly? hire = null,
        DateOnly? endOfService = null,
        AttendanceUnrecordedDaysMode mode = AttendanceUnrecordedDaysMode.RequireComplete) =>
        AttendanceSummaryCalculator.Calculate(records, Start, End, calendar, hire, endOfService, mode);

    [Fact]
    public void A_full_month_counts_its_working_days_only()
    {
        var calendar = Calendar();
        var records = Fill(calendar, DailyAttendanceStatus.Present);
        records[new DateOnly(2025, 3, 4)] = DailyAttendanceStatus.Absent;
        records[new DateOnly(2025, 3, 5)] = DailyAttendanceStatus.Absent;
        records[new DateOnly(2025, 3, 6)] = DailyAttendanceStatus.Remote;
        records[new DateOnly(2025, 3, 9)] = DailyAttendanceStatus.OnLeave;

        var figures = Calculate(records, calendar);

        Assert.Equal(new AttendanceSummaryFigures(22, 18, 2, 1, 1, 0), figures);
    }

    [Fact]
    public void A_record_on_a_rest_day_enters_no_count()
    {
        var calendar = Calendar();
        var records = Fill(calendar, DailyAttendanceStatus.Present);
        // The old bulk entry wrote every day of the month: absences on a Friday and a Saturday are not absences.
        records[new DateOnly(2025, 3, 7)] = DailyAttendanceStatus.Absent;
        records[new DateOnly(2025, 3, 8)] = DailyAttendanceStatus.Absent;

        var figures = Calculate(records, calendar);

        Assert.Equal(22, figures.WorkingDays);
        Assert.Equal(22, figures.PresentDays);
        Assert.Equal(0, figures.AbsentDays);
    }

    [Fact]
    public void A_configured_holiday_is_not_a_working_day_and_its_record_enters_no_count()
    {
        var calendar = Calendar("2025-03-30", "2025-03-31");
        var records = Fill(Calendar(), DailyAttendanceStatus.Present);
        records[new DateOnly(2025, 3, 30)] = DailyAttendanceStatus.Absent;

        var figures = Calculate(records, calendar);

        Assert.Equal(20, figures.WorkingDays);
        Assert.Equal(20, figures.PresentDays);
        Assert.Equal(0, figures.AbsentDays);
        Assert.Equal(0, figures.UnrecordedWorkingDays);
    }

    [Fact]
    public void A_hire_inside_the_month_starts_the_working_days_at_the_hire_date()
    {
        var calendar = Calendar();
        var hire = new DateOnly(2025, 3, 16);
        var records = Fill(calendar, DailyAttendanceStatus.Present, from: hire);
        // A record before the hire (a mistake) counts nowhere.
        records[new DateOnly(2025, 3, 10)] = DailyAttendanceStatus.Absent;

        var figures = Calculate(records, calendar, hire: hire);

        // 16..31 March: 16,17,18,19,20, 23,24,25,26,27, 30,31 = 12 working days.
        Assert.Equal(new AttendanceSummaryFigures(12, 12, 0, 0, 0, 0), figures);
    }

    [Fact]
    public void An_end_of_service_inside_the_month_ends_the_working_days_on_that_day()
    {
        var calendar = Calendar();
        var left = new DateOnly(2025, 3, 13);
        var records = Fill(calendar, DailyAttendanceStatus.Present, to: left);
        records[new DateOnly(2025, 3, 12)] = DailyAttendanceStatus.Absent;

        var figures = Calculate(records, calendar, endOfService: left);

        // 2..6 and 9..13 March = 10 working days; nothing is missing after the last day of service.
        Assert.Equal(new AttendanceSummaryFigures(10, 9, 1, 0, 0, 0), figures);
    }

    [Fact]
    public void A_hire_and_an_end_of_service_outside_the_month_change_nothing()
    {
        var calendar = Calendar();
        var records = Fill(calendar, DailyAttendanceStatus.Present);

        var figures = Calculate(records, calendar, hire: new DateOnly(2020, 1, 1), endOfService: new DateOnly(2025, 6, 30));

        Assert.Equal(22, figures.WorkingDays);
        Assert.Equal(22, figures.PresentDays);
    }

    [Fact]
    public void A_working_day_with_no_record_is_unrecorded_and_is_not_absence_unless_the_setting_says_so()
    {
        var calendar = Calendar();
        var records = Fill(calendar, DailyAttendanceStatus.Present);
        records.Remove(new DateOnly(2025, 3, 18));
        records.Remove(new DateOnly(2025, 3, 19));
        records[new DateOnly(2025, 3, 20)] = DailyAttendanceStatus.Absent;

        var required = Calculate(records, calendar, mode: AttendanceUnrecordedDaysMode.RequireComplete);
        var ignored = Calculate(records, calendar, mode: AttendanceUnrecordedDaysMode.Ignore);
        var counted = Calculate(records, calendar, mode: AttendanceUnrecordedDaysMode.CountAsAbsent);

        Assert.Equal(new AttendanceSummaryFigures(22, 19, 1, 0, 0, 2), required);
        Assert.Equal(required, ignored);
        Assert.Equal(new AttendanceSummaryFigures(22, 19, 3, 0, 0, 2), counted);
    }

    [Fact]
    public void An_employee_with_no_working_day_in_the_month_has_an_empty_summary()
    {
        var calendar = Calendar();

        // Hired after the month's end: the close leaves him out, and the calculator would give him nothing either.
        var figures = Calculate([], calendar, hire: new DateOnly(2025, 4, 1));

        Assert.Equal(new AttendanceSummaryFigures(0, 0, 0, 0, 0, 0), figures);
    }

    [Fact]
    public void The_calendar_reads_its_days_from_configuration_and_refuses_a_value_it_cannot_read()
    {
        var calendar = new AttendanceCalendar(new AttendanceCalendarOptions { WeeklyRestDays = " friday , Sunday ", Holidays = ["2025-03-03", " "] });

        Assert.False(calendar.IsWorkingDay(new DateOnly(2025, 3, 7)));   // Friday
        Assert.False(calendar.IsWorkingDay(new DateOnly(2025, 3, 9)));   // Sunday
        Assert.True(calendar.IsWorkingDay(new DateOnly(2025, 3, 8)));    // Saturday is a working day here
        Assert.False(calendar.IsWorkingDay(new DateOnly(2025, 3, 3)));   // the holiday
        Assert.True(calendar.HasHolidayIn(Start, End));
        Assert.False(calendar.HasHolidayIn(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30)));

        Assert.Throws<InvalidOperationException>(() => new AttendanceCalendar(new AttendanceCalendarOptions { WeeklyRestDays = "Friday,Caturday" }));
        Assert.Throws<InvalidOperationException>(() => new AttendanceCalendar(new AttendanceCalendarOptions { Holidays = ["31/03/2025"] }));
        Assert.Throws<InvalidOperationException>(() => new AttendanceCalendar(new AttendanceCalendarOptions
        {
            WeeklyRestDays = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday"
        }));
    }

    [Fact]
    public void The_defaults_are_friday_and_saturday_and_no_holiday()
    {
        var calendar = new AttendanceCalendar(new AttendanceCalendarOptions());

        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], calendar.WeeklyRestDays.OrderBy(d => d).ToArray());
        Assert.Empty(calendar.Holidays);
    }

    [Fact]
    public void Today_is_the_date_in_damascus_not_the_utc_date()
    {
        // 22:30 UTC on 30 June is 01:30 on 1 July in Damascus (UTC+3).
        var clock = new FixedClock(new DateTimeOffset(2025, 6, 30, 22, 30, 0, TimeSpan.Zero));
        var calendar = new AttendanceCalendar(new AttendanceCalendarOptions(), clock);

        Assert.Equal(new DateOnly(2025, 7, 1), calendar.Today);
        Assert.Equal(new DateOnly(2025, 7, 1), AttendanceCalendar.LocalDate(new DateTime(2025, 6, 30, 22, 30, 0, DateTimeKind.Utc)));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
