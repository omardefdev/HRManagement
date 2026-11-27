using System.Net;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-12: payroll separation of duties, for the right reason (PLAN-59 §2, P-7). On a calculated and validated run of agency A — its month
/// D-PAY-22's, the first ended month with no live regular run and no closed attendance (<see cref="PayrollCycleDriver.CycleMonthAsync"/>):
/// the creator cannot approve (403: no approval role); a token holding conflicting payroll roles cannot approve (403 naming the pair,
/// D-PAY-20); the approver cannot post (403: no poster role); the poster cannot post before the independent review (400 with
/// <c>PayrollPostingMessages.ReviewRequired</c>'s text); after one auditor opened the review and another closed it, the poster posts.
/// A run not posted is cancelled in <c>finally</c> (PLAN-56), so its month stays free. The handlers' own rules, which no persona reaches
/// since PLAN-58, are <c>Pay059PayrollSodHandlerTests</c>'.
/// </summary>
[RequiredAcceptance]
public class Ac12PayrollSodTests
{
    private const string Owner = "AC-12";

    /// <summary><c>PayrollPostingMessages.ReviewRequired</c> (PLAN-58).</summary>
    private const string ReviewRequired = "يجب أن يفتح مدقق سجل مراجعة للتشغيل ويغلقه مدقق آخر قبل الترحيل.";

    [SkippableFact]
    public async Task Payroll_SoD_blocks_creator_approve_and_approver_post()
    {
        AcceptanceEnv.RequireEnabled();
        var processorPersona = AcceptanceHarness.RequirePersona("payroll-processor");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processorPersona);
        var recorder = new EvidenceRecorder("AC-12", correlationId);
        recorder.WriteMetadata("AC-12 payroll SoD", processorPersona.Id);
        recorder.WriteSteps("""
            AC-12 (PLAN-59 §2): on a calculated, validated run of D-PAY-22's month:
            1. payroll-processor creates, calculates and validates → 200, ResultsFrozen=true.
            2. payroll-processor approves → 403 (no approval role).
            3. a-payroll-multirole approves → 403, the conflicting pair named.
            4. payroll-processor submits; payroll-approver approves, and a-payroll-approver-2 when two are required → Approved.
            5. payroll-approver posts → 403 (no poster role).
            6. a-payroll-poster posts before the review → 400 «يجب أن يفتح مدقق سجل مراجعة للتشغيل ويغلقه مدقق آخر قبل الترحيل.»
            7. auditor opens the review, a-auditor-2 closes it → 200.
            8. a-payroll-poster posts → 200, Posted, with its disbursement batch.
            In finally: a run not posted is cancelled through POST api/payroll/runs/{id}/cancel.
            """);

        using var processor = await PayrollApi.PayrollAsync(recorder, "payroll-processor", "ac12");
        using var approver = await PayrollApi.PayrollAsync(recorder, "payroll-approver", "ac12");
        using var multirole = await PayrollApi.PayrollAsync(recorder, "a-payroll-multirole", "ac12");
        using var poster = await PayrollApi.PayrollAsync(recorder, "a-payroll-poster", "ac12");
        using var central = await PayrollApi.AttendanceAsync(recorder, "central-a", "ac12");

        // The main stack's other classes may have added employment records after the seed; the regime gate reads them.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(HRManagement.ServiceDefaults.AcceptanceSeedIds.AgencyAId.ToString("D"));
        var (month, runId, refused) = await PayrollCycleDriver.CycleMonthAsync(processor, central, recorder, "ac12", Owner, AcceptancePayrollMonths.MainStack);
        recorder.WriteBefore(new { month = AcceptancePayrollMonths.Ym(month), runId, refused });
        string cleanUp;
        try
        {
            // 1. Created, calculated and validated: the results are frozen.
            var (calculated, calculatedBody, _) = await processor.PostAsync("calculate", $"api/payroll/runs/{runId}/calculate");
            Assert.True(calculated == HttpStatusCode.OK, $"calculate: {(int)calculated} {PayrollApi.Error(calculatedBody)}");
            var (validated, validatedBody, _) = await processor.PostAsync("validate", $"api/payroll/runs/{runId}/validate");
            Assert.True(validated == HttpStatusCode.OK, $"validate: {(int)validated} {PayrollApi.Error(validatedBody)}");
            var freeze = await processor.ReadAsync("freeze-status", $"api/payroll/runs/{runId}/freeze-status");
            Assert.True(freeze.GetProperty("resultsFrozen").GetBoolean());

            // 2. The creator cannot approve: the policy answers before any handler (no approval role).
            var (selfApprove, _, _) = await processor.PostAsync("approve-creator-negative", $"api/payroll/runs/{runId}/approve");
            Assert.Equal(HttpStatusCode.Forbidden, selfApprove);

            // 3. A token holding conflicting payroll roles is refused, the pair named (D-PAY-20).
            var (conflict, conflictBody, conflictText) = await multirole.PostAsync("approve-multirole-negative", $"api/payroll/runs/{runId}/approve");
            Assert.Equal(HttpStatusCode.Forbidden, conflict);
            var conflictError = PayrollApi.Error(conflictBody);
            Assert.True(System.Text.RegularExpressions.Regex.Matches(conflictError, "payroll-[a-z]+").Select(m => m.Value).Distinct().Count() >= 2,
                $"The conflict's refusal names no pair of roles: {PayrollApi.Snip(conflictText)}");

            // 4. Submitted; approved by every approver the amount asks for.
            var (submitted, submittedBody, _) = await processor.PostAsync("submit-for-review", $"api/payroll/runs/{runId}/submit-for-review");
            Assert.True(submitted == HttpStatusCode.OK, $"submit: {(int)submitted} {PayrollApi.Error(submittedBody)}");
            var (approved, approvedBody, _) = await approver.PostAsync("approve-distinct-checker", $"api/payroll/runs/{runId}/approve");
            Assert.True(approved == HttpStatusCode.OK, $"approve: {(int)approved} {PayrollApi.Error(approvedBody)}");
            var approvals = await AcceptancePayrollReview.CompleteApprovalAsync(recorder, runId, approvedBody, "ac12");
            var afterApproval = await processor.ReadAsync("run-approved", $"api/payroll/runs/{runId}");
            Assert.Equal("Approved", AcceptancePayrollMonths.RunStatus(afterApproval));
            Assert.Equal(afterApproval.GetProperty("approvalsRequired").GetInt32(), afterApproval.GetProperty("approvalsGiven").GetInt32());

            // 5. The approver cannot post: no poster role.
            var (approverPost, _, _) = await approver.PostAsync("post-approver-negative", $"api/payroll/runs/{runId}/post");
            Assert.Equal(HttpStatusCode.Forbidden, approverPost);

            // 6. The poster cannot post before the independent review.
            var (early, earlyBody, earlyText) = await poster.PostAsync("post-before-review-negative", $"api/payroll/runs/{runId}/post");
            Assert.Equal(HttpStatusCode.BadRequest, early);
            Assert.True(PayrollApi.Error(earlyBody) == ReviewRequired, $"The posting before the review answered: {PayrollApi.Snip(earlyText)}");
            Assert.Equal("Approved", AcceptancePayrollMonths.RunStatus(await processor.ReadAsync("run-still-approved", $"api/payroll/runs/{runId}")));

            // 7. The independent review: one auditor opens it, another closes it.
            var review = await AcceptancePayrollReview.ReviewAsync(recorder, runId, "ac12");

            // 8. The poster posts.
            var (posted, postedBody, postedText) = await poster.PostAsync("post", $"api/payroll/runs/{runId}/post");
            Assert.True(posted == HttpStatusCode.OK, $"post: {(int)posted} {PayrollApi.Snip(postedText)} {PayrollApi.Error(postedBody)}");
            var after = await processor.ReadAsync("run-posted", $"api/payroll/runs/{runId}");
            Assert.Equal("Posted", AcceptancePayrollMonths.RunStatus(after));
            Assert.Equal(JsonValueKind.String, after.GetProperty("paymentBatchId").ValueKind);

            recorder.WriteDbChecks(new
            {
                source = "API reads (GET api/payroll/runs/{id}, freeze-status) and the answers above",
                month = AcceptancePayrollMonths.Ym(month),
                runId,
                resultsFrozen = true,
                creatorApprove = (int)selfApprove,
                multiroleApprove = new { status = (int)conflict, error = conflictError },
                approvals,
                approverPost = (int)approverPost,
                postBeforeReview = new { status = (int)early, error = PayrollApi.Error(earlyBody) },
                review,
                posted = (int)posted,
                status = AcceptancePayrollMonths.RunStatus(after),
                paymentBatchId = after.GetProperty("paymentBatchId").GetString(),
            });
            recorder.WriteAfter(new { runId, status = AcceptancePayrollMonths.RunStatus(after), sodEnforced = true });
            recorder.WriteAudit(new { scenario = "AC-12", explicitAuditQuery = false, source = "the API answers above", review, approvals });
            recorder.FlushTiming();
            recorder.WriteResult("Pass");
        }
        finally
        {
            cleanUp = await PayrollCycleDriver.CancelUnlessPostedAsync(processor, approver, "ac12-finally", runId, "AC-12: انتهى اختبار فصل الواجبات (PLAN-59)");
            recorder.RecordTiming("cleanUp", cleanUp);
            recorder.FlushTiming();
        }
    }
}
