using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.ManualInputs;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (G-PAY-03, D-PAY-17): the correction run of a posted run takes only the approved adjustments aimed at it, has no base and
/// no tax on the difference, passes the validation a regular run passes, and turns a negative difference into a recovery deduction
/// of the next cycle. One correction not yet posted per source run.
/// </summary>
[Collection("IntegrationSerial")]
public class Pay056CorrectionTests
{
    private static readonly DateTime RunDate = new(2028, 3, 5, 0, 0, 0, DateTimeKind.Utc);

    private static Task<CorrectionRunResult> CorrectAsync(Pay056RunScope scope, Guid sourceRunId)
    {
        scope.Db.ChangeTracker.Clear();
        return scope.Mediator.Send(new CreateCorrectionRunCommand(sourceRunId, RunDate));
    }

    private static async Task<List<SalaryLineComponent>> ComponentsAsync(Pay056RunScope scope, Guid lineId) =>
        await scope.Db.SalaryLineComponents.AsNoTracking().Where(c => c.SalaryLineId == lineId).ToListAsync();

    [Fact]
    public async Task A_correction_takes_only_the_adjustments_aimed_at_its_source_and_passes_the_validation()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var other = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var targeted = await scope.TargetedAdjustmentAsync(employee, 18_000m, source, "فرق ترفيع عن كانون الثاني");
        var alsoTargeted = await scope.TargetedAdjustmentAsync(employee, -3_000m, source, "تصحيح بدل");
        var untargeted = await scope.AdjustmentAsync(other, 9_000m, "تسوية اعتيادية");
        var untargetedSameEmployee = await scope.AdjustmentAsync(employee, 1_111m, "تسوية اعتيادية");

        var result = await CorrectAsync(scope, source);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.LinesGenerated);
        var correction = await scope.RunAsync(result.CorrectionRunId!.Value);
        // Validated like a regular run: calculated, validated, frozen, with the total of its lines; nothing froze it by hand.
        Assert.Equal((PayrollRunType.Correction, PayrollRunStatus.Calculated, true, true), (correction.RunType, correction.Status, correction.ValidatedAtCalculate, correction.ResultsFrozen));
        Assert.Equal(source, correction.SourceRunId);
        Assert.Equal(15_000m, correction.TotalNetPay);

        // One line, for the employee with targeted adjustments: no base, no deduction, no tax; the difference once.
        var line = Assert.Single(await scope.LinesAsync(correction.Id));
        Assert.Equal(employee, line.EmployeeId);
        Assert.Equal((0m, 0m, 15_000m, 15_000m), (line.GrossPay, line.TotalDeductions, line.TotalAdjustments, line.NetPay));
        var components = await ComponentsAsync(scope, line.Id);
        Assert.Equal(2, components.Count);
        Assert.All(components, c => Assert.Equal(SalaryComponentType.Adjustment, c.ComponentType));
        Assert.Equal(new[] { alsoTargeted.Id, targeted.Id }.Order(), components.Select(c => c.ReferenceId!.Value).Order());
        Assert.Equal(PayrollNet.Unclipped(line), components.Sum(c => c.Amount));

        // The targeted adjustments are applied in the correction; the untargeted ones are untouched and free.
        var adjustments = await scope.Db.PayrollAdjustments.AsNoTracking().ToDictionaryAsync(a => a.Id);
        Assert.Equal(correction.Id, adjustments[targeted.Id].PayrollRunId);
        Assert.Equal(correction.Id, adjustments[alsoTargeted.Id].PayrollRunId);
        Assert.Null(adjustments[untargeted.Id].PayrollRunId);
        Assert.Null(adjustments[untargetedSameEmployee.Id].PayrollRunId);
        Assert.Empty(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task An_untargeted_adjustment_enters_the_next_regular_run_and_a_targeted_one_enters_no_regular_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var plainNet = (await scope.LinesAsync(source)).Single().NetPay;
        var targeted = await scope.TargetedAdjustmentAsync(employee, 18_000m, source);
        var untargeted = await scope.AdjustmentAsync(employee, 9_000m);

        var february = await scope.CalculatedRunAsync(2028, 2);

        var line = (await scope.LinesAsync(february)).Single();
        Assert.Equal(9_000m, line.TotalAdjustments);
        Assert.Equal(plainNet + 9_000m, line.NetPay);
        Assert.Equal(february, (await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == untargeted.Id)).PayrollRunId);
        Assert.Null((await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == targeted.Id)).PayrollRunId);

        // And a later month does not take the targeted one either; it waits for its source's correction.
        var march = await scope.CalculatedRunAsync(2028, 3);
        Assert.Equal(0m, (await scope.LinesAsync(march)).Single().TotalAdjustments);
        Assert.Null((await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == targeted.Id)).PayrollRunId);
    }

    [Fact]
    public async Task A_negative_difference_pays_nothing_and_becomes_a_recovery_the_next_cycle_applies()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var plainNet = (await scope.LinesAsync(source)).Single().NetPay;
        await scope.TargetedAdjustmentAsync(employee, -30_000m, source, "صُرف بدل لا يستحقه");

        var result = await CorrectAsync(scope, source);

        Assert.True(result.Success, result.Error);
        var correction = await scope.RunAsync(result.CorrectionRunId!.Value);
        Assert.True(correction.ValidatedAtCalculate);
        Assert.Equal(0m, correction.TotalNetPay);
        var line = Assert.Single(await scope.LinesAsync(correction.Id));
        Assert.Equal((0m, 0m, -30_000m, 0m), (line.GrossPay, line.TotalDeductions, line.TotalAdjustments, line.NetPay));

        // The difference is not lost: a recovery deduction the system made, approved, due on the first day after the corrected period.
        var recovery = await scope.Db.DeductionRecords.AsNoTracking().SingleAsync();
        Assert.Equal(CorrectionRecovery.DeductionType, recovery.DeductionType);
        Assert.Equal(30_000m, recovery.Amount);
        Assert.Equal(employee, recovery.EmployeeId);
        Assert.Equal(new DateOnly(2028, 2, 1), recovery.DueDate);
        Assert.Equal(CorrectionRecovery.SourceReferenceOf(line.Id), recovery.SourceReference);
        Assert.Equal((ManualInputOrigin.System, ManualInputApprovalStatus.Approved), (recovery.Origin, recovery.ApprovalStatus));
        Assert.Null(recovery.AppliedInRunId);

        // The next cycle's regular run takes it back.
        var february = await scope.CalculatedRunAsync(2028, 2);
        var next = (await scope.LinesAsync(february)).Single();
        Assert.Equal(plainNet - 30_000m, next.NetPay);
        Assert.Equal(february, (await scope.Db.DeductionRecords.AsNoTracking().SingleAsync()).AppliedInRunId);
        var components = await ComponentsAsync(scope, next.Id);
        Assert.Contains(components, c => c.ComponentType == SalaryComponentType.Deduction && c.Amount == -30_000m && c.ReferenceId == recovery.Id);
    }

    [Fact]
    public async Task A_recovery_is_not_due_in_the_corrected_period_itself()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(employee, -30_000m, source);
        Assert.True((await CorrectAsync(scope, source)).Success);

        // A regular run of an earlier month than the recovery's due date does not take it.
        var december = await scope.CalculatedRunAsync(2027, 12);

        Assert.Equal(0m, (await scope.LinesAsync(december)).Single().TotalAdjustments);
        Assert.Null((await scope.Db.DeductionRecords.AsNoTracking().SingleAsync()).AppliedInRunId);
    }

    [Fact]
    public async Task A_correction_with_a_positive_and_a_negative_line_posts_what_was_validated()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var raised = await scope.EmployeeAsync();
        var lowered = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(raised, 12_000m, source);
        await scope.TargetedAdjustmentAsync(lowered, -5_000m, source);

        var result = await CorrectAsync(scope, source);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.LinesGenerated);
        var correctionId = result.CorrectionRunId!.Value;
        Assert.Equal(12_000m, (await scope.RunAsync(correctionId)).TotalNetPay);

        await scope.SubmitAsync(correctionId);
        await scope.ApproveAsync(correctionId);
        await scope.CloseAuditAsync(correctionId);
        var (posted, error) = await scope.PostAsync(correctionId);

        Assert.True(posted, error);
        var after = await scope.RunAsync(correctionId);
        Assert.Equal((PayrollRunStatus.Posted, 12_000m), (after.Status, after.TotalNetPay));
        Assert.Equal(12_000m, (await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == correctionId)).TotalAmount);
        Assert.Equal(5_000m, (await scope.Db.DeductionRecords.AsNoTracking().SingleAsync()).Amount);
    }

    [Fact]
    public async Task A_correction_with_no_approved_targeted_adjustment_is_refused_and_leaves_nothing()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.AdjustmentAsync(employee, 9_000m);
        // Aimed at the source, but no second person approved it yet (PLAN-54, D-PAY-6).
        await scope.TargetedAdjustmentAsync(employee, 18_000m, source, approved: false);

        var result = await CorrectAsync(scope, source);

        Assert.Equal(RunActOutcome.Invalid, result.Result.Outcome);
        Assert.Equal(CorrectionRunMessages.NoTargetedAdjustments, result.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(0, await scope.Db.PayrollRuns.CountAsync(r => r.RunType == PayrollRunType.Correction));
        Assert.Equal(1, await scope.Db.SalaryLines.CountAsync());
        Assert.All(await scope.Db.PayrollAdjustments.AsNoTracking().ToListAsync(), a => Assert.Null(a.PayrollRunId));
    }

    [Fact]
    public async Task A_second_open_correction_of_the_same_source_is_a_conflict_until_the_first_is_posted_or_cancelled()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(employee, 4_000m, source);
        var first = await CorrectAsync(scope, source);
        Assert.True(first.Success, first.Error);
        await scope.TargetedAdjustmentAsync(employee, 2_000m, source);

        var second = await CorrectAsync(scope, source);

        Assert.Equal(RunActOutcome.Conflict, second.Result.Outcome);
        Assert.Equal(CorrectionRunMessages.OpenCorrectionExists, second.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(1, await scope.Db.PayrollRuns.CountAsync(r => r.RunType == PayrollRunType.Correction));

        // The first is cancelled: its targeted adjustment is free again and still aimed at the source, and a new correction takes both.
        var cancelled = await scope.Mediator.Send(new CancelPayrollRunCommand(first.CorrectionRunId!.Value, Pay056RunScope.Processor, "أُنشئ قبل اكتمال التسويات", true, false));
        Assert.True(cancelled.Success, cancelled.Error);
        var third = await CorrectAsync(scope, source);
        Assert.True(third.Success, third.Error);
        Assert.Equal(6_000m, (await scope.LinesAsync(third.CorrectionRunId!.Value)).Single().TotalAdjustments);

        // And once that one is posted, the source can be corrected again.
        var thirdId = third.CorrectionRunId!.Value;
        await scope.SubmitAsync(thirdId);
        await scope.ApproveAsync(thirdId);
        await scope.CloseAuditAsync(thirdId);
        Assert.True((await scope.PostAsync(thirdId)).Success);
        await scope.TargetedAdjustmentAsync(employee, 1_000m, source);
        var fourth = await CorrectAsync(scope, source);
        Assert.True(fourth.Success, fourth.Error);
        Assert.Equal(1_000m, (await scope.RunAsync(fourth.CorrectionRunId!.Value)).TotalNetPay);
    }

    [Fact]
    public async Task A_correction_needs_a_posted_source_that_exists()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var calculated = await scope.CalculatedRunAsync(2028, 1);

        Assert.Equal(RunActOutcome.NotFound, (await CorrectAsync(scope, Guid.NewGuid())).Result.Outcome);
        var notPosted = await CorrectAsync(scope, calculated);
        Assert.Equal(RunActOutcome.Invalid, notPosted.Result.Outcome);
        Assert.Equal(CorrectionRunMessages.SourceNotPosted, notPosted.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(0, await scope.Db.PayrollRuns.CountAsync(r => r.RunType == PayrollRunType.Correction));
    }

    [Fact]
    public async Task A_rejected_correction_is_calculated_again_as_a_correction_and_its_recovery_follows_the_new_lines()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(employee, -30_000m, source);
        var correctionId = (await CorrectAsync(scope, source)).CorrectionRunId!.Value;
        var firstRecovery = await scope.Db.DeductionRecords.AsNoTracking().SingleAsync();
        await scope.SubmitAsync(correctionId);

        var rejected = await scope.Mediator.Send(new RejectPayrollRunCommand(correctionId, Pay056RunScope.Approver, "المبلغ المسترد 20,000 لا 30,000"));

        Assert.True(rejected.Success, rejected.Error);
        scope.Db.ChangeTracker.Clear();
        // The rejected version's recovery is gone with its lines; its adjustment is free and still aimed at the source.
        Assert.Empty(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.LinesAsync(correctionId));
        var freed = await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync();
        Assert.Equal((null, (Guid?)source), (freed.PayrollRunId, freed.TargetSourceRunId));

        // The correction of the correction's inputs: +10,000 aimed at the same source. Then «حساب» on the reopened correction.
        await scope.TargetedAdjustmentAsync(employee, 10_000m, source, "تصحيح مبلغ الاسترداد");
        var (ok, lines, error) = await scope.CalculateAsync(correctionId);

        Assert.True(ok, error);
        Assert.Equal(1, lines);
        var run = await scope.RunAsync(correctionId);
        Assert.Equal((PayrollRunStatus.Calculated, true, 2), (run.Status, run.ValidatedAtCalculate, run.Revision));
        var line = Assert.Single(await scope.LinesAsync(correctionId));
        Assert.Equal((-20_000m, 0m), (line.TotalAdjustments, line.NetPay));
        var recovery = await scope.Db.DeductionRecords.AsNoTracking().SingleAsync();
        Assert.Equal(20_000m, recovery.Amount);
        Assert.NotEqual(firstRecovery.Id, recovery.Id);
        Assert.Equal(CorrectionRecovery.SourceReferenceOf(line.Id), recovery.SourceReference);
        // No calculation job was made for a correction.
        Assert.Empty(await scope.Db.PayrollRunJobs.AsNoTracking().Where(j => j.PayrollRunId == correctionId).ToListAsync());
    }

    [Fact]
    public async Task A_correction_is_never_calculated_by_a_job_over_the_pay_profiles()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        await scope.TargetedAdjustmentAsync(employee, 4_000m, source);
        var correctionId = (await CorrectAsync(scope, source)).CorrectionRunId!.Value;
        await scope.SubmitAsync(correctionId);
        Assert.True((await scope.Mediator.Send(new RejectPayrollRunCommand(correctionId, Pay056RunScope.Approver, "يُعاد"))).Success);
        scope.Db.ChangeTracker.Clear();

        var started = await scope.Mediator.Send(new StartPayrollRunJobCommand(correctionId));

        Assert.False(started.Success);
        Assert.Equal(CorrectionRunMessages.NotCalculatedByAJob, started.Error);
        Assert.Empty(await scope.Db.PayrollRunJobs.AsNoTracking().Where(j => j.PayrollRunId == correctionId).ToListAsync());
        Assert.Equal(RunActOutcome.Invalid,
            (await scope.Mediator.Send(new CalculateCorrectionRunCommand(source))).Result.Outcome);
    }

    [Fact]
    public async Task A_cancelled_correction_frees_its_adjustments_and_deletes_the_recovery_no_run_applied()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);
        var targeted = await scope.TargetedAdjustmentAsync(employee, -30_000m, source);
        var correctionId = (await CorrectAsync(scope, source)).CorrectionRunId!.Value;
        Assert.Single(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());

        var cancelled = await scope.Mediator.Send(new CancelPayrollRunCommand(correctionId, Pay056RunScope.Processor, "تصحيح خاطئ", true, false));

        Assert.True(cancelled.Success, cancelled.Error);
        scope.Db.ChangeTracker.Clear();
        Assert.Empty(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());
        var freed = await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == targeted.Id);
        Assert.Equal((null, (Guid?)source), (freed.PayrollRunId, freed.TargetSourceRunId));
    }

    // --- the adjustment's form: aiming an adjustment at a posted run ---

    private static CreateManualInputHandler ManualInputs(Pay056RunScope scope, bool approvalRequired = true) =>
        new(scope.Services.GetRequiredService<IPayrollRepository>(),
            Options.Create(new PayrollManualInputOptions { RequireApproval = approvalRequired }));

    [Fact]
    public async Task An_adjustment_can_be_aimed_at_a_posted_run_in_which_the_employee_has_a_line_and_waits_for_its_approval()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var source = await scope.PostedRunAsync(2028, 1);

        var created = await ManualInputs(scope).Handle(
            new CreateManualInputCommand(ManualInputKind.Adjustment, employee, -30_000m, "صُرف بدل لا يستحقه", Pay056RunScope.Processor, TargetSourceRunId: source),
            CancellationToken.None);

        Assert.True(created.Success, created.Error);
        var adjustment = await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync();
        Assert.Equal((source, ManualInputOrigin.Manual, ManualInputApprovalStatus.PendingApproval, (Guid?)Pay056RunScope.Processor),
            (adjustment.TargetSourceRunId!.Value, adjustment.Origin, adjustment.ApprovalStatus, adjustment.CreatedBy));

        // Not approved yet: the correction does not take it.
        Assert.Equal(CorrectionRunMessages.NoTargetedAdjustments, (await CorrectAsync(scope, source)).Error);

        // A second person approves it: the correction takes it.
        var decided = await new DecideManualInputHandler(scope.Services.GetRequiredService<IPayrollRepository>(), new Payroll.Application.Services.PayrollCalendar(TimeProvider.System))
            .Handle(new DecideManualInputCommand(ManualInputKind.Adjustment, adjustment.Id, ManualInputDecision.Approved, null, Pay056RunScope.Approver), CancellationToken.None);
        Assert.True(decided.Success, decided.Error);
        var correction = await CorrectAsync(scope, source);
        Assert.True(correction.Success, correction.Error);
        Assert.Equal(-30_000m, (await scope.LinesAsync(correction.CorrectionRunId!.Value)).Single().TotalAdjustments);
    }

    [Fact]
    public async Task An_adjustment_is_not_aimed_at_a_run_that_is_not_posted_nor_at_one_that_did_not_pay_the_employee()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var posted = await scope.PostedRunAsync(2028, 1);
        var hiredLater = await scope.EmployeeAsync();
        var calculated = await scope.CalculatedRunAsync(2028, 2);
        var handler = ManualInputs(scope);

        async Task<ManualInputResult> AimAsync(ManualInputKind kind, Guid who, Guid? target) =>
            await handler.Handle(
                new CreateManualInputCommand(kind, who, 5_000m, "تصحيح", Pay056RunScope.Processor, DeductionType: "Manual", TargetSourceRunId: target),
                CancellationToken.None);

        Assert.Equal(ManualInputMessages.TargetRunNotPosted, (await AimAsync(ManualInputKind.Adjustment, employee, calculated)).Error);
        Assert.Equal(ManualInputMessages.TargetRunNotPosted, (await AimAsync(ManualInputKind.Adjustment, employee, Guid.NewGuid())).Error);
        Assert.Equal(ManualInputMessages.TargetRunHasNoLine, (await AimAsync(ManualInputKind.Adjustment, hiredLater, posted)).Error);
        Assert.Equal(ManualInputMessages.TargetOnlyForAdjustment, (await AimAsync(ManualInputKind.Deduction, employee, posted)).Error);
        Assert.Empty(await scope.Db.PayrollAdjustments.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Db.DeductionRecords.AsNoTracking().ToListAsync());

        // An ordinary adjustment names no run, as before.
        var ordinary = await AimAsync(ManualInputKind.Adjustment, employee, null);
        Assert.True(ordinary.Success, ordinary.Error);
        Assert.Null((await scope.Db.PayrollAdjustments.AsNoTracking().SingleAsync()).TargetSourceRunId);
    }
}
