using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Attendance;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57 (§2): the rules of the one writer of an attendance day. Each refusal changes nothing: the writer never saves, and a
/// refused call leaves nothing for its caller's save.
/// </summary>
public class Lva057AttendanceDayWriterTests
{
    private static readonly DateOnly Day = new(2025, 3, 4);

    private static void AssertRefused(Lva057Db test, AttendanceWriteResult result, AttendanceRefusal refusal, string? message = null)
    {
        Assert.False(result.Ok);
        Assert.Equal(refusal, result.Refusal);
        if (message is not null) Assert.Equal(message, result.Error);
        Assert.False(test.Db.ChangeTracker.HasChanges(), "a refused write left something for the caller's save");
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task A_manual_create_writes_a_manual_record_and_does_not_save()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);

        var result = await test.Writer.CreateManualAsync(test.Branch1Manager(), employee, Day, DailyAttendanceStatus.Absent, "  غياب بلا عذر  ");

        Assert.True(result.Ok);
        Assert.Equal(0, await test.StoredCountAsync());
        await test.Db.SaveChangesAsync();
        var stored = await test.StoredAsync(employee, Day);
        Assert.NotNull(stored);
        Assert.Equal(DailyAttendanceStatus.Absent, stored!.Status);
        Assert.Equal(AttendanceRecordSource.Manual, stored.Source);
        Assert.Equal("غياب بلا عذر", stored.Notes);
        Assert.Null(stored.SourceReference);
    }

    [Fact]
    public async Task A_create_on_a_locked_day_is_refused_with_the_plans_message()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.LockAsync(employee, Lva057Db.MarchStart);

        var result = await test.Writer.CreateManualAsync(test.Central(), employee, Day, DailyAttendanceStatus.Present, null);

        AssertRefused(test, result, AttendanceRefusal.Locked, "اليوم ضمن فترة حضور مغلقة؛ قدّم طلب تصحيح");
        // The lock is the employee's: a colleague whose month is not closed is written, and so is the employee's next month.
        var colleague = await test.EmployeeAsync(test.Branch1);
        Assert.True((await test.Writer.CreateManualAsync(test.Central(), colleague, Day, DailyAttendanceStatus.Present, null)).Ok);
        Assert.True((await test.Writer.CreateManualAsync(test.Central(), employee, new DateOnly(2025, 4, 1), DailyAttendanceStatus.Present, null)).Ok);
    }

    [Fact]
    public async Task A_duplicate_day_is_a_validation_message_whether_stored_or_pending()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.RecordAsync(employee, Day, DailyAttendanceStatus.Present);

        var stored = await test.Writer.CreateManualAsync(test.Central(), employee, Day, DailyAttendanceStatus.Absent, null);
        AssertRefused(test, stored, AttendanceRefusal.Duplicate, AttendanceMessages.DuplicateDay);

        var next = Day.AddDays(1);
        Assert.True((await test.Writer.CreateManualAsync(test.Central(), employee, next, DailyAttendanceStatus.Present, null)).Ok);
        var pending = await test.Writer.CreateManualAsync(test.Central(), employee, next, DailyAttendanceStatus.Absent, null);
        Assert.Equal(AttendanceRefusal.Duplicate, pending.Refusal);
    }

    [Fact]
    public async Task On_leave_is_never_written_by_hand()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var record = await test.RecordAsync(employee, Day, DailyAttendanceStatus.Present);

        AssertRefused(test, await test.Writer.CreateManualAsync(test.Central(), employee, Day.AddDays(1), DailyAttendanceStatus.OnLeave, null),
            AttendanceRefusal.Invalid, AttendanceMessages.OnLeaveByHand);
        AssertRefused(test, await test.Writer.UpdateManualAsync(test.Central(), record, DailyAttendanceStatus.OnLeave, null),
            AttendanceRefusal.Invalid, AttendanceMessages.OnLeaveByHand);
        var bulk = await test.Writer.BulkFillAsync(test.Central(), test.Branch1, Lva057Db.MarchStart, DailyAttendanceStatus.OnLeave);
        Assert.Equal(AttendanceRefusal.Invalid, bulk.Refusal);
        Assert.False(test.Db.ChangeTracker.HasChanges());
        AssertRefused(test, await test.Writer.CreateManualAsync(test.Central(), employee, Day.AddDays(1), (DailyAttendanceStatus)9, null),
            AttendanceRefusal.Invalid, AttendanceMessages.UnknownStatus);
    }

    [Fact]
    public async Task The_employees_scope_is_checked_by_the_writer_for_a_create_an_update_and_a_delete()
    {
        await using var test = await Lva057Db.CreateAsync();
        var own = await test.EmployeeAsync(test.Branch1Sub);
        var foreign = await test.EmployeeAsync(test.Branch2);
        var unitless = await test.EmployeeAsync(null);
        var foreignRecord = await test.RecordAsync(foreign, Day, DailyAttendanceStatus.Present);
        var manager = test.Branch1Manager();

        Assert.True((await test.Writer.CreateManualAsync(manager, own, Day, DailyAttendanceStatus.Present, null)).Ok);
        test.Db.ChangeTracker.Clear();
        AssertRefused(test, await test.Writer.CreateManualAsync(manager, foreign, Day.AddDays(1), DailyAttendanceStatus.Present, null), AttendanceRefusal.OutOfScope);
        AssertRefused(test, await test.Writer.CreateManualAsync(manager, unitless, Day, DailyAttendanceStatus.Present, null), AttendanceRefusal.OutOfScope);
        AssertRefused(test, await test.Writer.UpdateManualAsync(manager, foreignRecord, DailyAttendanceStatus.Absent, null), AttendanceRefusal.OutOfScope);
        AssertRefused(test, await test.Writer.DeleteManualAsync(manager, foreignRecord), AttendanceRefusal.OutOfScope);
        AssertRefused(test, await test.Writer.CreateManualAsync(new AttendanceActor(Guid.NewGuid(), OrgAccessScope.Denied()), own, Day.AddDays(2), DailyAttendanceStatus.Present, null),
            AttendanceRefusal.OutOfScope);
        AssertRefused(test, await test.Writer.CreateManualAsync(test.Central(), Guid.NewGuid(), Day, DailyAttendanceStatus.Present, null), AttendanceRefusal.NotFound);
        Assert.Equal(DailyAttendanceStatus.Present, (await test.StoredAsync(foreign, Day))!.Status);
    }

    // ---------------------------------------------------------------- update and delete

    [Fact]
    public async Task An_update_changes_the_status_and_the_note_and_never_the_employee_or_the_day()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var record = await test.RecordAsync(employee, Day, DailyAttendanceStatus.Present, AttendanceRecordSource.Bulk);

        var result = await test.Writer.UpdateManualAsync(test.Central(), record, DailyAttendanceStatus.Remote, "عمل عن بُعد بموافقة المدير");
        Assert.True(result.Ok);
        await test.Db.SaveChangesAsync();

        var stored = await test.StoredAsync(employee, Day);
        Assert.Equal(record, stored!.Id);
        Assert.Equal(employee, stored.EmployeeId);
        Assert.Equal(Day, stored.AttendanceDate);
        Assert.Equal(DailyAttendanceStatus.Remote, stored.Status);
        Assert.Equal("عمل عن بُعد بموافقة المدير", stored.Notes);
        Assert.Equal(AttendanceRecordSource.Manual, stored.Source);
        Assert.Equal(1, await test.StoredCountAsync());
    }

    [Fact]
    public void The_edit_view_model_binds_no_employee_and_no_date()
    {
        var bound = typeof(DailyAttendanceRecordEditViewModel).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

        // What a posted form can reach: an overposted EmployeeId or AttendanceDate has nothing to bind to.
        Assert.Equal(["Id", "Notes", "Status"], bound);
        Assert.False(typeof(DailyAttendanceRecordCreateViewModel).IsAssignableFrom(typeof(DailyAttendanceRecordEditViewModel)));
        // The writer's own signature has no employee and no date either.
        var parameters = typeof(AttendanceDayWriter).GetMethod(nameof(AttendanceDayWriter.UpdateManualAsync))!.GetParameters().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("employeeId", parameters);
        Assert.DoesNotContain("date", parameters);
    }

    [Fact]
    public async Task An_update_or_a_delete_on_a_locked_day_is_refused_and_the_record_stays()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var record = await test.RecordAsync(employee, Day, DailyAttendanceStatus.Absent);
        await test.LockAsync(employee, Lva057Db.MarchStart, absentDays: 1);

        AssertRefused(test, await test.Writer.UpdateManualAsync(test.Central(), record, DailyAttendanceStatus.Present, null), AttendanceRefusal.Locked, AttendanceMessages.LockedDay);
        AssertRefused(test, await test.Writer.DeleteManualAsync(test.Central(), record), AttendanceRefusal.Locked, AttendanceMessages.LockedDay);
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(employee, Day))!.Status);
        AssertRefused(test, await test.Writer.UpdateManualAsync(test.Central(), Guid.NewGuid(), DailyAttendanceStatus.Present, null), AttendanceRefusal.NotFound);
    }

    [Fact]
    public async Task A_leave_made_record_is_never_edited_or_deleted_by_hand_and_a_manual_one_is_deleted()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        var byLeave = await test.RecordAsync(employee, Day, DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, AttendanceDayWriter.LeaveReference(leave));
        var manual = await test.RecordAsync(employee, Day.AddDays(1), DailyAttendanceStatus.Present);

        AssertRefused(test, await test.Writer.UpdateManualAsync(test.Central(), byLeave, DailyAttendanceStatus.Present, null), AttendanceRefusal.Invalid, AttendanceMessages.LeaveMadeRecord);
        AssertRefused(test, await test.Writer.DeleteManualAsync(test.Central(), byLeave), AttendanceRefusal.Invalid, AttendanceMessages.LeaveMadeRecord);

        Assert.True((await test.Writer.DeleteManualAsync(test.Central(), manual)).Ok);
        await test.Db.SaveChangesAsync();
        Assert.Null(await test.StoredAsync(employee, Day.AddDays(1)));
        Assert.Equal(DailyAttendanceStatus.OnLeave, (await test.StoredAsync(employee, Day))!.Status);
    }

    // ---------------------------------------------------------------- the bulk entry

    [Fact]
    public async Task The_bulk_entry_fills_working_days_only_for_the_units_tree()
    {
        await using var test = await Lva057Db.CreateAsync(null, "2025-03-30", "2025-03-31");
        var direct = await test.EmployeeAsync(test.Branch1);
        var inSubUnit = await test.EmployeeAsync(test.Branch1Sub);
        var otherBranch = await test.EmployeeAsync(test.Branch2);
        var notStarted = await test.EmployeeAsync(test.Branch1, status: EmploymentStatus.PendingActivation);

        var result = await test.Writer.BulkFillAsync(test.Branch1Manager(), test.Branch1, Lva057Db.MarchStart, DailyAttendanceStatus.Absent);
        Assert.True(result.Ok);
        await test.Db.SaveChangesAsync();

        // 22 working days in March 2025, less the two configured holidays (30 and 31 March) = 20 per employee.
        Assert.Equal(2, result.Employees);
        Assert.Equal(40, result.Written);
        Assert.Equal(20, await test.StoredCountAsync(direct));
        Assert.Equal(20, await test.StoredCountAsync(inSubUnit));
        Assert.Equal(0, await test.StoredCountAsync(otherBranch));
        Assert.Equal(0, await test.StoredCountAsync(notStarted));

        await using var db = test.Fresh();
        var days = await db.DailyAttendanceRecords.AsNoTracking().Select(r => new { r.AttendanceDate, r.Source, r.Status }).ToListAsync();
        Assert.DoesNotContain(days, d => d.AttendanceDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday);
        Assert.DoesNotContain(days, d => d.AttendanceDate == new DateOnly(2025, 3, 30) || d.AttendanceDate == new DateOnly(2025, 3, 31));
        Assert.All(days, d => Assert.Equal(AttendanceRecordSource.Bulk, d.Source));
        Assert.All(days, d => Assert.Equal(DailyAttendanceStatus.Absent, d.Status));
    }

    [Fact]
    public async Task The_bulk_entry_writes_no_day_after_today_in_damascus()
    {
        // 22:30 UTC on Tuesday 11 March 2025 is 01:30 on Wednesday 12 March in Damascus: the 12th is «today» and is written.
        await using var test = await Lva057Db.CreateAsync(new DateTimeOffset(2025, 3, 11, 22, 30, 0, TimeSpan.Zero));
        var employee = await test.EmployeeAsync(test.Branch1);

        var result = await test.Writer.BulkFillAsync(test.Central(), test.Branch1, Lva057Db.MarchStart, DailyAttendanceStatus.Present);
        await test.Db.SaveChangesAsync();

        // Working days 2..6 and 9..12 March = 9.
        Assert.Equal(9, result.Written);
        Assert.NotNull(await test.StoredAsync(employee, new DateOnly(2025, 3, 12)));
        Assert.Null(await test.StoredAsync(employee, new DateOnly(2025, 3, 13)));

        // A month that has not begun writes nothing at all.
        var april = await test.Writer.BulkFillAsync(test.Central(), test.Branch1, new DateOnly(2025, 4, 1), DailyAttendanceStatus.Present);
        Assert.True(april.Ok);
        Assert.Equal(0, april.Written);
    }

    [Fact]
    public async Task The_bulk_entry_follows_the_hire_and_the_end_of_service_and_leaves_out_who_left_before_the_month()
    {
        await using var test = await Lva057Db.CreateAsync();
        var hiredInside = await test.EmployeeAsync(test.Branch1, hire: new DateTime(2025, 3, 16, 0, 0, 0, DateTimeKind.Utc));
        var hiredAfter = await test.EmployeeAsync(test.Branch1, hire: new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var leftInside = await test.EmployeeAsync(test.Branch1);
        await test.EndServiceAsync(leftInside, new DateTime(2025, 3, 13, 9, 0, 0, DateTimeKind.Utc));
        var leftBefore = await test.EmployeeAsync(test.Branch1);
        await test.EndServiceAsync(leftBefore, new DateTime(2025, 2, 20, 9, 0, 0, DateTimeKind.Utc));
        var retiredInside = await test.EmployeeAsync(test.Branch1);
        await test.EndServiceAsync(retiredInside, new DateTime(2025, 3, 6, 9, 0, 0, DateTimeKind.Utc), EmploymentStatus.Retired);

        var result = await test.Writer.BulkFillAsync(test.Central(), test.Branch1, Lva057Db.MarchStart, DailyAttendanceStatus.Present);
        await test.Db.SaveChangesAsync();

        Assert.Equal(3, result.Employees);
        Assert.Equal(12, await test.StoredCountAsync(hiredInside));   // 16..31 March
        Assert.Equal(0, await test.StoredCountAsync(hiredAfter));
        Assert.Equal(10, await test.StoredCountAsync(leftInside));    // 2..6 and 9..13 March
        Assert.Equal(0, await test.StoredCountAsync(leftBefore));
        Assert.Equal(5, await test.StoredCountAsync(retiredInside));  // 2..6 March
        Assert.Null(await test.StoredAsync(leftInside, new DateOnly(2025, 3, 16)));
    }

    [Fact]
    public async Task The_bulk_entry_skips_locked_and_existing_days_and_reports_each()
    {
        await using var test = await Lva057Db.CreateAsync();
        var open = await test.EmployeeAsync(test.Branch1);
        var closed = await test.EmployeeAsync(test.Branch1);
        await test.RecordAsync(open, Day, DailyAttendanceStatus.Absent);
        await test.RecordAsync(open, Day.AddDays(1), DailyAttendanceStatus.Remote);
        await test.LockAsync(closed, Lva057Db.MarchStart);

        var result = await test.Writer.BulkFillAsync(test.Central(), test.Branch1, Lva057Db.MarchStart, DailyAttendanceStatus.Present);
        await test.Db.SaveChangesAsync();

        Assert.Equal(2, result.Employees);
        Assert.Equal(20, result.Written);
        Assert.Equal(2, result.SkippedExisting);
        Assert.Equal(22, result.SkippedLocked);
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(open, Day))!.Status);
        Assert.Equal(0, await test.StoredCountAsync(closed));

        // A second run writes nothing more.
        var again = await test.Writer.BulkFillAsync(test.Central(), test.Branch1, Lva057Db.MarchStart, DailyAttendanceStatus.Present);
        Assert.Equal(0, again.Written);
        Assert.Equal(22, again.SkippedExisting);
        Assert.False(test.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task The_bulk_entry_refuses_a_unit_outside_the_scope_and_a_unit_that_does_not_exist()
    {
        await using var test = await Lva057Db.CreateAsync();
        await test.EmployeeAsync(test.Branch2);

        var foreign = await test.Writer.BulkFillAsync(test.Branch1Manager(), test.Branch2, Lva057Db.MarchStart, DailyAttendanceStatus.Present);
        var missing = await test.Writer.BulkFillAsync(test.Central(), Guid.NewGuid(), Lva057Db.MarchStart, DailyAttendanceStatus.Present);

        Assert.Equal(AttendanceRefusal.OutOfScope, foreign.Refusal);
        Assert.Equal(AttendanceRefusal.NotFound, missing.Refusal);
        Assert.False(test.Db.ChangeTracker.HasChanges());
    }

    // ---------------------------------------------------------------- a leave and its cancellation

    [Fact]
    public async Task An_approved_leave_takes_its_days_and_keeps_what_each_day_was()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        await test.RecordAsync(employee, new DateOnly(2025, 3, 4), DailyAttendanceStatus.Present);
        await test.RecordAsync(employee, new DateOnly(2025, 3, 5), DailyAttendanceStatus.Absent, notes: "غياب");

        var result = await test.Writer.ApplyLeaveAsync(employee, new DateOnly(2025, 3, 4), new DateOnly(2025, 3, 6), leave, "إجازة معتمدة: سنوية");
        Assert.Equal(3, result.Written);
        Assert.Empty(result.LockedDays);
        Assert.Equal(DailyAttendanceStatus.Present, (await test.StoredAsync(employee, new DateOnly(2025, 3, 4)))!.Status);
        await test.Db.SaveChangesAsync();

        var kept = await test.StoredAsync(employee, new DateOnly(2025, 3, 5));
        Assert.Equal(DailyAttendanceStatus.OnLeave, kept!.Status);
        Assert.Equal(DailyAttendanceStatus.Absent, kept.ReplacedStatus);
        Assert.Equal(AttendanceRecordSource.Leave, kept.Source);
        Assert.Equal($"LeaveRequestId={leave:D}", kept.SourceReference);
        var created = await test.StoredAsync(employee, new DateOnly(2025, 3, 6));
        Assert.Equal(DailyAttendanceStatus.OnLeave, created!.Status);
        Assert.Null(created.ReplacedStatus);
        Assert.Equal(AttendanceRecordSource.Leave, created.Source);
    }

    [Fact]
    public async Task A_cancelled_leave_gives_back_only_its_own_days()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        var otherLeave = Guid.NewGuid();
        await test.RecordAsync(employee, new DateOnly(2025, 3, 4), DailyAttendanceStatus.Absent);
        // A «في إجازة» typed by hand before this plan, and a day another leave holds: neither is this leave's.
        await test.RecordAsync(employee, new DateOnly(2025, 3, 9), DailyAttendanceStatus.OnLeave);
        await test.RecordAsync(employee, new DateOnly(2025, 3, 10), DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, AttendanceDayWriter.LeaveReference(otherLeave));
        await test.Writer.ApplyLeaveAsync(employee, new DateOnly(2025, 3, 4), new DateOnly(2025, 3, 10), leave, "إجازة معتمدة: سنوية");
        await test.Db.SaveChangesAsync();
        // The hand-typed day is taken by the leave (it keeps «في إجازة» as what it was); the other leave's day is left to it.
        Assert.Equal(AttendanceDayWriter.LeaveReference(otherLeave), (await test.StoredAsync(employee, new DateOnly(2025, 3, 10)))!.SourceReference);

        var result = await test.Writer.RevertLeaveAsync(employee, new DateOnly(2025, 3, 4), new DateOnly(2025, 3, 10), leave);
        await test.Db.SaveChangesAsync();

        Assert.Equal(6, result.Written);
        var restored = await test.StoredAsync(employee, new DateOnly(2025, 3, 4));
        Assert.Equal(DailyAttendanceStatus.Absent, restored!.Status);
        Assert.Equal(AttendanceRecordSource.Manual, restored.Source);
        Assert.Null(restored.ReplacedStatus);
        Assert.Null(restored.SourceReference);
        Assert.Null(await test.StoredAsync(employee, new DateOnly(2025, 3, 5)));
        Assert.Null(await test.StoredAsync(employee, new DateOnly(2025, 3, 6)));
        Assert.Equal(DailyAttendanceStatus.OnLeave, (await test.StoredAsync(employee, new DateOnly(2025, 3, 9)))!.Status);
        Assert.Equal(AttendanceRecordSource.Manual, (await test.StoredAsync(employee, new DateOnly(2025, 3, 9)))!.Source);
        var others = await test.StoredAsync(employee, new DateOnly(2025, 3, 10));
        Assert.Equal(DailyAttendanceStatus.OnLeave, others!.Status);
        Assert.Equal(AttendanceDayWriter.LeaveReference(otherLeave), others.SourceReference);
    }

    [Fact]
    public async Task A_cancelled_leave_never_removes_a_hand_typed_on_leave_record_it_did_not_make()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.RecordAsync(employee, new DateOnly(2025, 3, 4), DailyAttendanceStatus.OnLeave);
        // A day a leave made before this plan: the migration marked it by its note, and it carries no reference.
        await test.RecordAsync(employee, new DateOnly(2025, 3, 5), DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, notes: "إجازة معتمدة: سنوية");

        // The old removal deleted every «في إجازة» record in the range, the hand-typed one included.
        var result = await test.Writer.RevertLeaveAsync(employee, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31), Guid.NewGuid());
        await test.Db.SaveChangesAsync();

        Assert.Equal(1, result.Written);
        Assert.NotNull(await test.StoredAsync(employee, new DateOnly(2025, 3, 4)));
        Assert.Null(await test.StoredAsync(employee, new DateOnly(2025, 3, 5)));
    }

    [Fact]
    public async Task A_leave_on_a_locked_day_writes_nothing_and_reports_the_day()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        var leave = Guid.NewGuid();
        await test.RecordAsync(employee, new DateOnly(2025, 3, 31), DailyAttendanceStatus.Present);
        await test.LockAsync(employee, Lva057Db.MarchStart);

        // 31 March is locked; 1 and 2 April are not.
        var applied = await test.Writer.ApplyLeaveAsync(employee, new DateOnly(2025, 3, 31), new DateOnly(2025, 4, 2), leave, "إجازة معتمدة: سنوية");
        await test.Db.SaveChangesAsync();

        Assert.Equal(2, applied.Written);
        var locked = Assert.Single(applied.LockedDays);
        Assert.Equal(new AttendanceLockedLeaveDay(new DateOnly(2025, 3, 31), DailyAttendanceStatus.Present, DailyAttendanceStatus.OnLeave), locked);
        Assert.Equal(DailyAttendanceStatus.Present, (await test.StoredAsync(employee, new DateOnly(2025, 3, 31)))!.Status);
        Assert.Equal(DailyAttendanceStatus.OnLeave, (await test.StoredAsync(employee, new DateOnly(2025, 4, 1)))!.Status);

        // The cancellation, once the locked day carries the leave (as its system correction leaves it): reported, not written.
        await test.Writer.ApplyApprovedCorrectionAsync(new AttendanceCorrection
        {
            EmployeeId = employee,
            AttendanceDate = new DateOnly(2025, 3, 31),
            OldStatus = DailyAttendanceStatus.Present,
            NewStatus = DailyAttendanceStatus.OnLeave,
            Source = AttendanceCorrectionSource.LeaveEvent,
            SourceReference = AttendanceDayWriter.LeaveReference(leave) + ":Approved"
        });
        await test.Db.SaveChangesAsync();
        var reverted = await test.Writer.RevertLeaveAsync(employee, new DateOnly(2025, 3, 31), new DateOnly(2025, 4, 2), leave);
        await test.Db.SaveChangesAsync();

        Assert.Equal(2, reverted.Written);
        Assert.Equal(new AttendanceLockedLeaveDay(new DateOnly(2025, 3, 31), DailyAttendanceStatus.OnLeave, DailyAttendanceStatus.Present), Assert.Single(reverted.LockedDays));
        Assert.Equal(DailyAttendanceStatus.OnLeave, (await test.StoredAsync(employee, new DateOnly(2025, 3, 31)))!.Status);
        Assert.Null(await test.StoredAsync(employee, new DateOnly(2025, 4, 1)));
    }

    // ---------------------------------------------------------------- the one write of a locked day

    [Fact]
    public async Task An_approved_correction_is_written_on_a_locked_day_with_its_reference()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.RecordAsync(employee, Day, DailyAttendanceStatus.Absent);
        var (period, _) = await test.LockAsync(employee, Lva057Db.MarchStart, absentDays: 1);
        var correction = new AttendanceCorrection
        {
            AttendancePeriodId = period,
            EmployeeId = employee,
            AttendanceDate = Day,
            OldStatus = DailyAttendanceStatus.Absent,
            NewStatus = DailyAttendanceStatus.Present,
            Reason = "كان في مهمة رسمية موثقة"
        };

        await test.Writer.ApplyApprovedCorrectionAsync(correction);
        await test.Db.SaveChangesAsync();

        var stored = await test.StoredAsync(employee, Day);
        Assert.Equal(DailyAttendanceStatus.Present, stored!.Status);
        Assert.Equal(AttendanceRecordSource.Correction, stored.Source);
        Assert.Equal($"AttendanceCorrection:{correction.Id:D}", stored.SourceReference);

        // A day with no record gets one; a correction that undoes a leave which created the record removes it.
        var empty = Day.AddDays(1);
        await test.Writer.ApplyApprovedCorrectionAsync(new AttendanceCorrection { EmployeeId = employee, AttendanceDate = empty, NewStatus = DailyAttendanceStatus.Remote });
        await test.Db.SaveChangesAsync();
        Assert.Equal(DailyAttendanceStatus.Remote, (await test.StoredAsync(employee, empty))!.Status);
        await test.Writer.ApplyApprovedCorrectionAsync(new AttendanceCorrection { EmployeeId = employee, AttendanceDate = empty, OldStatus = DailyAttendanceStatus.Remote, NewStatus = null });
        await test.Db.SaveChangesAsync();
        Assert.Null(await test.StoredAsync(employee, empty));
    }
}
