using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Review;
using Payroll.Application.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (§4, D-PAY-5, D-PAY-3): above the agency's threshold a run needs two different approvers — the first leaves it under review
/// («اعتماد 1 من 2»), the same approver never counts twice, the creator never approves — and the poster is none of its approvers. The
/// independent review is opened by an auditor and closed by another; neither made, approved or posts the run. An agency that turns the
/// review off posts without it, with a line in the run's log.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay058ApprovalReviewTests
{
    private static readonly Guid SecondApprover = Pay056RunScope.OtherApprover;
    private static readonly Guid AuditorA = Guid.Parse("58000000-0000-4000-8000-0000000000a7");
    private static readonly Guid AuditorB = Guid.Parse("58000000-0000-4000-8000-0000000000a8");

    /// <summary>A threshold of 1: every run of the harness is «above» it (its nets are far smaller than 50,000,000).</summary>
    private static Task<Pay056RunScope> DualAsync(Action<IServiceCollection>? more = null) =>
        Pay056RunScope.CreateAsync(services =>
        {
            services.Configure<PayrollApprovalOptions>(o => o.DualApprovalThreshold = 1m);
            more?.Invoke(services);
        });

    private static Task<RunActResult> ApproveAsync(Pay056RunScope scope, Guid runId, Guid approver)
    {
        scope.Db.ChangeTracker.Clear();
        return scope.Mediator.Send(new ApprovePayrollRunCommand(runId, approver, "payroll-approver", null));
    }

    private static async Task ReviewAsync(Pay056RunScope scope, Guid runId)
    {
        var opened = await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, "لا ملاحظات", AuditorA));
        Assert.True(opened.Success, opened.Error);
        var closed = await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, AuditorB));
        Assert.True(closed.Success, closed.Error);
    }

    [Fact]
    public async Task Above_the_threshold_the_first_approval_leaves_the_run_under_review_and_a_second_person_approves_it()
    {
        await using var scope = await DualAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);

        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.UnderReview, run.Status);
        Assert.Equal("اعتماد 1 من 2", PayrollApprovalMessages.Progress(1, 2));

        var again = await ApproveAsync(scope, runId, Pay056RunScope.Approver);
        Assert.Equal((RunActOutcome.Invalid, PayrollRun.SameApproverTwice), (again.Outcome, again.Error));
        Assert.Equal(PayrollRunStatus.UnderReview, (await scope.RunAsync(runId)).Status);
        if (run.CreatedBy is Guid creator)
            Assert.Equal(PayrollApprovalMessages.CreatorCannotApprove, (await ApproveAsync(scope, runId, creator)).Error);

        Assert.True((await ApproveAsync(scope, runId, SecondApprover)).Success);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
        var approvals = await scope.Db.PayrollRunApprovals.AsNoTracking().Where(a => a.PayrollRunId == runId && a.Decision == ApprovalDecision.Approved).ToListAsync();
        Assert.Equal(new[] { Pay056RunScope.Approver, SecondApprover }.Order(), approvals.Select(a => a.ApproverId!.Value).Order());
        Assert.All(approvals, a => Assert.Equal(1, a.Revision));
    }

    [Fact]
    public async Task At_or_below_the_threshold_one_approval_approves_the_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task A_rejection_reopens_the_run_and_the_earlier_versions_approval_no_longer_counts()
    {
        await using var scope = await DualAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);
        scope.Db.ChangeTracker.Clear();
        Assert.True((await scope.Mediator.Send(new RejectPayrollRunCommand(runId, SecondApprover, "صحّح البدلات"))).Success);

        var (ok, _, error) = await scope.CalculateAsync(runId);
        Assert.True(ok, error);
        await scope.SubmitAsync(runId);
        Assert.True((await ApproveAsync(scope, runId, SecondApprover)).Success);
        Assert.Equal(PayrollRunStatus.UnderReview, (await scope.RunAsync(runId)).Status);    // 1 of 2 on version 2
        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task The_poster_is_none_of_the_approvers_and_none_of_the_reviewers()
    {
        await using var scope = await DualAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);
        Assert.True((await ApproveAsync(scope, runId, SecondApprover)).Success);
        await ReviewAsync(scope, runId);

        // The first of two approvers: refused (the posting checks every approval, not the latest).
        Assert.Equal(PayrollPostingMessages.PosterIsApprover, (await scope.PostAsync(runId, Pay056RunScope.Approver)).Error);
        Assert.Equal(PayrollPostingMessages.PosterIsReviewer, (await scope.PostAsync(runId, AuditorB)).Error);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
        var (posted, error) = await scope.PostAsync(runId);
        Assert.True(posted, error);
    }

    [Fact]
    public async Task The_review_is_opened_by_one_auditor_and_closed_by_another_and_neither_made_or_approved_the_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();

        Assert.Equal(PayrollAuditReviewMessages.RunNotReviewable, (await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, null, AuditorA))).Error);
        await scope.SubmitAsync(runId);
        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);
        Assert.Equal(PayrollAuditReviewMessages.ReviewerApprovedTheRun,
            (await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, null, Pay056RunScope.Approver))).Error);
        var run = await scope.RunAsync(runId);
        if (run.CreatedBy is Guid creator)
            Assert.Equal(PayrollAuditReviewMessages.ReviewerMadeTheRun, (await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, null, creator))).Error);

        // Without a review the posting waits; the review needs two auditors.
        Assert.Equal(PayrollPostingMessages.ReviewRequired, (await scope.PostAsync(runId)).Error);
        var opened = await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, "مطابق", AuditorA));
        Assert.True(opened.Success, opened.Error);
        Assert.Equal(PayrollAuditReviewMessages.CloserIsOpener, (await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, AuditorA))).Error);
        Assert.Equal(PayrollAuditReviewMessages.ReviewerApprovedTheRun,
            (await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, Pay056RunScope.Approver))).Error);
        Assert.True((await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, AuditorB))).Success);
        Assert.Equal(RunActOutcome.Conflict, (await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, AuditorB))).Outcome);
        var (posted, error) = await scope.PostAsync(runId);
        Assert.True(posted, error);
    }

    [Fact]
    public async Task An_agency_that_turned_the_review_off_posts_without_it_and_the_runs_log_says_so()
    {
        await using var scope = await Pay056RunScope.CreateAsync(services => services.Configure<PayrollAuditOptions>(o => o.Required = false));
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        Assert.True((await ApproveAsync(scope, runId, Pay056RunScope.Approver)).Success);

        var (posted, error) = await scope.PostAsync(runId);

        Assert.True(posted, error);
        var job = await scope.Db.PayrollRunJobs.AsNoTracking().Where(j => j.PayrollRunId == runId).OrderByDescending(j => j.CreatedAt).FirstAsync();
        Assert.Contains(PayrollPostingMessages.ReviewTurnedOff, job.Log ?? string.Empty, StringComparison.Ordinal);
    }
}
