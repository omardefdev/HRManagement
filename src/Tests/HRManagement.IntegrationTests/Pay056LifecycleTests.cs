using HRManagement.TestSupport.PayrollRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (G-PAY-04, G-PAY-07): a rejection reopens the run with its revision kept; any run not posted can be cancelled with a
/// reason, by whom D-PAY-18 says; what a calculation consumed is released; a calculation that failed on a fingerprint conflict is
/// restarted with a new snapshot. A payment hold is never touched by any of them.
/// </summary>
[Collection("IntegrationSerial")]
public class Pay056LifecycleTests
{
    private const string Note = "بدل النقل لم يُحتسب لموظفي الفرع؛ يُصحَّح ثم يُعاد الإرسال.";
    private const string Reason = "أُنشئ التشغيل قبل اعتماد تسويات الشهر.";

    private static readonly DateOnly January = new(2028, 1, 1);
    private static readonly DateOnly JanuaryEnd = new(2028, 1, 31);

    private sealed record Inputs(Guid Employee, DeductionRecord Installment, AbsenceDeduction Absence, PayrollAdjustment Adjustment);

    /// <summary>One employee with an advance instalment due in the month, an absence of the month and an adjustment.</summary>
    private static async Task<Inputs> EmployeeWithThreeInputsAsync(Pay056RunScope scope)
    {
        var employee = await scope.EmployeeAsync();
        var installment = await scope.DeductionAsync(employee, 25_000m, "AdvanceRecovery", new DateOnly(2028, 1, 15));
        var absence = await scope.AbsenceAsync(employee, January, JanuaryEnd, 12_000m);
        var adjustment = await scope.AdjustmentAsync(employee, 40_000m, "فرق ترفيع");
        return new Inputs(employee, installment, absence, adjustment);
    }

    /// <summary>The three inputs' applied markers when all three are attached to <paramref name="run"/> (or free, for null).</summary>
    private static (Guid? Deduction, Guid? Absence, Guid? Adjustment) All(Guid? run) => (run, run, run);

    private static async Task<(Guid? Deduction, Guid? Absence, Guid? Adjustment)> AppliedInAsync(Pay056RunScope scope, Inputs inputs)
    {
        scope.Db.ChangeTracker.Clear();
        return (
            (await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == inputs.Installment.Id)).AppliedInRunId,
            (await scope.Db.AbsenceDeductions.AsNoTracking().SingleAsync(a => a.Id == inputs.Absence.Id)).AppliedInRunId,
            (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == inputs.Adjustment.Id)).PayrollRunId);
    }

    private static Task<RunActResult> RejectAsync(Pay056RunScope scope, Guid runId, string? notes = Note, Guid? by = null) =>
        scope.Mediator.Send(new RejectPayrollRunCommand(runId, by ?? Pay056RunScope.Approver, notes));

    private static Task<RunActResult> CancelAsync(
        Pay056RunScope scope, Guid runId, string? reason = Reason, bool mayProcess = true, bool mayApprove = true, Guid? by = null) =>
        scope.Mediator.Send(new CancelPayrollRunCommand(runId, by ?? Pay056RunScope.Processor, reason, mayProcess, mayApprove));

    private static async Task<Guid> PlantAsync(Pay056RunScope scope, PayrollRunStatus status, int month = 6)
    {
        var start = new DateTime(2028, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = new PayrollRun
        {
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            RunDate = start.AddMonths(1),
            Status = status,
            TotalNetPay = 900m,
            ResultsFrozen = status is PayrollRunStatus.UnderReview or PayrollRunStatus.Approved or PayrollRunStatus.Posted or PayrollRunStatus.Reversed,
            ValidatedAtCalculate = status is not (PayrollRunStatus.Draft or PayrollRunStatus.Calculating or PayrollRunStatus.Cancelled),
        };
        var line = new SalaryLine { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), GrossPay = 1000m, TotalDeductions = 100m, NetPay = 900m };
        run.SalaryLines.Add(line);
        scope.Db.PayrollRuns.Add(run);
        scope.Db.SalaryLineComponents.AddRange(TestDataFactory.ComponentsOf(line));
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        return run.Id;
    }

    // --- a rejection reopens the run ---

    [Fact]
    public async Task A_rejection_with_a_note_reopens_the_run_as_a_draft_with_its_revision_kept_its_lines_deleted_and_its_inputs_free()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        var reviewed = await scope.RunAsync(runId);
        var linesBefore = await scope.LinesAsync(runId);
        Assert.Equal(All(runId), await AppliedInAsync(scope, inputs));
        var fingerprintBefore = (await PayrollRunLifecycle.SnapshotLinesAsync(
            scope.Services.GetRequiredService<IPayrollRepository>(), runId, CancellationToken.None)).Fingerprint;

        var result = await RejectAsync(scope, runId);

        Assert.True(result.Success, result.Error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Draft, run.Status);
        Assert.Equal(2, run.Revision);
        Assert.False(run.ResultsFrozen);
        Assert.False(run.ValidatedAtCalculate);
        Assert.Equal(0m, run.TotalNetPay);

        // What the rejected version was.
        var revision = await scope.Db.PayrollRunRevisions.AsNoTracking().SingleAsync(r => r.PayrollRunId == runId);
        Assert.Equal(1, revision.Revision);
        Assert.Equal(linesBefore.Count, revision.LineCount);
        Assert.Equal(reviewed.TotalNetPay, revision.TotalNetPay);
        Assert.Equal(fingerprintBefore, revision.LinesFingerprint);
        Assert.Equal(64, revision.LinesFingerprint.Length);
        Assert.Equal(Pay056RunScope.Approver, revision.RejectedBy);
        Assert.Equal(Note, revision.Note);

        // Its lines and their components are gone, and the deletion and the release are in the audit log.
        Assert.Empty(await scope.LinesAsync(runId));
        var lineIdsBefore = linesBefore.Select(l => l.Id).ToList();
        Assert.Equal(0, await scope.Db.SalaryLineComponents.CountAsync(c => lineIdsBefore.Contains(c.SalaryLineId)));
        var audit = await scope.Db.AuditLogs.AsNoTracking().Where(a => a.EntityId == runId).ToListAsync();
        var deleted = Assert.Single(audit, a => a.Action == PayrollRunLifecycle.LinesDeletedAuditAction);
        Assert.Contains(fingerprintBefore, deleted.ChangedFields);
        Assert.Equal(Pay056RunScope.Approver, deleted.UserId);
        Assert.Single(audit, a => a.Action == PayrollInputRelease.AuditAction);

        // No input stays attached to the rejected version.
        Assert.Equal(All(null), await AppliedInAsync(scope, inputs));

        // The decision row carries the note.
        var decision = await scope.Db.PayrollRunApprovals.AsNoTracking().SingleAsync(a => a.PayrollRunId == runId);
        Assert.Equal(ApprovalDecision.Rejected, decision.Decision);
        Assert.Equal(Note, decision.Notes);
        Assert.Equal(Pay056RunScope.Approver, decision.ApproverId);
    }

    [Fact]
    public async Task Review_reject_correct_resubmit_works_and_the_recalculation_takes_the_released_inputs_and_the_correction()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        var firstNet = (await scope.LinesAsync(runId)).Single().NetPay;
        Assert.True((await RejectAsync(scope, runId)).Success);

        // The correction: one more adjustment for the same employee.
        var correction = await scope.AdjustmentAsync(inputs.Employee, 7_500m, "بدل نقل");
        var (ok, lines, error) = await scope.CalculateAsync(runId);

        Assert.True(ok, error);
        Assert.Equal(1, lines);
        var recalculated = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Calculated, recalculated.Status);
        Assert.True(recalculated.ValidatedAtCalculate);
        Assert.Equal(2, recalculated.Revision);
        var line = (await scope.LinesAsync(runId)).Single();
        Assert.Equal(firstNet + 7_500m, line.NetPay);
        Assert.Equal(47_500m, line.TotalAdjustments);
        Assert.Equal(All(runId), await AppliedInAsync(scope, inputs));
        Assert.Equal(runId, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == correction.Id)).PayrollRunId);

        await scope.SubmitAsync(runId);
        await scope.ApproveAsync(runId, Pay056RunScope.OtherApprover);
        var approved = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Approved, approved.Status);
        Assert.Equal(2, approved.Revision);
        // The decisions of the run, in order: rejected with its note, then approved.
        var decisions = await scope.Db.PayrollRunApprovals.AsNoTracking().Where(a => a.PayrollRunId == runId).ToListAsync();
        Assert.Equal(2, decisions.Count);
        Assert.Contains(decisions, d => d.Decision == ApprovalDecision.Rejected && d.Notes == Note);
        Assert.Contains(decisions, d => d.Decision == ApprovalDecision.Approved && d.ApproverId == Pay056RunScope.OtherApprover);
    }

    [Fact]
    public async Task A_second_rejection_keeps_a_second_revision()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        Assert.True((await RejectAsync(scope, runId)).Success);
        Assert.True((await scope.CalculateAsync(runId)).Ok);
        await scope.SubmitAsync(runId);

        Assert.True((await RejectAsync(scope, runId, "ما زال الإجمالي لا يطابق كشف المالية.")).Success);

        Assert.Equal(3, (await scope.RunAsync(runId)).Revision);
        var revisions = await scope.Db.PayrollRunRevisions.AsNoTracking().Where(r => r.PayrollRunId == runId).OrderBy(r => r.Revision).ToListAsync();
        Assert.Equal([1, 2], revisions.Select(r => r.Revision));
        // The same lines were calculated twice: the two versions have the same fingerprint.
        Assert.Equal(revisions[0].LinesFingerprint, revisions[1].LinesFingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_rejection_without_a_note_is_refused_and_changes_nothing(string? notes)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);

        var result = await RejectAsync(scope, runId, notes);

        Assert.Equal(RunActOutcome.Invalid, result.Outcome);
        Assert.Equal(PayrollRun.RejectionNoteRequired, result.Error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.UnderReview, run.Status);
        Assert.Equal(1, run.Revision);
        Assert.Single(await scope.LinesAsync(runId));
        Assert.Equal(All(runId), await AppliedInAsync(scope, inputs));
        Assert.Empty(await scope.Db.PayrollRunRevisions.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Db.PayrollRunApprovals.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.Calculated)]
    [InlineData(PayrollRunStatus.Approved)]
    [InlineData(PayrollRunStatus.Posted)]
    [InlineData(PayrollRunStatus.Reversed)]
    [InlineData(PayrollRunStatus.Cancelled)]
    public async Task Only_a_run_under_review_is_rejected(PayrollRunStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status);

        var result = await RejectAsync(scope, runId);

        Assert.Equal(RunActOutcome.Invalid, result.Outcome);
        Assert.Equal(status, (await scope.RunAsync(runId)).Status);
        Assert.Single(await scope.LinesAsync(runId));
    }

    [Fact]
    public async Task A_rejection_of_a_run_that_does_not_exist_is_not_found()
    {
        await using var scope = await Pay056RunScope.CreateAsync();

        Assert.Equal(RunActOutcome.NotFound, (await RejectAsync(scope, Guid.NewGuid())).Outcome);
        Assert.Equal(RunActOutcome.NotFound, (await CancelAsync(scope, Guid.NewGuid())).Outcome);
    }

    [Fact]
    public async Task An_approved_run_is_not_recalculated_without_a_rejection()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.ApprovedRunAsync();

        var (ok, _, error) = await scope.CalculateAsync(runId);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
    }

    // --- a cancellation ---

    [Fact]
    public async Task A_calculated_run_is_cancelled_with_a_reason_and_its_three_inputs_appear_in_a_new_run_of_the_same_period()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var runId = await scope.CalculatedRunAsync();
        var cancelledNet = (await scope.LinesAsync(runId)).Single().NetPay;
        Assert.Equal(All(runId), await AppliedInAsync(scope, inputs));

        var result = await CancelAsync(scope, runId, mayApprove: false);

        Assert.True(result.Success, result.Error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Cancelled, run.Status);
        Assert.Equal(Pay056RunScope.Processor, run.CancelledBy);
        Assert.NotNull(run.CancelledAt);
        Assert.Equal(Reason, run.CancellationReason);
        Assert.Equal(All(null), await AppliedInAsync(scope, inputs));
        var decision = await scope.Db.PayrollRunApprovals.AsNoTracking().SingleAsync(a => a.PayrollRunId == runId);
        Assert.Equal(ApprovalDecision.Cancelled, decision.Decision);
        Assert.Equal(Reason, decision.Notes);
        Assert.Equal(Pay056RunScope.Processor, decision.ApproverId);
        Assert.Single(await scope.Db.AuditLogs.AsNoTracking().Where(a => a.EntityId == runId && a.Action == PayrollInputRelease.AuditAction).ToListAsync());
        Assert.Equal(PayrollRunJobStatus.Completed, (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == runId)).Status);

        // The period is free: a new regular run of the same month, and it takes the three inputs.
        var again = await scope.CalculatedRunAsync();
        Assert.NotEqual(runId, again);
        Assert.Equal(All(again), await AppliedInAsync(scope, inputs));
        Assert.Equal(cancelledNet, (await scope.LinesAsync(again)).Single().NetPay);
    }

    [Fact]
    public async Task While_a_run_is_live_its_period_takes_no_second_regular_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        await scope.CalculatedRunAsync();
        scope.Db.ChangeTracker.Clear();

        var start = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (created, _, error) = await scope.Mediator.Send(new CreatePayrollRunCommand(start, start.AddMonths(1).AddDays(-1), start.AddMonths(1)));

        Assert.False(created);
        Assert.Contains("تشغيل رواتب حي", error);
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft, true, false)]
    [InlineData(PayrollRunStatus.Calculating, true, false)]
    [InlineData(PayrollRunStatus.Validating, true, false)]
    [InlineData(PayrollRunStatus.Calculated, true, false)]
    [InlineData(PayrollRunStatus.UnderReview, false, true)]
    [InlineData(PayrollRunStatus.Approved, false, true)]
    public async Task A_run_that_is_not_posted_is_cancelled_by_whom_its_state_asks_for(PayrollRunStatus status, bool mayProcess, bool mayApprove)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status);

        var result = await CancelAsync(scope, runId, mayProcess: mayProcess, mayApprove: mayApprove);

        Assert.True(result.Success, result.Error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Cancelled, run.Status);
        Assert.Equal(Reason, run.CancellationReason);
    }

    [Theory]
    [InlineData(PayrollRunStatus.UnderReview)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task The_processor_does_not_cancel_a_run_that_was_sent_for_review(PayrollRunStatus status)
    {
        // D-PAY-18: after the run was sent for review, cancelling it is the approver's decision.
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status);

        var result = await CancelAsync(scope, runId, mayProcess: true, mayApprove: false);

        Assert.Equal(RunActOutcome.Forbidden, result.Outcome);
        Assert.Equal(RunLifecycleMessages.CancelAfterReviewNeedsApprover, result.Error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(status, run.Status);
        Assert.Null(run.CancelledBy);
        Assert.Empty(await scope.Db.PayrollRunApprovals.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.Calculated)]
    public async Task The_approver_alone_does_not_cancel_a_run_that_was_not_sent_for_review(PayrollRunStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status);

        var result = await CancelAsync(scope, runId, mayProcess: false, mayApprove: true);

        Assert.Equal(RunActOutcome.Forbidden, result.Outcome);
        Assert.Equal(status, (await scope.RunAsync(runId)).Status);
    }

    [Theory]
    [InlineData(PayrollRunStatus.Posted)]
    [InlineData(PayrollRunStatus.Reversed)]
    [InlineData(PayrollRunStatus.Cancelled)]
    public async Task A_posted_reversed_or_cancelled_run_is_not_cancelled(PayrollRunStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status);

        var result = await CancelAsync(scope, runId);

        Assert.Equal(RunActOutcome.Invalid, result.Outcome);
        Assert.Equal(status, (await scope.RunAsync(runId)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task A_cancellation_without_a_reason_is_refused(string? reason)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, PayrollRunStatus.Calculated);

        var result = await CancelAsync(scope, runId, reason);

        Assert.Equal(RunActOutcome.Invalid, result.Outcome);
        Assert.Equal(PayrollRun.CancellationReasonRequired, result.Error);
        Assert.Equal(PayrollRunStatus.Calculated, (await scope.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task A_run_with_a_funding_decision_or_a_partial_batch_is_not_cancelled_here()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var funded = await PlantAsync(scope, PayrollRunStatus.Approved, month: 7);
        scope.Db.FundingDecisions.Add(new FundingDecision
        {
            PayrollRunId = funded,
            RequestedAmount = 900m,
            ApprovedAmount = 450m,
            FundingRatio = 0.5m,
            ApprovedBy = Pay056RunScope.Approver,
        });
        var partial = await PlantAsync(scope, PayrollRunStatus.Approved, month: 8);
        scope.Db.PaymentBatches.Add(new PaymentBatch
        {
            PayrollRunId = partial,
            TotalAmount = 450m,
            IsPartial = true,
            Status = PaymentBatchStatus.Pending,
            BankTransferReference = "PARTIAL-56",
        });
        await scope.Db.SaveChangesAsync();

        foreach (var runId in new[] { funded, partial })
        {
            var result = await CancelAsync(scope, runId);

            Assert.Equal(RunActOutcome.Invalid, result.Outcome);
            Assert.Equal(RunLifecycleMessages.HasFundingOrPartialDisbursement, result.Error);
            Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
        }
    }

    [Fact]
    public async Task Cancelling_by_the_job_cancels_the_run_with_its_reason_even_when_the_job_is_complete()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var runId = await scope.CalculatedRunAsync();
        var jobId = (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == runId)).Id;

        // The older command refused a completed job and freed nothing. Without a reason it is refused; with one it cancels the run.
        var noReason = await scope.Mediator.Send(new CancelPayrollRunJobCommand(jobId, Pay056RunScope.Processor, null, true, false));
        Assert.Equal(RunActOutcome.Invalid, noReason.Outcome);
        Assert.Equal(PayrollRunStatus.Calculated, (await scope.RunAsync(runId)).Status);

        var result = await scope.Mediator.Send(new CancelPayrollRunJobCommand(jobId, Pay056RunScope.Processor, Reason, true, false));

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayrollRunStatus.Cancelled, (await scope.RunAsync(runId)).Status);
        Assert.Equal(All(null), await AppliedInAsync(scope, inputs));
        Assert.Equal(RunActOutcome.NotFound,
            (await scope.Mediator.Send(new CancelPayrollRunJobCommand(Guid.NewGuid(), Pay056RunScope.Processor, Reason, true, true))).Outcome);
    }

    [Fact]
    public async Task A_cancellation_stops_the_runs_unfinished_job_and_its_open_partitions()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, PayrollRunStatus.Calculating);
        var job = new PayrollRunJob { PayrollRunId = runId, Status = PayrollRunJobStatus.Running, Phase = PayrollRunJobPhase.Calculate, LeaseOwner = "w", LeaseUntil = DateTime.UtcNow.AddMinutes(30) };
        job.Partitions.Add(new PayrollRunPartition { PartitionIndex = 0, Status = PayrollRunPartitionStatus.Completed, EmployeeCount = 1 });
        job.Partitions.Add(new PayrollRunPartition { PartitionIndex = 1, Status = PayrollRunPartitionStatus.Running, EmployeeCount = 1, WorkerId = "w0" });
        job.Partitions.Add(new PayrollRunPartition { PartitionIndex = 2, Status = PayrollRunPartitionStatus.Pending, EmployeeCount = 1 });
        scope.Db.PayrollRunJobs.Add(job);
        await scope.Db.SaveChangesAsync();

        Assert.True((await CancelAsync(scope, runId)).Success);

        scope.Db.ChangeTracker.Clear();
        var stopped = await scope.Db.PayrollRunJobs.AsNoTracking().Include(j => j.Partitions).SingleAsync(j => j.Id == job.Id);
        Assert.Equal(PayrollRunJobStatus.Cancelled, stopped.Status);
        Assert.Null(stopped.LeaseOwner);
        Assert.Equal(
            [PayrollRunPartitionStatus.Completed, PayrollRunPartitionStatus.Cancelled, PayrollRunPartitionStatus.Cancelled],
            stopped.Partitions.OrderBy(p => p.PartitionIndex).Select(p => p.Status));
    }

    // --- the release ---

    [Fact]
    public async Task The_release_is_idempotent_and_touches_the_applied_markers_only()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var runId = await scope.CalculatedRunAsync();
        var repository = scope.Services.GetRequiredService<IPayrollRepository>();
        var run = await scope.RunAsync(runId);
        var before = await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == inputs.Installment.Id);
        var absenceBefore = await scope.Db.AbsenceDeductions.AsNoTracking().SingleAsync(a => a.Id == inputs.Absence.Id);

        var first = await PayrollInputRelease.ReleaseAsync(repository, run, "اختبار", Pay056RunScope.Processor, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        var second = await PayrollInputRelease.ReleaseAsync(repository, run, "اختبار", Pay056RunScope.Processor, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(new PayrollRunInputsReleased(1, 1, 1, 0), first);
        Assert.Equal(new PayrollRunInputsReleased(0, 0, 0, 0), second);
        Assert.Equal(All(null), await AppliedInAsync(scope, inputs));
        // One audit row for the release that freed something; none for the one that found nothing.
        Assert.Single(await scope.Db.AuditLogs.AsNoTracking().Where(a => a.EntityId == runId && a.Action == PayrollInputRelease.AuditAction).ToListAsync());

        // Nothing else of the rows changed: amount, due date, type, approval, period, days, reason, source.
        var after = await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == inputs.Installment.Id);
        Assert.Equal((before.Amount, before.DueDate, before.DeductionType, before.ApprovalStatus, before.SourceReference, before.UpdatedAt),
            (after.Amount, after.DueDate, after.DeductionType, after.ApprovalStatus, after.SourceReference, after.UpdatedAt));
        var absenceAfter = await scope.Db.AbsenceDeductions.AsNoTracking().SingleAsync(a => a.Id == inputs.Absence.Id);
        Assert.Equal((absenceBefore.Amount, absenceBefore.PeriodStart, absenceBefore.PeriodEnd, absenceBefore.AbsentDays, absenceBefore.Reason, absenceBefore.SourceReference),
            (absenceAfter.Amount, absenceAfter.PeriodStart, absenceAfter.PeriodEnd, absenceAfter.AbsentDays, absenceAfter.Reason, absenceAfter.SourceReference));
    }

    [Fact]
    public async Task The_release_frees_only_its_own_runs_inputs()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var january = await scope.CalculatedRunAsync(2028, 1);
        var later = await scope.DeductionAsync(inputs.Employee, 3_000m, "Manual", new DateOnly(2028, 2, 10));
        var february = await scope.CalculatedRunAsync(2028, 2);
        Assert.Equal(february, (await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == later.Id)).AppliedInRunId);

        Assert.True((await CancelAsync(scope, february)).Success);

        Assert.Null((await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == later.Id)).AppliedInRunId);
        Assert.Equal(All(january), await AppliedInAsync(scope, inputs));
    }

    [Fact]
    public async Task The_release_deletes_a_recovery_no_run_applied_and_keeps_one_a_run_applied()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, PayrollRunStatus.Calculated);
        var lineId = (await scope.LinesAsync(runId)).Single().Id;
        var otherRun = await PlantAsync(scope, PayrollRunStatus.Posted, month: 9);
        var otherLine = (await scope.LinesAsync(otherRun)).Single().Id;
        var takenBy = await PlantAsync(scope, PayrollRunStatus.Posted, month: 10);
        scope.Db.DeductionRecords.AddRange(
            new DeductionRecord { EmployeeId = Guid.NewGuid(), Amount = 30_000m, DeductionType = CorrectionRecovery.DeductionType, SourceReference = CorrectionRecovery.SourceReferenceOf(lineId) },
            // A recovery of another run's line that a later run already took back: not this release's, and never deleted.
            new DeductionRecord { EmployeeId = Guid.NewGuid(), Amount = 10_000m, DeductionType = CorrectionRecovery.DeductionType, SourceReference = CorrectionRecovery.SourceReferenceOf(otherLine), AppliedInRunId = takenBy });
        await scope.Db.SaveChangesAsync();
        var repository = scope.Services.GetRequiredService<IPayrollRepository>();

        var released = await repository.ReleaseRunInputsAsync(runId, CancellationToken.None);

        Assert.Equal(1, released.RecoveriesDeleted);
        scope.Db.ChangeTracker.Clear();
        var left = await scope.Db.DeductionRecords.AsNoTracking().SingleAsync();
        Assert.Equal(CorrectionRecovery.SourceReferenceOf(otherLine), left.SourceReference);
        Assert.Equal(takenBy, left.AppliedInRunId);
    }

    // --- the worker writes nothing after a stop ---

    [Fact]
    public async Task The_workers_save_writes_nothing_for_a_job_that_is_no_longer_running()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var inputs = await EmployeeWithThreeInputsAsync(scope);
        var runId = await scope.CreateRunAsync();
        var job = new PayrollRunJob { PayrollRunId = runId, Status = PayrollRunJobStatus.Running, Phase = PayrollRunJobPhase.Calculate };
        scope.Db.PayrollRunJobs.Add(job);
        await scope.Db.SaveChangesAsync();
        var repository = scope.Services.GetRequiredService<IPayrollRepository>();

        PayrollEmployeeCalculationPackage Package() => new()
        {
            Line = new SalaryLine { PayrollRunId = runId, EmployeeId = inputs.Employee, GrossPay = 1000m, NetPay = 1000m },
            Components = [new SalaryLineComponent { ComponentType = SalaryComponentType.BaseSalary, ComponentName = "الراتب الأساسي", Amount = 1000m }],
            DeductionsToApply = [inputs.Installment],
            AdjustmentsToApply = [inputs.Adjustment],
            AbsencesToApply = [inputs.Absence],
        };

        // The job is stopped (as a cancellation or a restart does) before the worker's save.
        Assert.Equal(1, await repository.StopRunJobsAsync(runId, CancellationToken.None));
        scope.Db.ChangeTracker.Clear();
        var refused = await repository.SaveEmployeePackagesForJobAsync(runId, job.Id, [Package()], CancellationToken.None);

        Assert.Null(refused);
        Assert.Empty(await scope.LinesAsync(runId));
        Assert.Equal(0, await scope.Db.SalaryLineComponents.CountAsync());
        Assert.Equal(All(null), await AppliedInAsync(scope, inputs));

        // A running job's save is written as before.
        var running = new PayrollRunJob { PayrollRunId = runId, Status = PayrollRunJobStatus.Running, Phase = PayrollRunJobPhase.Calculate };
        scope.Db.PayrollRunJobs.Add(running);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        var written = await repository.SaveEmployeePackagesForJobAsync(runId, running.Id, [Package()], CancellationToken.None);

        Assert.Equal((1, 0), written!.Value);
        Assert.Single(await scope.LinesAsync(runId));
        Assert.Equal(All(runId), await AppliedInAsync(scope, inputs));
    }

    // --- the restart ---

    /// <summary>
    /// A run whose calculation failed on a fingerprint conflict: its job is failed, its snapshot holds a fingerprint of inputs that
    /// have changed since, one employee's line was written before the failure and consumed an adjustment.
    /// </summary>
    private static async Task<(Guid RunId, Guid JobId, Guid Done, Guid Conflicted, PayrollAdjustment Consumed)> FailedOnAFingerprintConflictAsync(Pay056RunScope scope)
    {
        var done = await scope.EmployeeAsync();
        var conflicted = await scope.EmployeeAsync();
        var consumed = await scope.AdjustmentAsync(done, 5_000m);
        var runId = await scope.CreateRunAsync();
        var framework = await scope.Db.PayrollConfigurations.AsNoTracking().SingleAsync();
        var run = await scope.Db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.PayrollConfigurationId = framework.Id;
        run.ConfigContentHash = framework.ContentHash;
        ForTesting.SetStatus(run, PayrollRunStatus.Calculating);

        var profiles = await scope.Db.EmployeePayProfiles.AsNoTracking().OrderBy(p => p.EmployeeId).ToListAsync();
        var job = new PayrollRunJob
        {
            PayrollRunId = runId,
            Status = PayrollRunJobStatus.Failed,
            Phase = PayrollRunJobPhase.Calculate,
            TotalEmployeeCount = 2,
            Error = "تعارض بصمة مدخلات الراتب للموظف.",
        };
        var order = 0;
        foreach (var profile in profiles)
        {
            job.InputSnapshots.Add(new PayrollRunInputSnapshot
            {
                EmployeeId = profile.EmployeeId,
                EmployeePayProfileId = profile.Id,
                ProfileVersion = PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile),
                // The fingerprint of inputs as they were at the snapshot; they have changed since, for one employee.
                InputFingerprint = profile.EmployeeId == conflicted ? "inputs-as-they-were-at-the-snapshot" : null,
                PayrollConfigurationId = framework.Id,
                ConfigContentHash = framework.ContentHash,
                SortOrder = order++,
            });
        }

        job.Partitions.Add(new PayrollRunPartition { PartitionIndex = 0, Status = PayrollRunPartitionStatus.Failed, EmployeeCount = 2, Error = job.Error });
        scope.Db.PayrollRunJobs.Add(job);

        // What the worker wrote before it failed: the first employee's line, with the adjustment it consumed.
        var line = new SalaryLine { PayrollRunId = runId, EmployeeId = done, GrossPay = 900_000m, TotalAdjustments = 5_000m, NetPay = 905_000m };
        scope.Db.SalaryLines.Add(line);
        scope.Db.SalaryLineComponents.Add(new SalaryLineComponent { SalaryLineId = line.Id, ComponentType = SalaryComponentType.BaseSalary, ComponentName = "الراتب الأساسي", Amount = 900_000m });
        (await scope.Db.PayrollAdjustments.SingleAsync(a => a.Id == consumed.Id)).PayrollRunId = runId;
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        return (runId, job.Id, done, conflicted, consumed);
    }

    /// <summary>A national scope guard that can be told to refuse the next calculation's start.</summary>
    private sealed class SwitchableScopeGuard : Payroll.Application.Abstractions.IPayrollNationalScopeGuard
    {
        public const string Refusal = "planted: the scope guard refuses this calculation";

        public bool Refuse { get; set; }

        public string DefaultCurrencyCode => "SYP";

        public string? ValidatePayrollPersonCount(int personCount) => Refuse ? Refusal : null;
    }

    [Fact]
    public async Task A_restart_whose_new_calculation_is_refused_says_that_the_reset_is_done_and_why_the_calculation_did_not_start()
    {
        var guard = new SwitchableScopeGuard();
        await using var scope = await Pay056RunScope.CreateAsync(
            s => s.AddSingleton<Payroll.Application.Abstractions.IPayrollNationalScopeGuard>(guard));
        var (runId, jobId, _, _, consumed) = await FailedOnAFingerprintConflictAsync(scope);
        guard.Refuse = true;

        scope.Db.ChangeTracker.Clear();
        var (result, lines, _) = await scope.Mediator.Send(new RestartCalculationCommand(jobId, Pay056RunScope.Processor));

        // The reset is committed (the old job stopped, the run a draft, its lines gone, its inputs free); the answer says so, and
        // carries the reason the new calculation did not start. It is not a refusal that changed nothing.
        Assert.False(result.Success);
        Assert.Equal(RunActOutcome.Invalid, result.Outcome);
        Assert.StartsWith(RestartCalculationHandler.ResetDonePrefix, result.Error);
        Assert.EndsWith(SwitchableScopeGuard.Refusal, result.Error);
        Assert.Equal(0, lines);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PayrollRunStatus.Draft, (await scope.RunAsync(runId)).Status);
        Assert.Equal(PayrollRunJobStatus.Cancelled, (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status);
        Assert.Empty(await scope.LinesAsync(runId));
        Assert.Null((await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == consumed.Id)).PayrollRunId);
    }

    [Fact]
    public async Task A_calculation_stuck_on_a_fingerprint_conflict_fails_again_when_resumed_and_completes_when_restarted()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var (runId, jobId, done, conflicted, consumed) = await FailedOnAFingerprintConflictAsync(scope);

        // Resuming reruns the same snapshot: the same conflict, again.
        var resumed = await scope.Mediator.Send(new ResumePayrollRunJobCommand(jobId));
        Assert.False(resumed.Success);
        Assert.Contains("تعارض بصمة", resumed.Error);
        Assert.Equal(PayrollRunJobStatus.Failed, (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status);
        Assert.Equal(PayrollRunStatus.Calculating, (await scope.RunAsync(runId)).Status);

        scope.Db.ChangeTracker.Clear();
        var (result, lines, newJobId) = await scope.Mediator.Send(new RestartCalculationCommand(jobId, Pay056RunScope.Processor));

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, lines);
        Assert.NotNull(newJobId);
        Assert.NotEqual(jobId, newJobId);
        // The old job is cancelled; the new one took a new snapshot and completed; the run is calculated and validated.
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PayrollRunJobStatus.Cancelled, (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status);
        var fresh = await scope.Db.PayrollRunJobs.AsNoTracking().Include(j => j.InputSnapshots).SingleAsync(j => j.Id == newJobId);
        Assert.Equal(PayrollRunJobStatus.Completed, fresh.Status);
        Assert.Equal(2, fresh.InputSnapshots.Count);
        Assert.DoesNotContain(fresh.InputSnapshots, s => s.InputFingerprint == "inputs-as-they-were-at-the-snapshot");
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Calculated, run.Status);
        Assert.True(run.ValidatedAtCalculate);
        Assert.Equal(1, run.Revision);

        // The line the failed job left was deleted and calculated again; the adjustment it had consumed is consumed by the new line.
        var newLines = await scope.LinesAsync(runId);
        Assert.Equal(2, newLines.Count);
        Assert.Contains(newLines, l => l.EmployeeId == conflicted);
        Assert.Equal(5_000m, newLines.Single(l => l.EmployeeId == done).TotalAdjustments);
        Assert.Equal(runId, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == consumed.Id)).PayrollRunId);
        Assert.Single(await scope.Db.AuditLogs.AsNoTracking().Where(a => a.EntityId == runId && a.Action == PayrollRunLifecycle.LinesDeletedAuditAction).ToListAsync());
    }

    [Fact]
    public async Task A_restart_is_refused_for_a_job_that_is_neither_failed_nor_paused_and_for_a_frozen_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        var completed = (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == runId)).Id;

        var ofCompleted = await scope.Mediator.Send(new RestartCalculationCommand(completed, Pay056RunScope.Processor));
        Assert.Equal(RunActOutcome.Invalid, ofCompleted.Result.Outcome);
        Assert.Equal(RestartCalculationHandler.OnlyFailedOrPaused, ofCompleted.Result.Error);

        // A failed job on a run whose results are frozen (it was validated before): the restart does not unfreeze it.
        var failed = new PayrollRunJob { PayrollRunId = runId, Status = PayrollRunJobStatus.Failed, Phase = PayrollRunJobPhase.Calculate };
        scope.Db.PayrollRunJobs.Add(failed);
        await scope.Db.SaveChangesAsync();
        var ofFrozen = await scope.Mediator.Send(new RestartCalculationCommand(failed.Id, Pay056RunScope.Processor));
        Assert.Equal(RunActOutcome.Invalid, ofFrozen.Result.Outcome);
        Assert.Equal(PayrollRun.RestartNeedsUnfrozenResults, ofFrozen.Result.Error);

        Assert.Equal(RunActOutcome.NotFound,
            (await scope.Mediator.Send(new RestartCalculationCommand(Guid.NewGuid(), Pay056RunScope.Processor))).Result.Outcome);
        Assert.Equal(PayrollRunStatus.Calculated, (await scope.RunAsync(runId)).Status);
        Assert.Single(await scope.LinesAsync(runId));
    }

    // --- the pinned account of a version that is gone ---

    [Fact]
    public async Task A_retirement_holds_a_run_for_its_line_but_not_for_the_snapshot_of_a_version_that_was_rejected()
    {
        // P-10 (D-PAY-15): a reopened run pins accounts again at its next snapshot. The snapshot of the rejected version still names
        // the old account, and the run has no line any more: that must not put a hold on the run.
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var account = new EmployeeBankAccount
        {
            EmployeeId = employee,
            AccountKind = BankAccountKind.Iban,
            AccountNumber = "SY56PLAN0000000000000000A1",
            BankNameAr = "مصرف الاختبار",
            Status = BankAccountStatus.Active,
        };
        scope.Db.EmployeeBankAccounts.Add(account);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        var runId = await scope.CalculatedRunAsync();
        Assert.Equal(account.Id, (await scope.LinesAsync(runId)).Single().BankAccountId);
        var rules = new Payroll.Application.Features.BankAccounts.BankAccountRules(scope.Services.GetRequiredService<IPayrollRepository>());
        var active = await scope.Db.EmployeeBankAccounts.AsNoTracking().SingleAsync();

        // The control: while the line exists, a retirement holds the run.
        Assert.Equal(1, await rules.StageRetirementHoldsAsync(active, Pay056RunScope.Approver, "حساب مغلق", DateTime.UtcNow, CancellationToken.None));
        scope.Db.ChangeTracker.Clear();

        await scope.SubmitAsync(runId);
        Assert.True((await RejectAsync(scope, runId)).Success);
        scope.Db.ChangeTracker.Clear();

        Assert.Equal(0, await rules.StageRetirementHoldsAsync(active, Pay056RunScope.Approver, "حساب مغلق", DateTime.UtcNow, CancellationToken.None));
    }

    // --- holds are controls ---

    [Fact]
    public async Task A_hold_bound_to_a_run_survives_a_rejection_a_restart_and_a_cancellation_untouched()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        var hold = new PaymentHold
        {
            EmployeeId = employee,
            PayrollRunId = runId,
            HoldType = PaymentHoldType.Manual,
            Status = PaymentHoldStatus.Active,
            Reason = "ACCOUNT-RETIRED: أُحيل الحساب المثبَّت بعد الحساب",
        };
        scope.Db.PaymentHolds.Add(hold);
        await scope.Db.SaveChangesAsync();

        async Task StillActiveAsync()
        {
            scope.Db.ChangeTracker.Clear();
            var kept = await scope.Db.PaymentHolds.AsNoTracking().SingleAsync();
            Assert.Equal((hold.Id, PaymentHoldStatus.Active, runId, hold.Reason), (kept.Id, kept.Status, kept.PayrollRunId, kept.Reason));
            Assert.Null(kept.ReleasedBy);
        }

        await scope.SubmitAsync(runId);
        Assert.True((await RejectAsync(scope, runId)).Success);
        await StillActiveAsync();

        Assert.True((await scope.CalculateAsync(runId)).Ok);
        await StillActiveAsync();

        Assert.True((await CancelAsync(scope, runId)).Success);
        await StillActiveAsync();
    }
}
