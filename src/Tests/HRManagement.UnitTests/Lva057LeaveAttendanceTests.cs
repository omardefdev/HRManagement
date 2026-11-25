using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57 (§2): a leave decision and the attendance days. The leave consumers write the employee's status history and the days in
/// one save and are idempotent; a cancellation gives back only its own days; and the leave schedule reconciler reads only the days an
/// approved leave made, and returns to work only an employee a leave put on leave.
/// </summary>
public class Lva057LeaveAttendanceTests
{
    private static readonly DateTime Start = new(2025, 3, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2025, 3, 6, 0, 0, 0, DateTimeKind.Utc);

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(NewId.NextGuid());
        return context.Object;
    }

    private static ILeaveApproved Approved(Guid leave, Guid employee) => Mock.Of<ILeaveApproved>(m =>
        m.TransactionId == Guid.NewGuid() && m.LeaveRequestId == leave && m.EmployeeId == employee && m.ApprovedBy == Guid.NewGuid()
        && m.OccurredAt == new DateTime(2025, 3, 1, 8, 0, 0, DateTimeKind.Utc) && m.SourceSystem == "HR.Contracts" && m.LeaveType == "سنوية"
        && m.StartDate == Start && m.EndDate == End && m.DurationDays == 3);

    private static ILeaveCancelled Cancelled(Guid leave, Guid employee) => Mock.Of<ILeaveCancelled>(m =>
        m.TransactionId == Guid.NewGuid() && m.LeaveRequestId == leave && m.EmployeeId == employee && m.CancelledBy == Guid.NewGuid()
        && m.OccurredAt == new DateTime(2025, 3, 2, 8, 0, 0, DateTimeKind.Utc) && m.SourceSystem == "HR.Contracts" && m.LeaveType == "سنوية"
        && m.StartDate == Start && m.EndDate == End && m.DurationDays == 3);

    private static AttendanceIntegrationService Integration(Lva057Db test) => new(test.Writer);

    [Fact]
    public async Task An_approved_leave_writes_the_status_history_and_the_days_in_one_save()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        await test.RecordAsync(employee, new DateOnly(2025, 3, 5), DailyAttendanceStatus.Absent);
        var consumer = new LeaveApprovedConsumer(test.Db, Integration(test));
        var savesBefore = test.Saves.Count;

        await consumer.Consume(Context(Approved(leave, employee)));

        Assert.Equal(savesBefore + 1, test.Saves.Count);
        await using var db = test.Fresh();
        Assert.Equal(1, await db.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == employee && h.Reason!.StartsWith($"LeaveApproved:{leave}")));
        var days = await db.DailyAttendanceRecords.AsNoTracking().Where(r => r.EmployeeId == employee).OrderBy(r => r.AttendanceDate).ToListAsync();
        Assert.Equal(3, days.Count);
        Assert.All(days, d => Assert.Equal(DailyAttendanceStatus.OnLeave, d.Status));
        Assert.All(days, d => Assert.Equal(AttendanceRecordSource.Leave, d.Source));
        Assert.All(days, d => Assert.Equal($"LeaveRequestId={leave:D}", d.SourceReference));
        Assert.Equal(DailyAttendanceStatus.Absent, days[1].ReplacedStatus);
        Assert.Equal("إجازة معتمدة: سنوية", days[0].Notes);
    }

    [Fact]
    public async Task A_failed_save_leaves_neither_the_marker_nor_the_days_so_the_redelivery_does_the_whole_work()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        var consumer = new LeaveApprovedConsumer(test.Db, Integration(test));

        // Before this plan the marker was saved first: a failure after it left the leave with no days, and the redelivery skipped them.
        test.Saves.FailNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(Context(Approved(leave, employee))));
        await using (var db = test.Fresh())
        {
            Assert.Equal(0, await db.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == employee));
            Assert.Equal(0, await db.DailyAttendanceRecords.CountAsync(r => r.EmployeeId == employee));
        }

        // The redelivery, as the bus makes it: a new scope.
        await using var redelivery = test.Fresh();
        var again = new LeaveApprovedConsumer(redelivery, new AttendanceIntegrationService(
            new AttendanceDayWriter(redelivery, new AttendanceLockService(redelivery), test.Calendar, new AttendanceEligibility(redelivery))));
        await again.Consume(Context(Approved(leave, employee)));

        await using var check = test.Fresh();
        Assert.Equal(1, await check.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == employee));
        Assert.Equal(3, await check.DailyAttendanceRecords.CountAsync(r => r.EmployeeId == employee));
    }

    [Fact]
    public async Task The_same_leave_event_twice_changes_nothing_the_second_time()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        await test.RecordAsync(employee, new DateOnly(2025, 3, 5), DailyAttendanceStatus.Absent);
        var consumer = new LeaveApprovedConsumer(test.Db, Integration(test));

        await consumer.Consume(Context(Approved(leave, employee)));
        var saves = test.Saves.Count;
        await consumer.Consume(Context(Approved(leave, employee)));

        Assert.Equal(saves, test.Saves.Count);
        await using var db = test.Fresh();
        Assert.Equal(1, await db.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == employee));
        Assert.Equal(3, await db.DailyAttendanceRecords.CountAsync(r => r.EmployeeId == employee));
        // The status the leave replaced is still kept: a second application would have overwritten it with «في إجازة».
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(employee, new DateOnly(2025, 3, 5)))!.ReplacedStatus);
    }

    [Fact]
    public async Task A_cancelled_leave_restores_its_days_in_one_save_and_touches_no_other_record()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        await test.RecordAsync(employee, new DateOnly(2025, 3, 5), DailyAttendanceStatus.Absent);
        await new LeaveApprovedConsumer(test.Db, Integration(test)).Consume(Context(Approved(leave, employee)));
        // A «في إجازة» typed by hand on a day outside the leave's own records, inside its range once widened by a mistake upstream.
        var colleague = await test.EmployeeAsync(test.Branch1);
        await test.RecordAsync(colleague, new DateOnly(2025, 3, 5), DailyAttendanceStatus.OnLeave);
        var consumer = new LeaveCancelledConsumer(test.Db, Integration(test));
        var savesBefore = test.Saves.Count;

        await consumer.Consume(Context(Cancelled(leave, employee)));
        await consumer.Consume(Context(Cancelled(leave, employee)));

        Assert.Equal(savesBefore + 1, test.Saves.Count);
        await using var db = test.Fresh();
        var days = await db.DailyAttendanceRecords.AsNoTracking().Where(r => r.EmployeeId == employee).ToListAsync();
        var restored = Assert.Single(days);
        Assert.Equal(new DateOnly(2025, 3, 5), restored.AttendanceDate);
        Assert.Equal(DailyAttendanceStatus.Absent, restored.Status);
        Assert.Equal(AttendanceRecordSource.Manual, restored.Source);
        Assert.Equal(1, await db.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == employee && h.Reason!.StartsWith($"LeaveCancelled:{leave}")));
        Assert.Equal(DailyAttendanceStatus.OnLeave, (await test.StoredAsync(colleague, new DateOnly(2025, 3, 5)))!.Status);
    }

    // ---------------------------------------------------------------- the leave schedule reconciler

    private static async Task MoveToOnLeaveAsync(Lva057Db test, Guid employee, string reason, DateTime at)
    {
        var entity = await test.Db.Employees.FirstAsync(e => e.Id == employee);
        entity.CurrentStatus = EmploymentStatus.OnLeave;
        test.Db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee,
            OldStatus = "Active",
            NewStatus = "OnLeave",
            ChangedAt = at,
            Reason = reason
        });
        await test.Db.SaveChangesAsync();
    }

    private static async Task<EmploymentStatus> StatusAsync(Lva057Db test, Guid employee)
    {
        await using var db = test.Fresh();
        return await db.Employees.Where(e => e.Id == employee).Select(e => e.CurrentStatus).FirstAsync();
    }

    [Fact]
    public async Task The_reconciler_puts_on_leave_only_an_employee_whose_day_an_approved_leave_made()
    {
        await using var test = await Lva057Db.CreateAsync();
        var today = new DateOnly(2025, 3, 4);
        var byLeave = await test.EmployeeAsync(test.Branch1);
        var byHand = await test.EmployeeAsync(test.Branch1);
        await test.RecordAsync(byLeave, today, DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, AttendanceDayWriter.LeaveReference(Guid.NewGuid()));
        // An old record typed «في إجازة» by hand, with no leave behind it.
        await test.RecordAsync(byHand, today, DailyAttendanceStatus.OnLeave);
        var reconciler = new LeaveScheduleReconciler(test.Db, NullLogger<LeaveScheduleReconciler>.Instance);

        var result = await reconciler.ReconcileAsync(today);

        Assert.Equal(1, result.MovedToOnLeave);
        Assert.Equal(EmploymentStatus.OnLeave, await StatusAsync(test, byLeave));
        Assert.Equal(EmploymentStatus.Active, await StatusAsync(test, byHand));
    }

    [Fact]
    public async Task The_reconciler_returns_to_work_only_an_employee_a_leave_put_on_leave()
    {
        await using var test = await Lva057Db.CreateAsync();
        var today = new DateOnly(2025, 3, 10);
        var afterALeave = await test.EmployeeAsync(test.Branch1);
        var afterTheReconciler = await test.EmployeeAsync(test.Branch1);
        var byAPerson = await test.EmployeeAsync(test.Branch1);
        var byAPersonAfterALeave = await test.EmployeeAsync(test.Branch1);
        var stillOnLeave = await test.EmployeeAsync(test.Branch1);
        var at = new DateTime(2025, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        await MoveToOnLeaveAsync(test, afterALeave, $"LeaveApproved:{Guid.NewGuid()} Source=HR.Contracts", at);
        await MoveToOnLeaveAsync(test, afterTheReconciler, "LeaveScheduleReconciler: بدء إجازة معتمدة", at);
        await MoveToOnLeaveAsync(test, byAPerson, "قرار إداري: إجازة خاصة بلا أجر", at);
        // A leave moved him first; a person moved him again later: the last move decides.
        await MoveToOnLeaveAsync(test, byAPersonAfterALeave, $"LeaveApproved:{Guid.NewGuid()} Source=HR.Contracts", at);
        await MoveToOnLeaveAsync(test, byAPersonAfterALeave, "قرار إداري: تمديد خارج النظام", at.AddDays(3));
        await MoveToOnLeaveAsync(test, stillOnLeave, $"LeaveApproved:{Guid.NewGuid()} Source=HR.Contracts", at);
        await test.RecordAsync(stillOnLeave, today, DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, AttendanceDayWriter.LeaveReference(Guid.NewGuid()));
        var reconciler = new LeaveScheduleReconciler(test.Db, NullLogger<LeaveScheduleReconciler>.Instance);

        var result = await reconciler.ReconcileAsync(today);

        Assert.Equal(2, result.ReturnedToActive);
        Assert.Equal(EmploymentStatus.Active, await StatusAsync(test, afterALeave));
        Assert.Equal(EmploymentStatus.Active, await StatusAsync(test, afterTheReconciler));
        Assert.Equal(EmploymentStatus.OnLeave, await StatusAsync(test, byAPerson));
        Assert.Equal(EmploymentStatus.OnLeave, await StatusAsync(test, byAPersonAfterALeave));
        Assert.Equal(EmploymentStatus.OnLeave, await StatusAsync(test, stillOnLeave));
        await using var db = test.Fresh();
        Assert.Equal(2, await db.EmploymentStatusHistories.CountAsync(h => h.Reason!.StartsWith("LeaveScheduleReconciler: انتهاء")));
    }
}
