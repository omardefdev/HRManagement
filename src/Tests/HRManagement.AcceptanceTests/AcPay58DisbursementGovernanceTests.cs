using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-PAY-58a–h (PLAN-58) on the prod-like stack, through the gateway: disbursement governance. Money leaves by one path — the posting
/// makes the batch and its orders, a funding decision comes after it, and only the poster's <c>process</c> or <c>submit-held</c>
/// sends, each with one bank file; every four-eyes act is a request and a decision by two people from their own sessions; a run above
/// the agency's threshold needs two approvers and an independent review by two auditors before its posting; a token with two
/// conflicting payroll roles passes no financial act; a held employee holds back nobody else; and the channel's confirmation is
/// recorded so that a batch reaches <c>Settled</c>.
/// <para>
/// The scenarios run in the order of their names, each in its own month of 2030 in agency A (months the seed's v1 framework governs,
/// that no seed and no other class takes): January 58a, February 58b, March 58c, April 58d, May 58f, June 58g (and its correction),
/// July 58h; 58e makes no run. A second run of the class on the same stack stops at 58a's precondition: the months are taken, and a
/// rerun needs a fresh stack. Nothing stops or recreates a container.
/// </para>
/// <para>
/// <b>Isolation.</b> No acceptance seed gives an employee a bank account, so each sending scenario first gives a few employees of its own
/// a verified account by PLAN-55's two people (the others' orders stay held <c>NO-ACCOUNT</c>, as PLAN-58 wants). The class leaves no
/// active hold, no open critical alert, no shared or unverified account, no pending four-eyes request and no free input with an amount
/// in agency A; it asserts on its own runs, batches and employees by id.
/// </para>
/// </summary>
[RequiredAcceptance]
[TestCaseOrderer(ScenarioNameOrderer.TypeName, ScenarioNameOrderer.AssemblyName)]
public class AcPay58DisbursementGovernanceTests
{
    private const string Ac = "AC-PAY-58";
    private const string Processor = "payroll-processor";
    private const string Approver = "payroll-approver";
    private const string Approver2 = "a-payroll-approver-2";
    private const string Poster = "a-payroll-poster";
    private const string Auditor = "auditor";
    private const string Auditor2 = "a-auditor-2";
    private const string Multirole = "a-payroll-multirole";
    private const string Payroll = "payroll_db";

    private const int UnderReview = 3;
    private const int Approved = 4;

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly DateOnly January = new(2030, 1, 1);
    private static readonly DateOnly February = new(2030, 2, 1);
    private static readonly DateOnly March = new(2030, 3, 1);
    private static readonly DateOnly April = new(2030, 4, 1);
    private static readonly DateOnly May = new(2030, 5, 1);
    private static readonly DateOnly June = new(2030, 6, 1);
    private static readonly DateOnly July = new(2030, 7, 1);

    private static Task<string> Db(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll, sql);

    private static Task<long> Count(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll, sql);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- the API, as one persona

    private sealed class Api(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
    {
        public static async Task<Api> OpenAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")),
                recorder, persona, tag);

        public static async Task<Api> AuditorAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollAuditClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")),
                recorder, persona, tag);

        public HttpClient Http => http;

        public EvidenceRecorder Recorder => recorder;

        public async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(string step, HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json = default;
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                json = JsonSerializer.Deserialize<JsonElement>(text);
            object captured = json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", method.Method, "/" + path, (int)response.StatusCode, captured);
            return (response.StatusCode, json, text);
        }

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(string step, string path, object? body = null) =>
            SendAsync(step, HttpMethod.Post, path, body ?? new { });

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string step, string path) =>
            SendAsync(step, HttpMethod.Get, path);

        public void Dispose() => http.Dispose();
    }

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static void Ok(HttpStatusCode status, JsonElement body, string what, HttpStatusCode expected = HttpStatusCode.OK) =>
        Assert.True(status == expected, $"{what} answered {(int)status} (expected {(int)expected}): {Text(body, "error")}");

    // ---------------------------------------------------------------- payroll_db

    private static Task<string> RunsCoveringAsync(DateOnly from, DateOnly lastMonth) => Db(
        $"SELECT COALESCE(string_agg(r.\"RunType\"::text || ':' || r.\"Status\"::text, ','), '') FROM \"PayrollRuns\" r WHERE r.\"TenantId\"='{TenantA}' " +
        $"AND r.\"PeriodStart\" <= '{Day(lastMonth.AddMonths(1).AddDays(-1))}' AND r.\"PeriodEnd\" >= '{Day(from)}';");

    private static Task<string> BatchOfAsync(string run) =>
        Db($"SELECT COALESCE((SELECT \"Id\"::text FROM \"PaymentBatches\" WHERE \"PayrollRunId\"='{run}' AND \"BatchKind\"='Disbursement'), '');");

    /// <summary>The batch's orders by employee: «status|hold reason».</summary>
    private static async Task<Dictionary<string, string>> OrdersAsync(string batch) =>
        (await Db($"SELECT COALESCE(string_agg(\"EmployeeId\"::text || '=' || \"Status\" || '|' || COALESCE(\"HoldReason\", ''), ','), '') FROM \"PaymentOrders\" WHERE \"PaymentBatchId\"='{batch}';"))
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split('=')).ToDictionary(r => r[0], r => r[1]);

    private static Task<long> FilesAsync(string batch) => Count($"SELECT count(*) FROM \"PaymentBatchFiles\" WHERE \"PaymentBatchId\"='{batch}';");

    /// <summary>
    /// What a class must not add in agency A (P-13's isolation): active holds, open critical alerts, shared accounts, unverified accounts,
    /// pending four-eyes requests, free inputs with an amount. Each scenario leaves these counts as it found them (another class of the
    /// group may have left some of its own, e.g. AC-PAY-55's holds on its unposted runs).
    /// </summary>
    private static Task<string> LeftoversAsync() => Db(
        $"SELECT (SELECT count(*) FROM \"PaymentHolds\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"='Active')::text || '|' || " +
        $"(SELECT count(*) FROM \"FraudAlerts\" WHERE \"TenantId\"='{TenantA}' AND \"Severity\"='critical' AND \"Status\" IN ('Open','UnderReview'))::text || '|' || " +
        $"(SELECT count(*) FROM (SELECT \"AccountNumber\" FROM \"EmployeeBankAccounts\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"=1 GROUP BY \"AccountNumber\" HAVING count(DISTINCT \"EmployeeId\") > 1) x)::text || '|' || " +
        $"(SELECT count(*) FROM \"EmployeeBankAccounts\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"=0)::text || '|' || " +
        $"(SELECT count(*) FROM \"PayrollDualControlRequests\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"=0)::text || '|' || " +
        $"(SELECT count(*) FROM \"PayrollAdjustments\" WHERE \"TenantId\"='{TenantA}' AND \"PayrollRunId\" IS NULL AND \"Amount\" <> 0)::text;");


    // ---------------------------------------------------------------- the cycle up to a posted batch

    private static async Task<string> CreateAndCalculateAsync(Api processor, string step, DateOnly month)
    {
        var end = month.AddMonths(1).AddDays(-1);
        var (created, createdBody, _) = await processor.PostAsync($"{step}-create-run", "api/payroll/runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        });
        Assert.True(created is HttpStatusCode.OK or HttpStatusCode.Created, $"{step}: creating the run of {Day(month)[..7]} answered {(int)created}: {Text(createdBody, "error")}");
        var run = createdBody.GetProperty("id").GetGuid().ToString("D");
        // A class that ran earlier may have added an employment record after the seed: the seed's classification is applied to it first.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(TenantA);
        var (calculated, calculatedBody, _) = await processor.PostAsync($"{step}-calculate", $"api/payroll/runs/{run}/calculate");
        Ok(calculated, calculatedBody, $"{step}: calculating the run of {Day(month)[..7]}");
        return run;
    }

    /// <summary>Submits, approves (twice above the threshold), reviews by two auditors and posts. Returns the batch the posting made.</summary>
    private static async Task<(string Batch, object Evidence)> ApproveReviewAndPostAsync(Api processor, Api approver, Api poster, string step, string run)
    {
        var (submitted, submittedBody, _) = await processor.PostAsync($"{step}-submit", $"api/payroll/runs/{run}/submit-for-review");
        Ok(submitted, submittedBody, $"{step}: the submission");
        var (approved, approvedBody, _) = await approver.PostAsync($"{step}-approve", $"api/payroll/runs/{run}/approve");
        Ok(approved, approvedBody, $"{step}: the approval");
        var approvals = await AcceptancePayrollReview.CompleteApprovalAsync(approver.Recorder, run, approvedBody, step);
        var review = await AcceptancePayrollReview.ReviewAsync(approver.Recorder, run, step);
        var (posted, postedBody, _) = await poster.PostAsync($"{step}-post", $"api/payroll/runs/{run}/post");
        Ok(posted, postedBody, $"{step}: the posting");
        var batch = await BatchOfAsync(run);
        Assert.True(batch.Length > 0, $"{step}: the posting made no batch");
        return (batch, new { approvals, review });
    }

    private static async Task<string[]> GiveAccountsAsync(Api processor, Api approver, string step, int count, string mark, bool lowest = false)
    {
        var employees = await AcceptancePayrollAccounts.PickWithoutAccountAsync(processor.Recorder, step, count, lowest: lowest);
        foreach (var employee in employees)
            await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(processor.Recorder, processor.Http, approver.Http, employee,
                AcceptancePayrollAccounts.Iban(employee, mark), $"كتاب المصرف — {Ac}{step[2..]}", step);
        return employees;
    }

    private static async Task<(Api Processor, Api Approver, Api Approver2, Api Poster)> PeopleAsync(EvidenceRecorder recorder, string tag) =>
        (await Api.OpenAsync(recorder, Processor, tag), await Api.OpenAsync(recorder, Approver, tag),
         await Api.OpenAsync(recorder, Approver2, tag), await Api.OpenAsync(recorder, Poster, tag));

    /// <summary>A hold's release: asked by payroll-approver, decided by a-payroll-approver-2 from his own session.</summary>
    private static async Task ReleaseAsync(Api approver, Api approver2, string step, string hold)
    {
        var (asked, askedBody, _) = await approver.PostAsync($"{step}-release-request-{hold[..8]}", $"api/payroll/payment-holds/{hold}/release", new { releaseReason = $"راجع المعتمدان الحجز — {step}" });
        Ok(asked, askedBody, $"{step}: the release request", HttpStatusCode.Accepted);
        var request = askedBody.GetProperty("requestId").GetGuid().ToString("D");
        var (decided, decidedBody, _) = await approver2.PostAsync($"{step}-release-decide-{hold[..8]}", $"api/payroll/dual-control/{request}/decide", new { approve = true, notes = "موافق" });
        Ok(decided, decidedBody, $"{step}: the release decision");
    }

    // ---------------------------------------------------------------- 58a

    [SkippableFact]
    public async Task Ac58a_a_funding_decision_after_the_posting_funds_60_percent_and_the_posters_file_carries_the_funded_orders()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58a",
            "AC-PAY-58a: the run is posted (its batch and orders made by the posting); payroll-approver funds 60% of the Pending batch; a-payroll-poster sends it: the file matches the sent orders and the rest is arrears. Negative: a funding decision on an approved run → 400; partial-disburse → 404; the processor sending → 403.",
            Processor, Approver);
        s.Steps("""
            1. Precondition: agency A has no run of 2030 (the months of this class); a rerun on the same stack stops here.
            2. Two agency-A employees get a verified account (payroll-processor registers, payroll-approver verifies).
            3. payroll-processor creates and calculates the Regular run of January 2030 and submits it; payroll-approver approves it
               (a-payroll-approver-2 too: the agency's run is above the threshold).
            4. Negative: a funding decision on the approved run → 400; POST runs/{id}/partial-disburse → 404.
            5. acc-auditor opens the review, acc-a-auditor-2 closes it; a-payroll-poster posts: one Pending batch of the run's total.
            6. payroll-approver funds 60% of the batch. Negative: payroll-processor sends it → 403.
            7. a-payroll-poster sends it: one Initial file whose ControlTotal is the sent orders' sum; the orders' sum and the arrears are
               the batch's total; the funded orders are 60% of it.
            """);
        var covering = await RunsCoveringAsync(January, new DateOnly(2030, 12, 1));
        AcceptanceGate.RequireData(s.Recorder, covering.Length == 0,
            $"للجهة A تشغيل رواتب في 2030 ({covering}): الصنف يحتاج مكدساً جديداً",
            $"Agency A already has a payroll run in 2030 ({covering}): this class needs a fresh stack.");
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58a", explicitDomainSnapshot = true, runsOf2030 = covering, leftovers = leftBefore });

        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58a");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        // The lowest ids: a partial funding pays the orders in the order of their employee ids.
        var paid = await GiveAccountsAsync(processor, approver, "58a", 2, "58A1", lowest: true);
        var run = await CreateAndCalculateAsync(processor, "58a", January);
        var (submitted, submittedBody, _) = await processor.PostAsync("submit", $"api/payroll/runs/{run}/submit-for-review");
        Ok(submitted, submittedBody, "58a: the submission");
        var (approved, approvedBody, _) = await approver.PostAsync("approve", $"api/payroll/runs/{run}/approve");
        Ok(approved, approvedBody, "58a: the approval");
        var approvals = await AcceptancePayrollReview.CompleteApprovalAsync(s.Recorder, run, approvedBody, "58a");

        var (early, earlyBody, _) = await approver.PostAsync("funding-before-the-posting", $"api/payroll/runs/{run}/funding-decisions", new { fundingRatio = 0.60m });
        var (removed, _, _) = await approver.PostAsync("partial-disburse-removed", $"api/payroll/runs/{run}/partial-disburse", new { fundingDecisionId = Guid.NewGuid() });
        var review = await AcceptancePayrollReview.ReviewAsync(s.Recorder, run, "58a");
        var (posted, postedBody, _) = await poster.PostAsync("post", $"api/payroll/runs/{run}/post");
        Ok(posted, postedBody, "58a: the posting");
        var batch = await BatchOfAsync(run);
        var total = decimal.Parse(await Db($"SELECT \"TotalAmount\"::text FROM \"PaymentBatches\" WHERE \"Id\"='{batch}';"), CultureInfo.InvariantCulture);

        var (funded, fundedBody, _) = await approver.PostAsync("fund-60", $"api/payroll/payment-batches/{batch}/funding-decisions", new { fundingRatio = 0.60m });
        Ok(funded, fundedBody, "58a: the funding decision");
        var (byProcessor, _, _) = await processor.PostAsync("process-by-processor", $"api/payroll/payment-batches/{batch}/process");
        var (sent, sentBody, _) = await poster.PostAsync("process", $"api/payroll/payment-batches/{batch}/process");
        Ok(sent, sentBody, "58a: the sending");
        var (view, viewBody, _) = await poster.GetAsync("batch", $"api/payroll/payment-batches/{batch}");
        Ok(view, viewBody, "58a: the batch's page");
        var file = Assert.Single(viewBody.GetProperty("files").EnumerateArray().ToList());
        var orders = viewBody.GetProperty("orders").EnumerateArray().ToList();
        var submittedSum = orders.Where(o => o.GetProperty("status").GetString() == "Submitted").Sum(o => o.GetProperty("amount").GetDecimal());
        var fundedSum = orders.Where(o => o.GetProperty("status").GetString() != "Cancelled").Sum(o => o.GetProperty("amount").GetDecimal());
        var arrears = viewBody.GetProperty("arrearsAmount").GetDecimal();
        var controlTotal = file.GetProperty("controlTotal").GetDecimal();
        var submittedCount = orders.Where(o => o.GetProperty("status").GetString() == "Submitted").Sum(o => o.GetProperty("count").GetInt32());
        var paidOrders = await OrdersAsync(batch);
        var leftovers = await LeftoversAsync();

        s.Checks(new
        {
            run,
            batch,
            approvals,
            review,
            employeesWithAnAccount = paid,
            paidOrders = paid.Select(e => paidOrders[e]),
            submittedCount,
            fundingBeforeThePosting = (int)early,
            fundingBeforeThePostingError = Text(earlyBody, "error"),
            partialDisburse = (int)removed,
            processByProcessor = (int)byProcessor,
            total,
            fundedSum,
            arrears,
            submittedSum,
            controlTotal,
            controlCount = file.GetProperty("controlCount").GetInt32(),
            kind = file.GetProperty("kind").GetString(),
            leftovers,
        });
        Assert.Equal(HttpStatusCode.BadRequest, early);
        Assert.Equal(HttpStatusCode.NotFound, removed);
        Assert.Equal(HttpStatusCode.Forbidden, byProcessor);
        Assert.Equal("Initial", file.GetProperty("kind").GetString());
        Assert.Equal(submittedSum, controlTotal);
        // The file carries every sent order: this scenario's two employees, and any employee another class gave an account to.
        Assert.Equal(submittedCount, file.GetProperty("controlCount").GetInt32());
        Assert.All(paid, e => Assert.StartsWith("Submitted|", paidOrders[e]));
        Assert.Equal(total, fundedSum + arrears);
        Assert.InRange(fundedSum, total * 0.60m - 1m, total * 0.60m + 1m);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 58b

    [SkippableFact]
    public async Task Ac58b_a_held_employee_holds_back_nobody_and_is_sent_alone_by_a_supplementary_file_after_a_four_eyes_release()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58b",
            "AC-PAY-58b: a batch with a held employee: the others are sent and the held one stays Held outside the file; payroll-approver asks for the release and a-payroll-approver-2 approves it from his session; submit-held sends him alone by a supplementary file. Negative: an approver in the body is ignored and the requester does not decide (400); a second submit-held makes no second file.",
            Processor, Approver);
        s.Steps("""
            1. Three agency-A employees get a verified account. The Regular run of February 2030 is calculated, approved, reviewed and
               posted; its batch has their three orders.
            2. payroll-approver retires the first employee's pinned account (PLAN-55): he is held in that run.
            3. a-payroll-poster sends the batch: the other two are in the Initial file; the first is Held, outside it.
            4. payroll-approver asks for the hold's release (an approver named in the body is ignored); deciding his own request → 400.
            5. a-payroll-approver-2 approves it from his own session. a-payroll-poster sends the held orders: a HeldRelease file with the
               first employee alone. Negative: a second submit-held → refused, and still two files.
            6. The first employee gets a new verified account (nothing is left unverified or shared).
            """);
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58b", explicitDomainSnapshot = true, leftovers = leftBefore });
        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58b");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        var paid = await GiveAccountsAsync(processor, approver, "58b", 3, "58B1");
        var held = paid[0];
        var run = await CreateAndCalculateAsync(processor, "58b", February);
        var (batch, cycle) = await ApproveReviewAndPostAsync(processor, approver, poster, "58b", run);

        var account = await Db($"SELECT \"Id\"::text FROM \"EmployeeBankAccounts\" WHERE \"EmployeeId\"='{held}' AND \"Status\"=1;");
        var (retired, retiredBody, _) = await approver.PostAsync("retire", $"api/payroll/employees/{held}/bank-accounts/{account}/retire", new { reason = "أغلق الموظف حسابه — AC-PAY-58b" });
        Ok(retired, retiredBody, "58b: the retirement of the pinned account");
        var (sent, sentBody, _) = await poster.PostAsync("process", $"api/payroll/payment-batches/{batch}/process");
        Ok(sent, sentBody, "58b: the sending");
        var afterSending = await OrdersAsync(batch);
        var hold = await Db($"SELECT \"Id\"::text FROM \"PaymentHolds\" WHERE \"EmployeeId\"='{held}' AND \"PayrollRunId\"='{run}' AND \"Status\"='Active';");

        var (asked, askedBody, _) = await approver.PostAsync("release-request", $"api/payroll/payment-holds/{hold}/release",
            new { releaseReason = "قرر المعتمدان الصرف إلى الحساب المثبَّت", approverUserId = Guid.NewGuid() });
        Ok(asked, askedBody, "58b: the release request", HttpStatusCode.Accepted);
        var request = askedBody.GetProperty("requestId").GetGuid().ToString("D");
        var holdAfterAsking = await Db($"SELECT \"Status\" FROM \"PaymentHolds\" WHERE \"Id\"='{hold}';");
        var (own, ownBody, _) = await approver.PostAsync("decide-own", $"api/payroll/dual-control/{request}/decide", new { approve = true });
        var (decided, decidedBody, _) = await approver2.PostAsync("decide", $"api/payroll/dual-control/{request}/decide", new { approve = true, notes = "موافق" });
        Ok(decided, decidedBody, "58b: the release decision");
        var (supplementary, supplementaryBody, _) = await poster.PostAsync("submit-held", $"api/payroll/payment-batches/{batch}/submit-held");
        Ok(supplementary, supplementaryBody, "58b: the supplementary sending");
        var (again, againBody, _) = await poster.PostAsync("submit-held-again", $"api/payroll/payment-batches/{batch}/submit-held");
        var afterRelease = await OrdersAsync(batch);
        var files = await Db($"SELECT string_agg(\"Sequence\"::text || ':' || \"Kind\"::text || ':' || \"ControlCount\"::text, ',' ORDER BY \"Sequence\") FROM \"PaymentBatchFiles\" WHERE \"PaymentBatchId\"='{batch}';");
        var requestRow = await Db($"SELECT \"Status\"::text || '|' || (\"RequestedBy\" <> \"DecidedBy\")::text FROM \"PayrollDualControlRequests\" WHERE \"Id\"='{request}';");

        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(s.Recorder, processor.Http, approver.Http, held,
            AcceptancePayrollAccounts.Iban(held, "58B2"), "كتاب المصرف — AC-PAY-58b/2", "58b-new-account");
        var leftovers = await LeftoversAsync();
        s.Checks(new
        {
            run,
            batch,
            cycle,
            held,
            afterSending,
            holdAfterAsking,
            ownDecision = (int)own,
            ownError = Text(ownBody, "error"),
            afterRelease,
            files,
            requestRow,
            secondSubmitHeld = (int)again,
            secondSubmitHeldError = Text(againBody, "error"),
            leftovers,
            fileKinds = "0 Initial, 1 HeldRelease",
        });
        Assert.StartsWith("Held|", afterSending[held]);
        Assert.All(paid.Skip(1), e => Assert.StartsWith("Submitted|", afterSending[e]));
        Assert.Equal("Active", holdAfterAsking);
        Assert.Equal(HttpStatusCode.BadRequest, own);
        Assert.StartsWith("Submitted|", afterRelease[held]);
        // The Initial file carries every order sent then (the two, and any employee another class gave an account to); the HeldRelease
        // file the held employee alone.
        Assert.Equal($"1:0:{afterSending.Values.Count(v => v.StartsWith("Submitted|", StringComparison.Ordinal))},2:1:1", files);
        Assert.True(again is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, $"58b: a second submit-held answered {(int)again}");
        Assert.Equal("1|true", requestRow);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 58c

    [SkippableFact]
    public async Task Ac58c_above_the_threshold_the_first_approval_leaves_the_run_under_review_and_a_second_person_approves_it()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58c",
            "AC-PAY-58c: a run above the threshold: a first approval → UnderReview «اعتماد 1 من 2»; a second by a-payroll-approver-2 → Approved. Negative: the first approver approving again → 400.",
            Processor, Approver);
        s.Steps("""
            1. payroll-processor creates and calculates the Regular run of March 2030 and submits it. Its total is above the agency's
               threshold (Payroll:Approval:DualApprovalThreshold, 50,000,000 on the prod-like stack).
            2. payroll-approver approves it: 200, «اعتماد 1 من 2», the run stays UnderReview.
            3. Negative: payroll-approver approves it again → 400, and it is still UnderReview.
            4. a-payroll-approver-2 approves it from his own session: Approved, with two approvals of the run's version by two people.
            5. The run is not posted (no batch): payroll-approver cancels it where the state machine allows it.
            """);
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58c", explicitDomainSnapshot = true, leftovers = leftBefore });
        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58c");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        var run = await CreateAndCalculateAsync(processor, "58c", March);
        var (submitted, submittedBody, _) = await processor.PostAsync("submit", $"api/payroll/runs/{run}/submit-for-review");
        Ok(submitted, submittedBody, "58c: the submission");
        var total = await Db($"SELECT \"TotalNetPay\"::text FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");

        var (first, firstBody, _) = await approver.PostAsync("approve-1", $"api/payroll/runs/{run}/approve");
        Ok(first, firstBody, "58c: the first approval");
        AcceptanceGate.RequireData(s.Recorder, firstBody.GetProperty("approvalsRequired").GetInt32() == 2,
            $"58c: صافي تشغيل آذار 2030 ({total}) لا يتجاوز عتبة الاعتماد المزدوج: لا يُثبت الاعتماد الثاني",
            $"58c: the March 2030 run's net ({total}) is not above the dual-approval threshold: the second approval cannot be shown.");
        var afterFirst = (int)await Count($"SELECT \"Status\" FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");
        var (again, againBody, _) = await approver.PostAsync("approve-again", $"api/payroll/runs/{run}/approve");
        var afterAgain = (int)await Count($"SELECT \"Status\" FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");
        var (second, secondBody, _) = await approver2.PostAsync("approve-2", $"api/payroll/runs/{run}/approve");
        Ok(second, secondBody, "58c: the second approval");
        var afterSecond = (int)await Count($"SELECT \"Status\" FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");
        var approvals = await Db($"SELECT count(DISTINCT \"ApproverId\")::text || '|' || count(*)::text FROM \"PayrollRunApprovals\" WHERE \"PayrollRunId\"='{run}' AND \"Decision\"='Approved';");
        // Not posted: cancelled where the state machine allows it, else left approved (it holds only its own inputs; nothing leaves).
        var (cancelled, _, _) = await approver.PostAsync("cancel", $"api/payroll/runs/{run}/cancel", new { reason = "مثال الاعتماد المزدوج — لا يُرحَّل (AC-PAY-58c)" });
        var leftovers = await LeftoversAsync();

        s.Checks(new
        {
            run,
            total,
            firstAnswer = firstBody,
            afterFirst,
            approveAgain = (int)again,
            approveAgainError = Text(againBody, "error"),
            afterAgain,
            secondAnswer = secondBody,
            afterSecond,
            approvals,
            cancelled = (int)cancelled,
            leftovers,
        });
        Assert.Equal("اعتماد 1 من 2", Text(firstBody, "progress"));
        Assert.Equal(UnderReview, afterFirst);
        Assert.Equal(HttpStatusCode.BadRequest, again);
        Assert.Equal(UnderReview, afterAgain);
        Assert.Equal(Approved, afterSecond);
        Assert.Equal("2|2", approvals);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 58d

    [SkippableFact]
    public async Task Ac58d_two_auditors_review_the_run_through_the_gateway_and_the_poster_posts_it()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58d",
            "AC-PAY-58d: acc-auditor opens the review record and acc-a-auditor-2 closes it, through the gateway; then the poster posts. Negative: the approver closing the record → 403; the auditor posting to PayrollRuns/Approve → 403; the opener closing → 400; a posting before the review → 400.",
            Processor, Approver);
        s.Steps("""
            1. The Regular run of April 2030 is calculated, submitted and approved (by both approvers).
            2. Negative: a-payroll-poster posts it before any review → 400, the run stays Approved.
            3. acc-auditor opens the review record (POST api/payroll/runs/{id}/audit-records through the gateway).
            4. Negative: payroll-approver closes it → 403; acc-auditor, its opener, closes it → 400; acc-auditor posts the screen's
               approval form (ui/payroll/PayrollRuns/Approve/{id}) → 403.
            5. acc-a-auditor-2 closes it; a-payroll-poster posts the run. Its batch stays Pending: nothing of it is sent.
            """);
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58d", explicitDomainSnapshot = true, leftovers = leftBefore });
        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58d");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        using var auditor = await Api.AuditorAsync(s.Recorder, Auditor, "58d");
        using var auditor2 = await Api.AuditorAsync(s.Recorder, Auditor2, "58d");
        var run = await CreateAndCalculateAsync(processor, "58d", April);
        var (submitted, submittedBody, _) = await processor.PostAsync("submit", $"api/payroll/runs/{run}/submit-for-review");
        Ok(submitted, submittedBody, "58d: the submission");
        var (approved, approvedBody, _) = await approver.PostAsync("approve", $"api/payroll/runs/{run}/approve");
        Ok(approved, approvedBody, "58d: the approval");
        var approvals = await AcceptancePayrollReview.CompleteApprovalAsync(s.Recorder, run, approvedBody, "58d");

        var (early, earlyBody, _) = await poster.PostAsync("post-before-review", $"api/payroll/runs/{run}/post");
        var (opened, openedBody, _) = await auditor.PostAsync("open", $"api/payroll/runs/{run}/audit-records", new { findings = "مطابقة الإجماليات — AC-PAY-58d" });
        Ok(opened, openedBody, "58d: the review's opening");
        var record = openedBody.GetProperty("recordId").GetGuid().ToString("D");
        var (byApprover, _, _) = await approver.PostAsync("close-by-approver", $"api/payroll/audit-records/{record}/close");
        var (byOpener, byOpenerBody, _) = await auditor.PostAsync("close-by-opener", $"api/payroll/audit-records/{record}/close");
        // The auditor opens no payroll screen (the gateway lets him reach the review's paths only): his POST to the screen's approval is
        // refused before anything runs.
        using var screen = await auditor.Http.PostAsync($"ui/payroll/PayrollRuns/Approve/{run}", new FormUrlEncodedContent(F()));
        var screenStatus = screen.StatusCode;
        s.Recorder.CaptureHttp("58d-auditor-approves-on-the-screen", "POST", $"/ui/payroll/PayrollRuns/Approve/{run}", (int)screenStatus);
        var (closed, closedBody, _) = await auditor2.PostAsync("close", $"api/payroll/audit-records/{record}/close");
        Ok(closed, closedBody, "58d: the review's closing");
        var (posted, postedBody, _) = await poster.PostAsync("post", $"api/payroll/runs/{run}/post");
        Ok(posted, postedBody, "58d: the posting");
        var recordRow = await Db($"SELECT \"Status\"::text || '|' || (\"AuditorId\" <> \"ClosedBy\")::text FROM \"PayrollAuditRecords\" WHERE \"Id\"='{record}';");
        var leftovers = await LeftoversAsync();

        s.Checks(new
        {
            run,
            approvals,
            postBeforeReview = (int)early,
            postBeforeReviewError = Text(earlyBody, "error"),
            closeByApprover = (int)byApprover,
            closeByOpener = (int)byOpener,
            closeByOpenerError = Text(byOpenerBody, "error"),
            auditorApprovesOnTheScreen = (int)screenStatus,
            recordRow,
            posted = (int)posted,
            batch = await BatchOfAsync(run),
            leftovers,
        });
        Assert.Equal(HttpStatusCode.BadRequest, early);
        Assert.Equal(HttpStatusCode.Forbidden, byApprover);
        Assert.Equal(HttpStatusCode.BadRequest, byOpener);
        Assert.Equal(HttpStatusCode.Forbidden, screenStatus);
        Assert.Equal("1|true", recordRow);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    private const string AdjustmentsUi = "ui/payroll/PayrollAdjustments";

    /// <summary>
    /// An adjustment aimed at a posted run's correction, entered on the screen by the processor and approved on the screen by the
    /// approver (a manual input counts only after a second person approved it), as AC-PAY-56f enters one.
    /// </summary>
    private static async Task<string> ApprovedTargetedAdjustmentAsync(RecruitmentSession s, string step, string employee, decimal amount, string reason, string targetRun)
    {
        var (entered, enteredBody) = await s[Processor].PostWithStatusAsync($"{step}-enter", $"{AdjustmentsUi}/Create", $"{AdjustmentsUi}/Create", F(
            ("EmployeeId", employee),
            ("Amount", amount.ToString("0.00", CultureInfo.InvariantCulture)),
            ("AdjustmentReason", reason),
            ("TargetSourceRunId", targetRun)));
        var adjustment = await Db(
            $"SELECT COALESCE(max(\"Id\"::text), '') FROM \"PayrollAdjustments\" WHERE \"EmployeeId\"='{employee}' AND \"AdjustmentReason\"='{reason}' AND \"PayrollRunId\" IS NULL;");
        Assert.True(adjustment.Length > 0, $"{step}: the adjustment was not entered ({(int)entered}): {(enteredBody.Length > 400 ? enteredBody[..400] : enteredBody)}");
        var (approved, approvedBody) = await s[Approver].PostWithStatusAsync($"{step}-approve", $"{AdjustmentsUi}/Approve/{adjustment}", AdjustmentsUi, F());
        var state = await Db($"SELECT \"ApprovalStatus\" || '|' || COALESCE(\"TargetSourceRunId\"::text, '') FROM \"PayrollAdjustments\" WHERE \"Id\"='{adjustment}';");
        Assert.True(state == $"Approved|{targetRun}", $"{step}: the adjustment is not approved as entered ({(int)approved}): {state}");
        return adjustment;
    }

    // ---------------------------------------------------------------- 58e

    [SkippableFact]
    public async Task Ac58e_a_holder_of_conflicting_payroll_roles_is_refused_every_financial_act_with_the_conflict_named_and_audited()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58e",
            "AC-PAY-58e: a-payroll-multirole (processor, approver and poster in one token) processes, approves or posts → 403 with the conflict's message, and a RoleConflict line in Payroll's security audit.",
            Processor);
        s.Steps("""
            1. a-payroll-multirole (the one persona the realm import lets hold a conflicting pair: sodConflictFixture) asks for
               Payroll's antiforgery token (the module's read) → 200.
            2. He calculates a run, approves one, posts one and sends a batch (ids that need not exist: the refusal comes first) → 403
               each, with a message that names the conflicting roles.
            3. Payroll's security audit has a RoleConflict line for each, with the policy and the pair (never a token).
            """);
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("payroll-web");
        async Task<long> ConflictLinesAsync()
        {
            var (_, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "exec", container, "sh", "-c",
                "cat /tmp/hrm-security-audit/security-*.jsonl 2>/dev/null | grep -c '\"RoleConflict\"' || true");
            return long.TryParse(output.Trim(), out var n) ? n : 0;
        }

        var before = await ConflictLinesAsync();
        s.Recorder.WriteBefore(new { scenario = "58e", roleConflictLines = before });
        using var multirole = await Api.OpenAsync(s.Recorder, Multirole, "58e");
        var target = Guid.NewGuid().ToString("D");
        var acts = new (string Step, string Path)[]
        {
            ("calculate", $"api/payroll/runs/{target}/calculate"),
            ("approve", $"api/payroll/runs/{target}/approve"),
            ("post", $"api/payroll/runs/{target}/post"),
            ("process", $"api/payroll/payment-batches/{target}/process"),
        };
        var answers = new List<object>();
        foreach (var (step, path) in acts)
        {
            var (status, body, _) = await multirole.PostAsync(step, path);
            answers.Add(new { step, status = (int)status, error = Text(body, "error") });
            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Contains("payroll-", Text(body, "error"), StringComparison.Ordinal);
        }

        var after = await ConflictLinesAsync();
        var (_, sample) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "exec", container, "sh", "-c",
            "cat /tmp/hrm-security-audit/security-*.jsonl 2>/dev/null | grep '\"RoleConflict\"' | tail -n 1");
        s.Checks(new { answers, roleConflictLinesBefore = before, roleConflictLinesAfter = after, lastLineHasNoBearer = !sample.Contains("Bearer", StringComparison.OrdinalIgnoreCase) });
        Assert.True(after - before >= acts.Length, $"58e: {after - before} RoleConflict line(s) for {acts.Length} refusals");
        Assert.DoesNotContain("Bearer", sample, StringComparison.OrdinalIgnoreCase);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 58f

    [SkippableFact]
    public async Task Ac58f_an_employee_with_no_verified_account_is_held_the_others_are_paid_and_his_verification_lifts_it()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58f",
            "AC-PAY-58f: an employee whose account is not verified: his order is Held NO-ACCOUNT and the others are paid; the verification of his account lifts the hold and submit-held sends him alone.",
            Processor, Approver);
        s.Steps("""
            1. Two agency-A employees get a verified account; a third has his account registered but not yet verified.
            2. The Regular run of May 2030 is calculated, approved, reviewed and posted.
            3. a-payroll-poster sends the batch: the two are in the Initial file; the third is Held NO-ACCOUNT.
            4. payroll-approver verifies the third's account: his held order is pinned to it (an EntityAuditLog line with ids only, no
               number) and its hold lifted, with no four-eyes request.
            5. a-payroll-poster sends the held orders: a HeldRelease file with the third alone.
            """);
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58f", explicitDomainSnapshot = true, leftovers = leftBefore });
        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58f");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        var paid = await GiveAccountsAsync(processor, approver, "58f", 2, "58F1");
        var pending = (await AcceptancePayrollAccounts.PickWithoutAccountAsync(s.Recorder, "58f", 1))[0];
        var (registered, registeredBody, _) = await processor.PostAsync("register-pending", $"api/payroll/employees/{pending}/bank-accounts", new
        {
            accountKind = "Iban",
            accountNumber = AcceptancePayrollAccounts.Iban(pending, "58F2"),
            bankNameAr = "المصرف التجاري السوري",
            holderNameAr = "موظف اختبار القبول",
        });
        Ok(registered, registeredBody, "58f: the registration", HttpStatusCode.Created);
        var account = registeredBody.GetProperty("accountId").GetGuid().ToString("D");

        var run = await CreateAndCalculateAsync(processor, "58f", May);
        var (batch, cycle) = await ApproveReviewAndPostAsync(processor, approver, poster, "58f", run);
        var (sent, sentBody, _) = await poster.PostAsync("process", $"api/payroll/payment-batches/{batch}/process");
        Ok(sent, sentBody, "58f: the sending");
        var afterSending = await OrdersAsync(batch);
        var (verified, verifiedBody, _) = await approver.PostAsync("verify", $"api/payroll/employees/{pending}/bank-accounts/{account}/verify", new { verificationReference = "كتاب المصرف — AC-PAY-58f" });
        Ok(verified, verifiedBody, "58f: the verification");
        var afterVerification = await OrdersAsync(batch);
        var repinned = await Db(
            "SELECT count(*)::text || '|' || COALESCE(bool_and(position('SY' in COALESCE(\"ChangedFields\", '')) = 0), true)::text FROM \"AuditLogs\" " +
            $"WHERE \"Action\"='RepinnedOnAccountVerification' AND \"EntityId\" IN (SELECT \"Id\" FROM \"PaymentOrders\" WHERE \"PaymentBatchId\"='{batch}' AND \"EmployeeId\"='{pending}');");
        var (supplementary, supplementaryBody, _) = await poster.PostAsync("submit-held", $"api/payroll/payment-batches/{batch}/submit-held");
        Ok(supplementary, supplementaryBody, "58f: the supplementary sending");
        var afterRelease = await OrdersAsync(batch);
        var files = await Db($"SELECT string_agg(\"Sequence\"::text || ':' || \"Kind\"::text || ':' || \"ControlCount\"::text, ',' ORDER BY \"Sequence\") FROM \"PaymentBatchFiles\" WHERE \"PaymentBatchId\"='{batch}';");
        var leftovers = await LeftoversAsync();

        s.Checks(new { run, batch, cycle, paid, pending, afterSending = afterSending[pending], afterVerification = afterVerification[pending], repinned, afterRelease = afterRelease[pending], files, leftovers });
        Assert.Equal("Held|NO-ACCOUNT", afterSending[pending]);
        Assert.All(paid, e => Assert.StartsWith("Submitted|", afterSending[e]));
        Assert.Equal("Held|", afterVerification[pending]);
        Assert.Equal("1|true", repinned);
        Assert.StartsWith("Submitted|", afterRelease[pending]);
        Assert.Equal($"1:0:{afterSending.Values.Count(v => v.StartsWith("Submitted|", StringComparison.Ordinal))},2:1:1", files);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 58g

    [SkippableFact]
    public async Task Ac58g_two_employees_paid_into_one_pinned_account_raise_FR_04_and_only_their_two_orders_are_held()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58g",
            "AC-PAY-58g: two employees paid into one pinned account → an FR-04 alert for each and their two orders held, the others sent. Negative: a correction for a real employee raises neither FR-01 nor FR-08.",
            Processor, Approver);
        s.Steps("""
            1. Two agency-A employees get the same verified account (PLAN-55's flow, twice), and a third his own.
            2. The Regular run of June 2030 is calculated, approved, reviewed and posted; a-payroll-poster sends it: an FR-04 alert for
               each of the two, their orders Held; the third in the Initial file.
            3. Negative: payroll-processor enters an adjustment for the third employee aimed at the run's correction, and payroll-approver
               approves it (on the screens); the correction is made and scanned: it raises neither FR-01 nor FR-08 for him (its line
               carries its source run). It stays calculated, unposted, holding its adjustment (no free input is left).
            4. The two holds are released by four eyes and their orders sent by a supplementary file; the second employee gets his own
               account (no shared account is left).
            """);
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58g", explicitDomainSnapshot = true, leftovers = leftBefore });
        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58g");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        var picked = await AcceptancePayrollAccounts.PickWithoutAccountAsync(s.Recorder, "58g", 3);
        var (first, second, third) = (picked[0], picked[1], picked[2]);
        var shared = AcceptancePayrollAccounts.Iban(first, "58G1");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(s.Recorder, processor.Http, approver.Http, first, shared, "كتاب المصرف — AC-PAY-58g/1", "58g");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(s.Recorder, processor.Http, approver.Http, second, shared, "كتاب المصرف — AC-PAY-58g/2", "58g");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(s.Recorder, processor.Http, approver.Http, third, AcceptancePayrollAccounts.Iban(third, "58G2"), "كتاب المصرف — AC-PAY-58g/3", "58g");

        var run = await CreateAndCalculateAsync(processor, "58g", June);
        var (batch, cycle) = await ApproveReviewAndPostAsync(processor, approver, poster, "58g", run);
        var (sent, sentBody, _) = await poster.PostAsync("process", $"api/payroll/payment-batches/{batch}/process");
        Ok(sent, sentBody, "58g: the sending");
        var afterSending = await OrdersAsync(batch);
        var fr04 = await Db($"SELECT COALESCE(string_agg(\"EmployeeId\"::text, ',' ORDER BY \"EmployeeId\"), '') FROM \"FraudAlerts\" WHERE \"PayrollRunId\"='{run}' AND \"RuleId\"='FR-04';");
        var holds = (await Db($"SELECT COALESCE(string_agg(\"Id\"::text, ','), '') FROM \"PaymentHolds\" WHERE \"PayrollRunId\"='{run}' AND \"Status\"='Active';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries);

        // The negative: a correction of the run for the third employee, from an adjustment entered and approved on the screens.
        var adjustment = await ApprovedTargetedAdjustmentAsync(s, "58g-adjustment", third, 1500m, "فرق بدل حزيران — AC-PAY-58g", run);
        var (corrected, correctedBody, _) = await processor.PostAsync("correction", $"api/payroll/runs/{run}/corrections");
        Ok(corrected, correctedBody, "58g: the correction");
        var correction = correctedBody.GetProperty("id").GetGuid().ToString("D");
        var (scanned, scannedBody, _) = await processor.PostAsync("correction-scan", $"api/payroll/runs/{correction}/fraud-scan");
        Ok(scanned, scannedBody, "58g: the correction's fraud scan");
        var correctionAlerts = await Db($"SELECT COALESCE(string_agg(\"RuleId\", ',' ORDER BY \"RuleId\"), '') FROM \"FraudAlerts\" WHERE \"PayrollRunId\"='{correction}' AND \"EmployeeId\"='{third}';");
        var adjustmentRun = await Db($"SELECT COALESCE(\"PayrollRunId\"::text, 'free') FROM \"PayrollAdjustments\" WHERE \"Id\"='{adjustment}';");

        foreach (var hold in holds)
            await ReleaseAsync(approver, approver2, "58g", hold);
        var (supplementary, supplementaryBody, _) = await poster.PostAsync("submit-held", $"api/payroll/payment-batches/{batch}/submit-held");
        Ok(supplementary, supplementaryBody, "58g: the supplementary sending");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(s.Recorder, processor.Http, approver.Http, second, AcceptancePayrollAccounts.Iban(second, "58G3"), "كتاب المصرف — AC-PAY-58g/4", "58g-own-account");
        var afterRelease = await OrdersAsync(batch);
        var leftovers = await LeftoversAsync();

        s.Checks(new
        {
            run,
            batch,
            cycle,
            first,
            second,
            third,
            afterSending = new[] { afterSending[first], afterSending[second], afterSending[third] },
            fr04,
            holds = holds.Length,
            correction,
            correctionAlerts,
            adjustment,
            adjustmentRun,
            afterRelease = new[] { afterRelease[first], afterRelease[second] },
            leftovers,
        });
        Assert.Equal(string.Join(',', new[] { first, second }.Order(StringComparer.Ordinal)), fr04);
        Assert.StartsWith("Held|", afterSending[first]);
        Assert.StartsWith("Held|", afterSending[second]);
        Assert.StartsWith("Submitted|", afterSending[third]);
        Assert.DoesNotContain("FR-01", correctionAlerts, StringComparison.Ordinal);
        Assert.DoesNotContain("FR-08", correctionAlerts, StringComparison.Ordinal);
        Assert.Equal(correction, adjustmentRun);
        Assert.StartsWith("Submitted|", afterRelease[first]);
        Assert.StartsWith("Submitted|", afterRelease[second]);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 58h

    [SkippableFact]
    public async Task Ac58h_the_channels_acknowledgement_and_a_settlement_decided_by_two_people_bring_the_batch_to_Settled()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "58h",
            "AC-PAY-58h: a-payroll-poster records the channel's ack, then asks for the settlement with the statement's reference; payroll-approver approves → the batch and its orders Settled. Negative: the requester approving his own request → 400; a settled amount that does not match the orders → 400 and the batch as it was.",
            Processor, Approver);
        s.Steps("""
            1. Two agency-A employees get a verified account. The Regular run of July 2030 is calculated, approved, reviewed, posted and
               sent by a-payroll-poster (the others' orders stay held NO-ACCOUNT).
            2. a-payroll-poster records the channel's acknowledgement with its reference: Acked.
            3. Negative: he asks for the settlement with an amount that is not the sent orders' sum → 400, the batch as it was.
            4. He asks for it with the statement's reference and the sent orders' sum; deciding his own request → 400 (403: he does not
               hold the approval policy).
            5. payroll-approver approves it: the batch is Settled, and its sent orders.
            """);
        var leftBefore = await LeftoversAsync();
        s.Recorder.WriteBefore(new { scenario = "58h", explicitDomainSnapshot = true, leftovers = leftBefore });
        var (processor, approver, approver2, poster) = await PeopleAsync(s.Recorder, "58h");
        using var _p = processor; using var _a = approver; using var _a2 = approver2; using var _po = poster;
        var paid = await GiveAccountsAsync(processor, approver, "58h", 2, "58H1");
        var run = await CreateAndCalculateAsync(processor, "58h", July);
        var (batch, cycle) = await ApproveReviewAndPostAsync(processor, approver, poster, "58h", run);
        var (sent, sentBody, _) = await poster.PostAsync("process", $"api/payroll/payment-batches/{batch}/process");
        Ok(sent, sentBody, "58h: the sending");
        var sentSum = decimal.Parse(await Db($"SELECT COALESCE(sum(\"Amount\"), 0)::text FROM \"PaymentOrders\" WHERE \"PaymentBatchId\"='{batch}' AND \"Status\"='Submitted';"), CultureInfo.InvariantCulture);

        var (acked, ackedBody, _) = await poster.PostAsync("ack", $"api/payroll/payment-batches/{batch}/ack", new { channelReference = "قناة الصرف 58h-ACK" });
        Ok(acked, ackedBody, "58h: the acknowledgement");
        var afterAck = await Db($"SELECT \"Status\" FROM \"PaymentBatches\" WHERE \"Id\"='{batch}';");
        var (wrong, wrongBody, _) = await poster.PostAsync("settle-wrong-amount", $"api/payroll/payment-batches/{batch}/settlement-requests",
            new { statementReference = "كشف المصرف 58h-1", settledAmount = sentSum + 1m });
        var afterWrong = await Db($"SELECT \"Status\" FROM \"PaymentBatches\" WHERE \"Id\"='{batch}';");
        var (asked, askedBody, _) = await poster.PostAsync("settle", $"api/payroll/payment-batches/{batch}/settlement-requests",
            new { statementReference = "كشف المصرف 58h-1", settledAmount = sentSum });
        Ok(asked, askedBody, "58h: the settlement request", HttpStatusCode.Accepted);
        var request = askedBody.GetProperty("requestId").GetGuid().ToString("D");
        var (own, ownBody, _) = await poster.PostAsync("decide-own", $"api/payroll/dual-control/{request}/decide", new { approve = true });
        var (decided, decidedBody, _) = await approver.PostAsync("decide", $"api/payroll/dual-control/{request}/decide", new { approve = true, notes = "يطابق كشف المصرف" });
        Ok(decided, decidedBody, "58h: the settlement decision");
        var afterSettlement = await Db($"SELECT \"Status\" || '|' || COALESCE(\"SettlementReference\", '') FROM \"PaymentBatches\" WHERE \"Id\"='{batch}';");
        var orders = await Db($"SELECT COALESCE(string_agg(DISTINCT \"Status\", ','), '') FROM \"PaymentOrders\" WHERE \"PaymentBatchId\"='{batch}' AND \"EmployeeId\" IN ({string.Join(",", paid.Select(e => $"'{e}'"))});");
        var leftovers = await LeftoversAsync();

        s.Checks(new
        {
            run,
            batch,
            cycle,
            paid,
            sentSum,
            afterAck,
            wrongAmount = (int)wrong,
            wrongAmountError = Text(wrongBody, "error"),
            afterWrong,
            ownDecision = (int)own,
            ownDecisionError = Text(ownBody, "error"),
            afterSettlement,
            paidOrders = orders,
            leftovers,
        });
        Assert.Equal("Acked", afterAck);
        Assert.Equal(HttpStatusCode.BadRequest, wrong);
        Assert.Equal("Acked", afterWrong);
        Assert.True(own is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden, $"58h: the requester deciding his own request answered {(int)own}");
        Assert.Equal("Settled|كشف المصرف 58h-1", afterSettlement);
        Assert.Equal("Settled", orders);
        Assert.Equal(leftBefore, leftovers);
        s.Recorder.WriteResult("Pass");
    }
}
