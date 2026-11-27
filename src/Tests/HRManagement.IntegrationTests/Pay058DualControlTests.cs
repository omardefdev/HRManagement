using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport.Integration;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (§3, §7, P-7): every «four eyes» is a request and a decision by two people. For each kind: one person asks, the same person
/// cannot decide (400), another decides and the act happens with the decision; a rejection changes nothing; a request not decided in time
/// expires with no effect. A service-regime blocker's hold is not released while HR.Core still blocks the employee; a settled amount that
/// is not the sum of the acknowledged orders is refused and the batch stays as it was; a batch reaches «مسدَّدة» only by a decision.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay058DualControlTests
{
    private static readonly Guid Asker = Guid.Parse("58000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Decider = Guid.Parse("58000000-0000-4000-8000-0000000000e2");
    private static Task<bool> Holds(PayrollDualControlKind _) => Task.FromResult(true);

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static PayrollDualControlService Service(Pay056RunScope scope) => scope.Services.GetRequiredService<PayrollDualControlService>();

    private static async Task<PaymentHold> HoldAsync(Pay056RunScope scope, Guid employee, string type = PaymentHoldType.Manual, Guid? run = null)
    {
        var hold = new PaymentHold { EmployeeId = employee, PayrollRunId = run, HoldType = type, Reason = "مراجعة", Status = PaymentHoldStatus.Active };
        scope.Db.PaymentHolds.Add(hold);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        return hold;
    }

    [Fact]
    public async Task A_hold_is_released_by_a_request_and_another_persons_decision_never_by_its_requester()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var hold = await HoldAsync(scope, Guid.NewGuid());
        var service = Service(scope);

        var asked = await service.RequestAsync(PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("تحقق المعتمد من الأدلة"), Asker, default);
        Assert.True(asked.Success, asked.Error);
        Assert.Equal(RunActOutcome.Conflict, (await service.RequestAsync(PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("مرة ثانية"), Decider, default)).Outcome);

        var own = await service.DecideAsync(asked.Id!.Value, true, null, Asker, Holds, default);
        Assert.Equal((RunActOutcome.Invalid, PayrollDualControlMessages.DeciderIsRequester), (own.Outcome, own.Error));
        Assert.Equal(PaymentHoldStatus.Active, (await scope.Db.PaymentHolds.AsNoTracking().SingleAsync()).Status);

        var notHis = await service.DecideAsync(asked.Id!.Value, true, null, Decider, _ => Task.FromResult(false), default);
        Assert.Equal(RunActOutcome.Forbidden, notHis.Outcome);

        var decided = await service.DecideAsync(asked.Id!.Value, true, "موافق", Decider, Holds, default);
        Assert.True(decided.Success, decided.Error);
        scope.Db.ChangeTracker.Clear();
        var released = await scope.Db.PaymentHolds.AsNoTracking().SingleAsync();
        Assert.Equal((PaymentHoldStatus.Released, (Guid?)Asker, (Guid?)Decider), (released.Status, released.ReleasedBy, released.ReleaseApprovedBy));
        var request = await scope.Db.PayrollDualControlRequests.AsNoTracking().SingleAsync();
        Assert.Equal((PayrollDualControlStatus.Approved, (Guid?)Decider), (request.Status, request.DecidedBy));
        Assert.Equal(RunActOutcome.Conflict, (await service.DecideAsync(asked.Id!.Value, true, null, Decider, Holds, default)).Outcome);
    }

    [Fact]
    public async Task A_rejection_needs_a_note_and_changes_nothing_but_the_request()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var hold = await HoldAsync(scope, Guid.NewGuid());
        var service = Service(scope);
        var asked = await service.RequestAsync(PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("سبب"), Asker, default);

        Assert.Equal(PayrollDualControlMessages.RejectionNoteRequired, (await service.DecideAsync(asked.Id!.Value, false, " ", Decider, Holds, default)).Error);
        Assert.True((await service.DecideAsync(asked.Id!.Value, false, "الأدلة ناقصة", Decider, Holds, default)).Success);

        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PaymentHoldStatus.Active, (await scope.Db.PaymentHolds.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(PayrollDualControlStatus.Rejected, (await scope.Db.PayrollDualControlRequests.AsNoTracking().SingleAsync()).Status);
        // A rejected request no longer stands in the way of a new one.
        Assert.True((await service.RequestAsync(PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("أدلة جديدة"), Asker, default)).Success);
    }

    [Fact]
    public async Task A_request_not_decided_in_time_expires_with_no_effect_and_leaves_the_inbox()
    {
        var clock = new MutableClock();
        await using var scope = await Pay056RunScope.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var hold = await HoldAsync(scope, Guid.NewGuid());
        var service = Service(scope);
        var asked = await service.RequestAsync(PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("سبب"), Asker, default);
        Assert.Single(await service.InboxAsync(Decider, Holds, default));
        Assert.Empty(await service.InboxAsync(Asker, Holds, default));       // his own request is not in his inbox

        clock.Now = clock.Now.AddHours(73);                                   // the default expiry is 72 hours
        var late = await service.DecideAsync(asked.Id!.Value, true, null, Decider, Holds, default);

        Assert.Equal(PayrollDualControlMessages.Expired, late.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PayrollDualControlStatus.Expired, (await scope.Db.PayrollDualControlRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(PaymentHoldStatus.Active, (await scope.Db.PaymentHolds.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await service.InboxAsync(Decider, Holds, default));
    }

    [Fact]
    public async Task A_regime_blockers_hold_is_not_released_while_HR_Core_still_blocks_the_employee()
    {
        var hrCore = new FakeHrCoreServiceRegimeClient();
        await using var scope = await Pay056RunScope.CreateAsync(services => services.AddSingleton<IHrCoreServiceRegimeClient>(hrCore));
        var employee = Guid.NewGuid();
        hrCore.BlockedEmployeeIds.Add(employee);
        var hold = await HoldAsync(scope, employee, PaymentHoldType.RegimeBlocker);
        var service = Service(scope);
        var asked = await service.RequestAsync(PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("صُنّف نظامه"), Asker, default);

        var refused = await service.DecideAsync(asked.Id!.Value, true, null, Decider, Holds, default);
        Assert.Equal(PayrollDualControlMessages.RegimeStillBlocked, refused.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PaymentHoldStatus.Active, (await scope.Db.PaymentHolds.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(PayrollDualControlStatus.Pending, (await scope.Db.PayrollDualControlRequests.AsNoTracking().SingleAsync()).Status);

        hrCore.BlockedEmployeeIds.Clear();                                     // HR.Core classified his regime
        Assert.True((await service.DecideAsync(asked.Id!.Value, true, null, Decider, Holds, default)).Success);
        Assert.Equal(PaymentHoldStatus.Released, (await scope.Db.PaymentHolds.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_critical_alert_is_resolved_by_two_people_with_its_hold_and_a_non_critical_one_is_not_a_request()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = Guid.NewGuid();
        var hold = await HoldAsync(scope, employee);
        var critical = new FraudAlert { EmployeeId = employee, RuleId = FraudRuleIds.MissingSourceRecord, Severity = FraudAlertSeverity.Critical, Status = FraudAlertStatus.Open, PaymentHoldId = hold.Id };
        var medium = new FraudAlert { EmployeeId = employee, RuleId = FraudRuleIds.AnomalousCompensation, Severity = FraudAlertSeverity.Medium, Status = FraudAlertStatus.Open };
        scope.Db.FraudAlerts.AddRange(critical, medium);
        await scope.Db.SaveChangesAsync();
        var service = Service(scope);

        Assert.Equal(PayrollDualControlMessages.AlertNotCritical,
            (await service.RequestAsync(PayrollDualControlKind.CriticalAlertResolve, medium.Id, new PayrollDualControlPayload("ضمن النطاق"), Asker, default)).Error);

        var asked = await service.RequestAsync(PayrollDualControlKind.CriticalAlertResolve, critical.Id,
            new PayrollDualControlPayload("السجل موجود في HR", TargetStatus: FraudAlertStatus.Dismissed), Asker, default);
        Assert.True(asked.Success, asked.Error);
        Assert.True((await service.DecideAsync(asked.Id!.Value, true, null, Decider, Holds, default)).Success);

        scope.Db.ChangeTracker.Clear();
        var resolved = await scope.Db.FraudAlerts.AsNoTracking().SingleAsync(a => a.Id == critical.Id);
        Assert.Equal((FraudAlertStatus.Dismissed, (Guid?)Asker, (Guid?)Decider), (resolved.Status, resolved.ResolvedBy, resolved.ApprovedBy));
        Assert.Equal(PaymentHoldStatus.Released, (await scope.Db.PaymentHolds.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task An_arrears_adjustment_is_applied_by_the_decision_with_the_requester_and_the_decider_as_its_two_people()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var entry = new ArrearsLedgerEntry { EmployeeId = Guid.NewGuid(), PayrollRunId = Guid.NewGuid(), OriginalAmount = 1000m, PaidAmount = 600m, ArrearsAmount = 400m, Status = ArrearsLedgerStatus.PartiallyPaid };
        scope.Db.ArrearsLedgerEntries.Add(entry);
        await scope.Db.SaveChangesAsync();
        var service = Service(scope);

        Assert.Equal(PayrollDualControlMessages.ArrearsAmounts,
            (await service.RequestAsync(PayrollDualControlKind.ArrearsAdjust, entry.Id, new PayrollDualControlPayload("خطأ", OriginalAmount: 100m, PaidAmount: 200m), Asker, default)).Error);
        var asked = await service.RequestAsync(PayrollDualControlKind.ArrearsAdjust, entry.Id,
            new PayrollDualControlPayload("تصحيح المستحق بقرار", OriginalAmount: 1000m, PaidAmount: 1000m), Asker, default);
        Assert.True(asked.Success, asked.Error);
        Assert.Equal(400m, (await scope.Db.ArrearsLedgerEntries.AsNoTracking().SingleAsync()).ArrearsAmount);   // nothing yet

        Assert.True((await service.DecideAsync(asked.Id!.Value, true, null, Decider, Holds, default)).Success);
        scope.Db.ChangeTracker.Clear();
        var adjusted = await scope.Db.ArrearsLedgerEntries.AsNoTracking().SingleAsync();
        Assert.Equal((0m, ArrearsLedgerStatus.FullyPaid, (Guid?)Decider), (adjusted.ArrearsAmount, adjusted.Status, adjusted.UpdatedBy));
    }

    [Fact]
    public async Task The_channels_confirmation_settles_a_batch_by_two_people_and_a_wrong_amount_changes_nothing()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        scope.Db.Add(Pay055TestAccounts.Active(employee));
        await scope.Db.SaveChangesAsync();
        var runId = await scope.PostedRunAsync();
        var batchId = await scope.Db.PaymentBatches.Where(b => b.PayrollRunId == runId).Select(b => b.Id).SingleAsync();
        var sent = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Pay056RunScope.Poster));
        Assert.True(sent.Success, sent.Error);
        Assert.Equal(PaymentBatchMessages.ChannelReferenceRequired, (await scope.Mediator.Send(new AckPaymentBatchCommand(batchId, " "))).Error);
        Assert.True((await scope.Mediator.Send(new AckPaymentBatchCommand(batchId, "ACK-58"))).Success);
        var total = await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId && o.Status == PaymentOrderStatus.Acknowledged).SumAsync(o => o.Amount);
        var service = Service(scope);

        var wrong = await service.RequestAsync(PayrollDualControlKind.BatchSettle, batchId, new PayrollDualControlPayload("تسوية", Amount: total - 1m, Reference: "STMT-58"), Pay056RunScope.Poster, default);
        Assert.Equal((RunActOutcome.Invalid, PaymentBatchMessages.SettledAmountMismatch), (wrong.Outcome, wrong.Error));
        Assert.Empty(await scope.Db.PayrollDualControlRequests.AsNoTracking().ToListAsync());

        var asked = await service.RequestAsync(PayrollDualControlKind.BatchSettle, batchId, new PayrollDualControlPayload("تسوية", Amount: total, Reference: "STMT-58"), Pay056RunScope.Poster, default);
        Assert.True(asked.Success, asked.Error);
        Assert.Equal(PayrollDualControlMessages.DeciderIsRequester, (await service.DecideAsync(asked.Id!.Value, true, null, Pay056RunScope.Poster, Holds, default)).Error);
        Assert.Equal(PaymentBatchStatus.Acked, (await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).Status);

        Assert.True((await service.DecideAsync(asked.Id!.Value, true, "طابق الكشف", Pay056RunScope.Approver, Holds, default)).Success);
        scope.Db.ChangeTracker.Clear();
        var settled = await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal((PaymentBatchStatus.Settled, "STMT-58", total), (settled.Status, settled.SettlementReference, settled.DisbursedAmount));
        Assert.All(await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId).ToListAsync(),
            o => Assert.Equal(PaymentOrderStatus.Settled, o.Status));
    }

    [Fact]
    public async Task A_rejection_by_the_channel_and_a_bank_files_reissue_are_decided_by_another_person()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        scope.Db.Add(Pay055TestAccounts.Active(employee));
        await scope.Db.SaveChangesAsync();
        var runId = await scope.PostedRunAsync();
        var batchId = await scope.Db.PaymentBatches.Where(b => b.PayrollRunId == runId).Select(b => b.Id).SingleAsync();
        var sent = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Pay056RunScope.Poster));
        var service = Service(scope);

        var reissue = await service.RequestAsync(PayrollDualControlKind.BankFileReissue, sent.Id!.Value, new PayrollDualControlPayload("لم تستلمه القناة"), Pay056RunScope.Poster, default);
        Assert.True(reissue.Success, reissue.Error);
        Assert.True((await service.DecideAsync(reissue.Id!.Value, true, null, Pay056RunScope.Approver, Holds, default)).Success);
        scope.Db.ChangeTracker.Clear();
        var files = await scope.Db.PaymentBatchFiles.AsNoTracking().Where(f => f.PaymentBatchId == batchId).OrderBy(f => f.Sequence).ToListAsync();
        Assert.Equal(2, files.Count);
        Assert.Equal((PaymentBatchFileKind.Reissue, (Guid?)Pay056RunScope.Approver, Pay056RunScope.Poster), (files[1].Kind, files[1].ReissueApprovedBy, files[1].GeneratedBy));
        Assert.Equal(files[1].Id, files[0].SupersededById);

        var rejection = await service.RequestAsync(PayrollDualControlKind.BatchReject, batchId, new PayrollDualControlPayload("رقم حساب مغلق"), Pay056RunScope.Poster, default);
        Assert.True(rejection.Success, rejection.Error);
        Assert.True((await service.DecideAsync(rejection.Id!.Value, true, null, Pay056RunScope.Approver, Holds, default)).Success);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PaymentBatchStatus.Rejected, (await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).Status);
        Assert.All(await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId).ToListAsync(),
            o => Assert.Equal(PaymentOrderStatus.Rejected, o.Status));
    }
}
