using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Consumers;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-57 (§4, §5): how payroll takes the attendance handover, through the real consumer logic, loader, engine and run acts (SQLite;
/// the row lock, the outbox and concurrency are <c>Lva057PayrollAttendanceHandoffIntegrationTests</c> on PostgreSQL). The latest
/// version wins; a version is recorded once; an older version is ignored; a zero difference writes no row; a difference that arrives
/// after its period's run was calculated is carried, applied by that run's recalculation if it comes first and otherwise by the first
/// later regular run, and the frozen run never changes; a negative difference is refunded.
/// </summary>
public sealed class Lva057PayrollAttendanceHandoffTests
{
    // Pay056RunScope's employee earns 900,000 over January 2028's working days; the tests compare lines with and without a row.
    private static async Task<SalaryLine> LineAsync(Pay056RunScope scope, Guid run, Guid employee) =>
        (await scope.LinesAsync(run)).Single(l => l.EmployeeId == employee);

    private static async Task<List<SalaryLineComponent>> ComponentsAsync(Pay056RunScope scope, Guid line)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.SalaryLineComponents.AsNoTracking().Where(c => c.SalaryLineId == line).ToListAsync();
    }

    // ---------------------------------------------------------------- the receipt and the difference

    [Fact]
    public async Task The_close_writes_a_receipt_and_one_row_of_the_absence_dated_in_its_period()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var close = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);

        var outcome = await Lva057Payroll.ReceiveAsync(scope.Db, close);

        Assert.Equal(AttendanceHandoffOutcome.Recorded, outcome);
        var receipt = await Lva057Payroll.ReceiptAsync(scope.Db, close.SummaryId);
        Assert.NotNull(receipt);
        Assert.Equal((1, 2, 2, employee), (receipt!.LatestVersion, receipt.LatestAbsentDays, receipt.RecordedAbsentDays, receipt.EmployeeId));
        Assert.Equal((close.PeriodStart, close.PeriodEnd, close.AttendancePeriodId), (receipt.PeriodStart, receipt.PeriodEnd, receipt.AttendancePeriodId));
        var row = Assert.Single(await Lva057Payroll.RowsAsync(scope.Db, close.SummaryId));
        Assert.Equal((2, 0m, employee, false, 1), (row.AbsentDays, row.Amount, row.EmployeeId, row.CarryForward, row.SourceVersion));
        Assert.Equal($"ATT:{close.SummaryId:D}:v1", row.SourceReference);
        Assert.Equal((close.PeriodStart, close.PeriodEnd, close.PeriodStart, close.PeriodEnd),
            (row.PeriodStart, row.PeriodEnd, row.OriginalPeriodStart!.Value, row.OriginalPeriodEnd!.Value));
        Assert.Null(row.AppliedInRunId);
    }

    [Fact]
    public async Task A_close_with_no_absence_writes_the_receipt_and_no_row()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var close = Lva057Summary.Close(employee, 2028, 1, absentDays: 0);

        // The old consumer returned on zero absence: payroll then could not tell «closed with no absence» from «not closed».
        var outcome = await Lva057Payroll.ReceiveAsync(scope.Db, close);

        Assert.Equal(AttendanceHandoffOutcome.RecordedNoDifference, outcome);
        var receipt = await Lva057Payroll.ReceiptAsync(scope.Db, close.SummaryId);
        Assert.Equal((1, 0, 0), (receipt!.LatestVersion, receipt.LatestAbsentDays, receipt.RecordedAbsentDays));
        Assert.Empty(await Lva057Payroll.RowsAsync(scope.Db, close.SummaryId));
        Assert.Equal(0, await scope.Db.AbsenceDeductions.CountAsync());
    }

    [Fact]
    public async Task Each_version_is_recorded_once_as_the_difference_against_what_was_recorded()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);
        var v2 = v1.Next(absentDays: 1);
        var v3 = v2.Next(absentDays: 1);          // a correction that changes no figure
        var v4 = v3.Next(absentDays: 4, reason: "LeaveEvent");

        Assert.Equal(AttendanceHandoffOutcome.Recorded, await Lva057Payroll.ReceiveAsync(scope.Db, v1));
        Assert.Equal(AttendanceHandoffOutcome.Recorded, await Lva057Payroll.ReceiveAsync(scope.Db, v2));
        Assert.Equal(AttendanceHandoffOutcome.RecordedNoDifference, await Lva057Payroll.ReceiveAsync(scope.Db, v3));
        Assert.Equal(AttendanceHandoffOutcome.Recorded, await Lva057Payroll.ReceiveAsync(scope.Db, v4));
        // The same versions again, in any order: nothing.
        Assert.Equal(AttendanceHandoffOutcome.Duplicate, await Lva057Payroll.ReceiveAsync(scope.Db, v4));
        Assert.Equal(AttendanceHandoffOutcome.IgnoredOlderVersion, await Lva057Payroll.ReceiveAsync(scope.Db, v2));
        Assert.Equal(AttendanceHandoffOutcome.IgnoredOlderVersion, await Lva057Payroll.ReceiveAsync(scope.Db, v1));

        var rows = await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId);
        Assert.Equal([(1, 2), (2, -1), (4, 3)], rows.Select(r => (r.SourceVersion!.Value, r.AbsentDays)).ToArray());
        Assert.Equal(4, rows.Sum(r => r.AbsentDays));
        var receipt = await Lva057Payroll.ReceiptAsync(scope.Db, v1.SummaryId);
        Assert.Equal((4, 4, 4), (receipt!.LatestVersion, receipt.LatestAbsentDays, receipt.RecordedAbsentDays));
        Assert.Equal(1, await scope.Db.AttendanceSummaryReceipts.CountAsync());
    }

    [Fact]
    public async Task Versions_that_arrive_in_reverse_order_end_at_the_latest_versions_absence()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);
        var v2 = v1.Next(absentDays: 1);
        var v3 = v2.Next(absentDays: 3);

        // The newest first: its value is absolute, so nothing is lost when the older ones arrive late and are ignored.
        Assert.Equal(AttendanceHandoffOutcome.Recorded, await Lva057Payroll.ReceiveAsync(scope.Db, v3));
        Assert.Equal(AttendanceHandoffOutcome.IgnoredOlderVersion, await Lva057Payroll.ReceiveAsync(scope.Db, v2));
        Assert.Equal(AttendanceHandoffOutcome.IgnoredOlderVersion, await Lva057Payroll.ReceiveAsync(scope.Db, v1));

        var row = Assert.Single(await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId));
        Assert.Equal((3, 3), (row.SourceVersion, row.AbsentDays));
        var receipt = await Lva057Payroll.ReceiptAsync(scope.Db, v1.SummaryId);
        Assert.Equal((3, 3, 3), (receipt!.LatestVersion, receipt.LatestAbsentDays, receipt.RecordedAbsentDays));

        // A gap in the versions (v2 first, then v1): the difference is still against what was recorded.
        var other = Lva057Summary.Close(await scope.EmployeeAsync(), 2028, 1, absentDays: 5);
        await Lva057Payroll.ReceiveAsync(scope.Db, other.Next(absentDays: 1));
        await Lva057Payroll.ReceiveAsync(scope.Db, other);
        await Lva057Payroll.ReceiveAsync(scope.Db, other.Next(absentDays: 0, version: 3));
        Assert.Equal([(2, 1), (3, -1)], (await Lva057Payroll.RowsAsync(scope.Db, other.SummaryId)).Select(r => (r.SourceVersion!.Value, r.AbsentDays)).ToArray());
    }

    [Fact]
    public async Task The_consumer_skips_a_redelivered_message_and_stages_its_receipt_with_the_rows()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var close = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);
        var consumer = new AttendanceSummaryPublishedConsumer(scope.Db);
        var messageId = NewId.NextGuid();
        ConsumeContext<IAttendanceSummaryPublished> Context(IAttendanceSummaryPublished message, Guid id)
        {
            var context = new Mock<ConsumeContext<IAttendanceSummaryPublished>>();
            context.SetupGet(c => c.Message).Returns(message);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            context.SetupGet(c => c.MessageId).Returns(id);
            return context.Object;
        }

        await consumer.Consume(Context(close, messageId));
        await consumer.Consume(Context(close, messageId));
        // The same version under another message id (a republish): the version check holds where the inbox does not.
        scope.Db.ChangeTracker.Clear();
        await consumer.Consume(Context(close, NewId.NextGuid()));

        Assert.Single(await Lva057Payroll.RowsAsync(scope.Db, close.SummaryId));
        Assert.Equal(1, (await Lva057Payroll.ReceiptAsync(scope.Db, close.SummaryId))!.LatestVersion);
        Assert.Equal(2, await scope.Db.IntegrationMessageReceipts.CountAsync(r => r.ConsumerType == nameof(AttendanceSummaryPublishedConsumer)));
    }

    [Fact]
    public async Task A_message_with_no_summary_or_no_version_is_refused_and_writes_nothing()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var close = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Lva057Payroll.ReceiveAsync(scope.Db, close with { Version = 0 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Lva057Payroll.ReceiveAsync(scope.Db, close with { SummaryId = Guid.Empty }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Lva057Payroll.ReceiveAsync(scope.Db, close with { PeriodEnd = close.PeriodStart.AddDays(-1) }));

        Assert.Equal(0, await scope.Db.AttendanceSummaryReceipts.CountAsync());
        Assert.Equal(0, await scope.Db.AbsenceDeductions.CountAsync());
    }

    [Fact]
    public async Task The_tenant_on_the_receipt_and_the_row_is_the_messages()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var tenant = Guid.Parse("a0000057-0000-4000-8000-0000000000a1");
        var close = Lva057Summary.Close(Guid.NewGuid(), 2028, 1, absentDays: 1, tenant: tenant);

        await Lva057Payroll.ReceiveAsync(scope.Db, close);

        scope.Db.ChangeTracker.Clear();
        Assert.Equal(tenant, (await scope.Db.AttendanceSummaryReceipts.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.SummaryId == close.SummaryId)).TenantId);
        Assert.Equal(tenant, (await scope.Db.AbsenceDeductions.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.SourceReference == $"ATT:{close.SummaryId:D}:v1")).TenantId);
    }

    // ---------------------------------------------------------------- the period's own run

    [Fact]
    public async Task Differences_that_arrive_before_the_periods_run_is_calculated_are_applied_by_it_net()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var colleague = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1.Next(absentDays: 1));
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(colleague, 2028, 1, absentDays: 0));

        var run = await scope.CalculatedRunAsync(2028, 1);

        var line = await LineAsync(scope, run, employee);
        var reference = await LineAsync(scope, run, colleague);
        var components = await ComponentsAsync(scope, line.Id);
        var rows = await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId);
        Assert.All(rows, r => Assert.Equal(run, r.AppliedInRunId));
        Assert.All(rows, r => Assert.False(r.CarryForward));
        var deducted = components.Single(c => c.ReferenceId == rows[0].Id);
        var refunded = components.Single(c => c.ReferenceId == rows[1].Id);
        // Two days deducted and one refunded: one day, net.
        Assert.True(deducted.Amount < 0);
        Assert.Equal(-deducted.Amount / 2, refunded.Amount);
        Assert.Equal(reference.TotalDeductions + refunded.Amount, line.TotalDeductions);
        Assert.Equal(reference.NetPay - refunded.Amount, line.NetPay);
        Assert.Equal("خصم غياب — فترة الحضور 2028-01", deducted.ComponentName);
        Assert.Equal("استرداد خصم غياب — فترة 2028-01", refunded.ComponentName);
        // Both were in the run's own period: nothing was carried.
        var status = await scope.Services.GetRequiredService<PayrollAttendanceGate>().StatusAsync(await scope.RunAsync(run));
        Assert.Equal((0, 0), (status.CarryForwardApplied, status.CarryForwardPending));
    }

    // ---------------------------------------------------------------- after the period's run was calculated

    [Fact]
    public async Task A_difference_after_the_freeze_is_carried_to_the_next_cycle_and_the_frozen_run_does_not_change()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var colleague = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 0);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);
        var january = await scope.CalculatedRunAsync(2028, 1);
        await scope.SubmitAsync(january);
        var frozen = await scope.RunAsync(january);
        var frozenLine = await LineAsync(scope, january, employee);
        Assert.True(await PayrollAttendanceFreeze.IsFrozenAsync(scope.Db.PayrollRuns, v1.PeriodStart, v1.PeriodEnd));

        // A correction approved after the freeze: one more day of absence.
        await Lva057Payroll.ReceiveAsync(scope.Db, v1.Next(absentDays: 1));

        var carried = Assert.Single(await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId));
        Assert.True(carried.CarryForward);
        Assert.Equal((1, v1.PeriodStart, v1.PeriodEnd), (carried.AbsentDays, carried.OriginalPeriodStart!.Value, carried.OriginalPeriodEnd!.Value));
        Assert.Null(carried.AppliedInRunId);
        var gate = scope.Services.GetRequiredService<PayrollAttendanceGate>();
        Assert.Equal(1, (await gate.StatusAsync(await scope.RunAsync(january))).CarryForwardPending);

        var february = await scope.CalculatedRunAsync(2028, 2);

        // February applies it, with an item that names January.
        var line = await LineAsync(scope, february, employee);
        var reference = await LineAsync(scope, february, colleague);
        var item = (await ComponentsAsync(scope, line.Id)).Single(c => c.ComponentName == "فرق غياب عن فترة 2028-01");
        Assert.True(item.Amount < 0);
        Assert.Equal(reference.TotalDeductions - item.Amount, line.TotalDeductions);
        Assert.Equal(february, (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single().AppliedInRunId);
        var februaryStatus = await gate.StatusAsync(await scope.RunAsync(february));
        Assert.Equal((1, 0), (februaryStatus.CarryForwardApplied, februaryStatus.CarryForwardPending));

        // January is as it was: its status, its total, its line and its components.
        var after = await scope.RunAsync(january);
        Assert.Equal((frozen.Status, frozen.TotalNetPay, frozen.Revision, frozen.ResultsFrozen), (after.Status, after.TotalNetPay, after.Revision, after.ResultsFrozen));
        var lineAfter = await LineAsync(scope, january, employee);
        Assert.Equal((frozenLine.GrossPay, frozenLine.TotalDeductions, frozenLine.NetPay), (lineAfter.GrossPay, lineAfter.TotalDeductions, lineAfter.NetPay));
        Assert.DoesNotContain(await ComponentsAsync(scope, lineAfter.Id), c => c.ReferenceId == carried.Id);

        // March takes nothing more: the row was applied once.
        var march = await scope.CalculatedRunAsync(2028, 3);
        Assert.DoesNotContain(await ComponentsAsync(scope, (await LineAsync(scope, march, employee)).Id), c => c.ReferenceId == carried.Id);
    }

    [Fact]
    public async Task A_negative_difference_after_the_freeze_is_refunded_by_the_next_cycle()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var colleague = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);
        var january = await scope.CalculatedRunAsync(2028, 1);
        await scope.SubmitAsync(january);
        var januaryLine = await LineAsync(scope, january, employee);

        // One of the two absences is corrected to presence after January was deducted for both.
        await Lva057Payroll.ReceiveAsync(scope.Db, v1.Next(absentDays: 1));
        var february = await scope.CalculatedRunAsync(2028, 2);

        var refundRow = (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single(r => r.SourceVersion == 2);
        Assert.Equal((-1, true, february), (refundRow.AbsentDays, refundRow.CarryForward, refundRow.AppliedInRunId!.Value));
        var line = await LineAsync(scope, february, employee);
        var reference = await LineAsync(scope, february, colleague);
        var refund = (await ComponentsAsync(scope, line.Id)).Single(c => c.ReferenceId == refundRow.Id);
        Assert.Equal("استرداد خصم غياب — فترة 2028-01", refund.ComponentName);
        Assert.True(refund.Amount > 0);
        Assert.Equal(reference.TotalDeductions - refund.Amount, line.TotalDeductions);
        Assert.Equal(reference.NetPay + refund.Amount, line.NetPay);
        // January keeps the two days it deducted.
        Assert.Equal(januaryLine.TotalDeductions, (await LineAsync(scope, january, employee)).TotalDeductions);
        // The refund is given once.
        var march = await scope.CalculatedRunAsync(2028, 3);
        Assert.DoesNotContain(await ComponentsAsync(scope, (await LineAsync(scope, march, employee)).Id), c => c.ReferenceId == refundRow.Id);
    }

    [Fact]
    public async Task A_carried_difference_is_taken_by_its_own_periods_recalculation_when_that_comes_first()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 0);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);
        var january = await scope.CalculatedRunAsync(2028, 1);
        await scope.SubmitAsync(january);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1.Next(absentDays: 1));
        Assert.True((await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single().CarryForward);

        // The reviewer rejects January: it reopens (PLAN-56) and is calculated again before any later run.
        scope.Db.ChangeTracker.Clear();
        var rejected = await scope.Mediator.Send(new RejectPayrollRunCommand(january, Pay056RunScope.Approver, "أعد الحساب بعد تصحيح الحضور"));
        Assert.True(rejected.Success, rejected.Error);
        var (ok, _, error) = await scope.CalculateAsync(january);
        Assert.True(ok, error);

        var row = (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single();
        Assert.Equal(january, row.AppliedInRunId);
        // In its own period's run the row carries its own reason, not «a difference of an earlier period».
        var item = (await ComponentsAsync(scope, (await LineAsync(scope, january, employee)).Id)).Single(c => c.ReferenceId == row.Id);
        Assert.Equal("فرق غياب بتصحيح الحضور — فترة 2028-01", item.ComponentName);
        // February then finds nothing left to carry.
        var february = await scope.CalculatedRunAsync(2028, 2);
        Assert.DoesNotContain(await ComponentsAsync(scope, (await LineAsync(scope, february, employee)).Id), c => c.ReferenceId == row.Id);
    }

    [Fact]
    public async Task A_cancelled_later_run_frees_the_carried_difference_for_the_next_one()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 0);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);
        var january = await scope.CalculatedRunAsync(2028, 1);
        await scope.SubmitAsync(january);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1.Next(absentDays: 1));
        var february = await scope.CalculatedRunAsync(2028, 2);
        Assert.Equal(february, (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single().AppliedInRunId);

        // PLAN-56's cancellation releases the applied marker and nothing else: the row is free again, and still carried.
        scope.Db.ChangeTracker.Clear();
        var cancelled = await scope.Mediator.Send(new CancelPayrollRunCommand(february, Pay056RunScope.Processor, "حُسب شباط قبل أوانه", ActorMayProcess: true, ActorMayApprove: false));
        Assert.True(cancelled.Success, cancelled.Error);
        var freed = (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single();
        Assert.Null(freed.AppliedInRunId);
        Assert.True(freed.CarryForward);
        Assert.Equal((1, v1.PeriodStart), (freed.AbsentDays, freed.OriginalPeriodStart!.Value));

        var again = await scope.CalculatedRunAsync(2028, 2);
        Assert.Equal(again, (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single().AppliedInRunId);
    }

    [Fact]
    public async Task A_difference_of_a_period_with_no_run_waits_for_that_periods_run_and_no_later_run_takes_it()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        // January's attendance is closed with an absence, and no January run exists.
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 2);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);

        var march = await scope.CalculatedRunAsync(2028, 3);

        var row = (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single();
        Assert.False(row.CarryForward);
        Assert.Null(row.AppliedInRunId);
        Assert.DoesNotContain(await ComponentsAsync(scope, (await LineAsync(scope, march, employee)).Id), c => c.ReferenceId == row.Id);

        // January's own run takes it when it is calculated.
        var january = await scope.CalculatedRunAsync(2028, 1);
        Assert.Equal(january, (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single().AppliedInRunId);
    }

    [Fact]
    public async Task A_difference_whose_mark_lost_the_race_with_its_periods_calculation_is_still_taken_by_the_next_cycle()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var v1 = Lva057Summary.Close(employee, 2028, 1, absentDays: 0);
        await Lva057Payroll.ReceiveAsync(scope.Db, v1);
        var january = await scope.CalculatedRunAsync(2028, 1);
        await scope.SubmitAsync(january);
        // The consumer decided «not carried» an instant before January was calculated, and January never saw the row.
        scope.Db.AbsenceDeductions.Add(new AbsenceDeduction
        {
            EmployeeId = employee,
            PeriodStart = v1.PeriodStart,
            PeriodEnd = v1.PeriodEnd,
            OriginalPeriodStart = v1.PeriodStart,
            OriginalPeriodEnd = v1.PeriodEnd,
            AbsentDays = 1,
            Reason = "فرق غياب بتصحيح الحضور — فترة 2028-01",
            SourceReference = AbsenceDeduction.AttendanceReference(v1.SummaryId, 2),
            SourceVersion = 2,
            CarryForward = false
        });
        await scope.Db.SaveChangesAsync();

        var february = await scope.CalculatedRunAsync(2028, 2);

        var row = (await Lva057Payroll.RowsAsync(scope.Db, v1.SummaryId)).Single();
        Assert.Equal(february, row.AppliedInRunId);
        Assert.Contains(await ComponentsAsync(scope, (await LineAsync(scope, february, employee)).Id),
            c => c.ReferenceId == row.Id && c.ComponentName == "فرق غياب عن فترة 2028-01");
    }

    // ---------------------------------------------------------------- the carried mark and the run's states

    [Fact]
    public async Task The_carried_mark_follows_the_state_of_the_periods_regular_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        async Task<bool> CarriedAsync(int month, int absent = 1)
        {
            var close = Lva057Summary.Close(Guid.NewGuid(), 2028, month, absent);
            await Lva057Payroll.ReceiveAsync(scope.Db, close);
            return (await Lva057Payroll.RowsAsync(scope.Db, close.SummaryId)).Single().CarryForward;
        }

        // No run; a draft: the period's own run will take the difference.
        Assert.False(await CarriedAsync(1));
        var draft = await scope.CreateRunAsync(2028, 1);
        Assert.False(await CarriedAsync(1));
        Assert.False(await PayrollAttendanceFreeze.HasCalculatedRunAsync(scope.Db.PayrollRuns, new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 31)));

        // Calculated, under review, approved: carried.
        var (ok, _, error) = await scope.CalculateAsync(draft);
        Assert.True(ok, error);
        Assert.True(await CarriedAsync(1));
        await scope.SubmitAsync(draft);
        Assert.True(await CarriedAsync(1));
        await scope.ApproveAsync(draft);
        Assert.True(await CarriedAsync(1));
        // Another month is not touched by January's run.
        Assert.False(await CarriedAsync(2));

        // A cancelled run does not count: its period is free again.
        var february = await scope.CalculatedRunAsync(2028, 2);
        Assert.True(await CarriedAsync(2));
        scope.Db.ChangeTracker.Clear();
        Assert.True((await scope.Mediator.Send(new CancelPayrollRunCommand(february, Pay056RunScope.Processor, "إلغاء للاختبار", true, false))).Success);
        Assert.False(await CarriedAsync(2));
        Assert.False(await PayrollAttendanceFreeze.IsFrozenAsync(scope.Db.PayrollRuns, new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)));
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft, false, false, false)]
    [InlineData(PayrollRunStatus.Calculating, false, true, false)]
    [InlineData(PayrollRunStatus.Calculated, false, true, false)]
    [InlineData(PayrollRunStatus.Calculated, true, true, true)]
    [InlineData(PayrollRunStatus.Validating, false, true, false)]
    [InlineData(PayrollRunStatus.UnderReview, true, true, true)]
    [InlineData(PayrollRunStatus.UnderReview, false, true, true)]
    [InlineData(PayrollRunStatus.Approved, true, true, true)]
    [InlineData(PayrollRunStatus.Posted, true, true, true)]
    [InlineData(PayrollRunStatus.Reversed, true, false, false)]
    [InlineData(PayrollRunStatus.Cancelled, true, false, false)]
    [InlineData(PayrollRunStatus.Cancelled, false, false, false)]
    public void Every_run_state_reads_as_calculated_or_not_and_frozen_or_not(PayrollRunStatus status, bool resultsFrozen, bool calculated, bool frozen)
    {
        Assert.Equal(calculated, PayrollAttendanceFreeze.IsCalculated(status));
        Assert.Equal(frozen, PayrollAttendanceFreeze.IsFrozen(status, resultsFrozen));
    }
}
