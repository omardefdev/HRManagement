using HRManagement.TestSupport.PayrollRuns;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56, what the independent review found between a correction, its recovery and a reversal, each with the scenario it named:
/// a recovery is taken back only once its correction is posted; a posted correction whose recovery a later run took is not
/// reversed; a run with a standing correction is not reversed, and a correction is not posted beside a source that is no longer
/// posted; an approved adjustment of an employee with no pay profile in force is not dropped; the general creation of a run makes no
/// correction; and a run posted before this release still has a poster the decider of its reversal must not be.
/// </summary>
[Collection("IntegrationSerial")]
public class Pay056ReviewFixTests
{
    private static readonly DateTime RunDate = new(2028, 3, 5, 0, 0, 0, DateTimeKind.Utc);
    private const string Why = "رُحّل التشغيل قبل اعتماد تسوية";

    private static async Task<Guid> CorrectAsync(Pay056RunScope scope, Guid sourceRunId)
    {
        scope.Db.ChangeTracker.Clear();
        var result = await scope.Mediator.Send(new CreateCorrectionRunCommand(sourceRunId, RunDate));
        Assert.True(result.Success, result.Error);
        return result.CorrectionRunId!.Value;
    }

    private static async Task PostAsync(Pay056RunScope scope, Guid runId)
    {
        await scope.SubmitAsync(runId);
        await scope.ApproveAsync(runId);
        await scope.CloseAuditAsync(runId);
        var (posted, error) = await scope.PostAsync(runId);
        Assert.True(posted, error);
    }

    private static Task<RunActResult> RequestAsync(Pay056RunScope scope, Guid runId)
    {
        scope.Db.ChangeTracker.Clear();
        return scope.Mediator.Send(new RequestPayrollReversalCommand(runId, Pay056RunScope.Reverser, Why));
    }

    private static Task<RunActResult> DecideAsync(Pay056RunScope scope, Guid requestId, bool approve, string? notes = null, Guid? by = null)
    {
        scope.Db.ChangeTracker.Clear();
        return scope.Mediator.Send(new DecidePayrollReversalCommand(requestId, by ?? Pay056RunScope.Approver, approve, notes));
    }

    private static async Task<DeductionRecord> RecoveryAsync(Pay056RunScope scope)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.DeductionRecords.AsNoTracking().SingleAsync(d => d.DeductionType == CorrectionRecovery.DeductionType);
    }

    // --- the recovery waits for its correction's posting ---

    [Fact]
    public async Task A_recovery_is_taken_back_only_once_its_correction_is_posted_and_then_exactly_once()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var plainNet = (await scope.LinesAsync(source)).Single().NetPay;
        await scope.TargetedAdjustmentAsync(employee, -30_000m, source, "صُرف بدل لا يستحقه");
        var correction = await CorrectAsync(scope, source);
        var recovery = await RecoveryAsync(scope);
        Assert.Equal((30_000m, new DateOnly(2028, 2, 1), (Guid?)null), (recovery.Amount, recovery.DueDate!.Value, recovery.AppliedInRunId));

        // The correction is only calculated: it can still be rejected, cancelled or calculated again. February's run does not take
        // its recovery, though the recovery is due.
        var february = await scope.CalculatedRunAsync(2028, 2);
        Assert.Equal(plainNet, (await scope.LinesAsync(february)).Single().NetPay);
        Assert.Null((await RecoveryAsync(scope)).AppliedInRunId);
        Assert.True((await scope.Mediator.Send(new CancelPayrollRunCommand(february, Pay056RunScope.Processor, "يُعاد بعد ترحيل التصحيح", true, false))).Success);

        // Posted: the next run takes it, once.
        await PostAsync(scope, correction);
        var again = await scope.CalculatedRunAsync(2028, 2);
        var line = (await scope.LinesAsync(again)).Single();
        Assert.Equal(plainNet - 30_000m, line.NetPay);
        Assert.Equal(again, (await RecoveryAsync(scope)).AppliedInRunId);
        Assert.Contains(
            await scope.Db.SalaryLineComponents.AsNoTracking().Where(c => c.SalaryLineId == line.Id).ToListAsync(),
            c => c.ComponentType == SalaryComponentType.Deduction && c.Amount == -30_000m && c.ReferenceId == recovery.Id);

        // And no later run takes it again.
        var march = await scope.CalculatedRunAsync(2028, 3);
        Assert.Equal(plainNet, (await scope.LinesAsync(march)).Single().NetPay);
        Assert.Equal(1, await scope.Db.DeductionRecords.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task A_correction_rejected_after_a_later_run_was_calculated_leaves_that_run_without_its_recovery_and_makes_no_second_one()
    {
        // The review's scenario: the correction is calculated, the next month's run is calculated, the correction is rejected and
        // calculated again. Before the fix the first recovery stayed applied in the month's run and a second one was made.
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var plainNet = (await scope.LinesAsync(source)).Single().NetPay;
        await scope.TargetedAdjustmentAsync(employee, -30_000m, source);
        var correction = await CorrectAsync(scope, source);
        var february = await scope.CalculatedRunAsync(2028, 2);

        await scope.SubmitAsync(correction);
        Assert.True((await scope.Mediator.Send(new RejectPayrollRunCommand(correction, Pay056RunScope.Approver, "المبلغ 20,000 لا 30,000"))).Success);
        scope.Db.ChangeTracker.Clear();

        // The rejected version's recovery was never applied, so it went with the version.
        Assert.Empty(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());
        Assert.Equal(plainNet, (await scope.LinesAsync(february)).Single().NetPay);

        var (ok, _, error) = await scope.CalculateAsync(correction);
        Assert.True(ok, error);
        var recovery = await RecoveryAsync(scope);
        Assert.Equal((30_000m, (Guid?)null), (recovery.Amount, recovery.AppliedInRunId));
    }

    // --- the reversal, a correction and its recovery ---

    [Fact]
    public async Task A_posted_correction_whose_recovery_a_later_run_took_is_not_reversed_and_nothing_changes()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var targeted = await scope.TargetedAdjustmentAsync(employee, -30_000m, source);
        var correction = await CorrectAsync(scope, source);
        await PostAsync(scope, correction);
        var february = await scope.CalculatedRunAsync(2028, 2);
        Assert.Equal(february, (await RecoveryAsync(scope)).AppliedInRunId);
        var request = (await RequestAsync(scope, correction)).Id!.Value;

        var decided = await DecideAsync(scope, request, approve: true);

        Assert.Equal((RunActOutcome.Invalid, PayrollReversalMessages.RecoveryApplied), (decided.Outcome, decided.Error));
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(correction)).Status);
        Assert.Equal(PaymentBatchStatus.Pending, (await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == correction)).Status);
        Assert.Equal(correction, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == targeted.Id)).PayrollRunId);
        Assert.Equal(february, (await RecoveryAsync(scope)).AppliedInRunId);
        Assert.Equal(PayrollReversalRequestStatus.Pending, (await scope.Db.PayrollReversalRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(scope.Events.Reversed);
        // The request is closed by a rejection with its note.
        Assert.True((await DecideAsync(scope, request, approve: false, notes: "استُرد الفرق في شباط؛ يُصحَّح بتسوية موجّهة جديدة")).Success);
    }

    [Fact]
    public async Task A_run_with_a_standing_correction_is_not_reversed_until_the_correction_is_cancelled()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(employee, 50_000m, source);
        var correction = await CorrectAsync(scope, source);
        var request = (await RequestAsync(scope, source)).Id!.Value;

        var refused = await DecideAsync(scope, request, approve: true);

        Assert.Equal((RunActOutcome.Invalid, PayrollReversalMessages.HasCorrection), (refused.Outcome, refused.Error));
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(source)).Status);
        Assert.Equal(PaymentBatchStatus.Pending, (await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == source)).Status);
        Assert.Empty(scope.Events.Reversed);

        // The correction is cancelled: nothing stands on the run any more, and the same request is approved.
        Assert.True((await scope.Mediator.Send(new CancelPayrollRunCommand(correction, Pay056RunScope.Processor, "يُعكس التشغيل المرجعي", true, false))).Success);
        var approved = await DecideAsync(scope, request, approve: true);

        Assert.True(approved.Success, approved.Error);
        Assert.Equal(PayrollRunStatus.Reversed, (await scope.RunAsync(source)).Status);
    }

    [Fact]
    public async Task A_correction_is_not_posted_beside_a_source_that_is_no_longer_posted()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(employee, 50_000m, source);
        var correction = await CorrectAsync(scope, source);
        await scope.SubmitAsync(correction);
        await scope.ApproveAsync(correction);
        await scope.CloseAuditAsync(correction);
        // A source that is reversed under an approved correction (a state the reversal now refuses to make; planted here).
        scope.Db.ChangeTracker.Clear();
        ForTesting.SetStatus(await scope.Db.PayrollRuns.SingleAsync(r => r.Id == source), PayrollRunStatus.Reversed);
        await scope.Db.SaveChangesAsync();

        var (posted, error) = await scope.PostAsync(correction);

        Assert.False(posted);
        Assert.Equal(CorrectionRunMessages.SourceNoLongerPosted, error);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(correction)).Status);
        Assert.Empty(await scope.Db.PaymentBatches.AsNoTracking().Where(b => b.PayrollRunId == correction).ToListAsync());
    }

    // --- the correction leaves no approved adjustment out ---

    [Fact]
    public async Task A_correction_keeps_the_adjustment_of_an_employee_who_has_no_pay_profile_in_force_any_more()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var stays = await scope.EmployeeAsync();
        var left = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(stays, 12_000m, source);
        var ofTheOneWhoLeft = await scope.TargetedAdjustmentAsync(left, -30_000m, source);
        // He left after January: his pay profile is no longer in force.
        (await scope.Db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == left)).IsActive = false;
        await scope.Db.SaveChangesAsync();

        var correction = await CorrectAsync(scope, source);

        var lines = await scope.LinesAsync(correction);
        Assert.Equal(2, lines.Count);
        var line = lines.Single(l => l.EmployeeId == left);
        Assert.Equal((0m, 0m, -30_000m, 0m), (line.GrossPay, line.TotalDeductions, line.TotalAdjustments, line.NetPay));
        Assert.Equal(correction, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == ofTheOneWhoLeft.Id)).PayrollRunId);
        Assert.Equal(CorrectionRecovery.SourceReferenceOf(line.Id), (await RecoveryAsync(scope)).SourceReference);
        Assert.True((await scope.RunAsync(correction)).ValidatedAtCalculate);
    }

    // --- one door to a correction ---

    [Fact]
    public async Task The_general_creation_of_a_run_makes_no_correction()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var start = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var (created, id, error) = await scope.Mediator.Send(new CreatePayrollRunCommand(
            start, start.AddMonths(1).AddDays(-1), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), PayrollRunType.Correction, source));

        Assert.False(created);
        Assert.Null(id);
        Assert.Equal(CorrectionRunMessages.CreatedFromItsSource, error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(0, await scope.Db.PayrollRuns.AsNoTracking().CountAsync(r => r.RunType == PayrollRunType.Correction));
    }

    // --- the poster of a run posted before PLAN-56 ---

    [Fact]
    public async Task The_last_writer_of_a_run_posted_before_this_release_stands_for_its_poster()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync(2028, 1);
        // As the release found it: posted, with no PostedBy; its last write was its posting.
        await scope.Db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "PayrollRuns" SET "PostedBy" = NULL, "PostedAt" = NULL, "UpdatedBy" = {Pay056RunScope.Poster} WHERE "Id" = {runId}""");
        var run = await scope.RunAsync(runId);
        Assert.Null(run.PostedBy);
        Assert.Equal(Pay056RunScope.Poster, run.KnownPoster);
        var request = (await RequestAsync(scope, runId)).Id!.Value;

        var byThePoster = await DecideAsync(scope, request, approve: true, by: Pay056RunScope.Poster);

        Assert.Equal((RunActOutcome.Invalid, PayrollReversalMessages.DeciderIsPoster), (byThePoster.Outcome, byThePoster.Error));
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        var byAnother = await DecideAsync(scope, request, approve: true);
        Assert.True(byAnother.Success, byAnother.Error);
    }

    [Fact]
    public async Task A_run_that_is_not_posted_names_no_poster_from_its_last_writer()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync(2028, 1);
        await scope.Db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "PayrollRuns" SET "UpdatedBy" = {Pay056RunScope.Processor} WHERE "Id" = {runId}""");

        Assert.Null((await scope.RunAsync(runId)).KnownPoster);
    }
}
