using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-PAY-56a–f (PLAN-56) on the prod-like stack, through the gateway: the payroll run's state machine. A cycle with a positive and
/// a negative adjustment validates and what is posted equals what was approved; a rejection with a note reopens the run and the
/// corrected run is approved; a cancelled run frees its period and its inputs; a validation never brings an approved run back; a
/// reversal is a request another person approves, which voids a batch that has not left and is refused for one that has (D-PAY-16);
/// and a correction run takes only the adjustments aimed at its source, passes the validation, and turns a negative difference into
/// a recovery the next cycle applies.
/// <para>
/// The scenarios run in the order of their names. Each has its own month of 2028 in agency A (months the seed's v1 framework governs,
/// that no seed and no other class takes): January 56a, February 56b, March 56c, April and May 56e, June 56f. 56c, 56d and 56f read
/// the runs 56a and 56b left (found again by their months). A second run of the class on the same stack stops at 56a's precondition:
/// the months are taken, and a rerun needs a fresh stack. Nothing stops or recreates a container.
/// </para>
/// <para>
/// <b>Isolation.</b> The classes of the group run in an order that changes from run to run, and a regular run takes every free
/// approved input of its agency whatever its date. So the class leaves no free input with an amount in agency A after any scenario:
/// what it enters is consumed by one of its own runs. It asserts on its own employees' lines and on its own inputs by id.
/// </para>
/// <b>PLAN-58:</b> every posting goes through the independent review by API, opened by <c>acc-auditor</c> and closed by
/// <c>acc-a-auditor-2</c>, and every approval the run's amount asks for (<see cref="AcceptancePayrollReview"/>); no SQL fixture remains.
/// <b>Test-only preconditions</b>, each recorded in its scenario's evidence: 56c's advance instalment and absence are planted by psql (they come from an upstream approval and from attendance, which no screen of this
/// release creates for a given month). Everything a scenario proves goes through the API or the screens. The rule «the decider of a
/// reversal is neither its requester nor the run's poster» is proved by <c>Pay056ReversalSodTests</c> on the handler: no persona
/// holds both the reversal and the approval before PLAN-58.
/// </summary>
[RequiredAcceptance]
[TestCaseOrderer(ScenarioNameOrderer.TypeName, ScenarioNameOrderer.AssemblyName)]
public class AcPay56RunStateMachineTests
{
    private const string Ac = "AC-PAY-56";
    private const string Processor = "payroll-processor";
    private const string Approver = "payroll-approver";
    private const string Poster = "a-payroll-poster";
    private const string Reverser = "a-payroll-reverser";
    private const string ForeignProcessor = "b-payroll-processor";
    private const string Payroll = "payroll_db";
    private const string AdjustmentsUi = "ui/payroll/PayrollAdjustments";

    private const int Draft = 0;
    private const int Calculated = 1;
    private const int Posted = 2;
    private const int UnderReview = 3;
    private const int Approved = 4;
    private const int Reversed = 5;
    private const int Cancelled = 8;

    /// <summary>PayrollRunType.Correction as the API reads it (the enum's number).</summary>
    private const int CorrectionRunType = 1;

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly DateOnly January = new(2028, 1, 1);
    private static readonly DateOnly February = new(2028, 2, 1);
    private static readonly DateOnly March = new(2028, 3, 1);
    private static readonly DateOnly April = new(2028, 4, 1);
    private static readonly DateOnly May = new(2028, 5, 1);
    private static readonly DateOnly June = new(2028, 6, 1);

    // The reasons the scenarios write: each names its scenario, so that a later scenario finds what an earlier one entered.
    private const string RaiseReason = "فرق نقل عن كانون الثاني — AC-PAY-56a";
    private const string LowerReason = "تسوية بدل صُرف زيادةً — AC-PAY-56a";
    private const string CorrectedReason = "بدل نقل لم يُحتسب قبل الرفض — AC-PAY-56b";
    private const string MarchReason = "فرق ترفيع عن آذار — AC-PAY-56c";
    private const string InstallmentReason = "قسط سلفة مستحق في آذار — AC-PAY-56c";
    private const string AbsenceReason = "خصم غياب عن آذار — AC-PAY-56c";
    private const string AprilReason = "فرق تكليف عن نيسان — AC-PAY-56e";
    private const string TargetedReason = "استرداد بدل صُرف في كانون الثاني بلا استحقاق — AC-PAY-56f";
    private const string UntargetedReason = "فرق اعتيادي غير موجّه — AC-PAY-56f";

    private static Task<string> Db(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll, sql);

    private static Task<long> Count(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll, sql);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static decimal Money(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    private static string Snip(string body) => body.Length > 400 ? body[..400] : body;

    // ---------------------------------------------------------------- the API, as one persona

    private sealed class Api(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
    {
        public static async Task<Api> OpenAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")),
                recorder, persona, tag);

        public async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(
            string step, HttpMethod method, string path, object? body = null, bool capture = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json = default;
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                json = JsonSerializer.Deserialize<JsonElement>(text);
            // A run's details hold every line of the agency: the evidence keeps the answer's size, the checks keep what was read.
            object captured = !capture
                ? new { bytes = text.Length }
                : json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", method.Method, "/" + path, (int)response.StatusCode, captured);
            return (response.StatusCode, json, text);
        }

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(string step, string path, object? body = null) =>
            SendAsync(step, HttpMethod.Post, path, body ?? new { });

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string step, string path, bool capture = true) =>
            SendAsync(step, HttpMethod.Get, path, capture: capture);

        public EvidenceRecorder Recorder => recorder;

        public void Dispose() => http.Dispose();
    }

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    // ---------------------------------------------------------------- payroll_db

    /// <summary>A run as one line: status|revision|frozen|validated|total. What a refusal must leave as it was.</summary>
    private static Task<string> RunRowAsync(string run) => Db(
        "SELECT \"Status\"::text || '|' || \"Revision\"::text || '|' || \"ResultsFrozen\"::text || '|' || \"ValidatedAtCalculate\"::text || '|' || " +
        $"\"TotalNetPay\"::text FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");

    private static async Task<int> StatusOfAsync(string run) =>
        (int)await Count($"SELECT \"Status\" FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");

    /// <summary>An employee's line of a run: gross|deductions|adjustments|net, or «no-line».</summary>
    private static Task<string> LineAsync(string run, string employee) => Db(
        "SELECT COALESCE((SELECT \"GrossPay\"::text || '|' || \"TotalDeductions\"::text || '|' || \"TotalAdjustments\"::text || '|' || \"NetPay\"::text " +
        $"FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}' AND \"EmployeeId\"='{employee}'), 'no-line');");

    private static Task<string> LineIdAsync(string run, string employee) => Db(
        $"SELECT COALESCE((SELECT \"Id\"::text FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}' AND \"EmployeeId\"='{employee}'), '');");

    /// <summary>The live Regular run of agency A whose period starts on <paramref name="month"/>, or empty.</summary>
    private static Task<string> RunOfAsync(DateOnly month, int? status = null) => Db(
        $"SELECT COALESCE((SELECT \"Id\"::text FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"RunType\"='Regular' " +
        $"AND \"PeriodStart\"::date = '{Day(month)}' AND \"Status\" {(status is int s ? $"= {s}" : "NOT IN (5, 8)")} ORDER BY \"CreatedAt\" DESC LIMIT 1), '');");

    /// <summary>The runs of agency A, of any type and in any state, whose period touches the month.</summary>
    private static Task<string> RunsCoveringAsync(DateOnly from, DateOnly lastMonth) => Db(
        $"SELECT COALESCE(string_agg(r.\"RunType\"::text || ':' || r.\"Status\"::text, ','), '') FROM \"PayrollRuns\" r WHERE r.\"TenantId\"='{TenantA}' " +
        $"AND r.\"PeriodStart\" <= '{Day(lastMonth.AddMonths(1).AddDays(-1))}' AND r.\"PeriodEnd\" >= '{Day(from)}';");

    /// <summary>The free inputs with an amount agency A holds: what this class must leave at zero for its own entries.</summary>
    private static Task<long> FreeAdjustmentsWithAnAmountAsync() =>
        Count($"SELECT count(*) FROM \"PayrollAdjustments\" WHERE \"TenantId\"='{TenantA}' AND \"PayrollRunId\" IS NULL AND \"Amount\" <> 0;");

    private static Task<string> AdjustmentRunAsync(string adjustment) =>
        Db($"SELECT COALESCE(\"PayrollRunId\"::text, 'free') FROM \"PayrollAdjustments\" WHERE \"Id\"='{adjustment}';");

    /// <summary>
    /// Agency-A employees for this class: an active pay profile with a unit, not the two employees the seed gives a branch, and no
    /// input a person entered. Taken from the middle of the ids: AC-PAY-55 takes the lowest and AC-IAM-53 the highest.
    /// </summary>
    private static async Task<string[]> PickEmployeesAsync(EvidenceRecorder recorder, string step, int count, int skip)
    {
        var seeded = string.Join(",", new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpBr2Id }.Select(id => $"'{id:D}'"));
        var rows = await Db(
            "SELECT COALESCE(string_agg(e, ',' ORDER BY e), '') FROM (SELECT p.\"EmployeeId\"::text AS e FROM \"EmployeePayProfiles\" p " +
            $"WHERE p.\"TenantId\"='{TenantA}' AND p.\"IsActive\" AND p.\"OrgUnitId\" IS NOT NULL AND p.\"EmployeeId\" NOT IN ({seeded}) " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayrollAdjustments\" a WHERE a.\"EmployeeId\" = p.\"EmployeeId\" AND a.\"Origin\"='Manual') " +
            "AND NOT EXISTS (SELECT 1 FROM \"DeductionRecords\" d WHERE d.\"EmployeeId\" = p.\"EmployeeId\" AND d.\"Origin\"='Manual') " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayProfileChangeRequests\" r WHERE r.\"EmployeeId\" = p.\"EmployeeId\") " +
            $"ORDER BY p.\"EmployeeId\" OFFSET {skip} LIMIT {count}) picked;");
        var picked = rows.Split(',', StringSplitOptions.RemoveEmptyEntries);
        AcceptanceGate.RequireData(recorder, picked.Length == count,
            $"{step}: لا يوجد في الجهة A {count} موظفين بملف أجر فعّال وبلا مدخل يدوي",
            $"{step}: agency A has no {count} employees with an active pay profile and no manual input.");
        return picked;
    }

    /// <summary>The employee an earlier scenario entered an adjustment for, by the adjustment's reason.</summary>
    private static Task<string> EmployeeOfAdjustmentAsync(string reason) => Db(
        $"SELECT COALESCE((SELECT \"EmployeeId\"::text FROM \"PayrollAdjustments\" WHERE \"TenantId\"='{TenantA}' AND \"AdjustmentReason\"='{reason}' " +
        "ORDER BY \"CreatedAt\" DESC LIMIT 1), '');");

    /// <summary>audit.json: payroll_db's audit rows of the records the scenario touched (the action, the user and the time; no values).</summary>
    private static async Task AuditAsync(RecruitmentSession s, params string[] entityIds)
    {
        var ids = string.Join(",", entityIds.Where(id => Guid.TryParse(id, out _)).Select(id => $"'{id}'"));
        var rows = ids.Length == 0 ? "" : await Db(
            "SELECT COALESCE(string_agg(\"EntityType\" || '|' || \"EntityId\" || '|' || \"Action\" || '|' || COALESCE(\"UserId\"::text,'') || '|' || " +
            $"to_char(\"CreatedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS'), E'\\n' ORDER BY \"CreatedAt\"), '') FROM \"AuditLogs\" WHERE \"EntityId\" IN ({ids});");
        s.Recorder.WriteAudit(new
        {
            scenario = s.Key,
            explicitAuditQuery = true,
            source = "payroll_db.AuditLogs",
            entityIds,
            rows = rows.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        });
    }

    // ---------------------------------------------------------------- the acts

    /// <summary>
    /// An adjustment entered on the screen by the processor and approved on the screen by the approver (PLAN-54: a manual input
    /// counts only after a second person approved it). <paramref name="targetRun"/> aims it at a posted run's correction.
    /// </summary>
    private static async Task<string> ApprovedAdjustmentAsync(
        RecruitmentSession s, string step, string employee, decimal amount, string reason, string? targetRun = null)
    {
        var fields = new List<(string, string)>
        {
            ("EmployeeId", employee),
            ("Amount", amount.ToString("0.00", CultureInfo.InvariantCulture)),
            ("AdjustmentReason", reason),
        };
        if (targetRun is not null)
            fields.Add(("TargetSourceRunId", targetRun));
        var (entered, enteredBody) = await s[Processor].PostWithStatusAsync($"{step}-enter", $"{AdjustmentsUi}/Create", $"{AdjustmentsUi}/Create", F(fields.ToArray()));
        var adjustment = await Db(
            $"SELECT COALESCE(max(\"Id\"::text), '') FROM \"PayrollAdjustments\" WHERE \"EmployeeId\"='{employee}' AND \"AdjustmentReason\"='{reason}' AND \"PayrollRunId\" IS NULL;");
        Assert.True(adjustment.Length > 0, $"{step}: the adjustment was not entered ({(int)entered}): {Snip(enteredBody)}");
        var (approved, approvedBody) = await s[Approver].PostWithStatusAsync($"{step}-approve", $"{AdjustmentsUi}/Approve/{adjustment}", AdjustmentsUi, F());
        var state = await Db(
            "SELECT \"ApprovalStatus\" || '|' || \"Origin\" || '|' || (\"ApprovedBy\" IS NOT NULL AND \"ApprovedBy\" <> \"CreatedBy\")::text || '|' || " +
            $"COALESCE(\"TargetSourceRunId\"::text, 'untargeted') FROM \"PayrollAdjustments\" WHERE \"Id\"='{adjustment}';");
        Assert.True(state == $"Approved|Manual|true|{targetRun ?? "untargeted"}",
            $"{step}: the adjustment is not approved by a second person as entered ({(int)approved}): {state} {Snip(approvedBody)}");
        return adjustment;
    }

    private static async Task<string> CreateRunAsync(Api processor, string step, DateOnly month)
    {
        var end = month.AddMonths(1).AddDays(-1);
        var (created, createdBody, _) = await processor.PostAsync($"{step}-create-run", "api/payroll/runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });
        Assert.True(created is HttpStatusCode.OK or HttpStatusCode.Created, $"{step}: creating the run of {Day(month)[..7]} answered {(int)created}: {Text(createdBody, "error")}");
        return createdBody.GetProperty("id").GetGuid().ToString("D");
    }

    private static async Task<(HttpStatusCode Status, string Error)> CalculateAsync(Api processor, string step, string run)
    {
        // A class that ran earlier on this stack may have added an employment record after the seed (AC-IAM-50b does), and the
        // service-regime gate then refuses every run of the agency. The seed's classification is applied to such records first.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(TenantA);
        var (calculated, body, _) = await processor.PostAsync($"{step}-calculate", $"api/payroll/runs/{run}/calculate");
        return (calculated, Text(body, "error"));
    }

    /// <summary>Creates and calculates the Regular run of a month of agency A, as the processor. Returns the run's id.</summary>
    private static async Task<string> CalculateMonthAsync(Api processor, string step, DateOnly month)
    {
        var run = await CreateRunAsync(processor, step, month);
        var (calculated, error) = await CalculateAsync(processor, step, run);
        Assert.True(calculated == HttpStatusCode.OK, $"{step}: calculating the run of {Day(month)[..7]} answered {(int)calculated}: {error}");
        return run;
    }

    private static async Task SubmitAndApproveAsync(Api processor, Api approver, string step, string run)
    {
        var (submitted, submittedBody, _) = await processor.PostAsync($"{step}-submit", $"api/payroll/runs/{run}/submit-for-review");
        Assert.True(submitted == HttpStatusCode.OK, $"{step}: the submission answered {(int)submitted}: {Text(submittedBody, "error")}");
        var (approved, approvedBody, _) = await approver.PostAsync($"{step}-approve", $"api/payroll/runs/{run}/approve");
        Assert.True(approved == HttpStatusCode.OK, $"{step}: the approval answered {(int)approved}: {Text(approvedBody, "error")}");
        // PLAN-58 (D-PAY-5): above the agency's threshold the second approver approves it too.
        await AcceptancePayrollReview.CompleteApprovalAsync(approver.Recorder, run, approvedBody, step);
    }

    /// <summary>
    /// The closed review a posting needs (PLAN-58, D-PAY-3): opened by acc-auditor and closed by acc-a-auditor-2, through the gateway.
    /// </summary>
    private static Task<object> CloseAuditAsync(EvidenceRecorder recorder, string run, string step) =>
        AcceptancePayrollReview.ReviewAsync(recorder, run, step);

    private static async Task PostRunAsync(Api poster, string step, string run)
    {
        var (posted, postedBody, _) = await poster.PostAsync($"{step}-post", $"api/payroll/runs/{run}/post");
        Assert.True(posted == HttpStatusCode.OK, $"{step}: the posting answered {(int)posted}: {Text(postedBody, "error")}");
    }

    /// <summary>The run's disbursement batches as «status:kind», in the order they were made.</summary>
    private static Task<string> BatchesAsync(string run) => Db(
        $"SELECT COALESCE(string_agg(\"Status\" || ':' || \"BatchKind\", ',' ORDER BY \"CreatedAt\"), '') FROM \"PaymentBatches\" WHERE \"PayrollRunId\"='{run}';");

    // ---------------------------------------------------------------- 56a

    [SkippableFact]
    public async Task Ac56a_a_cycle_with_a_positive_and_a_negative_adjustment_validates_and_what_is_posted_equals_what_was_approved()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "56a",
            "AC-PAY-56a: a cycle with an approved adjustment of +50,000 for one employee and −20,000 for another validates; after the posting the run's total equals the approved total and the sum of the lines' nets, and each payslip shows gross − deductions + adjustments = net.",
            Processor, Approver, Poster);
        s.Steps("""
            1. Precondition, in before.json: agency A has no run that touches January to June 2028 (the months of this class). A second
               run of the class on the same stack stops here: a rerun needs a fresh stack. Two agency-A employees are picked.
            2. payroll-processor enters an adjustment of +50,000 for the first and of −20,000 for the second on the screen;
               payroll-approver approves both (a manual input counts only after a second person approved it).
            3. payroll-processor creates and calculates the Regular run of January 2028. It validates (before PLAN-56 an adjustment
               with an amount failed the whole cycle): calculated, validated, frozen. Each of the two lines carries its adjustment,
               and its net is gross − deductions + adjustments.
            4. payroll-processor submits it; payroll-approver approves it. The total at the approval is noted.
            5. The independent review a posting needs: acc-auditor opens it and acc-a-auditor-2 closes it, through the gateway.
            6. a-payroll-poster posts it. The run's total is the approved total, to the unit; it is the sum of the lines' nets the
               API returns; the batch carries the same amount; and the two payslips (GET api/payroll/payslips/{line}) show the
               adjustments beside the gross and the deductions.
            """);

        var covering = await RunsCoveringAsync(January, June);
        AcceptanceGate.RequireData(s.Recorder, covering.Length == 0,
            "للجهة A تشغيل رواتب يمس الأشهر كانون الثاني–حزيران 2028: الصنف يحتاج مكدساً جديداً",
            $"Agency A already has a payroll run touching January–June 2028 ({covering}): AC-PAY-56 took the months in an earlier run; start a fresh stack.");
        var picked = await PickEmployeesAsync(s.Recorder, "56a", 2, skip: 30);
        var (raised, lowered) = (picked[0], picked[1]);
        s.Recorder.WriteBefore(new
        {
            scenario = "56a",
            explicitDomainSnapshot = true,
            runsTouchingJanuaryToJune2028 = covering,
            raised,
            lowered,
            freeAdjustmentsWithAnAmount = await FreeAdjustmentsWithAnAmountAsync()
        });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "56a");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "56a");
        using var poster = await Api.OpenAsync(s.Recorder, Poster, "56a");

        // 2. The two adjustments.
        var plus = await ApprovedAdjustmentAsync(s, "56a-plus", raised, 50_000m, RaiseReason);
        var minus = await ApprovedAdjustmentAsync(s, "56a-minus", lowered, -20_000m, LowerReason);

        // 3. January validates.
        var run = await CalculateMonthAsync(processor, "56a", January);
        var afterCalculation = await RunRowAsync(run);
        var raisedLine = await LineAsync(run, raised);
        var loweredLine = await LineAsync(run, lowered);

        // 4. Review and approval.
        await SubmitAndApproveAsync(processor, approver, "56a", run);
        var afterApproval = await RunRowAsync(run);
        var approvedTotal = Money(afterApproval.Split('|')[4]);

        // 5–6. The posting.
        var audit = await CloseAuditAsync(s.Recorder, run, "56a");
        await PostRunAsync(poster, "56a", run);
        var afterPosting = await RunRowAsync(run);
        var postedTotal = Money(afterPosting.Split('|')[4]);
        var sumOfLines = Money(await Db($"SELECT COALESCE(sum(\"NetPay\"), 0)::text FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';"));
        var lines = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");
        var (detailsStatus, details, _) = await processor.GetAsync("run-details", $"api/payroll/runs/{run}", capture: false);
        Assert.True(detailsStatus == HttpStatusCode.OK, $"56a: the run's details answered {(int)detailsStatus}.");
        var apiLines = details.GetProperty("salaryLines").EnumerateArray().ToList();
        var apiSum = apiLines.Sum(l => l.GetProperty("netPay").GetDecimal());
        var apiTotal = details.GetProperty("totalNetPay").GetDecimal();
        var batch = await Db($"SELECT \"Status\" || '|' || \"TotalAmount\"::text FROM \"PaymentBatches\" WHERE \"PayrollRunId\"='{run}';");

        var payslips = new List<object>();
        foreach (var (employee, expected) in new[] { (raised, 50_000m), (lowered, -20_000m) })
        {
            var lineId = await LineIdAsync(run, employee);
            var (payslipStatus, payslip, _) = await processor.GetAsync($"payslip-{(expected > 0 ? "raised" : "lowered")}", $"api/payroll/payslips/{lineId}");
            Assert.True(payslipStatus == HttpStatusCode.OK, $"56a: the payslip answered {(int)payslipStatus}.");
            var line = payslip.GetProperty("line");
            var (gross, deductions, adjustments, net) = (
                line.GetProperty("grossPay").GetDecimal(), line.GetProperty("totalDeductions").GetDecimal(),
                line.GetProperty("totalAdjustments").GetDecimal(), line.GetProperty("netPay").GetDecimal());
            payslips.Add(new { employee, gross, deductions, adjustments, net });
            Assert.Equal(expected, adjustments);
            // Every reader shows the same net: gross − deductions + adjustments, floored at zero.
            Assert.Equal(Math.Max(0m, gross - deductions + adjustments), net);
            Assert.Contains(payslip.GetProperty("components").EnumerateArray(), c => c.GetProperty("amount").GetDecimal() == expected);
        }

        await AuditAsync(s, run, plus, minus);
        s.Checks(new
        {
            run,
            afterCalculation,
            runFormat = "status|revision|frozen|validated|total",
            raisedLine,
            loweredLine,
            lineFormat = "gross|deductions|adjustments|net",
            afterApproval,
            approvedTotal,
            audit,
            afterPosting,
            postedTotal,
            sumOfLines,
            lines,
            apiLines = apiLines.Count,
            apiSum,
            apiTotal,
            batch,
            payslips,
            adjustments = new { plus = await AdjustmentRunAsync(plus), minus = await AdjustmentRunAsync(minus) },
            freeAdjustmentsWithAnAmountLeft = await FreeAdjustmentsWithAnAmountAsync()
        });

        Assert.StartsWith($"{Calculated}|1|true|true|", afterCalculation, StringComparison.Ordinal);
        Assert.Equal(50_000m, Money(raisedLine.Split('|')[2]));
        Assert.Equal(-20_000m, Money(loweredLine.Split('|')[2]));
        foreach (var line in new[] { raisedLine, loweredLine })
        {
            var parts = line.Split('|').Select(Money).ToArray();
            Assert.Equal(Math.Max(0m, parts[0] - parts[1] + parts[2]), parts[3]);
        }

        Assert.StartsWith($"{Approved}|", afterApproval, StringComparison.Ordinal);
        Assert.StartsWith($"{Posted}|", afterPosting, StringComparison.Ordinal);
        // What is posted equals what was approved, and it is the sum of the lines' nets, by the database and by the API.
        Assert.Equal(approvedTotal, postedTotal);
        Assert.Equal(postedTotal, sumOfLines);
        Assert.Equal(lines, apiLines.Count);
        Assert.Equal(postedTotal, apiSum);
        Assert.Equal(postedTotal, apiTotal);
        Assert.Equal($"Pending|{postedTotal.ToString("0.00", CultureInfo.InvariantCulture)}", batch);
        Assert.Equal(run, await AdjustmentRunAsync(plus));
        Assert.Equal(run, await AdjustmentRunAsync(minus));
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 56b

    [SkippableFact]
    public async Task Ac56b_a_rejection_with_a_note_reopens_the_run_and_the_corrected_run_is_approved()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "56b",
            "AC-PAY-56b: the approver rejects a run under review with a note and the run reopens as a draft at revision 2; the processor adds an adjustment, recalculates, the run validates, is submitted again and approved. Negative: a rejection without a note answers 400; recalculating an approved run without a rejection answers 400.",
            Processor, Approver);
        s.Steps("""
            1. Precondition, in before.json: agency A has no run of February 2028; the employee 56a raised is found by its adjustment.
            2. payroll-processor creates, calculates and submits the Regular run of February 2028.
            3. Negative: payroll-approver rejects it without a note. 400; the run is still under review at revision 1, with its lines.
            4. payroll-approver rejects it with a note. The run is a draft at revision 2, neither frozen nor validated; the rejected
               version is kept as a revision (its number, its line count, its total, a fingerprint, the note); its lines are gone.
            5. payroll-processor enters an adjustment for the employee (approved by payroll-approver) and calculates the same run
               again. It validates at revision 2, and the employee's line carries the adjustment.
            6. payroll-processor submits it again; payroll-approver approves it.
            7. Negative: payroll-processor asks to calculate the approved run. 400; the run is as it was.
            """);

        var existing = await RunsCoveringAsync(February, February);
        AcceptanceGate.RequireData(s.Recorder, existing.Length == 0,
            "للجهة A تشغيل رواتب في شباط 2028: الصنف يحتاج مكدساً جديداً", $"Agency A already has a payroll run of February 2028 ({existing}): start a fresh stack.");
        var employee = await EmployeeOfAdjustmentAsync(RaiseReason);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(employee, out _),
            "56b: لا توجد تسوية 56a: يُشغَّل الصنف كاملاً بترتيبه", "56b: 56a's adjustment is not there: the class runs whole, in the order of its scenarios.");
        s.Recorder.WriteBefore(new { scenario = "56b", explicitDomainSnapshot = true, runsOfFebruary2028 = existing, employee });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "56b");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "56b");

        // 2. Under review.
        var run = await CalculateMonthAsync(processor, "56b", February);
        var (submitted, submittedBody, _) = await processor.PostAsync("submit", $"api/payroll/runs/{run}/submit-for-review");
        Assert.True(submitted == HttpStatusCode.OK, $"56b: the submission answered {(int)submitted}: {Text(submittedBody, "error")}");
        var underReview = await RunRowAsync(run);
        var linesUnderReview = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");
        var totalUnderReview = underReview.Split('|')[4];

        // 3. No note.
        var (noNote, noNoteBody, _) = await approver.PostAsync("reject-without-a-note", $"api/payroll/runs/{run}/reject", new { notes = "" });
        var afterNoNote = await RunRowAsync(run);
        var linesAfterNoNote = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");

        // 4. The rejection.
        const string note = "بدل النقل لم يُحتسب لأحد الموظفين؛ يُصحَّح ثم يُعاد الإرسال.";
        var (rejected, rejectedBody, _) = await approver.PostAsync("reject", $"api/payroll/runs/{run}/reject", new { notes = note });
        var reopened = await RunRowAsync(run);
        var linesAfterRejection = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");
        var revision = await Db(
            "SELECT COALESCE((SELECT \"Revision\"::text || '|' || \"LineCount\"::text || '|' || \"TotalNetPay\"::text || '|' || length(\"LinesFingerprint\")::text || '|' || \"Note\" " +
            $"FROM \"PayrollRunRevisions\" WHERE \"PayrollRunId\"='{run}'), 'none');");
        var decision = await Db(
            $"SELECT COALESCE((SELECT \"Decision\" || '|' || \"Notes\" FROM \"PayrollRunApprovals\" WHERE \"PayrollRunId\"='{run}' ORDER BY \"DecidedAt\" DESC LIMIT 1), 'none');");

        // 5. The correction, and the same run again.
        var correction = await ApprovedAdjustmentAsync(s, "56b-correction", employee, 7_500m, CorrectedReason);
        var (recalculated, recalculatedError) = await CalculateAsync(processor, "recalculate", run);
        var afterRecalculation = await RunRowAsync(run);
        var line = await LineAsync(run, employee);

        // 6. Resubmitted and approved.
        await SubmitAndApproveAsync(processor, approver, "56b-again", run);
        var approved = await RunRowAsync(run);

        // 7. An approved run is not calculated again.
        var (again, againError) = await CalculateAsync(processor, "calculate-approved", run);
        var afterAgain = await RunRowAsync(run);

        await AuditAsync(s, run, correction);
        s.Checks(new
        {
            run,
            underReview,
            runFormat = "status|revision|frozen|validated|total",
            linesUnderReview,
            rejectionWithoutANote = (int)noNote,
            rejectionWithoutANoteError = Text(noNoteBody, "error"),
            afterNoNote,
            rejection = (int)rejected,
            rejectionError = Text(rejectedBody, "error"),
            reopened,
            linesAfterRejection,
            revision,
            revisionFormat = "revision|lines|total|fingerprint length|note",
            decision,
            correction,
            recalculation = (int)recalculated,
            recalculatedError,
            afterRecalculation,
            line,
            lineFormat = "gross|deductions|adjustments|net",
            approved,
            calculatingTheApprovedRun = (int)again,
            againError,
            afterAgain,
            freeAdjustmentsWithAnAmountLeft = await FreeAdjustmentsWithAnAmountAsync()
        });

        Assert.StartsWith($"{UnderReview}|1|true|true|", underReview, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, noNote);
        Assert.Equal(underReview, afterNoNote);
        Assert.Equal(linesUnderReview, linesAfterNoNote);
        Assert.True(rejected == HttpStatusCode.OK, $"56b: the rejection answered {(int)rejected}: {Text(rejectedBody, "error")}");
        Assert.Equal($"{Draft}|2|false|false|0.00", reopened);
        Assert.Equal(0, linesAfterRejection);
        Assert.Equal($"1|{linesUnderReview}|{totalUnderReview}|64|{note}", revision);
        Assert.Equal($"Rejected|{note}", decision);
        Assert.True(recalculated == HttpStatusCode.OK, $"56b: the recalculation answered {(int)recalculated}: {recalculatedError}");
        Assert.StartsWith($"{Calculated}|2|true|true|", afterRecalculation, StringComparison.Ordinal);
        Assert.Equal(7_500m, Money(line.Split('|')[2]));
        Assert.StartsWith($"{Approved}|2|true|true|", approved, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, again);
        Assert.Equal(approved, afterAgain);
        Assert.Equal(run, await AdjustmentRunAsync(correction));
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 56c

    [SkippableFact]
    public async Task Ac56c_a_cancelled_run_frees_its_period_and_its_inputs_for_a_new_run()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "56c",
            "AC-PAY-56c: a calculated run that holds an advance instalment, an absence and an adjustment is cancelled with a reason, and the three appear in a new run of the same period. Negative: cancelling a posted run answers 400; the processor cancelling an approved run answers 403.",
            Processor, Approver);
        s.Steps("""
            1. Precondition, in before.json: agency A has no run of March 2028; January's posted run (56a) and February's approved
               run (56b) are found; an agency-A employee of this class is picked.
               Test-only precondition (psql, payroll_db): an advance instalment due on 2028-03-15 and an absence of March 2028 for
               that employee. Neither is created by a screen of this release for a given month: an instalment comes from an
               upstream approval (due from today), an absence from attendance.
            2. payroll-processor enters an adjustment for the employee (approved by payroll-approver), then creates and calculates
               the Regular run of March 2028. The three inputs are applied in it.
            3. Negative: payroll-approver cancels January's posted run. 400; it is still posted.
            4. Negative: payroll-processor cancels February's approved run. 403 (D-PAY-18: after a run was sent for review, cancelling
               it is the approver's decision); it is still approved.
            5. Negative: payroll-processor cancels March's run without a reason. 400; it is still calculated.
            6. payroll-processor cancels March's run with a reason. It is cancelled, with who cancelled it and why; the three inputs
               are free; the decisions of the run hold the cancellation.
            7. payroll-processor creates and calculates a new Regular run of March 2028 (the period is free): the three inputs are
               applied in it, and the employee's line carries them.
            """);

        var existing = await RunsCoveringAsync(March, March);
        AcceptanceGate.RequireData(s.Recorder, existing.Length == 0,
            "للجهة A تشغيل رواتب في آذار 2028: الصنف يحتاج مكدساً جديداً", $"Agency A already has a payroll run of March 2028 ({existing}): start a fresh stack.");
        var january = await RunOfAsync(January, Posted);
        var february = await RunOfAsync(February, Approved);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(january, out _) && Guid.TryParse(february, out _),
            "56c: لا يوجد تشغيل كانون الثاني المرحّل (56a) أو شباط المعتمد (56b): يُشغَّل الصنف كاملاً بترتيبه",
            "56c: January's posted run (56a) or February's approved run (56b) is not there: the class runs whole, in the order of its scenarios.");
        var employee = (await PickEmployeesAsync(s.Recorder, "56c", 1, skip: 34))[0];

        // The test-only precondition: one instalment and one absence, of March 2028, for that employee.
        var installment = Guid.NewGuid().ToString("D");
        var absence = Guid.NewGuid().ToString("D");
        await Db(
            "INSERT INTO \"DeductionRecords\" (\"Id\",\"TenantId\",\"CreatedAt\",\"UpdatedAt\",\"EmployeeId\",\"DeductionType\",\"Amount\",\"Reason\",\"DueDate\",\"InstallmentNumber\",\"SourceReference\",\"ApprovalStatus\",\"Origin\") " +
            $"VALUES ('{installment}','{TenantA}',now(),now(),'{employee}','AdvanceRecovery',11111.00,'{InstallmentReason}','2028-03-15',1,'AC-PAY-56c:advance:{installment}','Approved','System');");
        await Db(
            "INSERT INTO \"AbsenceDeductions\" (\"Id\",\"TenantId\",\"CreatedAt\",\"UpdatedAt\",\"EmployeeId\",\"PeriodStart\",\"PeriodEnd\",\"AbsentDays\",\"Amount\",\"Reason\",\"SourceReference\") " +
            $"VALUES ('{absence}','{TenantA}',now(),now(),'{employee}','2028-03-01','2028-03-31',1,2222.00,'{AbsenceReason}','AC-PAY-56c:absence:{absence}');");
        s.Recorder.WriteBefore(new
        {
            scenario = "56c",
            explicitDomainSnapshot = true,
            runsOfMarch2028 = existing,
            january,
            february,
            employee,
            testOnlyPrecondition = "an advance instalment due 2028-03-15 and an absence of March 2028, planted by psql (no screen of this release creates either for a given month)",
            installment,
            absence
        });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "56c");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "56c");

        Task<string> InputsAsync() => Db(
            $"SELECT (SELECT COALESCE(\"AppliedInRunId\"::text,'free') FROM \"DeductionRecords\" WHERE \"Id\"='{installment}') || '|' || " +
            $"(SELECT COALESCE(\"AppliedInRunId\"::text,'free') FROM \"AbsenceDeductions\" WHERE \"Id\"='{absence}');");

        // 2. March holds the three.
        var adjustment = await ApprovedAdjustmentAsync(s, "56c-adjustment", employee, 12_345m, MarchReason);
        var run = await CalculateMonthAsync(processor, "56c", March);
        var calculated = await RunRowAsync(run);
        var inputsInTheRun = await InputsAsync() + "|" + await AdjustmentRunAsync(adjustment);

        // 3–5. The refusals.
        var januaryBefore = await RunRowAsync(january);
        var (ofPosted, ofPostedBody, _) = await approver.PostAsync("cancel-posted", $"api/payroll/runs/{january}/cancel", new { reason = "محاولة إلغاء تشغيل مرحّل" });
        var januaryAfter = await RunRowAsync(january);
        var februaryBefore = await RunRowAsync(february);
        var (byProcessor, byProcessorBody, _) = await processor.PostAsync("cancel-approved", $"api/payroll/runs/{february}/cancel", new { reason = "محاولة المعالج إلغاء تشغيل معتمد" });
        var februaryAfter = await RunRowAsync(february);
        var (noReason, noReasonBody, _) = await processor.PostAsync("cancel-without-a-reason", $"api/payroll/runs/{run}/cancel", new { reason = " " });
        var afterNoReason = await RunRowAsync(run);

        // 6. The cancellation.
        const string reason = "أُنشئ تشغيل آذار قبل اكتمال اعتماد التسويات.";
        var (cancelled, cancelledBody, _) = await processor.PostAsync("cancel", $"api/payroll/runs/{run}/cancel", new { reason });
        var afterCancellation = await Db(
            "SELECT \"Status\"::text || '|' || (\"CancelledBy\" IS NOT NULL)::text || '|' || (\"CancelledAt\" IS NOT NULL)::text || '|' || COALESCE(\"CancellationReason\",'') " +
            $"FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");
        var inputsAfterCancellation = await InputsAsync() + "|" + await AdjustmentRunAsync(adjustment);
        var decision = await Db(
            $"SELECT COALESCE((SELECT \"Decision\" || '|' || \"Notes\" FROM \"PayrollRunApprovals\" WHERE \"PayrollRunId\"='{run}' ORDER BY \"DecidedAt\" DESC LIMIT 1), 'none');");

        // 7. A new run of the same period takes the three.
        var again = await CalculateMonthAsync(processor, "56c-again", March);
        var inputsInTheNewRun = await InputsAsync() + "|" + await AdjustmentRunAsync(adjustment);
        var line = await LineAsync(again, employee);
        var lineId = await LineIdAsync(again, employee);
        var referenced = await Count(
            $"SELECT count(*) FROM \"SalaryLineComponents\" WHERE \"SalaryLineId\"='{lineId}' AND \"ReferenceId\" IN ('{installment}','{absence}','{adjustment}');");

        await AuditAsync(s, run, again, adjustment);
        s.Checks(new
        {
            run,
            calculated,
            runFormat = "status|revision|frozen|validated|total",
            inputsInTheRun,
            inputsFormat = "instalment|absence|adjustment (the run each is applied in, or free)",
            cancellingThePostedRun = (int)ofPosted,
            cancellingThePostedRunError = Text(ofPostedBody, "error"),
            januaryBefore,
            januaryAfter,
            theProcessorCancellingTheApprovedRun = (int)byProcessor,
            theProcessorCancellingTheApprovedRunError = Text(byProcessorBody, "error"),
            februaryBefore,
            februaryAfter,
            cancellingWithoutAReason = (int)noReason,
            cancellingWithoutAReasonError = Text(noReasonBody, "error"),
            afterNoReason,
            cancellation = (int)cancelled,
            cancellationError = Text(cancelledBody, "error"),
            afterCancellation,
            cancellationFormat = "status|has who|has when|reason",
            inputsAfterCancellation,
            decision,
            newRun = again,
            inputsInTheNewRun,
            line,
            lineFormat = "gross|deductions|adjustments|net",
            componentsThatNameTheThreeInputs = referenced,
            freeAdjustmentsWithAnAmountLeft = await FreeAdjustmentsWithAnAmountAsync()
        });

        Assert.StartsWith($"{Calculated}|1|true|true|", calculated, StringComparison.Ordinal);
        Assert.Equal($"{run}|{run}|{run}", inputsInTheRun);
        Assert.Equal(HttpStatusCode.BadRequest, ofPosted);
        Assert.Equal(januaryBefore, januaryAfter);
        Assert.Equal(HttpStatusCode.Forbidden, byProcessor);
        Assert.Equal(februaryBefore, februaryAfter);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        Assert.Equal(calculated, afterNoReason);
        Assert.True(cancelled == HttpStatusCode.OK, $"56c: the cancellation answered {(int)cancelled}: {Text(cancelledBody, "error")}");
        Assert.Equal($"{Cancelled}|true|true|{reason}", afterCancellation);
        Assert.Equal("free|free|free", inputsAfterCancellation);
        Assert.Equal($"Cancelled|{reason}", decision);
        Assert.NotEqual(run, again);
        Assert.Equal($"{again}|{again}|{again}", inputsInTheNewRun);
        Assert.Equal(12_345m, Money(line.Split('|')[2]));
        Assert.Equal(3, referenced);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 56d

    [SkippableFact]
    public async Task Ac56d_a_validation_does_not_bring_an_approved_run_back()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "56d",
            "AC-PAY-56d (negative): validate on an approved run answers 400 and the run's state is as it was. Before PLAN-56 the validation checked no state and set the run back to «محسوب».",
            Processor);
        s.Steps("""
            1. Precondition, in before.json: February's approved run (56b) is found, with its state.
            2. payroll-processor sends validate for it. 400, with a message that says a validation does not bring back a run that was
               sent for review, approved, posted, reversed or cancelled. The run's state, revision, flags, total and lines are as
               they were.
            """);

        var run = await RunOfAsync(February, Approved);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(run, out _),
            "56d: لا يوجد تشغيل شباط المعتمد (56b): يُشغَّل الصنف كاملاً بترتيبه",
            "56d: February's approved run (56b) is not there: the class runs whole, in the order of its scenarios.");
        var before = await RunRowAsync(run);
        var linesBefore = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");
        s.Recorder.WriteBefore(new { scenario = "56d", explicitDomainSnapshot = true, run, before, runFormat = "status|revision|frozen|validated|total", lines = linesBefore });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "56d");
        var (validated, validatedBody, _) = await processor.PostAsync("validate-approved", $"api/payroll/runs/{run}/validate");
        var after = await RunRowAsync(run);
        var linesAfter = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");

        await AuditAsync(s, run);
        s.Checks(new { run, validation = (int)validated, error = Text(validatedBody, "error"), before, after, linesBefore, linesAfter });

        Assert.Equal(HttpStatusCode.BadRequest, validated);
        Assert.Contains("لا يمكن التحقق من تشغيل", Text(validatedBody, "error"), StringComparison.Ordinal);
        Assert.StartsWith($"{Approved}|", before, StringComparison.Ordinal);
        Assert.Equal(before, after);
        Assert.Equal(linesBefore, linesAfter);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 56e

    [SkippableFact]
    public async Task Ac56e_a_reversal_is_a_request_another_person_approves_and_it_is_refused_once_the_batch_has_left()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "56e",
            "AC-PAY-56e: the reverser asks for the reversal of a posted run whose batch has not left, with a reason, and the approver approves: the run is reversed, its batch voided, its inputs free, and a new regular run of the period is made. Negative: the reverser deciding his own request answers 403; the reversal of a run whose batch was sent answers 400 with D-PAY-16's message.",
            Processor, Approver, Poster, Reverser, ForeignProcessor);
        s.Steps("""
            1. Precondition, in before.json: agency A has no run of April or May 2028; an agency-A employee of this class is picked.
            2. payroll-processor enters an adjustment for the employee (approved by payroll-approver), creates and calculates the
               Regular run of April 2028 and submits it; payroll-approver approves it; acc-auditor and acc-a-auditor-2 review it
               (PLAN-58); a-payroll-poster posts it. It is posted, with a pending batch.
            3. Negative: a-payroll-reverser asks for its reversal without a reason. 400; no request is made.
            4. a-payroll-reverser asks for its reversal with a reason. The request waits; the run and its batch are as they were.
               A second request while that one waits answers 409. Agency B's processor does not find the run (404).
            5. Negative: a-payroll-reverser decides his own request. 403: he does not hold the approval policy. (That the decider is
               neither the requester nor the poster even with it is proved by Pay056ReversalSodTests.)
            6. payroll-approver approves it. The run is reversed; its batch is voided and no reversal batch is written; no order of
               the run is left open (none was made: orders are made when a batch is sent); the adjustment is free; the request is
               approved.
            7. payroll-processor creates and calculates a new Regular run of April 2028: the period is free, and the run takes the
               adjustment.
            8. Negative (D-PAY-16): the Regular run of May 2028 is calculated, approved and posted, and a-payroll-poster sends its
               batch (process). a-payroll-reverser asks for its reversal; payroll-approver approves. 400 with the message that
               points to a correction run; the run is posted and its batch sent, as they were. The approver then rejects the request
               with a note, so nothing is left open.
            """);

        var existing = await RunsCoveringAsync(April, May);
        AcceptanceGate.RequireData(s.Recorder, existing.Length == 0,
            "للجهة A تشغيل رواتب في نيسان أو أيار 2028: الصنف يحتاج مكدساً جديداً", $"Agency A already has a payroll run of April or May 2028 ({existing}): start a fresh stack.");
        var employee = (await PickEmployeesAsync(s.Recorder, "56e", 1, skip: 36))[0];
        s.Recorder.WriteBefore(new { scenario = "56e", explicitDomainSnapshot = true, runsOfAprilOrMay2028 = existing, employee });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "56e");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "56e");
        using var poster = await Api.OpenAsync(s.Recorder, Poster, "56e");
        using var reverser = await Api.OpenAsync(s.Recorder, Reverser, "56e");
        using var foreigner = await Api.OpenAsync(s.Recorder, ForeignProcessor, "56e");

        // 2. April is posted.
        var adjustment = await ApprovedAdjustmentAsync(s, "56e-adjustment", employee, 9_000m, AprilReason);
        var run = await CalculateMonthAsync(processor, "56e", April);
        await SubmitAndApproveAsync(processor, approver, "56e", run);
        var audit = await CloseAuditAsync(s.Recorder, run, "56e");
        await PostRunAsync(poster, "56e", run);
        var posted = await RunRowAsync(run);
        var batchPosted = await BatchesAsync(run);

        // 3–4. The request.
        var requests = $"api/payroll/runs/{run}/reversal-requests";
        var (noReason, noReasonBody, _) = await reverser.PostAsync("request-without-a-reason", requests, new { reason = "" });
        var requestsAfterNoReason = await Count($"SELECT count(*) FROM \"PayrollReversalRequests\" WHERE \"PayrollRunId\"='{run}';");
        const string reason = "رُحّل تشغيل نيسان قبل اعتماد تسوية أحد الموظفين.";
        var (requested, requestedBody, _) = await reverser.PostAsync("request", requests, new { reason });
        Assert.True(requested == HttpStatusCode.OK, $"56e: the request answered {(int)requested}: {Text(requestedBody, "error")}");
        var request = requestedBody.GetProperty("id").GetGuid().ToString("D");
        var afterRequest = await RunRowAsync(run) + "|" + await BatchesAsync(run);
        var (second, secondBody, _) = await reverser.PostAsync("second-request", requests, new { reason = "طلب ثانٍ والأول بانتظار القرار" });
        var (foreign, _, _) = await foreigner.PostAsync("foreign-cancel", $"api/payroll/runs/{run}/cancel", new { reason = "محاولة من جهة أخرى" });
        var (listed, list, _) = await approver.GetAsync("list-pending", "api/payroll/reversal-requests?status=Pending");

        // 5. His own decision.
        var decide = $"api/payroll/reversal-requests/{request}/decide";
        var (own, _, _) = await reverser.PostAsync("own-decision", decide, new { approve = true });
        var afterOwn = await Db($"SELECT \"Status\" FROM \"PayrollReversalRequests\" WHERE \"Id\"='{request}';");

        // 6. The approval.
        var (approved, approvedBody, _) = await approver.PostAsync("approve-reversal", decide, new { approve = true, notes = "لم تُرسل الدفعة: يُعكس التشغيل" });
        var reversed = await RunRowAsync(run);
        var batchReversed = await BatchesAsync(run);
        var openOrders = await Count($"SELECT count(*) FROM \"PaymentOrders\" WHERE \"PayrollRunId\"='{run}' AND \"Status\" NOT IN ('Cancelled','Rejected');");
        var adjustmentAfterReversal = await AdjustmentRunAsync(adjustment);
        var requestAfter = await Db(
            $"SELECT \"Status\" || '|' || (\"DecidedBy\" IS NOT NULL AND \"DecidedBy\" <> \"RequestedBy\")::text || '|' || COALESCE(\"DecisionNotes\",'') FROM \"PayrollReversalRequests\" WHERE \"Id\"='{request}';");

        // 7. A new run of April.
        var again = await CalculateMonthAsync(processor, "56e-again", April);
        var newLine = await LineAsync(again, employee);
        var adjustmentInTheNewRun = await AdjustmentRunAsync(adjustment);

        // 8. D-PAY-16: May's batch has left.
        var may = await CalculateMonthAsync(processor, "56e-may", May);
        await SubmitAndApproveAsync(processor, approver, "56e-may", may);
        var mayAudit = await CloseAuditAsync(s.Recorder, may, "56e-may");
        await PostRunAsync(poster, "56e-may", may);
        var mayBatch = await Db($"SELECT \"Id\"::text FROM \"PaymentBatches\" WHERE \"PayrollRunId\"='{may}' AND \"BatchKind\"='Disbursement';");
        var (processed, processedBody, _) = await poster.PostAsync("process-may", $"api/payroll/payment-batches/{mayBatch}/process");
        var sentByTheChannel = await Db($"SELECT \"Status\" FROM \"PaymentBatches\" WHERE \"Id\"='{mayBatch}';") == "Submitted";
        object? plantedSubmitted = null;
        if (!sentByTheChannel)
        {
            // The disbursement gate refused the batch on this stack (a hold or a fraud alert of the seed). What is proved here is the
            // reversal's refusal once a batch has left, so the batch's state is then a test-only precondition, recorded.
            await Db($"UPDATE \"PaymentBatches\" SET \"Status\"='Submitted', \"SubmittedAt\"=now() WHERE \"Id\"='{mayBatch}';");
            plantedSubmitted = new
            {
                testOnlyPrecondition = "the batch was set Submitted by psql: the disbursement gate refused it on this stack",
                process = (int)processed,
                refusal = Text(processedBody, "error")
            };
        }

        var mayBefore = await RunRowAsync(may) + "|" + await BatchesAsync(may);
        var (mayRequested, mayRequestedBody, _) = await reverser.PostAsync("request-may", $"api/payroll/runs/{may}/reversal-requests", new { reason = "صُرف بدل في أيار بلا استحقاق." });
        Assert.True(mayRequested == HttpStatusCode.OK, $"56e: the request for May answered {(int)mayRequested}: {Text(mayRequestedBody, "error")}");
        var mayRequest = mayRequestedBody.GetProperty("id").GetGuid().ToString("D");
        var (refused, refusedBody, _) = await approver.PostAsync("approve-after-the-batch-left", $"api/payroll/reversal-requests/{mayRequest}/decide", new { approve = true });
        var mayAfter = await RunRowAsync(may) + "|" + await BatchesAsync(may);
        var (closed, closedBody, _) = await approver.PostAsync("reject-may", $"api/payroll/reversal-requests/{mayRequest}/decide",
            new { approve = false, notes = "خرجت الدفعة إلى المصرف: يُصحَّح بتشغيل تصحيح." });
        var pendingLeft = await Count($"SELECT count(*) FROM \"PayrollReversalRequests\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"='Pending';");

        await AuditAsync(s, run, request, again, may, mayRequest);
        s.Checks(new
        {
            run,
            audit,
            posted,
            runFormat = "status|revision|frozen|validated|total",
            batchPosted,
            requestWithoutAReason = (int)noReason,
            requestWithoutAReasonError = Text(noReasonBody, "error"),
            requestsAfterNoReason,
            request,
            afterRequest,
            secondRequest = (int)second,
            secondRequestError = Text(secondBody, "error"),
            anotherAgencysProcessor = (int)foreign,
            pendingList = (int)listed,
            pendingListHoldsIt = list.ValueKind == JsonValueKind.Array && list.EnumerateArray().Any(r => r.GetProperty("id").GetGuid().ToString("D") == request),
            theReverserDecidingHisOwnRequest = (int)own,
            afterOwn,
            approval = (int)approved,
            approvalError = Text(approvedBody, "error"),
            reversed,
            batchReversed,
            openOrders,
            adjustmentAfterReversal,
            requestAfter,
            requestFormat = "status|decided by another person|note",
            newRun = again,
            newLine,
            lineFormat = "gross|deductions|adjustments|net",
            adjustmentInTheNewRun,
            may,
            mayAudit,
            process = (int)processed,
            sentByTheChannel,
            plantedSubmitted,
            mayBefore,
            approvalAfterTheBatchLeft = (int)refused,
            approvalAfterTheBatchLeftError = Text(refusedBody, "error"),
            mayAfter,
            rejectionOfThatRequest = (int)closed,
            rejectionError = Text(closedBody, "error"),
            pendingRequestsLeft = pendingLeft,
            freeAdjustmentsWithAnAmountLeft = await FreeAdjustmentsWithAnAmountAsync()
        });

        Assert.StartsWith($"{Posted}|", posted, StringComparison.Ordinal);
        Assert.Equal("Pending:Disbursement", batchPosted);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        Assert.Equal(0, requestsAfterNoReason);
        Assert.Equal($"{posted}|Pending:Disbursement", afterRequest);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Equal(HttpStatusCode.NotFound, foreign);
        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.Contains(list.EnumerateArray(), r => r.GetProperty("id").GetGuid().ToString("D") == request);
        Assert.Equal(HttpStatusCode.Forbidden, own);
        Assert.Equal("Pending", afterOwn);
        Assert.True(approved == HttpStatusCode.OK, $"56e: the approval of the reversal answered {(int)approved}: {Text(approvedBody, "error")}");
        Assert.StartsWith($"{Reversed}|", reversed, StringComparison.Ordinal);
        // Voided, and no reversal batch that would claim to recover money nobody paid.
        Assert.Equal("Voided:Disbursement", batchReversed);
        Assert.Equal(0, openOrders);
        Assert.Equal("free", adjustmentAfterReversal);
        Assert.Equal("Approved|true|لم تُرسل الدفعة: يُعكس التشغيل", requestAfter);
        Assert.NotEqual(run, again);
        Assert.Equal(9_000m, Money(newLine.Split('|')[2]));
        Assert.Equal(again, adjustmentInTheNewRun);
        // D-PAY-16.
        Assert.EndsWith("|Submitted:Disbursement", mayBefore, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Contains("تشغيل تصحيح", Text(refusedBody, "error"), StringComparison.Ordinal);
        Assert.Equal(mayBefore, mayAfter);
        Assert.True(closed == HttpStatusCode.OK, $"56e: the rejection of May's request answered {(int)closed}: {Text(closedBody, "error")}");
        Assert.Equal(0, pendingLeft);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 56f

    [SkippableFact]
    public async Task Ac56f_a_correction_takes_only_the_adjustments_aimed_at_it_and_a_negative_difference_is_recovered_in_the_next_cycle()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "56f",
            "AC-PAY-56f: a correction of a posted run with an adjustment of −30,000 aimed at it passes the validation and creates a recovery deduction of 30,000 that the next cycle's run applies once the correction is posted. Negative: an adjustment not aimed at it does not enter it.",
            Processor, Approver, Poster);
        s.Steps("""
            1. Precondition, in before.json: January's posted run (56a) and the employee 56a raised are found; agency A has no run of
               June 2028; the run has no correction.
            2. On the adjustment's form payroll-processor enters −30,000 for the employee aimed at January's run, and an ordinary
               adjustment of +4,321 aimed at nothing; payroll-approver approves both.
            3. payroll-processor creates the correction of January's run (POST api/payroll/runs/{id}/corrections). It is calculated,
               validated and frozen like a regular run; it has one line, the employee's: no base, no deduction, no tax, adjustments
               −30,000, net 0. Negative: the ordinary adjustment did not enter it and is still free.
            4. A recovery deduction of 30,000 exists for the employee, made by the system, approved, due on 2028-02-01 (the first day
               of the period after the corrected one), naming the correction's line, applied in no run yet.
            5. A second correction of January's run while this one is not posted answers 409. The general creation of a run
               (POST api/payroll/runs with the type «correction») answers 400: a correction is made from its source only.
            6. The correction goes the way of every run: payroll-processor submits it, payroll-approver approves it, the closed audit
               record is made (test-only precondition), a-payroll-poster posts it. Until then no regular run takes its recovery: a
               correction that is only calculated can still be rejected or cancelled (proved by Pay056ReviewFixTests).
            7. payroll-processor creates and calculates the Regular run of June 2028 (the next run of the agency): it applies the
               recovery (a deduction component of the employee's line names it) and takes the ordinary adjustment.
            """);

        var january = await RunOfAsync(January, Posted);
        var employee = await EmployeeOfAdjustmentAsync(RaiseReason);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(january, out _) && Guid.TryParse(employee, out _),
            "56f: لا يوجد تشغيل كانون الثاني المرحّل (56a): يُشغَّل الصنف كاملاً بترتيبه",
            "56f: January's posted run (56a) is not there: the class runs whole, in the order of its scenarios.");
        var existing = await RunsCoveringAsync(June, June);
        var corrections = await Count($"SELECT count(*) FROM \"PayrollRuns\" WHERE \"SourceRunId\"='{january}';");
        AcceptanceGate.RequireData(s.Recorder, existing.Length == 0 && corrections == 0,
            "للجهة A تشغيل رواتب في حزيران 2028 أو لتشغيل كانون الثاني تصحيح: الصنف يحتاج مكدساً جديداً",
            $"Agency A already has a payroll run of June 2028 ({existing}) or January's run has a correction ({corrections}): start a fresh stack.");
        var januaryBefore = await RunRowAsync(january);
        var januaryLineBefore = await LineAsync(january, employee);
        s.Recorder.WriteBefore(new
        {
            scenario = "56f",
            explicitDomainSnapshot = true,
            january,
            januaryBefore,
            employee,
            januaryLineBefore,
            runsOfJune2028 = existing,
            correctionsOfJanuary = corrections
        });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "56f");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "56f");
        using var poster = await Api.OpenAsync(s.Recorder, Poster, "56f");

        // 2. One adjustment aimed at January's run, one aimed at nothing.
        var targeted = await ApprovedAdjustmentAsync(s, "56f-targeted", employee, -30_000m, TargetedReason, targetRun: january);
        var untargeted = await ApprovedAdjustmentAsync(s, "56f-untargeted", employee, 4_321m, UntargetedReason);
        var form = await s[Processor].GetAsync("56f-form", $"{AdjustmentsUi}/Create");

        // 3. The correction.
        var path = $"api/payroll/runs/{january}/corrections";
        var (created, createdBody, _) = await processor.PostAsync("correct", path);
        Assert.True(created == HttpStatusCode.OK, $"56f: the correction answered {(int)created}: {Text(createdBody, "error")}");
        var correction = createdBody.GetProperty("id").GetGuid().ToString("D");
        var correctionRow = await Db(
            "SELECT \"RunType\" || '|' || \"Status\"::text || '|' || \"ResultsFrozen\"::text || '|' || \"ValidatedAtCalculate\"::text || '|' || \"TotalNetPay\"::text || '|' || " +
            $"\"SourceRunId\"::text FROM \"PayrollRuns\" WHERE \"Id\"='{correction}';");
        var correctionLines = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{correction}';");
        var correctionLine = await LineAsync(correction, employee);
        var correctionLineId = await LineIdAsync(correction, employee);
        var components = await Db(
            "SELECT COALESCE(string_agg(\"ComponentType\" || ':' || \"Amount\"::text || ':' || COALESCE(\"ReferenceId\"::text,'∅'), ',' ORDER BY \"Amount\"), '') " +
            $"FROM \"SalaryLineComponents\" WHERE \"SalaryLineId\"='{correctionLineId}';");
        var targetedAfter = await AdjustmentRunAsync(targeted);
        var untargetedAfter = await AdjustmentRunAsync(untargeted);

        // 4. The recovery.
        var recovery = await Db(
            "SELECT COALESCE((SELECT \"Id\"::text || '|' || \"Amount\"::text || '|' || to_char(\"DueDate\", 'YYYY-MM-DD') || '|' || \"ApprovalStatus\" || '|' || \"Origin\" || '|' || " +
            "COALESCE(\"AppliedInRunId\"::text,'free') || '|' || \"SourceReference\" FROM \"DeductionRecords\" " +
            $"WHERE \"EmployeeId\"='{employee}' AND \"DeductionType\"='CorrectionRecovery' AND \"TenantId\"='{TenantA}'), 'none');");
        var recoveryId = recovery.Split('|')[0];

        // 5. One open correction per source run, and one door to a correction.
        var (second, secondBody, _) = await processor.PostAsync("correct-again", path);
        var (general, generalBody, _) = await processor.PostAsync("correction-by-the-general-creation", "api/payroll/runs", new
        {
            periodStart = January.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = January.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = January.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runType = CorrectionRunType,
            sourceRunId = january
        });
        var correctionsOfJanuary = await Count($"SELECT count(*) FROM \"PayrollRuns\" WHERE \"SourceRunId\"='{january}';");

        // 6. The correction is reviewed, approved and posted like every run.
        await SubmitAndApproveAsync(processor, approver, "56f-correction", correction);
        var audit = await CloseAuditAsync(s.Recorder, correction, "56f-correction");
        await PostRunAsync(poster, "56f-correction", correction);
        var correctionPosted = await RunRowAsync(correction) + "|" + await BatchesAsync(correction);
        var recoveryBeforeJune = await Db($"SELECT COALESCE(\"AppliedInRunId\"::text,'free') FROM \"DeductionRecords\" WHERE \"Id\"='{recoveryId}';");

        // 7. The next run of the agency takes the recovery back, and takes the ordinary adjustment.
        var june = await CalculateMonthAsync(processor, "56f-june", June);
        var juneLine = await LineAsync(june, employee);
        var juneLineId = await LineIdAsync(june, employee);
        var recoveryComponent = await Db(
            "SELECT COALESCE((SELECT \"ComponentType\" || ':' || \"Amount\"::text FROM \"SalaryLineComponents\" " +
            $"WHERE \"SalaryLineId\"='{juneLineId}' AND \"ReferenceId\"='{recoveryId}'), 'none');");
        var recoveryAppliedIn = await Db($"SELECT COALESCE(\"AppliedInRunId\"::text,'free') FROM \"DeductionRecords\" WHERE \"Id\"='{recoveryId}';");
        var untargetedInJune = await AdjustmentRunAsync(untargeted);
        var januaryAfter = await RunRowAsync(january);
        var januaryLineAfter = await LineAsync(january, employee);

        await AuditAsync(s, correction, targeted, untargeted, recoveryId, june);
        s.Checks(new
        {
            january,
            employee,
            targeted,
            untargeted,
            theFormOffersThePostedRun = form.Contains(january, StringComparison.OrdinalIgnoreCase),
            correction,
            correctionRow,
            correctionFormat = "type|status|frozen|validated|total|source",
            correctionLines,
            correctionLine,
            lineFormat = "gross|deductions|adjustments|net",
            components,
            targetedAfter,
            untargetedAfter,
            recovery,
            recoveryFormat = "id|amount|due|approval|origin|applied in|source reference",
            secondCorrection = (int)second,
            secondCorrectionError = Text(secondBody, "error"),
            correctionByTheGeneralCreation = (int)general,
            correctionByTheGeneralCreationError = Text(generalBody, "error"),
            correctionsOfJanuary,
            audit,
            correctionPosted,
            runFormat = "status|revision|frozen|validated|total|batches",
            recoveryBeforeJune,
            june,
            juneLine,
            recoveryComponent,
            recoveryAppliedIn,
            untargetedInJune,
            januaryBefore,
            januaryAfter,
            januaryLineBefore,
            januaryLineAfter,
            freeAdjustmentsWithAnAmountLeft = await FreeAdjustmentsWithAnAmountAsync()
        });

        Assert.Contains(january, form, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"Correction|{Calculated}|true|true|0.00|{january}", correctionRow);
        Assert.Equal(1, correctionLines);
        // No base and no tax on the difference: the line is the adjustment, and a negative one pays nothing.
        Assert.Equal("0.00|0.00|-30000.00|0.00", correctionLine);
        Assert.Equal($"Adjustment:-30000.00:{targeted}", components);
        Assert.Equal(correction, targetedAfter);
        Assert.Equal("free", untargetedAfter);
        Assert.Equal($"{recoveryId}|30000.00|2028-02-01|Approved|System|free|CorrectionRecovery:{correctionLineId.Replace("-", "", StringComparison.Ordinal)}", recovery);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Equal(HttpStatusCode.BadRequest, general);
        Assert.Equal(1, correctionsOfJanuary);
        Assert.Equal($"{Posted}|1|true|true|0.00|Pending:Disbursement", correctionPosted);
        Assert.Equal("free", recoveryBeforeJune);
        Assert.Equal("Deduction:-30000.00", recoveryComponent);
        Assert.Equal(june, recoveryAppliedIn);
        Assert.Equal(june, untargetedInJune);
        Assert.Equal(4_321m, Money(juneLine.Split('|')[2]));
        // The posted run and its line are as they were: a correction corrects beside the run, it does not rewrite it.
        Assert.Equal(januaryBefore, januaryAfter);
        Assert.Equal(januaryLineBefore, januaryLineAfter);
        Assert.Equal(0, await Count(
            $"SELECT count(*) FROM \"PayrollAdjustments\" WHERE \"Id\" IN ('{targeted}','{untargeted}') AND \"PayrollRunId\" IS NULL;"));
        s.Recorder.WriteResult("Pass");
    }
}
