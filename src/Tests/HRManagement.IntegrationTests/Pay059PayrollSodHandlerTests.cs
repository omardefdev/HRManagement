using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Review;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-59 (§1, P-6): the handlers' own separation of duties, which no real persona reaches through the API since PLAN-58's conflict
/// guard (the creator holds no approval role, the approver no poster role, and a token holding both is refused at the policy). Proved
/// here through the handlers themselves:
/// <list type="bullet">
/// <item>the run's creator approves it → <see cref="PayrollApprovalMessages.CreatorCannotApprove"/>, and nothing is written;</item>
/// <item>an approver posts a run that has no review → <see cref="PayrollPostingMessages.PosterIsApprover"/>, not
/// <see cref="PayrollPostingMessages.ReviewRequired"/> (the separation of duties is checked first), no batch, the run still approved;</item>
/// <item>an approver posts the same run after one auditor opened its review and another closed it → <see cref="PayrollPostingMessages.PosterIsApprover"/>
/// again, and then the poster posts it.</item>
/// </list>
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay059PayrollSodHandlerTests
{
    private static readonly Guid FirstAuditor = Guid.Parse("59000000-0000-4000-8000-0000000000a1");
    private static readonly Guid SecondAuditor = Guid.Parse("59000000-0000-4000-8000-0000000000a2");

    /// <summary>A calculated run whose creator is the processor (the API stamps the caller; this host has none, so it is set here).</summary>
    private static async Task<Guid> CalculatedRunOfTheProcessorAsync(Pay056RunScope scope)
    {
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        scope.Db.ChangeTracker.Clear();
        var run = await scope.Db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.CreatedBy = Pay056RunScope.Processor;
        await scope.Db.SaveChangesAsync();
        Assert.Equal(Pay056RunScope.Processor, (await scope.RunAsync(runId)).CreatedBy);
        return runId;
    }

    private static async Task<int> ApprovalsAsync(Pay056RunScope scope, Guid runId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PayrollRunApprovals.AsNoTracking().CountAsync(a => a.PayrollRunId == runId);
    }

    private static async Task<int> BatchesAsync(Pay056RunScope scope, Guid runId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentBatches.AsNoTracking().CountAsync(b => b.PayrollRunId == runId);
    }

    [Fact]
    public async Task The_runs_creator_cannot_approve_it_and_nothing_is_written()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await CalculatedRunOfTheProcessorAsync(scope);
        await scope.SubmitAsync(runId);

        scope.Db.ChangeTracker.Clear();
        var (ok, error) = await scope.Mediator.Send(new ApprovePayrollRunCommand(runId, Pay056RunScope.Processor, "payroll-approver", null));

        Assert.False(ok);
        Assert.Equal(PayrollApprovalMessages.CreatorCannotApprove, error);
        Assert.Equal(PayrollRunStatus.UnderReview, (await scope.RunAsync(runId)).Status);
        Assert.Equal(0, await ApprovalsAsync(scope, runId));
    }

    [Fact]
    public async Task An_approver_cannot_post_the_run_he_approved_with_or_without_its_review_and_the_poster_then_can()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await CalculatedRunOfTheProcessorAsync(scope);
        await scope.SubmitAsync(runId);
        await scope.ApproveAsync(runId, Pay056RunScope.Approver);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);

        // No review yet: the separation of duties answers first, not the missing review.
        var (postedWithoutReview, withoutReview) = await scope.PostAsync(runId, Pay056RunScope.Approver);
        Assert.False(postedWithoutReview);
        Assert.Equal(PayrollPostingMessages.PosterIsApprover, withoutReview);
        Assert.NotEqual(PayrollPostingMessages.ReviewRequired, withoutReview);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
        Assert.Equal(0, await BatchesAsync(scope, runId));

        // The independent review: one auditor opens it, another closes it (PLAN-58, D-PAY-3).
        scope.Db.ChangeTracker.Clear();
        var opened = await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, "مراجعة مستقلة — PLAN-59", FirstAuditor));
        Assert.True(opened.Success, opened.Error);
        scope.Db.ChangeTracker.Clear();
        var closed = await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, SecondAuditor));
        Assert.True(closed.Success, closed.Error);

        var (postedAfterReview, afterReview) = await scope.PostAsync(runId, Pay056RunScope.Approver);
        Assert.False(postedAfterReview);
        Assert.Equal(PayrollPostingMessages.PosterIsApprover, afterReview);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
        Assert.Equal(0, await BatchesAsync(scope, runId));

        // The rule refuses the person, not the run: the poster, who neither approved nor reviewed it, posts it.
        var (posted, postError) = await scope.PostAsync(runId, Pay056RunScope.Poster);
        Assert.True(posted, postError);
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.Equal(1, await BatchesAsync(scope, runId));
    }
}
