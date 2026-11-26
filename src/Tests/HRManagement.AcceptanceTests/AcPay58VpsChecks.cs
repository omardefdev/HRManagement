using System.Globalization;
using System.Net;
using System.Text.Json;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-58 (P10, P-16, P-17) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as the demo's
/// people: <c>hradmin</c> (refused every payroll financial act: in demo mode he holds every payroll policy, and a token with two
/// conflicting roles passes none), <c>e2e-payroll-processor</c> and <c>e2e-payroll-approver</c>. The password is read into memory only;
/// evidence goes under <c>artifacts/plan58/vps</c> and is never committed.
/// <para>
/// <b>These checks add no user and post, fund, process, settle or calculate nothing on the demo.</b> Their one write is a four-eyes
/// request on an existing arrears entry, asked by the processor with the entry's own amounts and <b>rejected</b> by the approver with a
/// note: the entry is as it was. The demo's runs, lines, batches, orders and arrears are compared with P0's picture by the same
/// checksums (<c>PAY058_VPS_RUNS</c>, <c>_LINES</c>, <c>_BATCHES</c>, <c>_ORDERS</c>, <c>_ARREARS</c>: «count|md5»), never an amount.
/// </para>
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pay58Processor = "e2e-payroll-processor";
    private const string Pay58Approver = "e2e-payroll-approver";
    private const string Pay58Api = "/ui/payroll/api/payroll";
    private const string Pay58Migration = "20261126135338_PAY058_DisbursementGovernance";
    private const string Pay58AdjustmentReason = "مراجعة قيد متأخرات بعد مطابقة كشف الخزينة؛ لا تغيير في المبالغ.";
    private const string Pay58RejectionNote = "المبالغ مطابقة للكشف؛ لا حاجة إلى تعديل القيد.";

    private static readonly string Pay58Out = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan58", "vps");

    private static EvidenceRecorder StartPay58(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pay58-{key}-{Guid.NewGuid():N}", Pay58Out) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static string Pay58One(string sql) => Sql("payroll_db", sql).SingleOrDefault() ?? "";

    /// <summary>P0's picture, by the same expressions (picture.sh): count|md5 of the runs, lines, batches, orders and arrears.</summary>
    private static Dictionary<string, string> Pay58Picture() => new(StringComparer.Ordinal)
    {
        ["RUNS"] = Pay58One("select count(*) || '|' || md5(string_agg(\"Id\"::text||\"Status\"::text||\"TotalNetPay\"::text||coalesce(\"PostedBy\"::text,''), ',' order by \"Id\")) from \"PayrollRuns\""),
        ["LINES"] = Pay58One("select count(*) || '|' || md5(string_agg(\"Id\"::text||\"NetPay\"::text||coalesce(\"BankAccountId\"::text,''), ',' order by \"Id\")) from \"SalaryLines\""),
        ["BATCHES"] = Pay58One("select count(*) || '|' || md5(string_agg(\"Id\"::text||\"Status\"::text||\"TotalAmount\"::text, ',' order by \"Id\")) from \"PaymentBatches\""),
        ["ORDERS"] = Pay58One("select count(*) || '|' || coalesce(md5(string_agg(\"Id\"::text||\"Status\"::text||\"Amount\"::text, ',' order by \"Id\")), '') from \"PaymentOrders\""),
        ["ARREARS"] = Pay58One("select count(*) || '|' || md5(string_agg(\"Id\"::text||\"Status\"::text||\"OriginalAmount\"::text||\"PaidAmount\"::text, ',' order by \"Id\")) from \"ArrearsLedgerEntries\""),
    };

    /// <summary>The picture's values from the environment (taken from the backup just before the deploy); empty when not given.</summary>
    private static Dictionary<string, string> Pay58Expected() =>
        new[] { "RUNS", "LINES", "BATCHES", "ORDERS", "ARREARS" }
            .Select(k => (k, v: Environment.GetEnvironmentVariable($"PAY058_VPS_{k}") ?? ""))
            .Where(x => x.v.Length > 0)
            .ToDictionary(x => x.k, x => x.v, StringComparer.Ordinal);

    /// <summary>The RoleConflict lines of Payroll's security audit (the payroll container's file), counted; the last one returned.</summary>
    private static (long Count, string Last) Pay58RoleConflicts()
    {
        var container = ContainerOf("payroll-web");
        var count = Sh($"docker exec {container} sh -c {Q("cat /tmp/hrm-security-audit/security-*.jsonl 2>/dev/null | grep -c '\"RoleConflict\"' || true")}").Output.Trim();
        var last = Sh($"docker exec {container} sh -c {Q("cat /tmp/hrm-security-audit/security-*.jsonl 2>/dev/null | grep '\"RoleConflict\"' | tail -n 1")}").Output.Trim();
        return (long.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0, last);
    }

    private static string Pay58Error(string body)
    {
        if (!body.TrimStart().StartsWith('{'))
            return "";
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    }

    [SkippableFact]
    public void AC_PAY_58_vps_the_migration_is_recorded_its_tables_are_isolated_and_the_demos_money_is_as_in_the_picture()
    {
        var recorder = StartPay58("migration", "Demo server: PAY058 recorded, its two tables under forced row security, and the runs, lines, batches, orders and arrears as in P0's picture");
        var recorded = Pay58One($"select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" = '{Pay58Migration}'");
        var isolated = Pay58One(
            "select string_agg(c.relname || ':' || c.relrowsecurity::text || ':' || c.relforcerowsecurity::text || ':' || " +
            "(select count(*) from pg_policies p where p.tablename = c.relname and p.qual is not null and p.with_check is not null)::text, ',' order by c.relname) " +
            "from pg_class c where c.relname in ('PaymentBatchFiles','PayrollDualControlRequests') and c.relkind = 'r'");
        var picture = Pay58Picture();
        var expected = Pay58Expected();
        recorder.WriteScenarioDbChecks(new { recorded, isolated, picture, expected, pictureFormat = "count|md5 (picture.sh's expressions; no amount)" });

        Assert.Equal("1", recorded);
        Assert.Equal("PaymentBatchFiles:true:true:1,PayrollDualControlRequests:true:true:1", isolated);
        foreach (var (key, value) in expected)
            Assert.True(picture[key] == value, $"{key}: {picture[key]} on the demo, {value} in P0's picture");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_58_vps_the_partial_disbursement_and_the_treasury_exports_are_gone()
    {
        var recorder = StartPay58("removed", "Demo server: partial-disburse, the treasury file and its bundle answer 404 (one payment path)");
        var password = DemoPassword();
        using var processor = await Pay56ApiAsync(Pay58Processor, password);
        var run = Pay58One("select \"Id\" from \"PayrollRuns\" where \"Status\" = 2 order by \"PeriodStart\" desc limit 1");
        var before = Pay58Picture();
        var (partial, _, _) = await Pay55PostAsync(processor, $"{Pay58Api}/runs/{run}/partial-disburse", new { fundingDecisionId = Guid.NewGuid() });
        var treasury = await Status(processor, $"https://{AppDomain}/ui/payroll/PayrollReports/ExportTreasuryFile?runId={run}");
        var bundle = await Status(processor, $"https://{AppDomain}/ui/payroll/PayrollReports/ExportExternalIntegrationBundle?runId={run}");
        var after = Pay58Picture();
        recorder.WriteScenarioDbChecks(new { run, partialDisburse = partial, treasuryFile = (int)treasury, bundle = (int)bundle, unchanged = before.SequenceEqual(after) });

        Assert.Equal(404, partial);
        Assert.Equal(HttpStatusCode.NotFound, treasury);
        Assert.Equal(HttpStatusCode.NotFound, bundle);
        Assert.Equal(before, after);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_58_vps_hradmin_is_refused_every_financial_act_with_the_conflict_named_and_audited_and_nothing_changes()
    {
        var recorder = StartPay58("conflict", "Demo server: hradmin (every payroll policy in demo mode) approves, posts or processes → 403 naming the pair, a RoleConflict line in the security audit, nothing changed");
        using var admin = await Pay56ApiAsync("hradmin", DemoPassword());
        var run = Pay58One("select \"Id\" from \"PayrollRuns\" order by \"PeriodStart\" desc limit 1");
        var batch = Pay58One("select \"Id\" from \"PaymentBatches\" order by \"CreatedAt\" desc limit 1");
        var before = Pay58Picture();
        var (linesBefore, _) = Pay58RoleConflicts();
        var answers = new List<(string Act, int Status, string Error)>();
        foreach (var (act, path) in new[]
                 {
                     ("approve", $"{Pay58Api}/runs/{run}/approve"),
                     ("post", $"{Pay58Api}/runs/{run}/post"),
                     ("process", $"{Pay58Api}/payment-batches/{batch}/process"),
                 })
        {
            var (status, error, _) = await Pay55PostAsync(admin, path, new { });
            answers.Add((act, status, error));
        }

        var (linesAfter, last) = Pay58RoleConflicts();
        var after = Pay58Picture();
        recorder.WriteScenarioDbChecks(new
        {
            answers = answers.Select(a => new { a.Act, a.Status, a.Error }),
            roleConflictLinesBefore = linesBefore,
            roleConflictLinesAfter = linesAfter,
            lastLineNamesAPair = last.Contains("payroll-", StringComparison.Ordinal),
            lastLineHasNoBearer = !last.Contains("Bearer", StringComparison.OrdinalIgnoreCase),
            unchanged = before.SequenceEqual(after),
        });

        Assert.All(answers, a =>
        {
            Assert.True(a.Status == 403, $"{a.Act}: hradmin answered {a.Status}");
            Assert.Contains("payroll-approver", a.Error, StringComparison.Ordinal);
        });
        Assert.True(linesAfter - linesBefore >= answers.Count, $"{linesAfter - linesBefore} RoleConflict line(s) for {answers.Count} refusals");
        Assert.DoesNotContain("Bearer", last, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, after);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_58_vps_an_arrears_adjustment_is_a_request_the_approver_rejects_and_the_entry_is_as_it_was()
    {
        var recorder = StartPay58("four-eyes", "Demo server: the processor asks for an arrears adjustment; the inbox shows it to the approver, not to the requester; the requester cannot decide it; the approver rejects it with a note; the entry is as it was");
        var password = DemoPassword();
        using var processor = await Pay56ApiAsync(Pay58Processor, password);
        using var approver = await Pay56ApiAsync(Pay58Approver, password);
        var entry = Pay58One(
            "select \"Id\" || '|' || \"OriginalAmount\"::text || '|' || \"PaidAmount\"::text from \"ArrearsLedgerEntries\" " +
            "where \"Status\" in ('Pending','PartiallyPaid') order by \"CreatedAt\" limit 1").Split('|');
        AcceptanceGate.RequireData(recorder, entry.Length == 3, "لا قيد متأخرات قائماً على العرض", "The demo has no open arrears entry to ask about.");
        string Row() => Pay58One($"select \"Status\" || '|' || md5(\"OriginalAmount\"::text || '|' || \"PaidAmount\"::text || '|' || \"ArrearsAmount\"::text) from \"ArrearsLedgerEntries\" where \"Id\" = '{entry[0]}'");
        var rowBefore = Row();
        string? request = null;
        try
        {
            var (asked, askError, askBody) = await Pay55PostAsync(processor, $"{Pay58Api}/arrears/{entry[0]}/adjustment-requests", new
            {
                originalAmount = decimal.Parse(entry[1], CultureInfo.InvariantCulture),
                paidAmount = decimal.Parse(entry[2], CultureInfo.InvariantCulture),
                reason = Pay58AdjustmentReason,
            });
            using (var json = JsonDocument.Parse(askBody))
                request = json.RootElement.TryGetProperty("requestId", out var id) ? id.GetString() : null;
            Assert.True(asked == 202 && request is not null, $"The request answered {asked}: {askError}");

            var approverInbox = await (await approver.GetAsync($"https://{AppDomain}{Pay58Api}/dual-control")).Content.ReadAsStringAsync();
            var processorInbox = await (await processor.GetAsync($"https://{AppDomain}{Pay58Api}/dual-control")).Content.ReadAsStringAsync();
            var inboxPage = await (await approver.GetAsync($"https://{AppDomain}/ui/payroll/DualControl/Inbox")).Content.ReadAsStringAsync();
            var (own, ownError, _) = await Pay55PostAsync(processor, $"{Pay58Api}/dual-control/{request}/decide", new { approve = true });
            var (rejected, rejectError, _) = await Pay55PostAsync(approver, $"{Pay58Api}/dual-control/{request}/decide", new { approve = false, notes = Pay58RejectionNote });
            var requestRow = Pay58One($"select \"Status\"::text || '|' || (\"RequestedBy\" <> \"DecidedBy\")::text || '|' || coalesce(\"DecisionNotes\", '') from \"PayrollDualControlRequests\" where \"Id\" = '{request}'");
            var rowAfter = Row();
            recorder.WriteScenarioDbChecks(new
            {
                entry = entry[0],
                asked,
                approverSeesIt = approverInbox.Contains(request!, StringComparison.OrdinalIgnoreCase),
                requesterSeesIt = processorInbox.Contains(request!, StringComparison.OrdinalIgnoreCase),
                inboxPageShowsTheReason = inboxPage.Contains(Pay58AdjustmentReason, StringComparison.Ordinal),
                inboxPageShowsNoEmployeeId = !inboxPage.Contains(Pay58One($"select \"EmployeeId\" from \"ArrearsLedgerEntries\" where \"Id\" = '{entry[0]}'"), StringComparison.OrdinalIgnoreCase),
                ownDecision = own,
                ownDecisionError = ownError,
                rejection = rejected,
                rejectError,
                requestRow,
                requestFormat = "status (2 = Rejected)|decider is not the requester|note",
                entryUnchanged = rowBefore == rowAfter,
            });

            Assert.Contains(request!, approverInbox, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(request!, processorInbox, StringComparison.OrdinalIgnoreCase);
            // The processor does not hold the decision's policy (Payroll.Approve): refused before the rule «the requester never decides».
            Assert.True(own is 400 or 403, $"The requester's own decision answered {own}");
            Assert.True(rejected == 200, $"The rejection answered {rejected}: {rejectError}");
            Assert.Equal($"2|true|{Pay58RejectionNote}", requestRow);
            Assert.Equal(rowBefore, rowAfter);
            recorder.WriteResult("Pass");
        }
        finally
        {
            // Nothing is left waiting: a request this check made that is still pending is rejected with the note.
            if (request is not null && Pay58One($"select \"Status\" from \"PayrollDualControlRequests\" where \"Id\" = '{request}'") == "0")
                await Pay55PostAsync(approver, $"{Pay58Api}/dual-control/{request}/decide", new { approve = false, notes = Pay58RejectionNote });
        }
    }

    [SkippableFact]
    public async Task AC_PAY_58_vps_the_new_screens_open_in_arabic_under_the_csp_and_the_holds_pages_ask_for_no_approver()
    {
        var recorder = StartPay58("screens", "Demo server: the inbox, a batch's page, a run's page, the holds' and alerts' pages under the enforced CSP with no violation; no approver field; the review records refused to hradmin (the demo has no auditor)");
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--disable-quic"] });
        var state = Path.Combine(Path.GetTempPath(), $"pay58-state-{Guid.NewGuid():N}.json");
        try
        {
            await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", "hradmin", DemoPassword(), state);
            var pages = new List<Ux067KeyPage>
            {
                new("payroll-dual-control-inbox", "/ui/payroll/DualControl/Inbox"),
                new("payroll-batches", "/ui/payroll/PaymentBatches"),
                new("payroll-batch-details", "/ui/payroll/PaymentBatches", @"/PaymentBatches/Details/[0-9a-fA-F-]{36}"),
                new("payroll-run-details", "/ui/payroll/PayrollRuns", @"/PayrollRuns/Details/[0-9a-fA-F-]{36}"),
                new("payroll-holds", "/ui/payroll/PaymentHolds"),
                new("payroll-alerts", "/ui/payroll/FraudAlerts"),
            };
            var (results, repeats) = await Ux067PageProbe.ProbeEachAsync(browser, $"https://{AppDomain}", state, pages, [(1366, 768)], Path.Combine(Pay58Out, "screenshots"));
            await File.WriteAllTextAsync(Path.Combine(Pay58Out, "csp-pages.json"), Ux067PageProbe.ToJson(results));
            await File.WriteAllLinesAsync(Path.Combine(Pay58Out, "csp-pages-repeats.txt"), repeats);

            var (admin, _) = await SignInAsync("hradmin", DemoPassword(), "/ui/payroll/");
            using (admin)
            {
                var holds = await (await admin.GetAsync($"https://{AppDomain}/ui/payroll/PaymentHolds")).Content.ReadAsStringAsync();
                var alerts = await (await admin.GetAsync($"https://{AppDomain}/ui/payroll/FraudAlerts")).Content.ReadAsStringAsync();
                var review = await Status(admin, $"https://{AppDomain}/ui/payroll/PayrollAuditRecords");
                recorder.WriteScenarioSteps(string.Join("\n", results.Select(r =>
                    $"- {r.Name}: HTTP {r.Status}, {r.CspPolicies} policy, {r.Violations.Count} violation(s){(r.Note is null ? "" : $" ({r.Note})")}")));
                recorder.WriteScenarioDbChecks(new
                {
                    pages = results.Select(r => new { r.Name, r.Status, r.Resolved, r.CspPolicies, violations = r.Violations.Count }),
                    holdsPageAsksForAnApprover = holds.Contains("approverUserId", StringComparison.OrdinalIgnoreCase),
                    alertsPageAsksForAnApprover = alerts.Contains("approverUserId", StringComparison.OrdinalIgnoreCase),
                    reviewRecordsForHradmin = (int)review,
                });

                var failures = results.Where(r => !r.Passes).Select(r => $"{r.Name}: {r.Status}, {r.CspPolicies} policies, {r.Violations.Count} violations {r.Note}").ToList();
                Assert.True(failures.Count == 0, "Pages failing the CSP check:\n" + string.Join("\n", failures));
                Assert.All(results.Where(r => r.Name is "payroll-dual-control-inbox" or "payroll-holds" or "payroll-alerts"), r => Assert.True(r.Resolved));
                Assert.DoesNotContain("approverUserId", holds, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("approverUserId", alerts, StringComparison.OrdinalIgnoreCase);
                // The review records are the auditor's (Payroll.AuditReview); hradmin counts as processor and approver and is refused.
                Assert.True(review is HttpStatusCode.Forbidden or HttpStatusCode.Found, $"The review records answered {(int)review} to hradmin");
            }

            recorder.WriteResult("Pass");
        }
        finally
        {
            await browser.DisposeAsync();
            playwright.Dispose();
            if (File.Exists(state))
                File.Delete(state);
        }
    }
}
