using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57 (§3): a change after the close is a correction. Its rules (a locked day only, a reason of 15 characters, another status,
/// never «في إجازة» by hand), the separation of duties checked on the server, the atomic approval and its version, a correction that
/// changes no figure, and the system corrections a leave decision makes on locked days. Each refusal changes nothing.
/// </summary>
public class Lva057AttendanceCorrectionRulesTests
{
    private const string Reason = "كان في مهمة رسمية موثقة بكتاب الإدارة";
    private static readonly DateOnly March = Lva057Db.MarchStart;
    private static readonly DateOnly Day = new(2025, 3, 4);

    private sealed record Closed(Guid Employee, Guid Summary, Guid Requester, Guid Approver);

    /// <summary>An employee of branch 1 whose March is closed with one absence (4 March), by the real close.</summary>
    private static async Task<Closed> ClosedMonthAsync(Lva057Db test)
    {
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(employee, March, Lva057Db.MarchEnd);
        await using (var db = test.Fresh())
        {
            (await db.DailyAttendanceRecords.FirstAsync(r => r.EmployeeId == employee && r.AttendanceDate == Day)).Status = DailyAttendanceStatus.Absent;
            await db.SaveChangesAsync();
        }

        Assert.True((await test.PeriodService().CloseAsync(March, test.Branch1, test.Central())).Ok);
        test.Published.Clear();
        await using var check = test.Fresh();
        var summary = await check.AttendanceEmployeeSummaries.AsNoTracking().SingleAsync(s => s.EmployeeId == employee);
        Assert.Equal((1, 1), (summary.Version, summary.AbsentDays));
        return new Closed(employee, summary.Id, Guid.NewGuid(), Guid.NewGuid());
    }

    private static async Task<AttendanceEmployeeSummary> SummaryAsync(Lva057Db test, Guid employee)
    {
        await using var db = test.Fresh();
        return await db.AttendanceEmployeeSummaries.AsNoTracking().SingleAsync(s => s.EmployeeId == employee);
    }

    private static async Task<int> CorrectionsAsync(Lva057Db test)
    {
        await using var db = test.Fresh();
        return await db.AttendanceCorrections.CountAsync();
    }

    private static void AssertRefused(Lva057Db test, AttendanceCorrectionResult result, AttendanceCorrectionOutcome outcome, string? message = null)
    {
        Assert.Equal(outcome, result.Outcome);
        if (message is not null) Assert.Equal(message, result.Error);
        Assert.False(test.Db.ChangeTracker.HasChanges(), "a refused correction left something for a later save");
    }

    // ---------------------------------------------------------------- the request's rules

    [Fact]
    public async Task A_request_needs_a_locked_day_a_reason_of_15_characters_and_another_status()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var service = test.CorrectionService();
        var requester = test.Branch1Manager(closed.Requester);

        AssertRefused(test, await service.RequestAsync(requester, closed.Employee, new DateOnly(2025, 4, 1), DailyAttendanceStatus.Present, Reason),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.DayNotLocked);
        AssertRefused(test, await service.RequestAsync(requester, closed.Employee, Day, DailyAttendanceStatus.Present, "سبب قصير"),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.ShortReason);
        AssertRefused(test, await service.RequestAsync(requester, closed.Employee, Day, DailyAttendanceStatus.Present, "   أربعة عشر حرف   "),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.ShortReason);
        AssertRefused(test, await service.RequestAsync(requester, closed.Employee, Day, DailyAttendanceStatus.Present, null),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.ShortReason);
        AssertRefused(test, await service.RequestAsync(requester, closed.Employee, Day, DailyAttendanceStatus.Absent, Reason),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.SameStatus);
        AssertRefused(test, await service.RequestAsync(requester, closed.Employee, Day, DailyAttendanceStatus.OnLeave, Reason),
            AttendanceCorrectionOutcome.Invalid, AttendanceMessages.OnLeaveByHand);
        AssertRefused(test, await service.RequestAsync(new AttendanceActor(null, OrgAccessScope.Unrestricted()), closed.Employee, Day, DailyAttendanceStatus.Present, Reason),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.NoRequester);
        Assert.Equal(0, await CorrectionsAsync(test));

        var ok = await service.RequestAsync(requester with { DisplayName = "مدير الفرع الأول" }, closed.Employee, Day, DailyAttendanceStatus.Present, "  " + Reason + "  ");

        Assert.True(ok.Ok, ok.Error);
        await using var db = test.Fresh();
        var stored = await db.AttendanceCorrections.AsNoTracking().SingleAsync();
        Assert.Equal((AttendanceCorrectionStatus.Pending, DailyAttendanceStatus.Absent, DailyAttendanceStatus.Present, Reason),
            (stored.Status, stored.OldStatus, stored.NewStatus, stored.Reason));
        Assert.Equal((closed.Requester, "مدير الفرع الأول", AttendanceCorrectionSource.Manual), (stored.RequestedBy, stored.RequestedByName, stored.Source));
        // A request changes neither the day nor the summary, and publishes nothing.
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(closed.Employee, Day))!.Status);
        Assert.Equal(1, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Empty(test.Published);
    }

    [Fact]
    public async Task The_requester_asks_only_for_an_employee_in_his_scope_and_one_request_waits_per_day()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var foreign = await test.EmployeeAsync(test.Branch2);
        await test.LockAsync(foreign, new DateOnly(2025, 2, 1));
        var service = test.CorrectionService();
        var manager = test.Branch1Manager(closed.Requester);

        AssertRefused(test, await service.RequestAsync(manager, foreign, new DateOnly(2025, 2, 4), DailyAttendanceStatus.Absent, Reason), AttendanceCorrectionOutcome.OutOfScope);
        AssertRefused(test, await service.RequestAsync(manager, Guid.NewGuid(), Day, DailyAttendanceStatus.Present, Reason), AttendanceCorrectionOutcome.NotFound);

        Assert.True((await service.RequestAsync(manager, closed.Employee, Day, DailyAttendanceStatus.Present, Reason)).Ok);
        AssertRefused(test, await service.RequestAsync(test.Central(), closed.Employee, Day, DailyAttendanceStatus.Remote, Reason),
            AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.PendingExists);
        // Another day of the same employee is another request.
        Assert.True((await service.RequestAsync(manager, closed.Employee, Day.AddDays(1), DailyAttendanceStatus.Absent, Reason)).Ok);
        Assert.Equal(2, await CorrectionsAsync(test));
    }

    [Fact]
    public async Task A_leave_made_day_is_not_corrected_by_a_person()
    {
        await using var test = await Lva057Db.CreateAsync();
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(employee, March, Lva057Db.MarchEnd);
        await using (var db = test.Fresh())
        {
            var record = await db.DailyAttendanceRecords.FirstAsync(r => r.EmployeeId == employee && r.AttendanceDate == Day);
            record.Status = DailyAttendanceStatus.OnLeave;
            record.Source = AttendanceRecordSource.Leave;
            record.SourceReference = AttendanceDayWriter.LeaveReference(Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        Assert.True((await test.PeriodService().CloseAsync(March, test.Branch1, test.Central())).Ok);

        AssertRefused(test, await test.CorrectionService().RequestAsync(test.Central(), employee, Day, DailyAttendanceStatus.Present, Reason),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.LeaveMadeDay);
    }

    // ---------------------------------------------------------------- the decision

    [Fact]
    public async Task An_approval_writes_the_day_the_next_version_the_event_and_the_decision_in_one_save()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var service = test.CorrectionService();
        var request = await service.RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, Day, DailyAttendanceStatus.Present, Reason);
        var savesBefore = test.Saves.Count;

        var result = await service.ApproveAsync(test.Central(closed.Approver) with { DisplayName = "مراجع مركزي" }, request.Correction!.Id, "مطابق لكتاب المهمة");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(savesBefore + 1, test.Saves.Count);
        var day = await test.StoredAsync(closed.Employee, Day);
        Assert.Equal((DailyAttendanceStatus.Present, AttendanceRecordSource.Correction, $"AttendanceCorrection:{request.Correction.Id:D}"),
            (day!.Status, day.Source, day.SourceReference));
        var summary = await SummaryAsync(test, closed.Employee);
        Assert.Equal((2, 22, 22, 0, AttendanceSummaryReason.Correction), (summary.Version, summary.WorkingDays, summary.PresentDays, summary.AbsentDays, summary.LastReason));
        Assert.Equal(request.Correction.Id, summary.LastCorrectionId);
        Assert.Equal(closed.Summary, summary.Id);

        await using var db = test.Fresh();
        var stored = await db.AttendanceCorrections.AsNoTracking().SingleAsync();
        Assert.Equal((AttendanceCorrectionStatus.Approved, 2, closed.Approver, "مراجع مركزي", "مطابق لكتاب المهمة"),
            (stored.Status, stored.ResultingVersion, stored.DecidedBy, stored.DecidedByName, stored.DecisionNote));
        Assert.NotNull(stored.DecidedAt);

        // The event: the same summary, version 2, the absolute values of that version, the correction's id.
        var message = Assert.Single(test.Published);
        Assert.Equal(closed.Summary, Lva057Db.Field<Guid>(message, "SummaryId"));
        Assert.Equal(2, Lva057Db.Field<int>(message, "Version"));
        Assert.Equal(0, Lva057Db.Field<int>(message, "AbsentDays"));
        Assert.Equal(22, Lva057Db.Field<int>(message, "PresentDays"));
        Assert.Equal("Correction", Lva057Db.Field<string>(message, "Reason"));
        Assert.Equal(request.Correction.Id, Lva057Db.NullableField<Guid>(message, "CorrectionId"));
        // The day stays locked: the correction did not reopen it.
        Assert.True(await test.Locks.IsLockedAsync(closed.Employee, Day));
    }

    [Fact]
    public async Task The_requester_cannot_decide_his_own_request_whatever_his_roles()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var service = test.CorrectionService();
        // A central user asks: he holds the approval policy too, and the server still refuses him on his own request.
        var requester = test.Central(closed.Requester);
        var request = await service.RequestAsync(requester, closed.Employee, Day, DailyAttendanceStatus.Present, Reason);

        AssertRefused(test, await service.ApproveAsync(requester, request.Correction!.Id, null), AttendanceCorrectionOutcome.SeparationOfDuties, AttendanceCorrectionMessages.OwnRequest);
        AssertRefused(test, await service.RejectAsync(requester, request.Correction.Id, "أسحب طلبي"), AttendanceCorrectionOutcome.SeparationOfDuties, AttendanceCorrectionMessages.OwnRequest);
        AssertRefused(test, await service.ApproveAsync(new AttendanceActor(null, OrgAccessScope.Unrestricted()), request.Correction.Id, null),
            AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.NoDecider);

        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(closed.Employee, Day))!.Status);
        Assert.Equal(1, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Empty(test.Published);
        await using var db = test.Fresh();
        Assert.Equal(AttendanceCorrectionStatus.Pending, (await db.AttendanceCorrections.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_branch_scoped_caller_does_not_hold_the_approval_policy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddHrManagementAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal User(params string[] roles) =>
            new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test", ClaimTypes.Name, ClaimTypes.Role));

        var branch = User(HrManagementRoles.HrCore, HrManagementRoles.BranchManager);
        var central = User(HrManagementRoles.HrCore);
        var admin = User(HrManagementRoles.Admin, HrManagementRoles.BranchManager);
        var reader = User(HrManagementRoles.HrEmployee);

        // D-LVA-4: a branch enters attendance and asks, and closes its tree; it never decides a correction.
        Assert.True((await authorization.AuthorizeAsync(branch, HrManagementPolicies.HrCoreAttendanceWrite)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(branch, HrManagementPolicies.HrCoreAttendanceClose)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(branch, HrManagementPolicies.HrCoreAttendanceCorrectionApprove)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(central, HrManagementPolicies.HrCoreAttendanceCorrectionApprove)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(admin, HrManagementPolicies.HrCoreAttendanceCorrectionApprove)).Succeeded);
        foreach (var policy in new[] { HrManagementPolicies.HrCoreAttendanceWrite, HrManagementPolicies.HrCoreAttendanceClose, HrManagementPolicies.HrCoreAttendanceCorrectionApprove })
            Assert.False((await authorization.AuthorizeAsync(reader, policy)).Succeeded);
    }

    [Fact]
    public async Task A_rejection_needs_a_note_and_a_decided_request_is_not_decided_again()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var service = test.CorrectionService();
        var approver = test.Central(closed.Approver);
        var request = await service.RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, Day, DailyAttendanceStatus.Present, Reason);
        var id = request.Correction!.Id;

        AssertRefused(test, await service.RejectAsync(approver, id, "  "), AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.RejectionNeedsNote);
        var rejected = await service.RejectAsync(approver, id, "لا كتاب مهمة مرفق");
        Assert.True(rejected.Ok);

        AssertRefused(test, await service.ApproveAsync(approver, id, null), AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.AlreadyDecided);
        AssertRefused(test, await service.RejectAsync(approver, id, "مرة ثانية"), AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.AlreadyDecided);
        AssertRefused(test, await service.ApproveAsync(approver, Guid.NewGuid(), null), AttendanceCorrectionOutcome.NotFound);
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(closed.Employee, Day))!.Status);
        Assert.Equal(1, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Empty(test.Published);

        // A rejected request does not stand in the way of a new one for the same day, which is approved once and only once.
        var second = await service.RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, Day, DailyAttendanceStatus.Present, Reason + " مع الكتاب");
        Assert.True((await service.ApproveAsync(approver, second.Correction!.Id, null)).Ok);
        AssertRefused(test, await service.ApproveAsync(approver, second.Correction.Id, null), AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.AlreadyDecided);
        Assert.Equal(2, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Single(test.Published);
    }

    [Fact]
    public async Task A_correction_that_changes_no_figure_still_raises_the_version_and_publishes_it()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var service = test.CorrectionService();
        var approver = test.Central(closed.Approver);
        var remote = new DateOnly(2025, 3, 5);
        var request = await service.RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, remote, DailyAttendanceStatus.Remote, Reason);

        Assert.True((await service.ApproveAsync(approver, request.Correction!.Id, null)).Ok);

        var summary = await SummaryAsync(test, closed.Employee);
        // Present 20 -> 20 with one Remote: the absence did not move, the version did.
        Assert.Equal((2, 1, 20, 1), (summary.Version, summary.AbsentDays, summary.PresentDays, summary.RemoteDays));
        var message = Assert.Single(test.Published);
        Assert.Equal((2, 1), (Lva057Db.Field<int>(message, "Version"), Lva057Db.Field<int>(message, "AbsentDays")));

        // The next correction of the same summary is version 3: each approval reads what the one before wrote.
        var next = await service.RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, Day, DailyAttendanceStatus.Present, Reason);
        Assert.True((await service.ApproveAsync(approver, next.Correction!.Id, null)).Ok);
        Assert.Equal((3, 0), ((await SummaryAsync(test, closed.Employee)).Version, (await SummaryAsync(test, closed.Employee)).AbsentDays));
        Assert.Equal([2, 3], test.Published.Select(m => Lva057Db.Field<int>(m, "Version")).ToArray());
    }

    [Fact]
    public async Task A_failed_save_of_an_approval_leaves_the_day_the_version_and_the_request_as_they_were()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var service = test.CorrectionService();
        var request = await service.RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, Day, DailyAttendanceStatus.Present, Reason);

        test.Saves.FailNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(test.Central(closed.Approver), request.Correction!.Id, null));

        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(closed.Employee, Day))!.Status);
        Assert.Equal(1, (await SummaryAsync(test, closed.Employee)).Version);
        await using var db = test.Fresh();
        Assert.Equal(AttendanceCorrectionStatus.Pending, (await db.AttendanceCorrections.AsNoTracking().SingleAsync()).Status);
    }

    // ---------------------------------------------------------------- a leave decision on locked days

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(NewId.NextGuid());
        return context.Object;
    }

    private static ILeaveApproved Approved(Guid leave, Guid employee, DateTime start, DateTime end) => Mock.Of<ILeaveApproved>(m =>
        m.TransactionId == Guid.NewGuid() && m.LeaveRequestId == leave && m.EmployeeId == employee && m.ApprovedBy == Guid.NewGuid()
        && m.OccurredAt == new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc) && m.SourceSystem == "HR.Contracts" && m.LeaveType == "مرضية"
        && m.StartDate == start && m.EndDate == end && m.DurationDays == 2);

    private static ILeaveCancelled Cancelled(Guid leave, Guid employee, DateTime start, DateTime end) => Mock.Of<ILeaveCancelled>(m =>
        m.TransactionId == Guid.NewGuid() && m.LeaveRequestId == leave && m.EmployeeId == employee && m.CancelledBy == Guid.NewGuid()
        && m.OccurredAt == new DateTime(2025, 6, 2, 8, 0, 0, DateTimeKind.Utc) && m.SourceSystem == "HR.Contracts" && m.LeaveType == "مرضية"
        && m.StartDate == start && m.EndDate == end && m.DurationDays == 2);

    [Fact]
    public async Task An_approved_leave_on_locked_days_makes_system_corrections_one_version_and_one_event_in_the_consumers_save()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var leave = Guid.NewGuid();
        // A sick leave approved after the close for 4 March (absent) and 5 March (present).
        var start = new DateTime(2025, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        var consumer = new LeaveApprovedConsumer(test.Db, test.Integration());
        var savesBefore = test.Saves.Count;

        await consumer.Consume(Context(Approved(leave, closed.Employee, start, end)));

        Assert.Equal(savesBefore + 1, test.Saves.Count);
        var summary = await SummaryAsync(test, closed.Employee);
        // One version for the two days: the absence became leave, and a present day became leave.
        Assert.Equal((2, 0, 2, 20, AttendanceSummaryReason.LeaveEvent), (summary.Version, summary.AbsentDays, summary.OnLeaveDays, summary.PresentDays, summary.LastReason));
        var message = Assert.Single(test.Published);
        Assert.Equal((2, 0, 2, "LeaveEvent"), (Lva057Db.Field<int>(message, "Version"), Lva057Db.Field<int>(message, "AbsentDays"), Lva057Db.Field<int>(message, "OnLeaveDays"), Lva057Db.Field<string>(message, "Reason")));

        await using (var db = test.Fresh())
        {
            var corrections = await db.AttendanceCorrections.AsNoTracking().OrderBy(c => c.AttendanceDate).ToListAsync();
            Assert.Equal(2, corrections.Count);
            Assert.All(corrections, c =>
            {
                Assert.Equal((AttendanceCorrectionStatus.Approved, AttendanceCorrectionSource.LeaveEvent, 2), (c.Status, c.Source, c.ResultingVersion));
                Assert.Null(c.RequestedBy);
                Assert.Null(c.DecidedBy);
                Assert.Equal($"إجازة معتمدة {leave:D}", c.Reason);
                Assert.Equal($"LeaveRequestId={leave:D}:Approved", c.SourceReference);
            });
            Assert.Equal((DailyAttendanceStatus.Absent, DailyAttendanceStatus.OnLeave), (corrections[0].OldStatus, corrections[0].NewStatus));
            Assert.Equal(1, await db.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == closed.Employee && h.Reason!.StartsWith($"LeaveApproved:{leave}")));
        }

        var day = await test.StoredAsync(closed.Employee, Day);
        Assert.Equal((DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, $"LeaveRequestId={leave:D}", DailyAttendanceStatus.Absent),
            (day!.Status, day.Source, day.SourceReference, day.ReplacedStatus));

        // The same event again: no second correction, no new version, no event.
        await consumer.Consume(Context(Approved(leave, closed.Employee, start, end)));
        Assert.Equal(2, await CorrectionsAsync(test));
        Assert.Equal(2, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Single(test.Published);

        // Even past the consumer's own marker, the service makes nothing twice for a leave and a day.
        var again = await test.CorrectionService().ApplySystemCorrectionAsync(closed.Employee, leave, cancelled: false,
            [new AttendanceLockedLeaveDay(Day, DailyAttendanceStatus.Absent, DailyAttendanceStatus.OnLeave)]);
        Assert.Equal(0, again);
        Assert.False(test.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task A_cancelled_leave_on_locked_days_restores_what_each_day_was_with_a_reverse_correction()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var leave = Guid.NewGuid();
        var start = new DateTime(2025, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        await new LeaveApprovedConsumer(test.Db, test.Integration()).Consume(Context(Approved(leave, closed.Employee, start, end)));
        test.Published.Clear();
        var savesBefore = test.Saves.Count;

        var consumer = new LeaveCancelledConsumer(test.Db, test.Integration());
        await consumer.Consume(Context(Cancelled(leave, closed.Employee, start, end)));
        await consumer.Consume(Context(Cancelled(leave, closed.Employee, start, end)));

        Assert.Equal(savesBefore + 1, test.Saves.Count);
        var summary = await SummaryAsync(test, closed.Employee);
        Assert.Equal((3, 1, 0, 21), (summary.Version, summary.AbsentDays, summary.OnLeaveDays, summary.PresentDays));
        Assert.Equal((3, 1), (Lva057Db.Field<int>(Assert.Single(test.Published), "Version"), Lva057Db.Field<int>(test.Published[0], "AbsentDays")));
        var day = await test.StoredAsync(closed.Employee, Day);
        Assert.Equal((DailyAttendanceStatus.Absent, AttendanceRecordSource.Correction), (day!.Status, day.Source));
        Assert.Null(day.ReplacedStatus);
        Assert.Equal(DailyAttendanceStatus.Present, (await test.StoredAsync(closed.Employee, new DateOnly(2025, 3, 5)))!.Status);

        await using var db = test.Fresh();
        var reverse = await db.AttendanceCorrections.AsNoTracking().Where(c => c.SourceReference == $"LeaveRequestId={leave:D}:Cancelled").OrderBy(c => c.AttendanceDate).ToListAsync();
        Assert.Equal(2, reverse.Count);
        Assert.Equal((DailyAttendanceStatus.OnLeave, DailyAttendanceStatus.Absent, 3, $"إجازة ملغاة {leave:D}"),
            (reverse[0].OldStatus, reverse[0].NewStatus, reverse[0].ResultingVersion, reverse[0].Reason));
        Assert.Equal(4, await db.AttendanceCorrections.CountAsync());
    }

    [Fact]
    public async Task A_leave_that_creates_a_locked_days_record_is_removed_with_it_when_cancelled()
    {
        await using var test = await Lva057Db.CreateAsync();
        // The month is closed with a working day that has no record (the setting ignores it), then a leave covers that day.
        var employee = await test.EmployeeAsync(test.Branch1);
        await test.FillAsync(employee, March, new DateOnly(2025, 3, 27));
        Assert.True((await test.PeriodService(AttendanceUnrecordedDaysMode.Ignore).CloseAsync(March, test.Branch1, test.Central())).Ok);
        test.Published.Clear();
        var leave = Guid.NewGuid();
        var day = new DateTime(2025, 3, 30, 0, 0, 0, DateTimeKind.Utc);
        var integration = new HR.Core.Infrastructure.Integration.AttendanceIntegrationService(
            test.Writer, test.CorrectionService(unrecorded: AttendanceUnrecordedDaysMode.Ignore));

        await new LeaveApprovedConsumer(test.Db, integration).Consume(Context(Approved(leave, employee, day, day)));
        var afterLeave = await SummaryAsync(test, employee);
        Assert.Equal((2, 1, 1), (afterLeave.Version, afterLeave.OnLeaveDays, afterLeave.UnrecordedWorkingDays));
        Assert.Null((await test.StoredAsync(employee, new DateOnly(2025, 3, 30)))!.ReplacedStatus);

        await new LeaveCancelledConsumer(test.Db, integration).Consume(Context(Cancelled(leave, employee, day, day)));

        Assert.Null(await test.StoredAsync(employee, new DateOnly(2025, 3, 30)));
        var afterCancel = await SummaryAsync(test, employee);
        Assert.Equal((3, 0, 2), (afterCancel.Version, afterCancel.OnLeaveDays, afterCancel.UnrecordedWorkingDays));
        await using var db = test.Fresh();
        var reverse = await db.AttendanceCorrections.AsNoTracking().SingleAsync(c => c.SourceReference == $"LeaveRequestId={leave:D}:Cancelled");
        Assert.Equal((DailyAttendanceStatus.OnLeave, (DailyAttendanceStatus?)null), (reverse.OldStatus, reverse.NewStatus));
    }

    [Fact]
    public async Task Under_the_Pending_setting_a_leave_on_a_locked_day_waits_for_a_central_person()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var leave = Guid.NewGuid();
        var day = new DateTime(2025, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var manual = await test.CorrectionService().RequestAsync(test.Branch1Manager(closed.Requester), closed.Employee, Day, DailyAttendanceStatus.Present, Reason);
        var pending = test.Integration(AttendanceLeaveEventMode.Pending);

        await new LeaveApprovedConsumer(test.Db, pending).Consume(Context(Approved(leave, closed.Employee, day, day)));

        // Nothing is applied: the day, the version and payroll are as they were; the person's request gave way to the leave's.
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(closed.Employee, Day))!.Status);
        Assert.Equal(1, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Empty(test.Published);
        Guid waiting;
        await using (var db = test.Fresh())
        {
            var byLeave = await db.AttendanceCorrections.AsNoTracking().SingleAsync(c => c.Source == AttendanceCorrectionSource.LeaveEvent);
            Assert.Equal((AttendanceCorrectionStatus.Pending, DailyAttendanceStatus.OnLeave), (byLeave.Status, byLeave.NewStatus));
            Assert.Null(byLeave.RequestedBy);
            waiting = byLeave.Id;
            var earlier = await db.AttendanceCorrections.AsNoTracking().SingleAsync(c => c.Id == manual.Correction!.Id);
            Assert.Equal((AttendanceCorrectionStatus.Rejected, AttendanceCorrectionMessages.SupersededByLeave), (earlier.Status, earlier.DecisionNote));
        }

        // A central person approves it: the day becomes a leave-made day, with the next version.
        test.Db.ChangeTracker.Clear();
        Assert.True((await test.CorrectionService(AttendanceLeaveEventMode.Pending).ApproveAsync(test.Central(closed.Approver), waiting, null)).Ok);
        var applied = await test.StoredAsync(closed.Employee, Day);
        Assert.Equal((DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave, $"LeaveRequestId={leave:D}"), (applied!.Status, applied.Source, applied.SourceReference));
        Assert.Equal((2, AttendanceSummaryReason.LeaveEvent), ((await SummaryAsync(test, closed.Employee)).Version, (await SummaryAsync(test, closed.Employee)).LastReason));
    }

    [Fact]
    public async Task Under_the_Pending_setting_a_leave_cancelled_before_its_correction_was_decided_withdraws_it()
    {
        await using var test = await Lva057Db.CreateAsync();
        var closed = await ClosedMonthAsync(test);
        var leave = Guid.NewGuid();
        var day = new DateTime(2025, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var pending = test.Integration(AttendanceLeaveEventMode.Pending);
        await new LeaveApprovedConsumer(test.Db, pending).Consume(Context(Approved(leave, closed.Employee, day, day)));

        await new LeaveCancelledConsumer(test.Db, pending).Consume(Context(Cancelled(leave, closed.Employee, day, day)));

        await using var db = test.Fresh();
        var correction = await db.AttendanceCorrections.AsNoTracking().SingleAsync();
        Assert.Equal((AttendanceCorrectionStatus.Rejected, AttendanceCorrectionMessages.LeaveCancelledBeforeApproval), (correction.Status, correction.DecisionNote));
        Assert.Equal(DailyAttendanceStatus.Absent, (await test.StoredAsync(closed.Employee, Day))!.Status);
        Assert.Equal(1, (await SummaryAsync(test, closed.Employee)).Version);
        Assert.Empty(test.Published);
    }
}
