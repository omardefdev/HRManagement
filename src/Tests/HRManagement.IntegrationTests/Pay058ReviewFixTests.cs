using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.Fraud;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Review;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58: what the independent review found, each with its proof. A bank file whose orders the channel acknowledged, settled or
/// rejected is not reissued (it would ask to pay again); the review counts for the run's present version only (a rejection reopens it
/// and its figures change); a reviewer of the run is none of its approvers, whichever came first; an alert of a run, employee and rule
/// is raised once, so what two people resolved is not raised again by the next sending's scan; arrears voided by a reversal are owed
/// to nobody.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay058ReviewFixTests
{
    private static readonly Guid AuditorA = Guid.Parse("58000000-0000-4000-8000-0000000000b7");
    private static readonly Guid AuditorB = Guid.Parse("58000000-0000-4000-8000-0000000000b8");

    private static Task<bool> Holds(PayrollDualControlKind _) => Task.FromResult(true);

    private static async Task ReviewAsync(Pay056RunScope scope, Guid runId)
    {
        var opened = await scope.Mediator.Send(new OpenPayrollAuditRecordCommand(runId, "مطابق", AuditorA));
        Assert.True(opened.Success, opened.Error);
        var closed = await scope.Mediator.Send(new ClosePayrollAuditRecordCommand(opened.Id!.Value, AuditorB));
        Assert.True(closed.Success, closed.Error);
    }

    private static async Task<(Guid RunId, Guid BatchId, Guid FileId)> SentAsync(Pay056RunScope scope)
    {
        var employee = await scope.EmployeeAsync();
        scope.Db.Add(Pay055TestAccounts.Active(employee));
        await scope.Db.SaveChangesAsync();
        var runId = await scope.PostedRunAsync();
        var batchId = await scope.Db.PaymentBatches.Where(b => b.PayrollRunId == runId).Select(b => b.Id).SingleAsync();
        var sent = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Pay056RunScope.Poster));
        Assert.True(sent.Success, sent.Error);
        return (runId, batchId, sent.Id!.Value);
    }

    [Fact]
    public async Task A_file_whose_orders_the_channel_acknowledged_is_not_reissued()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var (_, batchId, fileId) = await SentAsync(scope);
        Assert.True((await scope.Mediator.Send(new AckPaymentBatchCommand(batchId, "ACK-58R"))).Success);
        var service = scope.Services.GetRequiredService<PayrollDualControlService>();

        var reissue = await service.RequestAsync(PayrollDualControlKind.BankFileReissue, fileId, new PayrollDualControlPayload("لم تستلمه القناة"), Pay056RunScope.Poster, default);

        Assert.Equal((RunActOutcome.Invalid, PayrollDualControlMessages.FileOrdersMovedOn), (reissue.Outcome, reissue.Error));
        Assert.Empty(await scope.Db.PayrollDualControlRequests.AsNoTracking().ToListAsync());
        Assert.Single(await scope.Db.PaymentBatchFiles.AsNoTracking().Where(f => f.PaymentBatchId == batchId).ToListAsync());
    }

    [Fact]
    public async Task A_review_of_the_version_a_rejection_reopened_no_longer_lets_the_run_be_posted()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        await ReviewAsync(scope, runId);

        // Reviewed while under review, then rejected: version 2 is calculated again, submitted and approved; version 1's review no longer
        // counts.
        scope.Db.ChangeTracker.Clear();
        Assert.True((await scope.Mediator.Send(new RejectPayrollRunCommand(runId, Pay056RunScope.OtherApprover, "صحّح البدلات"))).Success);
        var (calculated, _, error) = await scope.CalculateAsync(runId);
        Assert.True(calculated, error);
        await scope.SubmitAsync(runId);
        await scope.ApproveAsync(runId);
        Assert.Equal(PayrollPostingMessages.ReviewRequired, (await scope.PostAsync(runId)).Error);

        await ReviewAsync(scope, runId);
        var (posted, postError) = await scope.PostAsync(runId);
        Assert.True(posted, postError);
    }

    [Fact]
    public async Task A_reviewer_does_not_approve_the_run_he_reviewed_and_a_review_closed_by_an_approver_does_not_count()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.CalculatedRunAsync();
        await scope.SubmitAsync(runId);
        await ReviewAsync(scope, runId);

        scope.Db.ChangeTracker.Clear();
        var byReviewer = await scope.Mediator.Send(new ApprovePayrollRunCommand(runId, AuditorB, "payroll-approver", null));
        Assert.Equal((RunActOutcome.Invalid, PayrollApprovalMessages.ReviewerCannotApprove), (byReviewer.Outcome, byReviewer.Error));
        Assert.Equal(PayrollRunStatus.UnderReview, (await scope.RunAsync(runId)).Status);

        await scope.ApproveAsync(runId);
        // A record closed by an approver (as an older build could leave): the posting does not take it for an independent review.
        scope.Db.PayrollAuditRecords.Add(new PayrollAuditRecord
        {
            PayrollRunId = runId,
            AuditorId = Guid.NewGuid(),
            ClosedBy = Pay056RunScope.Approver,
            ClosedAt = DateTime.UtcNow,
            Status = AuditStatus.Closed,
            Findings = "لا ملاحظات",
        });
        await scope.Db.SaveChangesAsync();
        Assert.Equal(PayrollPostingMessages.ReviewerIsApprover, (await scope.PostAsync(runId)).Error);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task An_alert_two_people_resolved_is_not_raised_again_by_the_next_scan_of_the_run()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var first = await scope.EmployeeAsync();
        var second = await scope.EmployeeAsync();
        var shared = Pay055TestAccounts.Active(second);
        shared.AccountNumber = Pay055TestAccounts.Iban(first);
        scope.Db.Add(Pay055TestAccounts.Active(first));
        scope.Db.Add(shared);
        // The first employee is already held (with no run): the scan writes no fraud hold for him, so no fraud hold of his is released.
        scope.Db.PaymentHolds.Add(new PaymentHold { EmployeeId = first, HoldType = PaymentHoldType.Manual, Reason = "مراجعة", Status = PaymentHoldStatus.Active });
        await scope.Db.SaveChangesAsync();
        var runId = await scope.PostedRunAsync();

        Assert.True((await scope.Mediator.Send(new RunPayrollFraudScanCommand(runId))).Success);
        scope.Db.ChangeTracker.Clear();
        var alert = await scope.Db.FraudAlerts.SingleAsync(a => a.PayrollRunId == runId && a.EmployeeId == first && a.RuleId == FraudRuleIds.SharedIban);
        alert.Status = FraudAlertStatus.Resolved;
        await scope.Db.SaveChangesAsync();

        Assert.True((await scope.Mediator.Send(new RunPayrollFraudScanCommand(runId))).Success);
        scope.Db.ChangeTracker.Clear();
        var alerts = await scope.Db.FraudAlerts.AsNoTracking().Where(a => a.PayrollRunId == runId && a.EmployeeId == first && a.RuleId == FraudRuleIds.SharedIban).ToListAsync();
        Assert.Equal(FraudAlertStatus.Resolved, Assert.Single(alerts).Status);
    }

    [Fact]
    public void Arrears_voided_by_a_reversal_are_owed_to_nobody()
    {
        var employee = Guid.NewGuid();
        var owed = new ArrearsLedgerEntry { EmployeeId = employee, OriginalAmount = 1_000m, PaidAmount = 400m, ArrearsAmount = 600m, Status = ArrearsLedgerStatus.PartiallyPaid };
        var voided = new ArrearsLedgerEntry { EmployeeId = employee, OriginalAmount = 2_000m, PaidAmount = 0m, ArrearsAmount = 2_000m, Status = ArrearsLedgerStatus.Voided };

        var balance = ArrearsSettlementService.GetBalance(employee, [owed, voided]);

        Assert.Equal((1_000m, 400m, 600m, 1), (balance.EntitledTotal, balance.PaidTotal, balance.ArrearsTotal, balance.OpenEntryCount));
    }
}
