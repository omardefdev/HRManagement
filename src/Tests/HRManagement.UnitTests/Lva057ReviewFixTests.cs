using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57, what the independent review found in HR.Core and what holds it now: a status history row that notes a status over the
/// same status is not a move (the end of a service, the leave that put an employee on leave); a waiting request does not write over
/// a day a leave took meanwhile; a repeat close reads the period's state again; the month's lock has one key per agency and month.
/// </summary>
public class Lva057ReviewFixTests
{
    private const string Reason = "كان في مهمة رسمية موثقة بكتاب الإدارة";
    private static readonly DateOnly March = Lva057Db.MarchStart;
    private static readonly DateTime MidJanuary = new(2025, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- the end of a service

    [Fact]
    public void The_end_of_service_is_the_last_row_that_moved_the_employee_into_an_ended_status()
    {
        var march = new DateTime(2025, 3, 20, 9, 0, 0, DateTimeKind.Utc);

        // He left in January; in March a leftover leave's cancellation noted «Terminated» over «Terminated».
        Assert.Equal(MidJanuary, AttendanceEligibility.EndOfService([("Active", MidJanuary), ("Terminated", march)]));
        // Rehired and left again: the later move is the end.
        Assert.Equal(march, AttendanceEligibility.EndOfService([("Active", MidJanuary), ("OnLeave", march)]));
        // A status that ends a service over another that does (resigned, then recorded as deceased) is not a new end.
        Assert.Equal(MidJanuary, AttendanceEligibility.EndOfService([("Active", MidJanuary), ("Resigned", march)]));
        // An old record with no old status is a move.
        Assert.Equal(MidJanuary, AttendanceEligibility.EndOfService([(null, MidJanuary)]));
        // Only notes are left of an old record: the earliest is the end.
        Assert.Equal(MidJanuary, AttendanceEligibility.EndOfService([("Terminated", march), ("Terminated", MidJanuary)]));
        Assert.Null(AttendanceEligibility.EndOfService([]));
    }

    [Fact]
    public async Task A_later_note_of_an_ended_status_does_not_bring_a_leaver_back_into_the_months_after_he_left()
    {
        await using var test = await Lva057Db.CreateAsync();
        var leaver = await test.EmployeeAsync(test.Branch1);
        await test.EndServiceAsync(leaver, MidJanuary);
        // What LeaveCancelledConsumer and the medical consumers write: the status they found, over itself.
        test.Db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = leaver,
            OldStatus = "Terminated",
            NewStatus = "Terminated",
            ChangedAt = new DateTime(2025, 3, 10, 9, 0, 0, DateTimeKind.Utc),
            Reason = $"LeaveCancelled:{Guid.NewGuid()} Source=HR.Contracts"
        });
        await test.Db.SaveChangesAsync();
        var eligibility = new AttendanceEligibility(test.Db);

        var january = await eligibility.GetAsync(test.Branch1, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var february = await eligibility.GetAsync(test.Branch1, new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28));
        var march = await eligibility.GetAsync(test.Branch1, March, Lva057Db.MarchEnd);
        var one = await eligibility.GetOneAsync(leaver);

        Assert.Equal(new DateOnly(2025, 1, 15), Assert.Single(january).EndOfService);
        Assert.Empty(february);
        Assert.Empty(march);
        Assert.Equal(new DateOnly(2025, 1, 15), one!.EndOfService);
    }

    // ---------------------------------------------------------------- the reconciler

    [Fact]
    public async Task The_reconciler_returns_an_employee_whose_leave_ended_whatever_was_noted_while_he_was_on_leave()
    {
        await using var test = await Lva057Db.CreateAsync();
        var today = new DateOnly(2025, 3, 10);
        var employee = await test.EmployeeAsync(test.Branch1);
        var entity = await test.Db.Employees.FirstAsync(e => e.Id == employee);
        entity.CurrentStatus = EmploymentStatus.OnLeave;
        test.Db.EmploymentStatusHistories.AddRange(
            new EmploymentStatusHistory
            {
                EmployeeId = employee,
                OldStatus = "Active",
                NewStatus = "OnLeave",
                ChangedAt = new DateTime(2025, 3, 1, 8, 0, 0, DateTimeKind.Utc),
                Reason = $"LeaveApproved:{Guid.NewGuid()} Source=HR.Contracts"
            },
            // Another, future leave of his was cancelled while he was on leave: the consumer noted «on leave» over «on leave».
            new EmploymentStatusHistory
            {
                EmployeeId = employee,
                OldStatus = "OnLeave",
                NewStatus = "OnLeave",
                ChangedAt = new DateTime(2025, 3, 5, 8, 0, 0, DateTimeKind.Utc),
                Reason = $"LeaveCancelled:{Guid.NewGuid()} Source=HR.Contracts"
            });
        await test.Db.SaveChangesAsync();
        var reconciler = new LeaveScheduleReconciler(test.Db, NullLogger<LeaveScheduleReconciler>.Instance);

        var result = await reconciler.ReconcileAsync(today);

        Assert.Equal(1, result.ReturnedToActive);
        await using var db = test.Fresh();
        Assert.Equal(EmploymentStatus.Active, await db.Employees.Where(e => e.Id == employee).Select(e => e.CurrentStatus).FirstAsync());
    }

    // ---------------------------------------------------------------- a request that waited while a leave took the day

    [Fact]
    public async Task A_waiting_request_is_not_approved_over_a_day_a_leave_took_meanwhile()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var day = new DateOnly(2025, 3, 4);
        await test.FillAsync(employee, March, Lva057Db.MarchEnd);
        Assert.True((await test.PeriodService().CloseAsync(March, test.Branch1, test.Central())).Ok);
        var service = test.CorrectionService();
        var requested = await service.RequestAsync(test.Branch1Manager(), employee, day, DailyAttendanceStatus.Absent, Reason);
        Assert.True(requested.Ok, requested.Error);
        // An approved leave took the day (a system correction applied it): the day is the leave's.
        await using (var db = test.Fresh())
        {
            var record = await db.DailyAttendanceRecords.FirstAsync(r => r.EmployeeId == employee && r.AttendanceDate == day);
            record.Status = DailyAttendanceStatus.OnLeave;
            record.Source = AttendanceRecordSource.Leave;
            record.SourceReference = AttendanceDayWriter.LeaveReference(Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        test.Db.ChangeTracker.Clear();
        var approved = await test.CorrectionService().ApproveAsync(test.Central(), requested.Correction!.Id, "معتمد");

        Assert.Equal((AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.LeaveMadeDay), (approved.Outcome, approved.Error));
        await using var check = test.Fresh();
        var stored = await check.DailyAttendanceRecords.AsNoTracking().FirstAsync(r => r.EmployeeId == employee && r.AttendanceDate == day);
        Assert.Equal((DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave), (stored.Status, stored.Source));
        Assert.Equal(1, (await check.AttendanceEmployeeSummaries.AsNoTracking().SingleAsync(s => s.EmployeeId == employee)).Version);
        Assert.Equal(AttendanceCorrectionStatus.Pending, (await check.AttendanceCorrections.AsNoTracking().SingleAsync()).Status);
    }

    // ---------------------------------------------------------------- the period's state

    [Fact]
    public async Task A_repeat_close_reads_the_periods_state_again()
    {
        await using var test = await Lva057Db.CreateAsync();
        var first = await test.EmployeeAsync(test.Branch1);
        var second = await test.EmployeeAsync(test.Branch2);
        await test.FillAsync(first, March, Lva057Db.MarchEnd);
        await test.FillAsync(second, March, Lva057Db.MarchEnd);
        Assert.True((await test.PeriodService().CloseAsync(March, test.Branch1, test.Central())).Ok);
        Assert.True((await test.PeriodService().CloseAsync(March, test.Branch2, test.Central())).Ok);
        // Two closes at once can each leave «partially closed» on a period they closed between them: that state, as stored data.
        await using (var db = test.Fresh())
        {
            var period = await db.AttendancePeriods.SingleAsync();
            period.Status = AttendancePeriodStatus.PartiallyClosed;
            period.ClosedAt = null;
            period.ClosedBy = null;
            await db.SaveChangesAsync();
        }

        test.Db.ChangeTracker.Clear();
        test.Published.Clear();
        var again = await test.PeriodService().CloseAsync(March, test.Branch1, test.Central());

        Assert.Equal((true, 0, AttendancePeriodStatus.Closed), (again.Ok, again.ClosedEmployees, again.Status));
        await using var check = test.Fresh();
        var stored = await check.AttendancePeriods.AsNoTracking().SingleAsync();
        Assert.Equal(AttendancePeriodStatus.Closed, stored.Status);
        Assert.NotNull(stored.ClosedAt);
        // Nothing else was written: no closure row, no summary, no event.
        Assert.Equal(2, await check.AttendancePeriodClosures.CountAsync());
        Assert.Equal(2, await check.AttendanceEmployeeSummaries.CountAsync());
        Assert.Empty(test.Published);
    }

    // ---------------------------------------------------------------- the month's lock

    [Fact]
    public void The_months_lock_has_one_key_per_agency_and_month_in_one_order_for_everyone()
    {
        var agency = Guid.Parse("a0000001-0000-4000-8000-000000000001");
        var other = Guid.Parse("a0000002-0000-4000-8000-000000000001");

        Assert.Equal(AttendanceMonthLock.Key(agency, 2025, 3), AttendanceMonthLock.Key(agency, 2025, 3));
        Assert.NotEqual(AttendanceMonthLock.Key(agency, 2025, 3), AttendanceMonthLock.Key(agency, 2025, 4));
        Assert.NotEqual(AttendanceMonthLock.Key(agency, 2025, 3), AttendanceMonthLock.Key(other, 2025, 3));
        // A period inside a month is one key; a period over three months is three, ascending whatever the months' order.
        Assert.Single(AttendanceMonthLock.Keys(agency, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31)));
        var three = AttendanceMonthLock.Keys(agency, new DateOnly(2025, 11, 20), new DateOnly(2026, 1, 10));
        Assert.Equal(3, three.Count);
        Assert.Equal(three.Order(), three);
    }
}
