using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57 (§1): the close of an attendance period. Who it covers, what it counts, what it refuses, that a repeat changes nothing,
/// the period's status, and that everything it writes goes in one save with one event per summary. (The outbox's own atomicity and
/// two concurrent closes are proved on PostgreSQL by <c>Lva057AttendanceCloseIntegrationTests</c>.)
/// </summary>
public class Lva057AttendanceCloseTests
{
    private static readonly DateOnly March = Lva057Db.MarchStart;

    private static async Task<(int Periods, int Summaries, int Closures)> StoredAsync(Lva057Db test)
    {
        await using var db = test.Fresh();
        return (await db.AttendancePeriods.CountAsync(), await db.AttendanceEmployeeSummaries.CountAsync(), await db.AttendancePeriodClosures.CountAsync());
    }

    [Fact]
    public async Task A_close_covers_the_units_tree_and_who_left_inside_the_month_and_counts_working_days_only()
    {
        await using var test = await Lva057Db.CreateAsync();
        var direct = await test.EmployeeAsync(test.Branch1);
        var inSubUnit = await test.EmployeeAsync(test.Branch1Sub);
        var leftInside = await test.EmployeeAsync(test.Branch1);
        await test.EndServiceAsync(leftInside, new DateTime(2025, 3, 13, 9, 0, 0, DateTimeKind.Utc));
        var leftBefore = await test.EmployeeAsync(test.Branch1);
        await test.EndServiceAsync(leftBefore, new DateTime(2025, 2, 20, 9, 0, 0, DateTimeKind.Utc));
        var hiredAfter = await test.EmployeeAsync(test.Branch1, hire: new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var otherBranch = await test.EmployeeAsync(test.Branch2);
        await test.FillAsync(direct, March, Lva057Db.MarchEnd);
        await test.FillAsync(inSubUnit, March, Lva057Db.MarchEnd);
        await test.FillAsync(leftInside, March, new DateOnly(2025, 3, 13));
        // Two absences on working days, and one on a Friday that the old bulk entry would have written: it counts nowhere.
        await using (var db = test.Fresh())
        {
            foreach (var day in new[] { new DateOnly(2025, 3, 4), new DateOnly(2025, 3, 5) })
                (await db.DailyAttendanceRecords.FirstAsync(r => r.EmployeeId == direct && r.AttendanceDate == day)).Status = DailyAttendanceStatus.Absent;
            await db.SaveChangesAsync();
        }

        await test.RecordAsync(direct, new DateOnly(2025, 3, 7), DailyAttendanceStatus.Absent);
        var savesBefore = test.Saves.Count;
        var actor = test.Branch1Manager();

        var result = await test.PeriodService().CloseAsync(March, test.Branch1, actor);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(3, result.ClosedEmployees);
        Assert.Equal(0, result.SkippedAlreadyClosed);
        Assert.Equal(AttendancePeriodStatus.PartiallyClosed, result.Status);
        // Rule 7: one save writes the period, the three summaries, the closure and what was staged.
        Assert.Equal(savesBefore + 1, test.Saves.Count);
        Assert.Equal(3, test.Published.Count);

        await using var check = test.Fresh();
        var summaries = await check.AttendanceEmployeeSummaries.AsNoTracking().ToDictionaryAsync(s => s.EmployeeId);
        Assert.Equal(new[] { direct, inSubUnit, leftInside }.Order(), summaries.Keys.Order());
        Assert.False(summaries.ContainsKey(leftBefore));
        Assert.False(summaries.ContainsKey(hiredAfter));
        Assert.False(summaries.ContainsKey(otherBranch));
        Assert.Equal((1, 22, 20, 2, 0), (summaries[direct].Version, summaries[direct].WorkingDays, summaries[direct].PresentDays, summaries[direct].AbsentDays, summaries[direct].UnrecordedWorkingDays));
        Assert.Equal((22, 22, 0), (summaries[inSubUnit].WorkingDays, summaries[inSubUnit].PresentDays, summaries[inSubUnit].AbsentDays));
        // His working days end with his service: 2..6 and 9..13 March.
        Assert.Equal((10, 10, 0, 0), (summaries[leftInside].WorkingDays, summaries[leftInside].PresentDays, summaries[leftInside].AbsentDays, summaries[leftInside].UnrecordedWorkingDays));

        var closure = await check.AttendancePeriodClosures.AsNoTracking().SingleAsync();
        Assert.Equal(test.Branch1, closure.OrgUnitId);
        Assert.Equal(actor.UserId, closure.ClosedBy);
        Assert.Equal(3, closure.EmployeeCount);

        // The event of the employee with absences: version 1, absolute values, the period, the reason.
        var message = test.Published.Single(m => Lva057Db.Field<Guid>(m, "EmployeeId") == direct);
        Assert.Equal(summaries[direct].Id, Lva057Db.Field<Guid>(message, "SummaryId"));
        Assert.Equal(1, Lva057Db.Field<int>(message, "Version"));
        Assert.Equal(2, Lva057Db.Field<int>(message, "AbsentDays"));
        Assert.Equal(22, Lva057Db.Field<int>(message, "WorkingDays"));
        Assert.Equal(March, Lva057Db.Field<DateOnly>(message, "PeriodStart"));
        Assert.Equal(Lva057Db.MarchEnd, Lva057Db.Field<DateOnly>(message, "PeriodEnd"));
        Assert.Equal("Close", Lva057Db.Field<string>(message, "Reason"));
        Assert.Null(Lva057Db.NullableField<Guid>(message, "CorrectionId"));
        Assert.Equal("HR.Core", Lva057Db.Field<string>(message, "SourceSystem"));

        // The days are locked now, for every writer.
        Assert.True(await test.Locks.IsLockedAsync(direct, new DateOnly(2025, 3, 4)));
        Assert.False(await test.Locks.IsLockedAsync(otherBranch, new DateOnly(2025, 3, 4)));
        Assert.False(await test.Locks.IsLockedAsync(direct, new DateOnly(2025, 4, 1)));
    }

    [Fact]
    public async Task With_RequireComplete_a_missing_working_day_refuses_the_whole_close_and_nothing_is_written()
    {
        await using var test = await Lva057Db.CreateAsync();
        var complete = await test.EmployeeAsync(test.Branch1);
        var missing = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(complete, March, Lva057Db.MarchEnd);
        await test.FillAsync(missing, March, new DateOnly(2025, 3, 27));
        var savesBefore = test.Saves.Count;

        var result = await test.PeriodService().CloseAsync(March, test.Branch1, test.Central());

        Assert.Equal(AttendanceCloseOutcome.Incomplete, result.Outcome);
        // 30 and 31 March are working days with no record.
        Assert.Equal([new AttendanceCloseIncomplete(missing, 2)], result.Incomplete);
        Assert.Contains("1", result.Error);
        Assert.Equal((0, 0, 0), await StoredAsync(test));
        Assert.Empty(test.Published);
        Assert.Equal(savesBefore, test.Saves.Count);
        Assert.False(test.Db.ChangeTracker.HasChanges(), "a refused close left something for a later save");
        Assert.False(await test.Locks.IsLockedAsync(complete, new DateOnly(2025, 3, 4)));
    }

    [Fact]
    public async Task CountAsAbsent_counts_the_missing_days_as_absence_and_Ignore_shows_them_only()
    {
        await using var counted = await Lva057Db.CreateAsync();
        var employee = await counted.EmployeeAsync(counted.Branch1);
        await counted.FillAsync(employee, March, new DateOnly(2025, 3, 27));
        Assert.True((await counted.PeriodService(AttendanceUnrecordedDaysMode.CountAsAbsent).CloseAsync(March, counted.Branch1, counted.Central())).Ok);
        await using (var db = counted.Fresh())
        {
            var summary = await db.AttendanceEmployeeSummaries.AsNoTracking().SingleAsync();
            Assert.Equal((22, 20, 2, 2), (summary.WorkingDays, summary.PresentDays, summary.AbsentDays, summary.UnrecordedWorkingDays));
        }

        Assert.Equal(2, Lva057Db.Field<int>(counted.Published.Single(), "AbsentDays"));

        await using var ignored = await Lva057Db.CreateAsync();
        var other = await ignored.EmployeeAsync(ignored.Branch1);
        await ignored.FillAsync(other, March, new DateOnly(2025, 3, 27));
        Assert.True((await ignored.PeriodService(AttendanceUnrecordedDaysMode.Ignore).CloseAsync(March, ignored.Branch1, ignored.Central())).Ok);
        await using (var db = ignored.Fresh())
        {
            var summary = await db.AttendanceEmployeeSummaries.AsNoTracking().SingleAsync();
            Assert.Equal((22, 20, 0, 2), (summary.WorkingDays, summary.PresentDays, summary.AbsentDays, summary.UnrecordedWorkingDays));
        }

        Assert.Equal(0, Lva057Db.Field<int>(ignored.Published.Single(), "AbsentDays"));
    }

    [Fact]
    public async Task A_second_close_skips_who_is_closed_and_writes_nothing()
    {
        await using var test = await Lva057Db.CreateAsync();
        var first = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(first, March, Lva057Db.MarchEnd);
        var service = test.PeriodService();
        Assert.True((await service.CloseAsync(March, test.Branch1, test.Central())).Ok);
        var savesAfterFirst = test.Saves.Count;

        var again = await service.CloseAsync(March, test.Branch1, test.Central());

        Assert.True(again.Ok);
        Assert.Equal(0, again.ClosedEmployees);
        Assert.Equal(1, again.SkippedAlreadyClosed);
        Assert.Equal((1, 1, 1), await StoredAsync(test));
        Assert.Single(test.Published);
        Assert.Equal(savesAfterFirst, test.Saves.Count);

        // A later arrival in the unit is closed by the next close, and the first employee is skipped and counted.
        var second = await test.EmployeeAsync(test.Branch1Sub);
        await test.FillAsync(second, March, Lva057Db.MarchEnd);
        var third = await service.CloseAsync(March, test.Branch1, test.Central());
        Assert.Equal((1, 1), (third.ClosedEmployees, third.SkippedAlreadyClosed));
        Assert.Equal((1, 2, 2), await StoredAsync(test));
        Assert.Equal(2, test.Published.Count);
        await using var db = test.Fresh();
        Assert.Equal(1, (await db.AttendanceEmployeeSummaries.AsNoTracking().SingleAsync(s => s.EmployeeId == first)).Version);
    }

    [Fact]
    public async Task The_period_is_partially_closed_until_no_eligible_employee_of_the_agency_lacks_a_summary()
    {
        await using var test = await Lva057Db.CreateAsync();
        var inBranch1 = await test.EmployeeAsync(test.Branch1);
        var inBranch2 = await test.EmployeeAsync(test.Branch2);
        var atRoot = await test.EmployeeAsync(test.Root);
        // Neither needs a summary: one left before the month, one has not started.
        var leftBefore = await test.EmployeeAsync(test.Branch2);
        await test.EndServiceAsync(leftBefore, new DateTime(2025, 1, 10, 9, 0, 0, DateTimeKind.Utc), EmploymentStatus.Resigned);
        await test.EmployeeAsync(test.Branch2, status: EmploymentStatus.PendingActivation);
        foreach (var employee in new[] { inBranch1, inBranch2, atRoot })
            await test.FillAsync(employee, March, Lva057Db.MarchEnd);
        var service = test.PeriodService();
        var central = test.Central();

        var branch1 = await service.CloseAsync(March, test.Branch1, central);
        Assert.Equal(AttendancePeriodStatus.PartiallyClosed, branch1.Status);
        await using (var db = test.Fresh())
            Assert.Null((await db.AttendancePeriods.AsNoTracking().SingleAsync()).ClosedAt);

        var agency = await service.CloseAsync(March, null, central);
        Assert.Equal(AttendancePeriodStatus.Closed, agency.Status);
        Assert.Equal((2, 1), (agency.ClosedEmployees, agency.SkippedAlreadyClosed));
        await using (var db = test.Fresh())
        {
            var period = await db.AttendancePeriods.AsNoTracking().SingleAsync();
            Assert.Equal(AttendancePeriodStatus.Closed, period.Status);
            Assert.NotNull(period.ClosedAt);
            Assert.Equal(central.UserId, period.ClosedBy);
            Assert.Equal(2, await db.AttendancePeriodClosures.CountAsync());
            Assert.Null((await db.AttendancePeriodClosures.AsNoTracking().OrderByDescending(c => c.EmployeeCount).FirstAsync()).OrgUnitId);
        }
    }

    [Fact]
    public async Task Closing_the_last_unit_closes_the_period()
    {
        await using var test = await Lva057Db.CreateAsync();
        var inBranch1 = await test.EmployeeAsync(test.Branch1);
        var inBranch2 = await test.EmployeeAsync(test.Branch2);
        await test.FillAsync(inBranch1, March, Lva057Db.MarchEnd);
        await test.FillAsync(inBranch2, March, Lva057Db.MarchEnd);
        var service = test.PeriodService();

        Assert.Equal(AttendancePeriodStatus.PartiallyClosed, (await service.CloseAsync(March, test.Branch1, test.Central())).Status);
        Assert.Equal(AttendancePeriodStatus.Closed, (await service.CloseAsync(March, test.Branch2, test.Central())).Status);
    }

    [Fact]
    public async Task A_branch_closes_its_own_tree_only()
    {
        await using var test = await Lva057Db.CreateAsync();
        var foreign = await test.EmployeeAsync(test.Branch2);
        await test.FillAsync(foreign, March, Lva057Db.MarchEnd);
        var service = test.PeriodService();
        var manager = test.Branch1Manager();

        var otherBranch = await service.CloseAsync(March, test.Branch2, manager);
        var wholeAgency = await service.CloseAsync(March, null, manager);
        var parent = await service.CloseAsync(March, test.Root, manager);
        var unknown = await service.CloseAsync(March, Guid.NewGuid(), manager);

        Assert.Equal(AttendanceCloseOutcome.OutOfScope, otherBranch.Outcome);
        Assert.Equal(AttendanceCloseOutcome.OutOfScope, wholeAgency.Outcome);
        Assert.Equal(AttendanceCloseOutcome.OutOfScope, parent.Outcome);
        // A unit that is not in the caller's agency does not exist for him: 404, before any question of scope.
        Assert.Equal(AttendanceCloseOutcome.UnitNotFound, unknown.Outcome);
        Assert.Equal((0, 0, 0), await StoredAsync(test));
        Assert.Empty(test.Published);

        // His own sub-unit alone is his to close.
        var own = await test.EmployeeAsync(test.Branch1Sub);
        await test.FillAsync(own, March, Lva057Db.MarchEnd);
        Assert.True((await service.CloseAsync(March, test.Branch1Sub, manager)).Ok);
    }

    [Fact]
    public async Task A_period_that_has_not_ended_or_is_not_a_month_is_refused()
    {
        // «Today» is 15 March 2025 in Damascus.
        await using var test = await Lva057Db.CreateAsync(new DateTimeOffset(2025, 3, 15, 9, 0, 0, TimeSpan.Zero));
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(employee, new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 15));

        var running = await test.PeriodService().CloseAsync(March, test.Branch1, test.Central());
        var notAMonth = await test.PeriodService().CloseAsync(new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 15), test.Branch1, test.Central());
        var reversed = await test.PeriodService().CloseAsync(new DateOnly(2025, 2, 28), new DateOnly(2025, 2, 1), test.Branch1, test.Central());

        Assert.Equal((AttendanceCloseOutcome.InvalidPeriod, AttendancePeriodMessages.NotEnded), (running.Outcome, running.Error));
        Assert.Equal((AttendanceCloseOutcome.InvalidPeriod, AttendancePeriodMessages.NotCalendarMonth), (notAMonth.Outcome, notAMonth.Error));
        Assert.Equal(AttendanceCloseOutcome.InvalidPeriod, reversed.Outcome);
        Assert.Equal((0, 0, 0), await StoredAsync(test));

        // February has ended: it closes.
        Assert.True((await test.PeriodService().CloseAsync(new DateOnly(2025, 2, 1), test.Branch1, test.Central())).Ok);
        Assert.True(AttendancePeriodService.TryParseMonth("2025-02", out var start, out var end));
        Assert.Equal((new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28)), (start, end));
        Assert.False(AttendancePeriodService.TryParseMonth("2025-13", out _, out _));
        Assert.False(AttendancePeriodService.TryParseMonth("02/2025", out _, out _));
    }

    [Fact]
    public async Task Custom_periods_stay_behind_the_setting_and_never_overlap()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(employee, new DateOnly(2025, 2, 1), new DateOnly(2025, 4, 30));
        var custom = test.PeriodService(periods: AttendancePeriodMode.Custom);

        // 16 February .. 15 March, a custom period.
        Assert.True((await custom.CloseAsync(new DateOnly(2025, 2, 16), new DateOnly(2025, 3, 15), test.Branch1, test.Central())).Ok);
        var overlapping = await custom.CloseAsync(March, Lva057Db.MarchEnd, test.Branch1, test.Central());
        var next = await custom.CloseAsync(new DateOnly(2025, 3, 16), new DateOnly(2025, 4, 15), test.Branch1, test.Central());

        Assert.Equal((AttendanceCloseOutcome.InvalidPeriod, AttendancePeriodMessages.Overlap), (overlapping.Outcome, overlapping.Error));
        Assert.True(next.Ok);
        Assert.Equal((2, 2, 2), await StoredAsync(test));
        // The lock follows the period's own bounds.
        Assert.True(await test.Locks.IsLockedAsync(employee, new DateOnly(2025, 4, 15)));
        Assert.False(await test.Locks.IsLockedAsync(employee, new DateOnly(2025, 4, 16)));
    }

    [Fact]
    public async Task A_branch_reads_its_own_tree_in_a_periods_status()
    {
        await using var test = await Lva057Db.CreateAsync();
        var inBranch1 = await test.EmployeeAsync(test.Branch1);
        var inSub = await test.EmployeeAsync(test.Branch1Sub);
        var inBranch2 = await test.EmployeeAsync(test.Branch2);
        foreach (var employee in new[] { inBranch1, inBranch2 })
            await test.FillAsync(employee, March, Lva057Db.MarchEnd);
        var service = test.PeriodService(AttendanceUnrecordedDaysMode.Ignore);
        Assert.True((await service.CloseAsync(March, test.Branch1Sub, test.Central())).Ok);
        Assert.True((await service.CloseAsync(March, test.Branch2, test.Central())).Ok);

        var branch = await service.GetStatusAsync(March, test.Branch1Manager());
        var central = await service.GetStatusAsync(March, test.Central());
        var unopened = await service.GetStatusAsync(new DateOnly(2025, 4, 1), test.Central());

        Assert.Equal(AttendancePeriodStatus.PartiallyClosed, branch.Status);
        Assert.Equal((1, 1), (branch.ClosedEmployees, branch.RemainingEmployees));
        Assert.Equal(inSub, Assert.Single(branch.Summaries).EmployeeId);
        Assert.Equal((2, 1), (central.ClosedEmployees, central.RemainingEmployees));
        Assert.Equal((AttendancePeriodStatus.Open, 0, 3), (unopened.Status, unopened.ClosedEmployees, unopened.RemainingEmployees));
        Assert.Null(unopened.PeriodId);
    }
}
