using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (G-PAY-06, D-PAY-16): a reversal is a request with a reason that another person approves. An approval voids every batch
/// that has not left and cancels its draft orders, refuses a run whose money has left, frees the run's inputs, reverses the run and
/// stages <c>IPayrollReversed</c> — and writes no reversal batch. After it a new regular run of the period can be made.
/// </summary>
[Collection("IntegrationSerial")]
public class Pay056ReversalTests
{
    private const string Why = "رُحّل التشغيل قبل اعتماد تسويات الشهر.";

    private static Task<RunActResult> RequestAsync(Pay056RunScope scope, Guid runId, string? reason = Why, Guid? by = null)
    {
        scope.Db.ChangeTracker.Clear();
        return scope.Mediator.Send(new RequestPayrollReversalCommand(runId, by ?? Pay056RunScope.Reverser, reason));
    }

    private static Task<RunActResult> DecideAsync(Pay056RunScope scope, Guid requestId, bool approve, string? notes = null, Guid? by = null)
    {
        scope.Db.ChangeTracker.Clear();
        return scope.Mediator.Send(new DecidePayrollReversalCommand(requestId, by ?? Pay056RunScope.Approver, approve, notes));
    }

    private static async Task<PaymentBatch> BatchAsync(Pay056RunScope scope, Guid runId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == runId);
    }

    [Fact]
    public async Task An_approved_reversal_voids_the_batch_that_has_not_left_frees_the_inputs_reverses_the_run_and_stages_its_event()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var adjustment = await scope.AdjustmentAsync(employee, 40_000m);
        var installment = await scope.DeductionAsync(employee, 25_000m, "AdvanceRecovery", new DateOnly(2028, 1, 15));
        var absence = await scope.AbsenceAsync(employee, new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 31), 12_000m);
        var runId = await scope.PostedRunAsync();
        var posted = await scope.RunAsync(runId);
        Assert.Equal((PayrollRunStatus.Posted, (Guid?)Pay056RunScope.Poster), (posted.Status, posted.PostedBy));
        Assert.NotNull(posted.PostedAt);
        var batch = await BatchAsync(scope, runId);
        Assert.Equal(PaymentBatchStatus.Pending, batch.Status);
        // Two draft orders on the batch, as the order service leaves them before the batch is sent; and one the channel rejected.
        var line = (await scope.LinesAsync(runId)).Single();
        scope.Db.PaymentOrders.AddRange(
            new PaymentOrder { PayrollRunId = runId, PaymentBatchId = batch.Id, SalaryLineId = line.Id, EmployeeId = employee, Amount = line.NetPay, Status = PaymentOrderStatus.Draft, IdempotencyKey = "draft" },
            new PaymentOrder { PayrollRunId = runId, PaymentBatchId = batch.Id, SalaryLineId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Amount = 1m, Status = PaymentOrderStatus.Rejected, IdempotencyKey = "rejected" });
        await scope.Db.SaveChangesAsync();

        var requested = await RequestAsync(scope, runId);
        Assert.True(requested.Success, requested.Error);
        var pending = await scope.Db.PayrollReversalRequests.AsNoTracking().SingleAsync();
        Assert.Equal((PayrollReversalRequestStatus.Pending, Pay056RunScope.Reverser, Why), (pending.Status, pending.RequestedBy, pending.Reason));
        // Asking changes nothing but the request.
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.Equal(PaymentBatchStatus.Pending, (await BatchAsync(scope, runId)).Status);

        var decided = await DecideAsync(scope, pending.Id, approve: true, "موافق: لم تُرسل الدفعة");

        Assert.True(decided.Success, decided.Error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Reversed, run.Status);
        Assert.Equal(posted.TotalNetPay, run.TotalNetPay);
        // The batch is voided, not «reversed», and no reversal batch claims to recover money nobody paid.
        var batches = await scope.Db.PaymentBatches.AsNoTracking().Where(b => b.PayrollRunId == runId).ToListAsync();
        var voided = Assert.Single(batches);
        Assert.Equal((PaymentBatchStatus.Voided, PaymentBatchKind.Disbursement), (voided.Status, voided.BatchKind));
        Assert.DoesNotContain(batches, b => b.BatchKind == PaymentBatchKind.Reversal);
        // Its draft order is cancelled; the order the channel rejected stays the channel's record.
        var orders = await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PayrollRunId == runId).ToDictionaryAsync(o => o.IdempotencyKey!, o => o.Status);
        Assert.Equal(PaymentOrderStatus.Cancelled, orders["draft"]);
        Assert.Equal(PaymentOrderStatus.Rejected, orders["rejected"]);
        // The inputs are free: the adjustment too (the old reversal left adjustments behind).
        Assert.Null((await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == adjustment.Id)).PayrollRunId);
        Assert.Null((await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == installment.Id)).AppliedInRunId);
        Assert.Null((await scope.Db.AbsenceDeductions.AsNoTracking().SingleAsync(a => a.Id == absence.Id)).AppliedInRunId);
        // The request is decided, by the approver, with his note.
        var request = await scope.Db.PayrollReversalRequests.AsNoTracking().SingleAsync();
        Assert.Equal((PayrollReversalRequestStatus.Approved, (Guid?)Pay056RunScope.Approver, "موافق: لم تُرسل الدفعة"), (request.Status, request.DecidedBy, request.DecisionNotes));
        Assert.NotNull(request.DecidedAt);
        // The event: the run, the amount the posting published, the request.
        var reversed = Assert.Single(scope.Events.Reversed);
        Assert.Equal((runId, posted.TotalNetPay, request.Id), (reversed.RunId, reversed.ReversedNetPay, reversed.RequestId));
        Assert.Equal(scope.Events.Processed.Single(p => p.RunId == runId).TotalNetPay, reversed.ReversedNetPay);
        // No closed audit record «by nobody» is written by a reversal any more: the only closed one is the posting's precondition.
        // (PLAN-57: the calculation's open attendance warning is another record of the run, and is counted by what it is.)
        var auditRecords = await scope.Db.PayrollAuditRecords.AsNoTracking().Where(a => a.PayrollRunId == runId).ToListAsync();
        Assert.Single(auditRecords, a => a.Status == AuditStatus.Closed);
        Assert.All(auditRecords.Where(a => a.Status != AuditStatus.Closed),
            a => Assert.StartsWith(PayrollAttendanceGate.WarningPrefix, a.Findings));
        Assert.Single(await scope.Db.AuditLogs.AsNoTracking().Where(a => a.EntityId == runId && a.Action == PayrollInputRelease.AuditAction).ToListAsync());
        // The lines stay: a reversed run keeps its record.
        Assert.Single(await scope.LinesAsync(runId));

        // A new regular run of the same period can be made, and it takes the three inputs.
        var again = await scope.CalculatedRunAsync();
        Assert.NotEqual(runId, again);
        Assert.Equal(again, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == adjustment.Id)).PayrollRunId);
        Assert.Equal(again, (await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == installment.Id)).AppliedInRunId);
        Assert.Equal(again, (await scope.Db.AbsenceDeductions.AsNoTracking().SingleAsync(a => a.Id == absence.Id)).AppliedInRunId);
        Assert.Equal(line.NetPay, (await scope.LinesAsync(again)).Single().NetPay);
    }

    [Theory]
    [InlineData(PaymentBatchStatus.Pending)]
    [InlineData(PaymentBatchStatus.Failed)]
    [InlineData(PaymentBatchStatus.Rejected)]
    public async Task A_batch_that_has_not_left_is_voided(PaymentBatchStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync();
        await scope.Db.PaymentBatches.Where(b => b.PayrollRunId == runId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, status));
        var request = (await RequestAsync(scope, runId)).Id!.Value;

        var decided = await DecideAsync(scope, request, approve: true);

        Assert.True(decided.Success, decided.Error);
        Assert.Equal(PaymentBatchStatus.Voided, (await BatchAsync(scope, runId)).Status);
        Assert.Equal(PayrollRunStatus.Reversed, (await scope.RunAsync(runId)).Status);
    }

    [Theory]
    [InlineData(PaymentBatchStatus.Submitted)]
    [InlineData(PaymentBatchStatus.Acked)]
    [InlineData(PaymentBatchStatus.Settled)]
    [InlineData(PaymentBatchStatus.Processed)]
    [InlineData(PaymentBatchStatus.Returned)]
    public async Task A_run_whose_batch_has_left_is_not_reversed_and_nothing_changes(PaymentBatchStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var adjustment = await scope.AdjustmentAsync(employee, 40_000m);
        var runId = await scope.PostedRunAsync();
        await scope.Db.PaymentBatches.Where(b => b.PayrollRunId == runId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, status));
        var request = (await RequestAsync(scope, runId)).Id!.Value;

        var decided = await DecideAsync(scope, request, approve: true);

        // D-PAY-16: refused, with the message that points to a correction run.
        Assert.Equal(RunActOutcome.Invalid, decided.Outcome);
        Assert.Equal(PayrollReversalMessages.MoneyHasLeft, decided.Error);
        Assert.Contains("تشغيل تصحيح", decided.Error);
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.Equal(status, (await BatchAsync(scope, runId)).Status);
        Assert.Equal(runId, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == adjustment.Id)).PayrollRunId);
        Assert.Equal(PayrollReversalRequestStatus.Pending, (await scope.Db.PayrollReversalRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(scope.Events.Reversed);

        // The request can still be rejected, with a note: it leaves nothing open.
        Assert.True((await DecideAsync(scope, request, approve: false, "خرج المال: يُصحَّح بتشغيل تصحيح")).Success);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Submitted)]
    [InlineData(PaymentOrderStatus.Acknowledged)]
    [InlineData(PaymentOrderStatus.Settled)]
    [InlineData(PaymentOrderStatus.PartiallySettled)]
    public async Task An_order_that_has_left_refuses_the_reversal_even_on_a_batch_that_has_not(PaymentOrderStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync();
        var batch = await BatchAsync(scope, runId);
        scope.Db.PaymentOrders.Add(new PaymentOrder { PayrollRunId = runId, PaymentBatchId = batch.Id, SalaryLineId = Guid.NewGuid(), EmployeeId = employee, Amount = 1m, Status = status, IdempotencyKey = "left" });
        await scope.Db.SaveChangesAsync();
        var request = (await RequestAsync(scope, runId)).Id!.Value;

        var decided = await DecideAsync(scope, request, approve: true);

        Assert.Equal(PayrollReversalMessages.MoneyHasLeft, decided.Error);
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.Equal(PaymentBatchStatus.Pending, (await BatchAsync(scope, runId)).Status);
        Assert.Equal(status, (await scope.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.IdempotencyKey == "left")).Status);
    }

    [Fact]
    public async Task A_rejected_request_needs_its_note_and_changes_nothing_but_the_request()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync();
        var request = (await RequestAsync(scope, runId)).Id!.Value;

        var noNote = await DecideAsync(scope, request, approve: false, notes: "  ");
        Assert.Equal(RunActOutcome.Invalid, noNote.Outcome);
        Assert.Equal(PayrollReversalMessages.RejectionNoteRequired, noNote.Error);
        Assert.Equal(PayrollReversalRequestStatus.Pending, (await scope.Db.PayrollReversalRequests.AsNoTracking().SingleAsync()).Status);

        var rejected = await DecideAsync(scope, request, approve: false, "الترحيل صحيح: لا موجب للعكس");

        Assert.True(rejected.Success, rejected.Error);
        var row = await scope.Db.PayrollReversalRequests.AsNoTracking().SingleAsync();
        Assert.Equal((PayrollReversalRequestStatus.Rejected, (Guid?)Pay056RunScope.Approver, "الترحيل صحيح: لا موجب للعكس"), (row.Status, row.DecidedBy, row.DecisionNotes));
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.Equal(PaymentBatchStatus.Pending, (await BatchAsync(scope, runId)).Status);
        Assert.Empty(scope.Events.Reversed);

        // A decided request is not decided again; a new request may be made after a rejection.
        Assert.Equal(RunActOutcome.Conflict, (await DecideAsync(scope, request, approve: true)).Outcome);
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.True((await RequestAsync(scope, runId, "سبب آخر")).Success);
    }

    [Fact]
    public async Task One_request_waits_on_a_run_at_a_time_and_a_request_needs_a_reason_and_a_posted_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync();
        var approvedOnly = await scope.ApprovedRunAsync(2028, 2);

        Assert.Equal(PayrollReversalMessages.ReasonRequired, (await RequestAsync(scope, runId, reason: " ")).Error);
        Assert.Equal(RunActOutcome.NotFound, (await RequestAsync(scope, Guid.NewGuid())).Outcome);
        var notPosted = await RequestAsync(scope, approvedOnly);
        Assert.Equal(RunActOutcome.Invalid, notPosted.Outcome);
        Assert.Empty(await scope.Db.PayrollReversalRequests.AsNoTracking().ToListAsync());

        Assert.True((await RequestAsync(scope, runId)).Success);
        var second = await RequestAsync(scope, runId, "طلب ثانٍ", Pay056RunScope.Poster);

        Assert.Equal(RunActOutcome.Conflict, second.Outcome);
        Assert.Equal(PayrollReversalMessages.PendingRequestExists, second.Error);
        Assert.Single(await scope.Db.PayrollReversalRequests.AsNoTracking().ToListAsync());
        Assert.Equal(RunActOutcome.NotFound, (await DecideAsync(scope, Guid.NewGuid(), approve: true)).Outcome);
    }

    [Fact]
    public async Task A_run_with_a_funding_decision_is_not_reversed_here()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync();
        scope.Db.FundingDecisions.Add(new FundingDecision
        {
            PayrollRunId = runId,
            RequestedAmount = 900m,
            ApprovedAmount = 450m,
            FundingRatio = 0.5m,
            ApprovedBy = Pay056RunScope.Approver,
        });
        await scope.Db.SaveChangesAsync();

        var requested = await RequestAsync(scope, runId);

        Assert.Equal(RunActOutcome.Invalid, requested.Outcome);
        Assert.Equal(RunLifecycleMessages.HasFundingOrPartialDisbursement, requested.Error);
        Assert.Empty(await scope.Db.PayrollReversalRequests.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_reversed_correction_frees_its_targeted_adjustments_and_deletes_its_unapplied_recovery()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync();
        var targeted = await scope.TargetedAdjustmentAsync(employee, -30_000m, source);
        var correction = (await scope.Mediator.Send(new CreateCorrectionRunCommand(source, new DateTime(2028, 3, 5, 0, 0, 0, DateTimeKind.Utc)))).CorrectionRunId!.Value;
        await scope.SubmitAsync(correction);
        await scope.ApproveAsync(correction);
        await scope.CloseAuditAsync(correction);
        Assert.True((await scope.PostAsync(correction)).Success);
        Assert.Single(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());
        var request = (await RequestAsync(scope, correction)).Id!.Value;

        var decided = await DecideAsync(scope, request, approve: true);

        Assert.True(decided.Success, decided.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PayrollRunStatus.Reversed, (await scope.RunAsync(correction)).Status);
        Assert.Empty(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());
        var freed = await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == targeted.Id);
        Assert.Equal((null, (Guid?)source), (freed.PayrollRunId, freed.TargetSourceRunId));
        // The source is untouched, and can be corrected again.
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(source)).Status);
    }
}
