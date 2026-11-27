using System.Diagnostics;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-PAY-59 (PLAN-59 §4, P-10): one monthly payroll cycle of agency A proved on the production-like profile, by HTTP alone and one person
/// per role (<see cref="PayrollCycleDriver"/>). Its month is D-PAY-22's — the first ended month from <c>ACCEPTANCE_PAYROLL_CYCLE_FROM</c>
/// (default: 12 months before today) with no live regular run and no closed attendance in agency A — so three executions in a row on one
/// stack take three consecutive months (AC-PAY-59a). AC-PAY-59b is inside the cycle: the processing does not send the held employee's
/// order, and a second processing of the same batch makes no second file.
/// <para>
/// It runs on a stack of its own in CI (<c>prodlike-cycle</c>, P-11): it gives every employee of agency A a verified account, which the
/// classes that pick employees with no account (AC-PAY-55, AC-PAY-58) must not meet. No SQL: no psql, no database read, no classification
/// of records — every check is an API read, written to <c>db-checks.json</c>.
/// </para>
/// </summary>
[RequiredAcceptance]
public class AcPay59FullCycleTests
{
    private const string Steps = """
        AC-PAY-59 — one monthly cycle of agency A, by HTTP alone, one person per role (PLAN-59 §4; P-4's step 3; PLAN-58's people):
        0. The month M (D-PAY-22): the first ended month from ACCEPTANCE_PAYROLL_CYCLE_FROM with no live regular run (the run is created:
           201; the live-period refusal moves on) and no closed attendance (central-a reads GET api/attendance/periods/{M}); the months of
           lva057-periods.json and of the registry's other classes are skipped. The run's scope is its attendance-status before the close;
           Y, X and Z are three of its employees in the order of their ids (Z with no open salary change request).
        1. payroll-processor reads the framework in force for LAW_50_2004 in M; none in force → import law50-interim.json from the first
           day of M, payroll-approver approves, a-payroll-approver-2 attests, payroll-approver publishes.
        2. Accounts: payroll-processor registers and payroll-approver verifies one for everyone in scope without one in force; X's account in
           force (an earlier execution's) is retired by payroll-approver, and a new one is registered and left unverified.
        3. payroll-processor asks for a salary change of Z from the first day of next month (D-53-1: not before the current month; M has
           ended); his own approval → 403; payroll-approver approves → Scheduled; Z's salary in force unchanged. Cancelled in finally.
        4. central-a: Y absent on M's first two working days (the create screen), the bulk entry «حاضر» from agency A's root unit, then
           POST api/attendance/periods/{M}/close with no unit → 200; payroll holds one receipt per employee closed.
        5. payroll-processor calculates and validates: missingAttendance = []; Y's line has the absence deduction of the close; Z's base
           component is his salary in force in M; provenance names step 1's framework; bank-readiness lists X alone.
        6. payroll-processor submits; payroll-approver rejects with a note → Draft, revision 2.
        7. payroll-processor enters +25,000 for Y on the adjustments screen, payroll-approver approves it there; calculate, validate (with a
           non-zero adjustment), submit: Y's net = gross − deductions + 25,000.
        8. payroll-approver approves, and a-payroll-approver-2 when approvalsRequired is 2 → Approved.
        9. auditor opens the review, a-auditor-2 closes it.
        10. a-payroll-poster posts → Posted, TotalNetPay as approved, a Pending batch; X's order Held NO-ACCOUNT, everyone else's Draft; the
            held report names X alone.
        11. a-payroll-poster processes → the others Submitted; one Initial file whose ControlCount and ControlTotal are theirs, X's order
            not in it. AC-PAY-59b: a second process makes no second file, and X's order is still not sent.
        12. payroll-approver verifies X's account → NO-ACCOUNT lifted, the account pinned on X's order and line (bank-readiness no longer
            lists him).
        13. a-payroll-poster sends submit-held → a HeldRelease file with X's order alone.
        14. payroll-processor: every line's payslip net equals its order; Σ orders = TotalNetPay.
        15. a-payroll-poster acks with the channel's reference, asks for the settlement with the statement's reference and the sum;
            payroll-approver approves → the batch and its orders Settled (D-PAY-21).
        In finally: Z's change cancelled whatever happened; a run not posted cancelled (PLAN-56).
        """;

    [SkippableFact]
    public async Task One_monthly_cycle_by_HTTP_alone_one_person_per_role()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-PAY-59", correlationId);
        recorder.WriteMetadata(
            "AC-PAY-59a/b: one monthly payroll cycle by HTTP alone, one person per role; the held employee's order not sent and no second file",
            "payroll-processor+payroll-approver+a-payroll-approver-2+a-payroll-poster+auditor+a-auditor-2+central-a");
        recorder.WriteSteps(Steps);

        var checks = new Dictionary<string, object?>();
        var timing = new Dictionary<string, long>();
        var total = Stopwatch.StartNew();
        object? figures = null;
        try
        {
            figures = await PayrollCycleDriver.RunAsync(recorder, checks, timing);
        }
        finally
        {
            recorder.RecordTiming("totalMs", total.ElapsedMilliseconds);
            recorder.WriteDbChecks(new { source = "API reads only (no SQL): each check names the read that answers it", checks });
            recorder.WriteAudit(new
            {
                scenario = "AC-PAY-59",
                explicitAuditQuery = false,
                source = "the API's own answers: the salary change's requester and decider, the review's opener and closer, the settlement's requester and decider",
                cleanUp = new { change = checks.GetValueOrDefault("finally-change"), run = checks.GetValueOrDefault("finally-run") },
            });
            recorder.FlushTiming();
        }

        // P-4: the change is cancelled, so nothing applies later.
        Assert.Equal("Cancelled", checks.GetValueOrDefault("finally-change") as string);
        recorder.WriteAfter(figures!);
        recorder.WriteResult("Pass");
    }
}
