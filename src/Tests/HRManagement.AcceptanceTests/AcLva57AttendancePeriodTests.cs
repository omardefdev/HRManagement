using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-LVA-57a–h (PLAN-57) on the prod-like stack, through the gateway: an attendance period that really closes, corrections that
/// become differences, and the handover to payroll. A branch fills a month by the bulk entry and closes its tree; the close writes one
/// versioned summary per employee (a sub-unit's employee and one whose service ended inside the month included), locks their days,
/// and payroll receives each summary. A change after the close is a correction a central person who is not the requester approves;
/// payroll records it as a difference, applied by the month's own run when that is not calculated yet, and carried to the next cycle
/// when the month's run is frozen. Payroll knows before a calculation whose attendance is not closed.
/// <para>
/// The scenarios run in the order of their names. Each has its own month of 2025 in agency A
/// (<c>testdata/acceptance/lva057-periods.json</c>: months in the past, with no run of agency A in the seed and taken by no other
/// class): January 57a and 57g, February 57b, March 57c, April and May 57d, June 57e, July 57f and 57h. 57g reads the month 57a
/// closed.
/// </para>
/// <para>
/// <b>Test-only preconditions</b>, each recorded in its scenario's steps: every scenario starts with a reset of its own month (its
/// attendance records, summaries, closures and corrections in HR.Core and its receipts and free absence rows in Payroll are deleted
/// by psql; its own payroll runs are cancelled through PLAN-56's API, never deleted), so a second run on the same stack gives the same
/// numbers; 57a's employee whose service ended inside the month is written by psql (no screen ends a service on a past date); 57g's
/// leave events and 57d's duplicate and older summary versions are published to the broker by <c>AcceptanceMessagingHelper</c> (the
/// leave's own approval is LVA-A's, wave 2). Everything a scenario proves goes through the screens or the API.
/// </para>
/// <para>
/// <b>Isolation.</b> The classes of the group run one at a time, in an order that changes. This class writes attendance only in its
/// own months (and, for «no future day», in the current month of one small unit, removed again); its payroll runs live in its own
/// months, and each scenario that calculates cancels its runs at its end (PLAN-56's API) and deletes the carried difference a
/// cancellation freed, so that no other class finds a run of this class by its state or has its own run take a carried row of
/// agency A; its other rows are dated in its own months, which only its own runs take. The zero adjustments payroll notes for an
/// injected leave are deleted too. It asserts on its own employees and rows by id. Nothing stops or recreates a container.
/// </para>
/// The leave schedule reconciler's rule (it reads leave-made days only) is proved by <c>Lva057LeaveAttendanceTests</c>: its job runs
/// hourly and this class restarts no container.
/// </summary>
[RequiredAcceptance]
[TestCaseOrderer(ScenarioNameOrderer.TypeName, ScenarioNameOrderer.AssemblyName)]
public class AcLva57AttendancePeriodTests
{
    private const string Ac = "AC-LVA-57";
    private const string Br1 = "br1-mgr";
    private const string Br2 = "br2-mgr";
    private const string Central = "central-a";
    private const string Central2 = "central-a2";
    private const string Processor = "payroll-processor";
    private const string Approver = "payroll-approver";
    private const string Outsider = "outsider-b";
    private const string Core = "hr_core_db";
    private const string Payroll = "payroll_db";

    private const string AttendanceUi = "ui/hr-core/DailyAttendanceRecords";
    private const string PeriodsUi = "ui/hr-core/AttendancePeriods";
    private const string CorrectionsUi = "ui/hr-core/AttendanceCorrections";
    private const string PeriodsApi = "api/hr-core/api/attendance/periods";
    private const string CorrectionsApi = "api/hr-core/api/attendance/corrections";

    private const int Draft = 0;
    private const int Calculated = 1;
    private const int UnderReview = 3;
    private const int Cancelled = 8;

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string Hq = AcceptanceSeedIds.HqOrgUnitId.ToString("D");
    private static readonly string Rif = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string RifHr = AcceptanceSeedIds.Br1D1OrgUnitId.ToString("D");
    private static readonly string Employee = AcceptanceSeedIds.EmpBr1Id.ToString("D");
    private static readonly string SubUnitEmployee = AcceptanceSeedIds.EmpD1Id.ToString("D");

    /// <summary>The employee whose service ended inside January 2025: written by psql in 57a, with this id.</summary>
    private const string Leaver = "a0000057-0000-4000-8000-0000000000e1";

    private const string CorrectionReason = "كان في مهمة رسمية موثقة بكتاب الإدارة";

    private static readonly Lazy<(IReadOnlyDictionary<string, DateOnly> Months, IReadOnlyList<DateOnly> Holidays)> Reserved = new(() =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "testdata", "acceptance", "lva057-periods.json")));
        var months = json.RootElement.GetProperty("months").EnumerateObject()
            .ToDictionary(p => p.Name, p => DateOnly.ParseExact(p.Value.GetString() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparer.Ordinal);
        var holidays = json.RootElement.GetProperty("holidays").EnumerateArray()
            .Select(h => DateOnly.ParseExact(h.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        return (months, holidays);
    });

    private static DateOnly Month(string key) => Reserved.Value.Months[key];

    private static Task<string> Db(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Ym(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateOnly EndOf(DateOnly month) => month.AddMonths(1).AddDays(-1);

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    private static string Snip(string body) => body.Length > 400 ? body[..400] : body;

    /// <summary>How many times a page holds a text: a form states some rules as hints, so a refusal is the text once more than on the form.</summary>
    private static int Count(string body, string text)
    {
        var n = 0;
        for (var i = body.IndexOf(text, StringComparison.Ordinal); i >= 0; i = body.IndexOf(text, i + text.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static bool IsRestDay(DateOnly day) => day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    /// <summary>The working days of a month as the acceptance layer configures them: not a Friday, a Saturday or a reserved holiday.</summary>
    private static List<DateOnly> WorkingDays(DateOnly month, DateOnly? until = null)
    {
        var days = new List<DateOnly>();
        for (var day = month; day <= (until ?? EndOf(month)); day = day.AddDays(1))
        {
            if (!IsRestDay(day) && !Reserved.Value.Holidays.Contains(day))
                days.Add(day);
        }

        return days;
    }

    private static async Task<bool> WaitAsync(Func<Task<bool>> reached, int seconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await reached())
                return true;
            await Task.Delay(1000);
        }

        return await reached();
    }

    // ---------------------------------------------------------------- the API, as one persona

    private sealed class Api(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
    {
        public static async Task<Api> PayrollAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")), recorder, persona, tag);

        public static async Task<Api> AttendanceAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreateAttendanceClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")), recorder, persona, tag);

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

        public void Dispose() => http.Dispose();
    }

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    // ---------------------------------------------------------------- hr_core_db

    /// <summary>An employee's summary of a month: id|version|working|present|absent|onLeave|remote|unrecorded, or empty.</summary>
    private static Task<string> SummaryAsync(string employee, DateOnly month) => Db(Core,
        "SELECT COALESCE((SELECT s.\"Id\"::text || '|' || s.\"Version\" || '|' || s.\"WorkingDays\" || '|' || s.\"PresentDays\" || '|' || s.\"AbsentDays\" || '|' || " +
        "s.\"OnLeaveDays\" || '|' || s.\"RemoteDays\" || '|' || s.\"UnrecordedWorkingDays\" FROM \"AttendanceEmployeeSummaries\" s " +
        $"JOIN \"AttendancePeriods\" p ON p.\"Id\" = s.\"AttendancePeriodId\" WHERE p.\"TenantId\"='{TenantA}' AND p.\"PeriodStart\"='{Day(month)}' " +
        $"AND s.\"EmployeeId\"='{employee}'), '');");

    private static async Task<(string Id, int Version, int Working, int Present, int Absent, int OnLeave)> ReadSummaryAsync(string employee, DateOnly month)
    {
        var parts = (await SummaryAsync(employee, month)).Split('|');
        return parts.Length < 8
            ? ("", 0, 0, 0, 0, 0)
            : (parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture),
                int.Parse(parts[3], CultureInfo.InvariantCulture), int.Parse(parts[4], CultureInfo.InvariantCulture), int.Parse(parts[5], CultureInfo.InvariantCulture));
    }

    private static Task<string> SummaryCountAsync(DateOnly month) => Db(Core,
        "SELECT count(*)::text FROM \"AttendanceEmployeeSummaries\" s JOIN \"AttendancePeriods\" p ON p.\"Id\" = s.\"AttendancePeriodId\" " +
        $"WHERE p.\"TenantId\"='{TenantA}' AND p.\"PeriodStart\"='{Day(month)}';");

    /// <summary>A record of an employee's day: id|status|source|employee|date, or empty.</summary>
    private static Task<string> RecordAsync(string employee, DateOnly day) => Db(Core,
        "SELECT COALESCE((SELECT \"Id\"::text || '|' || \"Status\" || '|' || \"Source\" || '|' || \"EmployeeId\"::text || '|' || \"AttendanceDate\"::text " +
        $"FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\"='{employee}' AND \"AttendanceDate\"='{Day(day)}'), '');");

    private static async Task<string> RecordIdAsync(string employee, DateOnly day, string step)
    {
        var id = (await RecordAsync(employee, day)).Split('|')[0];
        Assert.True(Guid.TryParse(id, out _), $"{step}: no attendance record of {employee} on {Day(day)}.");
        return id;
    }

    /// <summary>
    /// The employees a close of a unit's tree covers in a month, as <c>AttendanceEligibility</c> reads them: of the unit or a unit
    /// below it, hired by the month's end, not waiting for activation, and — when the service has ended — ended on or after the
    /// month's first day (the last status history row that ends a service, as a Damascus date).
    /// </summary>
    private static Task<string> TreeEmployeesAsync(string unit, DateOnly month) => Db(Core,
        "SELECT COALESCE(string_agg(e.\"Id\"::text, ',' ORDER BY e.\"Id\"), '') FROM \"Employees\" e JOIN \"OrgUnits\" o ON o.\"Id\" = e.\"PrimaryOrgUnitId\" " +
        $"WHERE e.\"TenantId\"='{TenantA}' AND o.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{unit}') || '%' AND {InServiceIn(month)};");

    /// <summary>The service's rule of who is in service in a month, on the <c>Employees</c> row <c>e</c>.</summary>
    private static string InServiceIn(DateOnly month)
    {
        const string ended = "'Terminated','Retired','Resigned','Deceased','Merged','Anonymized'";
        // The end of a service: the last history row that moved the employee into an ended status; the earliest ended row when none did.
        return $"e.\"CurrentStatus\" <> 10 AND (e.\"HireDate\" IS NULL OR e.\"HireDate\" < '{Day(EndOf(month).AddDays(1))}') " +
            "AND (e.\"CurrentStatus\" NOT IN (2, 3, 6, 7, 8, 9) OR (SELECT (COALESCE(" +
            $"max(h.\"ChangedAt\") FILTER (WHERE h.\"OldStatus\" IS NULL OR h.\"OldStatus\" = '' OR h.\"OldStatus\" NOT IN ({ended})), min(h.\"ChangedAt\")) " +
            "AT TIME ZONE 'Asia/Damascus')::date FROM \"EmploymentStatusHistories\" h " +
            $"WHERE h.\"EmployeeId\" = e.\"Id\" AND h.\"NewStatus\" IN ({ended})) >= '{Day(month)}')";
    }

    /// <summary>Whether an employee sits in a unit or in a unit below it.</summary>
    private static async Task<bool> InTreeAsync(string employee, string unit) => await Db(Core,
        "SELECT count(*)::text FROM \"Employees\" e JOIN \"OrgUnits\" o ON o.\"Id\" = e.\"PrimaryOrgUnitId\" " +
        $"WHERE e.\"Id\"='{employee}' AND o.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{unit}') || '%';") == "1";

    /// <summary>The test-only reset of a month of agency A (the SQL is in each scenario's steps).</summary>
    private static async Task ResetMonthAsync(RecruitmentSession s, string step, params DateOnly[] months)
    {
        // Its own payroll runs are cancelled through PLAN-56's API: a cancellation frees what the run applied. Every month's runs
        // first: a difference carried from one month is applied by the next month's run, and is deleted only once that run let it go.
        foreach (var month in months)
        {
            var runs = (await Db(Payroll,
                $"SELECT COALESCE(string_agg(\"Id\"::text || ':' || \"Status\", ','), '') FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' " +
                $"AND \"RunType\"='Regular' AND \"PeriodStart\"::date = '{Day(month)}' AND \"Status\" NOT IN (2, 5, 8);")).Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var run in runs)
            {
                var (id, status) = (run.Split(':')[0], int.Parse(run.Split(':')[1], CultureInfo.InvariantCulture));
                using var api = await Api.PayrollAsync(s.Recorder, status is UnderReview or 4 ? Approver : Processor, s.Key);
                var (cancelled, body, _) = await api.PostAsync($"{step}-reset-cancel-{Ym(month)}", $"api/payroll/runs/{id}/cancel",
                    new { reason = $"إعادة ضبط شهر {Ym(month)} قبل سيناريو AC-LVA-{s.Key}" });
                Assert.True(cancelled == HttpStatusCode.OK, $"{step}: the run of {Ym(month)} left by an earlier run could not be cancelled ({(int)cancelled}): {Text(body, "error")}");
            }
        }

        foreach (var month in months)
        {
            await Db(Payroll,
                $"DELETE FROM \"AbsenceDeductions\" WHERE \"TenantId\"='{TenantA}' AND \"OriginalPeriodStart\"='{Day(month)}' AND \"AppliedInRunId\" IS NULL; " +
                $"DELETE FROM \"AttendanceSummaryReceipts\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}';");
            await Db(Core,
                $"DELETE FROM \"AttendanceCorrections\" WHERE \"TenantId\"='{TenantA}' AND \"AttendanceDate\" BETWEEN '{Day(month)}' AND '{Day(EndOf(month))}'; " +
                $"DELETE FROM \"AttendanceEmployeeSummaries\" WHERE \"AttendancePeriodId\" IN (SELECT \"Id\" FROM \"AttendancePeriods\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}'); " +
                $"DELETE FROM \"AttendancePeriodClosures\" WHERE \"AttendancePeriodId\" IN (SELECT \"Id\" FROM \"AttendancePeriods\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}'); " +
                $"DELETE FROM \"AttendancePeriods\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}'; " +
                $"DELETE FROM \"DailyAttendanceRecords\" WHERE \"TenantId\"='{TenantA}' AND \"AttendanceDate\" BETWEEN '{Day(month)}' AND '{Day(EndOf(month))}';");
        }
    }

    /// <summary>
    /// Test-only clean-up after an injected leave: payroll notes every leave decision as an adjustment of zero with no run, which the
    /// agency's next regular run — another class's — would take. The notes of this leave are deleted once they arrived.
    /// </summary>
    private static async Task RemoveLeaveNotesAsync(Guid leave, int expected)
    {
        var mine = $"FROM \"PayrollAdjustments\" WHERE \"TenantId\"='{TenantA}' AND \"PayrollRunId\" IS NULL AND \"Amount\" = 0 AND \"AdjustmentReason\" LIKE '%LeaveRequestId={leave:D}%'";
        await WaitAsync(async () => int.Parse(await Db(Payroll, $"SELECT count(*)::text {mine};"), CultureInfo.InvariantCulture) >= expected, 60);
        await Db(Payroll, $"DELETE {mine};");
    }

    /// <summary>
    /// The end of a scenario that calculated: its own runs are cancelled through PLAN-56's API (never deleted), so that no class
    /// that looks for the agency's run in a state finds this class's; the cancellation frees what the runs applied, and a carried
    /// difference among it is deleted (test-only), so that no other class's run takes it. Runs whatever the scenario's result.
    /// </summary>
    private static async Task EndRunsAsync(RecruitmentSession s, string step, params DateOnly[] months)
    {
        foreach (var month in months)
        {
            var runs = (await Db(Payroll,
                $"SELECT COALESCE(string_agg(\"Id\"::text || ':' || \"Status\", ','), '') FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' " +
                $"AND \"RunType\"='Regular' AND \"PeriodStart\"::date = '{Day(month)}' AND \"Status\" NOT IN (2, 5, 8);")).Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var run in runs)
            {
                var (id, status) = (run.Split(':')[0], int.Parse(run.Split(':')[1], CultureInfo.InvariantCulture));
                using var api = await Api.PayrollAsync(s.Recorder, status is UnderReview or 4 ? Approver : Processor, s.Key);
                await api.PostAsync($"{step}-end-cancel-{Ym(month)}", $"api/payroll/runs/{id}/cancel", new { reason = $"نهاية سيناريو AC-LVA-{s.Key}: تشغيل تجريبي لشهر {Ym(month)}" });
            }
        }

        var mine = string.Join(",", months.Select(m => $"'{Day(m)}'"));
        await Db(Payroll,
            $"DELETE FROM \"AbsenceDeductions\" WHERE \"TenantId\"='{TenantA}' AND \"CarryForward\" AND \"AppliedInRunId\" IS NULL AND \"OriginalPeriodStart\" IN ({mine});");
    }

    private const string EndSteps =
        "At the end, whatever the result: the scenario's own runs are cancelled through POST api/payroll/runs/{id}/cancel, and a carried difference a cancellation freed is deleted (psql, payroll_db), so that no other class's run finds or takes them.";

    private const string ResetSteps = """
        Test-only reset of the scenario's month(s), so a second run on the same stack gives the same numbers:
          - its own payroll runs still alive are cancelled through POST api/payroll/runs/{id}/cancel (never deleted);
          - payroll_db: DELETE FROM "AbsenceDeductions" WHERE "TenantId"=A AND "OriginalPeriodStart"=<month> AND "AppliedInRunId" IS NULL;
                        DELETE FROM "AttendanceSummaryReceipts" WHERE "TenantId"=A AND "PeriodStart"=<month>;
          - hr_core_db: DELETE FROM "AttendanceCorrections" (the month's days), "AttendanceEmployeeSummaries", "AttendancePeriodClosures",
                        "AttendancePeriods" (the month) and "DailyAttendanceRecords" (the month's days) WHERE "TenantId"=A.
        """;

    // ---------------------------------------------------------------- payroll_db

    /// <summary>What payroll holds of a summary: latestVersion|latestAbsent|recorded, or empty.</summary>
    private static Task<string> ReceiptAsync(string summary) => Db(Payroll,
        "SELECT COALESCE((SELECT \"LatestVersion\" || '|' || \"LatestAbsentDays\" || '|' || \"RecordedAbsentDays\" FROM \"AttendanceSummaryReceipts\" " +
        $"WHERE \"SummaryId\"='{summary}'), '');");

    /// <summary>The difference rows of a summary as «version:days:carried:run», in the order of their versions.</summary>
    private static Task<string> RowsAsync(string summary) => Db(Payroll,
        "SELECT COALESCE(string_agg(\"SourceVersion\" || ':' || \"AbsentDays\" || ':' || \"CarryForward\" || ':' || COALESCE(\"AppliedInRunId\"::text, 'free'), ',' ORDER BY \"SourceVersion\"), '') " +
        $"FROM \"AbsenceDeductions\" WHERE \"SourceReference\" LIKE 'ATT:{summary}:%';");

    private static Task<string> ReceiptCountAsync(DateOnly month) => Db(Payroll,
        $"SELECT count(*)::text FROM \"AttendanceSummaryReceipts\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}';");

    private static Task<string> RunRowAsync(string run) => Db(Payroll,
        "SELECT \"Status\"::text || '|' || \"Revision\"::text || '|' || \"ResultsFrozen\"::text || '|' || \"TotalNetPay\"::text " +
        $"FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");

    /// <summary>An employee's line of a run: id|gross|deductions|net, or empty.</summary>
    private static Task<string> LineAsync(string run, string employee) => Db(Payroll,
        "SELECT COALESCE((SELECT \"Id\"::text || '|' || \"GrossPay\"::text || '|' || \"TotalDeductions\"::text || '|' || \"NetPay\"::text " +
        $"FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}' AND \"EmployeeId\"='{employee}'), '');");

    /// <summary>The attendance components of a line as «name=amount», in the order of their rows' versions.</summary>
    private static Task<string> AttendanceComponentsAsync(string line) => Db(Payroll,
        "SELECT COALESCE(string_agg(c.\"ComponentName\" || '=' || c.\"Amount\"::text, ';' ORDER BY a.\"OriginalPeriodStart\", a.\"SourceVersion\"), '') " +
        "FROM \"SalaryLineComponents\" c JOIN \"AbsenceDeductions\" a ON a.\"Id\" = c.\"ReferenceId\" " +
        $"WHERE c.\"SalaryLineId\"='{line}' AND a.\"SourceReference\" LIKE 'ATT:%';");

    private static Task<string> AttendanceWarningAsync(string run) => Db(Payroll,
        "SELECT COALESCE((SELECT \"Status\"::text || '|' || (\"ClosedAt\" IS NULL)::text || '|' || split_part(\"Findings\", E'\\n', 1) FROM \"PayrollAuditRecords\" " +
        $"WHERE \"PayrollRunId\"='{run}' AND \"Findings\" LIKE 'تحذير بوابة الحضور%' ORDER BY \"CreatedAt\" LIMIT 1), '');");

    private static async Task<string> CreateRunAsync(Api processor, string step, DateOnly month)
    {
        var end = EndOf(month);
        var (created, body, _) = await processor.PostAsync($"{step}-create-run", "api/payroll/runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });
        Assert.True(created is HttpStatusCode.OK or HttpStatusCode.Created, $"{step}: creating the run of {Ym(month)} answered {(int)created}: {Text(body, "error")}");
        return body.GetProperty("id").GetGuid().ToString("D");
    }

    private static async Task CalculateAsync(Api processor, string step, string run)
    {
        // A class that ran earlier on this stack may have added an employment record after the seed (AC-IAM-50b does), and the
        // service-regime gate then refuses every run of the agency. The seed's classification is applied to such records first.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(TenantA);
        var (calculated, body, _) = await processor.PostAsync($"{step}-calculate", $"api/payroll/runs/{run}/calculate");
        Assert.True(calculated == HttpStatusCode.OK, $"{step}: the calculation answered {(int)calculated}: {Text(body, "error")}");
    }

    // ---------------------------------------------------------------- the screens' acts

    private static async Task BulkAsync(AcceptanceUi ui, string step, string unit, DateOnly month, string status)
    {
        var (answered, body) = await ui.PostWithStatusAsync(step, $"{AttendanceUi}/BulkEntry", $"{AttendanceUi}/BulkEntry",
            F(("orgUnitId", unit), ("month", Ym(month)), ("defaultStatus", status)));
        // The success message («الإدخال الجماعي: N موظف، كُتب …»): the form's own title and hint do not carry the colon.
        Assert.True(answered == HttpStatusCode.OK && body.Contains("الإدخال الجماعي: ", StringComparison.Ordinal),
            $"{step}: the bulk entry of {Ym(month)} answered {(int)answered}: {Snip(body)}");
    }

    private static Task<(HttpStatusCode Status, string Body)> CloseAsync(AcceptanceUi ui, string step, DateOnly month, string? unit) =>
        ui.PostWithStatusAsync(step, $"{PeriodsUi}/Close", $"{PeriodsUi}/Close", F(("month", Ym(month)), ("orgUnitId", unit ?? "")));

    private static async Task SetStatusAsync(AcceptanceUi ui, string step, string employee, DateOnly day, string status, string note)
    {
        var id = await RecordIdAsync(employee, day, step);
        var (answered, body) = await ui.PostWithStatusAsync(step, $"{AttendanceUi}/Edit/{id}", $"{AttendanceUi}/Edit/{id}",
            F(("Id", id), ("Status", status), ("Notes", note)));
        Assert.True(answered == HttpStatusCode.OK, $"{step}: editing {Day(day)} answered {(int)answered}: {Snip(body)}");
    }

    /// <summary>A correction asked on the screen from a locked day; returns the request's id (or empty when it was refused).</summary>
    private static async Task<(HttpStatusCode Status, string Body, string Id)> RequestCorrectionAsync(
        AcceptanceUi ui, string step, string employee, DateOnly day, string newStatus, string reason)
    {
        var page = $"{CorrectionsUi}/Create?employeeId={employee}&date={Day(day)}";
        var (answered, body) = await ui.PostWithStatusAsync(step, $"{CorrectionsUi}/Create", page,
            F(("EmployeeId", employee), ("AttendanceDate", Day(day)), ("NewStatus", newStatus), ("Reason", reason)));
        var id = await Db(Core,
            "SELECT COALESCE((SELECT \"Id\"::text FROM \"AttendanceCorrections\" " +
            $"WHERE \"EmployeeId\"='{employee}' AND \"AttendanceDate\"='{Day(day)}' AND \"Status\" = 0 ORDER BY \"CreatedAt\" DESC LIMIT 1), '');");
        return (answered, body, id);
    }

    private static Task<(HttpStatusCode Status, string Body)> DecideAsync(AcceptanceUi ui, string step, string correction, bool approve, string note) =>
        ui.PostWithStatusAsync(step, $"{CorrectionsUi}/{(approve ? "Approve" : "Reject")}/{correction}", $"{CorrectionsUi}/Details/{correction}", F(("note", note)));

    /// <summary>A correction: status|oldStatus|newStatus|resultingVersion|source|requester|decider.</summary>
    private static Task<string> CorrectionAsync(string id) => Db(Core,
        "SELECT COALESCE((SELECT \"Status\" || '|' || COALESCE(\"OldStatus\"::text, '-') || '|' || COALESCE(\"NewStatus\"::text, '-') || '|' || COALESCE(\"ResultingVersion\"::text, '-') || '|' || " +
        $"\"Source\" || '|' || COALESCE(\"RequestedBy\"::text, '-') || '|' || COALESCE(\"DecidedBy\"::text, '-') FROM \"AttendanceCorrections\" WHERE \"Id\"='{id}'), '');");

    /// <summary>Fills a unit's month as present, marks the employee's absences, and closes the unit, all by the branch on its screens.</summary>
    private static async Task FillAndCloseAsync(RecruitmentSession s, string step, DateOnly month, params DateOnly[] absences)
    {
        await BulkAsync(s[Br1], $"{step}-bulk", Rif, month, "Present");
        foreach (var day in absences)
            await SetStatusAsync(s[Br1], $"{step}-absent-{day.Day}", Employee, day, "Absent", "غياب بلا عذر");
        var (closed, body) = await CloseAsync(s[Br1], $"{step}-close", month, Rif);
        Assert.True(closed == HttpStatusCode.OK && body.Contains("أُغلقت فترة الحضور", StringComparison.Ordinal),
            $"{step}: closing {Ym(month)} for the branch answered {(int)closed}: {Snip(body)}");
    }

    private static async Task RequireEmployeeAsync(RecruitmentSession s)
    {
        var profile = await Db(Payroll,
            $"SELECT count(*)::text FROM \"EmployeePayProfiles\" WHERE \"TenantId\"='{TenantA}' AND \"EmployeeId\"='{Employee}' AND \"IsActive\";");
        AcceptanceGate.RequireData(s.Recorder, profile == "1" && await InTreeAsync(Employee, Rif),
            "موظف فرع ريف دمشق في البذرة بلا ملف أجر فعّال أو خارج شجرة الفرع", "The seeded RIF employee has no active pay profile or is not in RIF's tree; run scripts/acceptance-reset.ps1.");
    }

    // ---------------------------------------------------------------- 57a

    [SkippableFact]
    public async Task Ac57a_a_branch_fills_a_month_and_closes_its_tree_and_payroll_receives_each_summary()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57a");
        var s = await StartAsync(Ac, "57a",
            "AC-LVA-57a: br1-mgr fills January 2025 by the bulk entry, marks two absences for an employee, and closes his unit: a summary v1 for every employee of the tree (a sub-unit's employee and one whose service ended inside the month included), a receipt in payroll for each, and an absence row of two days for the employee. A day of the closed month is not edited any more; br2-mgr closing br1's unit is 403.",
            Br1, Br2, Central, Processor, Approver);
        s.Steps($"""
            {ResetSteps}
            1. Test-only precondition (psql, hr_core_db): an employee of the branch whose service ended on 15 January 2025 — a copy of the
               seeded RIF employee's row under a new id ({Leaver}), status «منتهي», with the EmploymentStatusHistories row that carries the
               date. No screen ends a service on a past date.
            2. br1-mgr fills January 2025 for his unit by the bulk entry («حاضر»): working days only, for the unit and its sub-unit.
            3. br1-mgr edits two days of the RIF employee to «غائب» (6 and 7 January), then closes January for his unit.
            4. Positive: every employee the close covers (the unit's tree, as the service's own rule reads it) has a summary at
               version 1 with no working day unrecorded, and there is no other summary; the two seeded employees, who sit in two
               directorates below the branch, have the month's 22 working days; the employee who left has the working days up to
               15 January; the RIF employee's summary has 2 absent days.
               Payroll holds a receipt for each summary, and an absence row of 2 days for the RIF employee keyed ATT:<summary>:v1.
            5. Negative: br1-mgr edits a day of the closed month → refused with «اليوم ضمن فترة حضور مغلقة», the record unchanged;
               he creates a record on a locked day → refused, no record. br2-mgr closes br1's unit → 403, nothing written.
            """);
        await RequireEmployeeAsync(s);
        await ResetMonthAsync(s, "57a", month);

        // The leaver: written once (a later run of the class finds him).
        if (await Db(Core, $"SELECT count(*)::text FROM \"Employees\" WHERE \"Id\"='{Leaver}';") == "0")
        {
            await Db(Core,
                "INSERT INTO \"Employees\" SELECT (jsonb_populate_record(NULL::\"Employees\", to_jsonb(e) || jsonb_build_object(" +
                $"'Id', '{Leaver}', 'NationalId', '57{Leaver[^10..]}', 'NationalIdHash', md5('{Leaver}'), 'FirstName', 'منتهي', 'LastName', 'الخدمة', " +
                "'FullName', 'منتهي الخدمة في كانون الثاني', 'FirstNameNormalized', 'منتهي', 'LastNameNormalized', 'الخدمه', 'FullNameNormalized', 'منتهي الخدمه في كانون الثاني', " +
                $"'HireDate', '2020-01-01T00:00:00+00:00', 'CurrentStatus', 2))).* FROM \"Employees\" e WHERE e.\"Id\"='{Employee}'; " +
                "INSERT INTO \"EmploymentStatusHistories\" (\"Id\",\"TenantId\",\"CreatedAt\",\"UpdatedAt\",\"EmployeeId\",\"OldStatus\",\"NewStatus\",\"ChangedAt\",\"Reason\") " +
                $"VALUES (gen_random_uuid(), '{TenantA}', now(), now(), '{Leaver}', 'Active', 'Terminated', '2025-01-15T09:00:00+00:00', 'انتهاء الخدمة — AC-LVA-57a');");
        }

        var tree = (await TreeEmployeesAsync(Rif, month)).Split(',', StringSplitOptions.RemoveEmptyEntries);
        AcceptanceGate.RequireData(s.Recorder, tree.Contains(Employee) && tree.Contains(SubUnitEmployee) && tree.Contains(Leaver),
            "شجرة فرع ريف دمشق لا تضم موظفي البذرة", "RIF's tree does not hold the seeded employees; run scripts/acceptance-reset.ps1.");
        s.Recorder.WriteBefore(new { scenario = "57a", explicitDomainSnapshot = true, month = Ym(month), employeesInTheTree = tree.Length, leaver = Leaver });

        var absences = new[] { new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 7) };
        await FillAndCloseAsync(s, "57a", month, absences);

        // Every employee of the tree has a summary at version 1.
        var working = WorkingDays(month).Count;
        var withoutSummary = await Db(Core,
            "SELECT count(*)::text FROM \"Employees\" e WHERE e.\"Id\" IN (" + string.Join(",", tree.Select(id => $"'{id}'")) + ") AND NOT EXISTS (" +
            "SELECT 1 FROM \"AttendanceEmployeeSummaries\" x JOIN \"AttendancePeriods\" p ON p.\"Id\" = x.\"AttendancePeriodId\" " +
            $"WHERE x.\"EmployeeId\" = e.\"Id\" AND p.\"PeriodStart\"='{Day(month)}' AND x.\"Version\" = 1 AND x.\"UnrecordedWorkingDays\" = 0);");
        var employee = await ReadSummaryAsync(Employee, month);
        var subUnit = await ReadSummaryAsync(SubUnitEmployee, month);
        var leaver = await ReadSummaryAsync(Leaver, month);
        var leaverWorking = WorkingDays(month, new DateOnly(2025, 1, 15)).Count;
        var summaries = await SummaryCountAsync(month);

        // Payroll receives each summary.
        var received = await WaitAsync(async () => await ReceiptCountAsync(month) == summaries, 120);
        var receipts = await ReceiptCountAsync(month);
        var receipt = await ReceiptAsync(employee.Id);
        var rows = await RowsAsync(employee.Id);
        var subUnitRows = await RowsAsync(subUnit.Id);

        // Negative: a locked day is not written by hand any more.
        var lockedDay = new DateOnly(2025, 1, 8);
        var before = await RecordAsync(Employee, lockedDay);
        var lockedId = before.Split('|')[0];
        var (edited, editedBody) = await s[Br1].PostWithStatusAsync("57a-edit-locked", $"{AttendanceUi}/Edit/{lockedId}", $"{AttendanceUi}/Create",
            F(("Id", lockedId), ("Status", "Remote"), ("Notes", "محاولة على يوم مقفل")));
        var (created, createdBody) = await s[Br1].PostWithStatusAsync("57a-create-locked", $"{AttendanceUi}/Create", $"{AttendanceUi}/Create",
            F(("EmployeeId", Employee), ("AttendanceDate", "2025-01-10"), ("Status", "Present"), ("Notes", "محاولة على يوم مقفل")));
        var after = await RecordAsync(Employee, lockedDay);
        var fridayRecord = await RecordAsync(Employee, new DateOnly(2025, 1, 10));
        // Negative: another branch's manager does not close this branch's unit.
        var summariesBeforeForeign = await SummaryCountAsync(month);
        var (foreign, _) = await CloseAsync(s[Br2], "57a-close-foreign", month, Rif);
        var summariesAfterForeign = await SummaryCountAsync(month);

        await s.AuditAsync(lockedId, employee.Id);
        s.Checks(new
        {
            month = Ym(month),
            tree = tree.Length,
            working,
            withoutSummary,
            employee = employee.ToString(),
            subUnit = subUnit.ToString(),
            leaver = leaver.ToString(),
            leaverWorking,
            summaries,
            receipts,
            receipt,
            rows,
            subUnitRows,
            edited = (int)edited,
            created = (int)created,
            before,
            after,
            fridayRecord,
            foreign = (int)foreign,
            summariesBeforeForeign,
            summariesAfterForeign
        });

        // One summary per employee of the tree and no other: the two seeded employees have the month's 22 working days (an employee
        // hired inside January has fewer, and so has the one who left).
        Assert.Equal("0", withoutSummary);
        Assert.Equal(tree.Length.ToString(CultureInfo.InvariantCulture), summaries);
        Assert.Equal(22, working);
        Assert.Equal((1, working, working - 2, 2), (employee.Version, employee.Working, employee.Present, employee.Absent));
        Assert.Equal((1, working, working, 0), (subUnit.Version, subUnit.Working, subUnit.Present, subUnit.Absent));
        Assert.Equal((1, leaverWorking, leaverWorking, 0), (leaver.Version, leaver.Working, leaver.Present, leaver.Absent));
        Assert.True(received, $"57a: payroll holds {receipts} receipts for {summaries} summaries.");
        Assert.Equal("1|2|2", receipt);
        Assert.Equal("1:2:false:free", rows);
        // No absence, no row: the receipt alone says the sub-unit's employee is closed.
        Assert.Equal("", subUnitRows);
        Assert.Contains("اليوم ضمن فترة حضور مغلقة", editedBody);
        Assert.Contains("اليوم ضمن فترة حضور مغلقة", createdBody);
        Assert.Equal(before, after);
        Assert.Equal("", fridayRecord);
        Assert.Equal(HttpStatusCode.Forbidden, foreign);
        Assert.Equal(summariesBeforeForeign, summariesAfterForeign);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57b

    [SkippableFact]
    public async Task Ac57b_the_bulk_entry_writes_working_days_only_and_a_close_with_a_missing_working_day_is_refused()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57b");
        var s = await StartAsync(Ac, "57b",
            "AC-LVA-57b: the bulk entry with «غائب» writes no Friday, Saturday, configured holiday or future day, and the close counts working days only. A close while an employee has a working day with no record answers 409 with the list and writes no summary and no event (RequireComplete).",
            Br1, Central);
        s.Steps($"""
            {ResetSteps}
            The acceptance layer configures 17 and 18 February 2025 as holidays (docker-compose.acceptance.yml).
            1. Negative first: br1-mgr asks the API to close February 2025 for his sub-unit while nobody has a record → 409 with the
               employees and their missing working days; no summary, no period, no receipt in payroll.
            2. br1-mgr fills February for the sub-unit by the bulk entry with «غائب». No record falls on a Friday, a Saturday, 17 or 18
               February; each employee has the 18 working days.
            3. Test-only (psql): one of those days is deleted for one employee. The close is refused again (409) and lists that employee
               with one day. br1-mgr creates the day on the screen, and the close succeeds: 18 working days and 18 absent days each.
            4. «No future day»: br1-mgr fills the current month for the sub-unit; no record is dated after today (Damascus), and each
               employee of the sub-unit has the working days up to today. Test-only (psql): the records that bulk entry wrote are
               deleted again.
            """);
        await ResetMonthAsync(s, "57b", month);
        var people = (await TreeEmployeesAsync(RifHr, month)).Split(',', StringSplitOptions.RemoveEmptyEntries);
        AcceptanceGate.RequireData(s.Recorder, people.Length > 0,
            "الوحدة الفرعية لفرع ريف دمشق بلا موظفين", "RIF's sub-unit has no employee; run scripts/acceptance-reset.ps1.");
        var ids = string.Join(",", people.Select(id => $"'{id}'"));
        // The employee whose day goes missing: the first of the sub-unit's tree.
        var one = people[0];
        using var api = await Api.AttendanceAsync(s.Recorder, Br1, "57b");

        var (empty, emptyBody, _) = await api.PostAsync("close-with-no-record", $"{PeriodsApi}/{Ym(month)}/close", new { orgUnitId = RifHr });
        var summariesAfterRefusal = await SummaryCountAsync(month);
        var periodsAfterRefusal = await Db(Core, $"SELECT count(*)::text FROM \"AttendancePeriods\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}';");

        await BulkAsync(s[Br1], "57b-bulk-absent", RifHr, month, "Absent");
        var working = WorkingDays(month);
        var onRestDays = await Db(Core,
            $"SELECT count(*)::text FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\" IN ({ids}) AND \"AttendanceDate\" BETWEEN '{Day(month)}' AND '{Day(EndOf(month))}' " +
            "AND (extract(dow from \"AttendanceDate\") IN (5, 6) OR \"AttendanceDate\" IN (" + string.Join(",", Reserved.Value.Holidays.Select(h => $"'{Day(h)}'")) + "));");
        var perEmployee = await Db(Core,
            $"SELECT COALESCE(string_agg(DISTINCT n::text, ','), '') FROM (SELECT count(*) AS n FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\" IN ({ids}) " +
            $"AND \"AttendanceDate\" BETWEEN '{Day(month)}' AND '{Day(EndOf(month))}' AND \"Status\" = 1 AND \"Source\" = 1 GROUP BY \"EmployeeId\") t;");

        // One working day goes missing for one employee: the close names him.
        var gap = working[3];
        await Db(Core, $"DELETE FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\"='{one}' AND \"AttendanceDate\"='{Day(gap)}';");
        var (incomplete, incompleteBody, _) = await api.PostAsync("close-with-a-gap", $"{PeriodsApi}/{Ym(month)}/close", new { orgUnitId = RifHr });
        var listed = incompleteBody.ValueKind == JsonValueKind.Object && incompleteBody.TryGetProperty("incomplete", out var list)
            ? list.EnumerateArray().Select(i => $"{i.GetProperty("employeeId").GetGuid():D}:{i.GetProperty("unrecordedWorkingDays").GetInt32()}").ToArray()
            : [];
        var summariesAfterGap = await SummaryCountAsync(month);
        var receiptsAfterGap = await ReceiptCountAsync(month);

        var (recreated, recreatedBody) = await s[Br1].PostWithStatusAsync("57b-create-the-day", $"{AttendanceUi}/Create", $"{AttendanceUi}/Create",
            F(("EmployeeId", one), ("AttendanceDate", Day(gap)), ("Status", "Absent"), ("Notes", "استكمال يوم ناقص")));
        var (closed, closedBody, _) = await api.PostAsync("close-complete", $"{PeriodsApi}/{Ym(month)}/close", new { orgUnitId = RifHr });
        var summary = await ReadSummaryAsync(one, month);

        // No future day: the current month, then what the bulk entry wrote is removed again. The month has its own people (a later
        // hire is in, an ended service is out), and a day that had a record is skipped.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.Zone));
        var current = new DateOnly(today.Year, today.Month, 1);
        var currentPeople = (await TreeEmployeesAsync(RifHr, current)).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var currentIds = string.Join(",", currentPeople.Select(id => $"'{id}'"));
        var started = DateTime.UtcNow.AddSeconds(-5).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var before = await Db(Core,
            $"SELECT count(*)::text FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\" IN ({currentIds}) AND \"AttendanceDate\" BETWEEN '{Day(current)}' AND '{Day(today)}' " +
            "AND extract(dow from \"AttendanceDate\") NOT IN (5, 6);");
        await BulkAsync(s[Br1], "57b-bulk-current-month", RifHr, current, "Present");
        var future = await Db(Core, $"SELECT count(*)::text FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\" IN ({currentIds}) AND \"AttendanceDate\" > '{Day(today)}';");
        var written = await Db(Core,
            $"SELECT count(*)::text FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\" IN ({currentIds}) AND \"AttendanceDate\" BETWEEN '{Day(current)}' AND '{Day(today)}' " +
            $"AND \"Source\" = 1 AND \"CreatedAt\" >= '{started}';");
        await Db(Core,
            $"DELETE FROM \"DailyAttendanceRecords\" WHERE \"TenantId\"='{TenantA}' AND \"AttendanceDate\" BETWEEN '{Day(current)}' AND '{Day(EndOf(current))}' " +
            $"AND \"Source\" = 1 AND \"CreatedAt\" >= '{started}';");

        await s.AuditAsync(summary.Id);
        s.Checks(new
        {
            month = Ym(month),
            people = people.Length,
            working = working.Count,
            empty = (int)empty,
            summariesAfterRefusal,
            periodsAfterRefusal,
            onRestDays,
            perEmployee,
            gap = Day(gap),
            incomplete = (int)incomplete,
            listed,
            summariesAfterGap,
            receiptsAfterGap,
            recreated = (int)recreated,
            closed = (int)closed,
            summary = summary.ToString(),
            currentMonth = Ym(current),
            recordsBefore = before,
            future,
            written
        });

        Assert.Equal(HttpStatusCode.Conflict, empty);
        Assert.Equal(people.Length, emptyBody.GetProperty("total").GetInt32());
        Assert.All(emptyBody.GetProperty("incomplete").EnumerateArray(), i => Assert.Equal(working.Count, i.GetProperty("unrecordedWorkingDays").GetInt32()));
        Assert.Equal(("0", "0"), (summariesAfterRefusal, periodsAfterRefusal));
        Assert.Equal(18, working.Count);
        Assert.Equal("0", onRestDays);
        Assert.Equal("18", perEmployee);
        Assert.Equal(HttpStatusCode.Conflict, incomplete);
        Assert.Equal([$"{one}:1"], listed);
        Assert.Equal(("0", "0"), (summariesAfterGap, receiptsAfterGap));
        Assert.True(recreated == HttpStatusCode.OK, Snip(recreatedBody));
        Assert.True(closed == HttpStatusCode.OK, Text(closedBody, "error"));
        Assert.Equal(people.Length, closedBody.GetProperty("closedEmployees").GetInt32());
        // The close counts working days only: 18, all of them absent.
        Assert.Equal((1, 18, 0, 18), (summary.Version, summary.Working, summary.Present, summary.Absent));
        Assert.Equal("0", future);
        Assert.Equal((WorkingDays(current, today).Count * currentPeople.Length - int.Parse(before, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture), written);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57c

    [SkippableFact]
    public async Task Ac57c_a_correction_before_the_months_run_is_applied_by_it_net()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57c");
        var s = await StartAsync(Ac, "57c",
            "AC-LVA-57c: before the month's run is calculated, br1-mgr asks to correct one of the employee's two absences to «حاضر» with a reason and central-a approves: the summary's version 2 and a difference of −1 in payroll; the month's run deducts one day net. The requester's own approval is refused, a branch-scoped approval is 403, a short reason is refused.",
            Br1, Central, Central2, Processor, Approver);
        s.Steps($"""
            {ResetSteps}
            {EndSteps}
            1. br1-mgr fills March 2025 for his unit («حاضر»), marks 4 and 5 March «غائب» for the RIF employee, and closes the unit:
               version 1 with 2 absent days; payroll holds a row of +2.
            2. Negative: br1-mgr asks for a correction with a reason of 8 characters → refused on the form, no request.
            3. br1-mgr asks to correct 4 March to «حاضر» with a reason. Negative: br1-mgr posts the approval himself → 403 (the policy
               leaves a branch out), the request still waits.
            4. central-a approves it on the decision page: version 2 with 1 absent day, the day «حاضر» with the source «تصحيح», and a
               difference row of −1 in payroll, not carried (March has no calculated run).
            5. Negative: central-a asks for a correction of 6 March himself and posts its approval → refused («لا يبتّ مقدّم الطلب في
               طلبه»), the day and the version unchanged; central-a2 rejects it with a note.
            6. payroll-processor creates and calculates the run of March 2025: the RIF employee's line carries a deduction of two days
               and a refund of one («استرداد خصم غياب — فترة 2025-03»), one day net, and both rows are applied in that run.
            """);
        await RequireEmployeeAsync(s);
        await ResetMonthAsync(s, "57c", month);
        try
        {
            var absences = new[] { new DateOnly(2025, 3, 4), new DateOnly(2025, 3, 5) };
            await FillAndCloseAsync(s, "57c", month, absences);
            var v1 = await ReadSummaryAsync(Employee, month);
            var firstRow = await WaitAsync(async () => await RowsAsync(v1.Id) == "1:2:false:free");

            // The form states the rule as a hint: a refusal is the rule once more, as the form's error.
            const string shortReasonRule = "لا يقل عن 15 حرفاً";
            var correctionForm = await s[Br1].GetAsync("57c-correction-form", $"{CorrectionsUi}/Create?employeeId={Employee}&date={Day(absences[0])}");
            var (shortStatus, shortBody, shortId) = await RequestCorrectionAsync(s[Br1], "57c-short-reason", Employee, absences[0], "Present", "سبب قصير");
            var (asked, askedBody, correction) = await RequestCorrectionAsync(s[Br1], "57c-request", Employee, absences[0], "Present", CorrectionReason);
            Assert.True(Guid.TryParse(correction, out _), $"57c: the correction was not asked ({(int)asked}): {Snip(askedBody)}");
            var (byBranch, _) = await DecideAsync(s[Br1], "57c-approve-by-branch", correction, approve: true, "أعتمد طلبي");
            var afterBranch = await CorrectionAsync(correction);

            var (approved, approvedBody) = await DecideAsync(s[Central], "57c-approve", correction, approve: true, "مطابق لكتاب المهمة");
            var decided = await CorrectionAsync(correction);
            var v2 = await ReadSummaryAsync(Employee, month);
            var day = await RecordAsync(Employee, absences[0]);
            var difference = await WaitAsync(async () => await RowsAsync(v1.Id) == "1:2:false:free,2:-1:false:free");
            var rows = await RowsAsync(v1.Id);
            var receipt = await ReceiptAsync(v1.Id);

            // The requester never decides his own request, whatever his roles.
            var ownDay = new DateOnly(2025, 3, 6);
            var (ownAsked, ownAskedBody, own) = await RequestCorrectionAsync(s[Central], "57c-own-request", Employee, ownDay, "Remote", CorrectionReason);
            Assert.True(Guid.TryParse(own, out _), $"57c: central-a's own request was not asked ({(int)ownAsked}): {Snip(ownAskedBody)}");
            var (ownApproval, ownApprovalBody) = await DecideAsync(s[Central], "57c-approve-own", own, approve: true, "أعتمد طلبي");
            var ownAfter = await CorrectionAsync(own);
            var ownDayAfter = await RecordAsync(Employee, ownDay);
            var versionAfterOwn = (await ReadSummaryAsync(Employee, month)).Version;
            var (rejected, _) = await DecideAsync(s[Central2], "57c-reject-own", own, approve: false, "لا مستند مرفق");
            var ownRejected = await CorrectionAsync(own);

            using var processor = await Api.PayrollAsync(s.Recorder, Processor, "57c");
            var run = await CreateRunAsync(processor, "57c", month);
            await CalculateAsync(processor, "57c", run);
            var line = (await LineAsync(run, Employee)).Split('|');
            var components = await AttendanceComponentsAsync(line[0]);
            var applied = await RowsAsync(v1.Id);
            var parts = components.Split(';').Select(c => (Name: c.Split('=')[0], Amount: decimal.Parse(c.Split('=')[1], CultureInfo.InvariantCulture))).ToList();

            await s.AuditAsync(correction, own, v1.Id);
            s.Checks(new
            {
                month = Ym(month),
                v1 = v1.ToString(),
                shortStatus = (int)shortStatus,
                shortId,
                correction,
                byBranch = (int)byBranch,
                afterBranch,
                approved = (int)approved,
                decided,
                v2 = v2.ToString(),
                day,
                rows,
                receipt,
                own,
                ownApproval = (int)ownApproval,
                ownAfter,
                ownDayAfter,
                versionAfterOwn,
                rejected = (int)rejected,
                ownRejected,
                run,
                runRow = await RunRowAsync(run),
                line,
                components,
                applied
            });

            Assert.Equal((1, 2), (v1.Version, v1.Absent));
            Assert.True(firstRow, "57c: payroll did not record the close's two days.");
            Assert.True(Count(shortBody, shortReasonRule) > Count(correctionForm, shortReasonRule), "57c: the short reason was not refused on the form.");
            Assert.Equal("", shortId);
            Assert.Equal(HttpStatusCode.Forbidden, byBranch);
            Assert.StartsWith("0|", afterBranch);
            Assert.True(approved == HttpStatusCode.OK, Snip(approvedBody));
            // Approved (1): absent (1) → present (0), version 2, asked by one person and decided by another.
            var decision = decided.Split('|');
            Assert.Equal(("1", "1", "0", "2", "0"), (decision[0], decision[1], decision[2], decision[3], decision[4]));
            Assert.NotEqual(decision[5], decision[6]);
            Assert.Equal((2, 1), (v2.Version, v2.Absent));
            Assert.Equal(v1.Id, v2.Id);
            // The day: present (0), written by a correction (3), still the employee's and the day's.
            Assert.Equal($"0|3|{Employee}|{Day(absences[0])}", day[(day.IndexOf('|') + 1)..]);
            Assert.True(difference, $"57c: payroll's rows are {rows}.");
            Assert.Equal("2|1|1", receipt);
            Assert.Contains("لا يبتّ مقدّم الطلب في طلبه", ownApprovalBody);
            Assert.StartsWith("0|", ownAfter);
            Assert.Equal(2, versionAfterOwn);
            Assert.EndsWith($"|0|1|{Employee}|{Day(ownDay)}", ownDayAfter);
            Assert.True(rejected == HttpStatusCode.OK);
            Assert.StartsWith("2|", ownRejected);
            // The run: two days deducted, one refunded, both rows applied in it.
            Assert.Equal(2, parts.Count);
            Assert.True(parts[0].Amount < 0 && parts[1].Amount > 0, components);
            // One day back of the two: each amount is rounded on its own, so the half is compared to the cent.
            Assert.InRange(parts[1].Amount + parts[0].Amount / 2, -0.01m, 0.01m);
            Assert.Equal($"استرداد خصم غياب — فترة {Ym(month)}", parts[1].Name);
            Assert.Equal($"1:2:false:{run},2:-1:false:{run}", applied);
        }
        finally
        {
            await EndRunsAsync(s, "57c", month);
        }

        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57d

    [SkippableFact]
    public async Task Ac57d_a_correction_after_the_months_run_is_frozen_is_carried_to_the_next_cycle()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57d");
        var next = Month("57dNext");
        var s = await StartAsync(Ac, "57d",
            "AC-LVA-57d: after the month's run is frozen (calculated, validated and submitted for review), an approved correction («حاضر» → «غائب») becomes a carried difference; the run of the next month carries «فرق غياب عن فترة 2025-04» for one day and the frozen run does not change. The same version delivered again makes no second row, and an older version that arrives after the newer one is ignored.",
            Br1, Central, Processor, Approver);
        s.Steps($"""
            {ResetSteps}
            {EndSteps}
            1. br1-mgr fills April 2025 for his unit («حاضر») and closes it: version 1 with no absence, a receipt in payroll and no row.
            2. payroll-processor creates and calculates the run of April 2025 and submits it for review: it is frozen (PLAN-56).
            3. br1-mgr asks to correct 8 April to «غائب»; central-a approves: version 2 with 1 absent day. Payroll records a row of +1
               marked «carried», dated in April, applied in no run; April's run is as it was (status, total, the employee's line).
            4. payroll-processor creates and calculates the run of May 2025: the employee's line carries «فرق غياب عن فترة 2025-04»,
               one day, and the row is applied in May's run. The class leaves no free carried row.
            5. Negative: version 2 is published again to the broker under a new message id (AcceptanceMessagingHelper) → no second
               row. Version 1 is published after it (an older version arriving late) → ignored: the receipt stays at version 2.
            """);
        await RequireEmployeeAsync(s);
        await ResetMonthAsync(s, "57d", month, next);
        try
        {
            await FillAndCloseAsync(s, "57d", month);
            var v1 = await ReadSummaryAsync(Employee, month);
            Assert.True(await WaitAsync(async () => await ReceiptAsync(v1.Id) == "1|0|0"), "57d: payroll did not receive April's summary.");

            using var processor = await Api.PayrollAsync(s.Recorder, Processor, "57d");
            var april = await CreateRunAsync(processor, "57d-april", month);
            await CalculateAsync(processor, "57d-april", april);
            var (submitted, submittedBody, _) = await processor.PostAsync("april-submit", $"api/payroll/runs/{april}/submit-for-review");
            Assert.True(submitted == HttpStatusCode.OK, $"57d: submitting April answered {(int)submitted}: {Text(submittedBody, "error")}");
            var frozenRun = await RunRowAsync(april);
            var frozenLine = await LineAsync(april, Employee);

            var day = new DateOnly(2025, 4, 8);
            var (asked, askedBody, correction) = await RequestCorrectionAsync(s[Br1], "57d-request", Employee, day, "Absent", "لم يحضر ولم يقدّم عذراً بحسب سجل البوابة");
            Assert.True(Guid.TryParse(correction, out _), $"57d: the correction was not asked ({(int)asked}): {Snip(askedBody)}");
            var (approved, approvedBody) = await DecideAsync(s[Central], "57d-approve", correction, approve: true, "مطابق لسجل البوابة");
            Assert.True(approved == HttpStatusCode.OK, Snip(approvedBody));
            var v2 = await ReadSummaryAsync(Employee, month);
            var carried = await WaitAsync(async () => await RowsAsync(v1.Id) == "2:1:true:free");
            var rowsAfterCorrection = await RowsAsync(v1.Id);
            var runAfterCorrection = await RunRowAsync(april);
            var lineAfterCorrection = await LineAsync(april, Employee);
            var (statusBefore, statusBeforeBody, _) = await processor.GetAsync("april-attendance-status", $"api/payroll/runs/{april}/attendance-status", capture: false);

            var may = await CreateRunAsync(processor, "57d-may", next);
            await CalculateAsync(processor, "57d-may", may);
            var mayLine = (await LineAsync(may, Employee)).Split('|');
            var components = await AttendanceComponentsAsync(mayLine[0]);
            var rowsAfterMay = await RowsAsync(v1.Id);
            var (mayStatus, mayStatusBody, _) = await processor.GetAsync("may-attendance-status", $"api/payroll/runs/{may}/attendance-status", capture: false);
            var aprilAtTheEnd = await RunRowAsync(april);
            var aprilLineAtTheEnd = await LineAsync(april, Employee);

            // The same version again under a new message id, then the older version after it.
            var period = await Db(Core, $"SELECT \"Id\"::text FROM \"AttendancePeriods\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}';");
            var correlation = Guid.NewGuid().ToString("D");
            var again = await AcceptanceMessagingHelper.PublishAttendanceSummaryAsync(
                AcceptanceSeedIds.AgencyAId, Guid.Parse(v1.Id), 2, Guid.Parse(period), month, EndOf(month), AcceptanceSeedIds.EmpBr1Id,
                v2.Working, v2.Present, v2.Absent, 0, 0, 0, "Correction", correlation);
            var older = await AcceptanceMessagingHelper.PublishAttendanceSummaryAsync(
                AcceptanceSeedIds.AgencyAId, Guid.Parse(v1.Id), 1, Guid.Parse(period), month, EndOf(month), AcceptanceSeedIds.EmpBr1Id,
                v1.Working, v1.Present, v1.Absent, 0, 0, 0, "Close", correlation);
            // Payroll's consumer took both (its inbox holds each message id) before the rows are read: «unchanged» is its decision.
            var consumed = await WaitAsync(async () => await Db(Payroll,
                $"SELECT count(DISTINCT \"MessageId\")::text FROM \"IntegrationMessageReceipts\" WHERE \"MessageId\" IN ('{again:D}','{older:D}') " +
                "AND \"ConsumerType\" = 'AttendanceSummaryPublishedConsumer' AND \"ProcessedAt\" IS NOT NULL;") == "2", 60);
            var rowsAfterReplay = await RowsAsync(v1.Id);
            var receiptAfterReplay = await ReceiptAsync(v1.Id);
            var freeCarried = await Db(Payroll,
                $"SELECT count(*)::text FROM \"AbsenceDeductions\" WHERE \"TenantId\"='{TenantA}' AND \"CarryForward\" AND \"AppliedInRunId\" IS NULL AND \"SourceReference\" LIKE 'ATT:%';");

            await s.AuditAsync(correction, v1.Id);
            s.Checks(new
            {
                month = Ym(month),
                next = Ym(next),
                v1 = v1.ToString(),
                april,
                frozenRun,
                frozenLine,
                correction,
                v2 = v2.ToString(),
                rowsAfterCorrection,
                runAfterCorrection,
                lineAfterCorrection,
                statusBefore = (int)statusBefore,
                carryForwardPending = statusBeforeBody.GetProperty("carryForwardPending").GetInt32(),
                may,
                mayLine,
                components,
                rowsAfterMay,
                mayStatus = (int)mayStatus,
                carryForwardApplied = mayStatusBody.GetProperty("carryForwardApplied").GetInt32(),
                aprilAtTheEnd,
                aprilLineAtTheEnd,
                rowsAfterReplay,
                receiptAfterReplay,
                freeCarried
            });

            Assert.Equal((1, 0), (v1.Version, v1.Absent));
            Assert.StartsWith($"{UnderReview}|", frozenRun);
            Assert.Equal((2, 1), (v2.Version, v2.Absent));
            Assert.True(carried, $"57d: payroll's rows are {rowsAfterCorrection}.");
            // April did not move: not after the correction, and not after May took the difference.
            Assert.Equal((frozenRun, frozenLine), (runAfterCorrection, lineAfterCorrection));
            Assert.Equal((frozenRun, frozenLine), (aprilAtTheEnd, aprilLineAtTheEnd));
            Assert.True(statusBeforeBody.GetProperty("carryForwardPending").GetInt32() >= 1);
            // May carries it, by name, once.
            var item = components.Split('=');
            Assert.Equal($"فرق غياب عن فترة {Ym(month)}", item[0]);
            Assert.True(decimal.Parse(item[1], CultureInfo.InvariantCulture) < 0, components);
            Assert.Equal($"2:1:true:{may}", rowsAfterMay);
            Assert.True(mayStatusBody.GetProperty("carryForwardApplied").GetInt32() >= 1);
            // A version is recorded once, and an older one is ignored.
            Assert.True(consumed, "57d: payroll's consumer did not take the two replayed versions.");
            Assert.Equal(rowsAfterMay, rowsAfterReplay);
            Assert.Equal("2|1|1", receiptAfterReplay);
            Assert.Equal("0", freeCarried);
        }
        finally
        {
            await EndRunsAsync(s, "57d", month, next);
        }

        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57e

    [SkippableFact]
    public async Task Ac57e_payroll_knows_before_a_calculation_whose_attendance_is_not_closed()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57e");
        var s = await StartAsync(Ac, "57e",
            "AC-LVA-57e: a run of a month whose attendance is not closed is calculated under «Warn» with one open audit record that lists who is missing, and attendance-status gives the same list. Once central-a closes the month for the whole agency, nobody HR.Core closed is missing from the month's new run: who is left, if anyone, is who payroll expects and HR.Core did not employ in the month.",
            Central, Processor, Approver);
        s.Steps($"""
            {ResetSteps}
            {EndSteps}
            1. Negative first (nothing of June 2025 is closed): payroll-processor creates and calculates the run of June 2025. It is
               calculated (the gate is «Warn»), and one audit record of the run, open, lists the employees whose attendance is not
               closed; GET api/payroll/runs/<run>/attendance-status gives the same employees. The run is cancelled (PLAN-56's API).
               («Block» is proved by Lva057PayrollAttendanceGateTests and Lva057PayrollAttendanceHandoffIntegrationTests.)
            2. central-a fills June 2025 for the agency's root unit by the bulk entry («حاضر») and closes June for the whole agency (no
               unit): the period is «مغلقة», and payroll holds a receipt for every summary.
            3. Positive: payroll-processor creates and calculates a new run of June 2025. Nobody HR.Core closed is missing any more:
               attendance-status.missingAttendance holds only employees payroll expects and HR.Core did not employ in June (the
               seed dates some pay profiles before their employee's hire date; HR.Core closes no attendance before a hire). With
               nobody left the run has no warning audit record; otherwise its one open record lists exactly those employees.
            """);
        await ResetMonthAsync(s, "57e", month);
        try
        {
            using var processor = await Api.PayrollAsync(s.Recorder, Processor, "57e");

            var open = await CreateRunAsync(processor, "57e-open", month);
            await CalculateAsync(processor, "57e-open", open);
            var openRun = await RunRowAsync(open);
            var warning = await AttendanceWarningAsync(open);
            var warned = await Db(Payroll,
                "SELECT COALESCE(string_agg(id, ',' ORDER BY id), '') FROM (SELECT unnest(string_to_array(substr(\"Findings\", strpos(\"Findings\", E'\\n') + 1), E'\\n')) AS id " +
                $"FROM \"PayrollAuditRecords\" WHERE \"PayrollRunId\"='{open}' AND \"Findings\" LIKE 'تحذير بوابة الحضور%') t;");
            var (openStatus, openBody, _) = await processor.GetAsync("open-attendance-status", $"api/payroll/runs/{open}/attendance-status", capture: false);
            var missing = openBody.GetProperty("missingAttendance").EnumerateArray().Select(e => e.GetGuid().ToString("D")).Order(StringComparer.Ordinal).ToList();
            var auditRecords = await Db(Payroll, $"SELECT count(*)::text || '|' || count(*) FILTER (WHERE \"ClosedAt\" IS NOT NULL)::text FROM \"PayrollAuditRecords\" WHERE \"PayrollRunId\"='{open}';");
            var (cancelled, cancelledBody, _) = await processor.PostAsync("open-cancel", $"api/payroll/runs/{open}/cancel",
                new { reason = "تشغيل حزيران 2025 قبل إغلاق الحضور — AC-LVA-57e" });
            Assert.True(cancelled == HttpStatusCode.OK, $"57e: cancelling the first run answered {(int)cancelled}: {Text(cancelledBody, "error")}");

            // The whole agency: filled from the root, closed with no unit.
            await BulkAsync(s[Central], "57e-bulk-agency", Hq, month, "Present");
            var (closed, closedBody) = await CloseAsync(s[Central], "57e-close-agency", month, null);
            var period = await Db(Core, $"SELECT COALESCE((SELECT \"Status\"::text FROM \"AttendancePeriods\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='{Day(month)}'), '');");
            var summaries = await SummaryCountAsync(month);
            var received = await WaitAsync(async () => await ReceiptCountAsync(month) == summaries, 240);
            var receipts = await ReceiptCountAsync(month);

            var full = await CreateRunAsync(processor, "57e-closed", month);
            await CalculateAsync(processor, "57e-closed", full);
            var (fullStatus, fullBody, _) = await processor.GetAsync("closed-attendance-status", $"api/payroll/runs/{full}/attendance-status", capture: false);
            var stillMissing = fullBody.GetProperty("missingAttendance").EnumerateArray().Select(e => e.GetGuid().ToString("D")).Order(StringComparer.Ordinal).ToList();
            var fullWarning = await AttendanceWarningAsync(full);
            var fullWarned = await Db(Payroll,
                "SELECT COALESCE(string_agg(id, ',' ORDER BY id), '') FROM (SELECT unnest(string_to_array(substr(\"Findings\", strpos(\"Findings\", E'\\n') + 1), E'\\n')) AS id " +
                $"FROM \"PayrollAuditRecords\" WHERE \"PayrollRunId\"='{full}' AND \"Findings\" LIKE 'تحذير بوابة الحضور%') t;");
            var fullAudit = await Db(Payroll, $"SELECT count(*)::text || '|' || count(*) FILTER (WHERE \"ClosedAt\" IS NOT NULL)::text FROM \"PayrollAuditRecords\" WHERE \"PayrollRunId\"='{full}';");
            // Who HR.Core closed, and — of those still missing — who HR.Core holds in service in June (nobody: each was hired later).
            var closedIds = (await Db(Core,
                "SELECT COALESCE(string_agg(s.\"EmployeeId\"::text, ','), '') FROM \"AttendanceEmployeeSummaries\" s JOIN \"AttendancePeriods\" p ON p.\"Id\" = s.\"AttendancePeriodId\" " +
                $"WHERE p.\"TenantId\"='{TenantA}' AND p.\"PeriodStart\"='{Day(month)}';")).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var missingAndInService = stillMissing.Count == 0
                ? "0"
                : await Db(Core,
                    $"SELECT count(*)::text FROM \"Employees\" e WHERE e.\"Id\" IN ({string.Join(",", stillMissing.Select(id => $"'{id}'"))}) AND {InServiceIn(month)};");
            var expectedStillMissing = missing.Where(id => !closedIds.Contains(id)).ToList();

            s.Checks(new
            {
                month = Ym(month),
                open,
                openRun,
                warning,
                warnedCount = warned.Split(',', StringSplitOptions.RemoveEmptyEntries).Length,
                missingCount = missing.Count,
                gateMode = Text(openBody, "gateMode"),
                employeesInScope = openBody.GetProperty("employeesInScope").GetInt32(),
                auditRecords,
                closed = (int)closed,
                period,
                summaries,
                receipts,
                full,
                fullRun = await RunRowAsync(full),
                closedByHrCore = closedIds.Count,
                stillMissing = stillMissing.Count,
                missingAndInService,
                fullWarning,
                fullAudit,
                fullInScope = fullBody.GetProperty("employeesInScope").GetInt32()
            });

            Assert.StartsWith($"{Calculated}|", openRun);
            Assert.Equal(HttpStatusCode.OK, openStatus);
            Assert.Equal("Warn", Text(openBody, "gateMode"));
            Assert.NotEmpty(missing);
            // The record: open (0), never closed, with the count; its list is the API's list.
            Assert.StartsWith($"0|true|تحذير بوابة الحضور: {missing.Count} ", warning);
            Assert.Equal(string.Join(",", missing), warned);
            // Nothing of June was closed: everyone in the run's scope is missing.
            Assert.Equal(openBody.GetProperty("employeesInScope").GetInt32(), missing.Count);
            Assert.Equal("1|0", auditRecords);
            Assert.True(closed == HttpStatusCode.OK && closedBody.Contains("أُغلقت فترة الحضور", StringComparison.Ordinal), Snip(closedBody));
            // AttendancePeriodStatus.Closed = 2.
            Assert.Equal("2", period);
            Assert.True(received, $"57e: payroll holds {receipts} receipts for {summaries} summaries.");
            Assert.Equal(HttpStatusCode.OK, fullStatus);
            // Nobody HR.Core closed is missing; who is left is who payroll expected and HR.Core did not close, none of them in service.
            Assert.True(closedIds.Count > 0 && closedIds.Count == int.Parse(summaries, CultureInfo.InvariantCulture));
            Assert.DoesNotContain(stillMissing, closedIds.Contains);
            Assert.Equal(expectedStillMissing, stillMissing);
            Assert.True(stillMissing.Count < missing.Count, $"57e: {stillMissing.Count} of {missing.Count} are still missing after the close.");
            Assert.Equal("0", missingAndInService);
            if (stillMissing.Count == 0)
            {
                Assert.Equal("", fullWarning);
                Assert.Equal("0|0", fullAudit);
            }
            else
            {
                // One open record, with the count; its list is the API's list.
                Assert.StartsWith($"0|true|تحذير بوابة الحضور: {stillMissing.Count} ", fullWarning);
                Assert.Equal(string.Join(",", stillMissing), fullWarned);
                Assert.Equal("1|0", fullAudit);
            }
        }
        finally
        {
            await EndRunsAsync(s, "57e", month);
        }

        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57f

    [SkippableFact]
    public async Task Ac57f_an_edit_keeps_the_records_owner_and_day_and_on_leave_is_never_typed_by_hand()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57f");
        var s = await StartAsync(Ac, "57f",
            "AC-LVA-57f: an edit of a record's status keeps its employee and its day and is written to the audit log; a posted EmployeeId or AttendanceDate is ignored; «في إجازة» is refused on a create and on an edit; a leave-made record is not edited or deleted by hand.",
            Br1, Central);
        s.Steps($"""
            {ResetSteps}
            1. An employee of the branch outside the first 50 of the agency by name is picked if the branch has one (the old form listed
               the first 50 only), otherwise any; a second employee of the branch is the one a forged post names.
            2. br1-mgr creates a record for the first on 7 July 2025 («حاضر»), then edits it to «عن بُعد»: the same record, employee and
               day; the audit log holds the change.
            3. Negative: the edit form is posted with another employee's id and another date beside the status → the status changes,
               the employee and the day do not, and the other employee gets no record.
            4. Negative: a create with «في إجازة» and an edit to «في إجازة» → refused on the form, nothing written.
            5. Negative: an approved leave of 8–9 July is injected for the RIF employee (AcceptanceMessagingHelper, until LVA-A): its two
               days are leave-made. br1-mgr posts an edit and a delete of one → refused, the record unchanged. The leave's
               cancellation is injected: the two days are gone, and nothing else of the month. Test-only clean-up (psql,
               payroll_db): the two zero adjustments payroll notes for the leave's approval and cancellation are deleted, so
               that no other class's run takes them.
            """);
        await ResetMonthAsync(s, "57f", month);
        var owner = await Db(Core,
            "WITH ranked AS (SELECT e.\"Id\", row_number() OVER (ORDER BY e.\"FullName\") AS n FROM \"Employees\" e " +
            $"WHERE e.\"TenantId\"='{TenantA}') SELECT COALESCE((SELECT r.\"Id\"::text || '|' || r.n FROM ranked r JOIN \"Employees\" e ON e.\"Id\" = r.\"Id\" " +
            "JOIN \"OrgUnits\" o ON o.\"Id\" = e.\"PrimaryOrgUnitId\" " +
            $"WHERE o.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{Rif}') || '%' AND e.\"CurrentStatus\" = 0 " +
            $"AND e.\"HireDate\" < '{Day(month)}' AND r.\"Id\" NOT IN ('{Employee}','{Leaver}') ORDER BY (r.n > 50) DESC, r.n LIMIT 1), '');");
        AcceptanceGate.RequireData(s.Recorder, owner.Contains('|'), "لا يوجد موظف نشط في فرع ريف دمشق غير موظف البذرة", "RIF has no active employee but the seeded one.");
        var (ownerId, rank) = (owner.Split('|')[0], int.Parse(owner.Split('|')[1], CultureInfo.InvariantCulture));
        var day = new DateOnly(2025, 7, 7);

        var (created, createdBody) = await s[Br1].PostWithStatusAsync("57f-create", $"{AttendanceUi}/Create", $"{AttendanceUi}/Create",
            F(("EmployeeId", ownerId), ("AttendanceDate", Day(day)), ("Status", "Present"), ("Notes", "حضور AC-LVA-57f")));
        var record = await RecordIdAsync(ownerId, day, "57f-create");
        await SetStatusAsync(s[Br1], "57f-edit", ownerId, day, "Remote", "عمل عن بُعد بموافقة المدير");
        var afterEdit = await RecordAsync(ownerId, day);
        var audit = await Db(Core, $"SELECT count(*)::text FROM \"AuditLogs\" WHERE \"EntityId\"='{record}';");
        var editPage = await s[Br1].GetAsync("57f-edit-page", $"{AttendanceUi}/Edit/{record}");

        // The forged post: another employee and another date beside the status.
        var (forged, _) = await s[Br1].PostWithStatusAsync("57f-forged-edit", $"{AttendanceUi}/Edit/{record}", $"{AttendanceUi}/Edit/{record}",
            F(("Id", record), ("EmployeeId", Employee), ("AttendanceDate", "2025-07-21"), ("Status", "Absent"), ("Notes", "محاولة نقل السجل")));
        var afterForged = await RecordAsync(ownerId, day);
        var moved = await RecordAsync(Employee, new DateOnly(2025, 7, 21)) + await RecordAsync(Employee, day);

        // The create form states the rule as a hint: a refusal is the rule once more, as the form's error.
        const string onLeaveRule = "تأتي من إجازة معتمدة فقط";
        var createForm = await s[Br1].GetAsync("57f-create-form", $"{AttendanceUi}/Create");
        var (onLeaveCreate, onLeaveCreateBody) = await s[Br1].PostWithStatusAsync("57f-create-on-leave", $"{AttendanceUi}/Create", $"{AttendanceUi}/Create",
            F(("EmployeeId", ownerId), ("AttendanceDate", "2025-07-08"), ("Status", "OnLeave")));
        var onLeaveCreated = await RecordAsync(ownerId, new DateOnly(2025, 7, 8));
        var (onLeaveEdit, onLeaveEditBody) = await s[Br1].PostWithStatusAsync("57f-edit-on-leave", $"{AttendanceUi}/Edit/{record}", $"{AttendanceUi}/Edit/{record}",
            F(("Id", record), ("Status", "OnLeave")));
        var afterOnLeave = await RecordAsync(ownerId, day);

        // A leave-made record is the leave's: neither edited nor deleted by hand.
        var leave = Guid.NewGuid();
        var from = new DateOnly(2025, 7, 8);
        var to = new DateOnly(2025, 7, 9);
        var correlation = Guid.NewGuid().ToString("D");
        await AcceptanceMessagingHelper.PublishLeaveApprovedAsync(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.EmpBr1Id, leave, from, to, "Annual", correlation);
        var leaveMade = await WaitAsync(async () => (await RecordAsync(Employee, from)).Contains("|2|2|", StringComparison.Ordinal));
        var leaveRecord = await RecordAsync(Employee, from);
        var leaveId = leaveRecord.Split('|')[0];
        var (editLeave, editLeaveBody) = await s[Br1].PostWithStatusAsync("57f-edit-leave-made", $"{AttendanceUi}/Edit/{leaveId}", $"{AttendanceUi}/Create",
            F(("Id", leaveId), ("Status", "Present")));
        var (deleteLeave, deleteLeaveBody) = await s[Br1].PostWithStatusAsync("57f-delete-leave-made", $"{AttendanceUi}/Delete/{leaveId}", $"{AttendanceUi}/Create", F());
        var leaveAfter = await RecordAsync(Employee, from);
        // A refused delete leads to the record's page, which says of a leave-made record what made it: the refusal is that text once more.
        const string leaveMadeRule = "أنشأته إجازة معتمدة";
        var leavePage = await s[Br1].GetAsync("57f-leave-made-page", $"{AttendanceUi}/Details/{leaveId}");
        await AcceptanceMessagingHelper.PublishLeaveCancelledAsync(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.EmpBr1Id, leave, from, to, "Annual", correlation);
        var givenBack = await WaitAsync(async () => await RecordAsync(Employee, from) == "" && await RecordAsync(Employee, to) == "");
        await RemoveLeaveNotesAsync(leave, 2);
        var ownerAtTheEnd = await RecordAsync(ownerId, day);

        await s.AuditAsync(record, leaveId);
        s.Checks(new
        {
            month = Ym(month),
            ownerId,
            rankByName = rank,
            record,
            created = (int)created,
            afterEdit,
            audit,
            forged = (int)forged,
            afterForged,
            moved,
            onLeaveCreate = (int)onLeaveCreate,
            onLeaveCreated,
            onLeaveEdit = (int)onLeaveEdit,
            afterOnLeave,
            leave,
            leaveRecord,
            editLeave = (int)editLeave,
            deleteLeave = (int)deleteLeave,
            leaveAfter,
            givenBack,
            ownerAtTheEnd
        });

        Assert.True(created == HttpStatusCode.OK, Snip(createdBody));
        // The same record, employee and day; the status is «عن بُعد» (3), then «غائب» (1) after the forged post.
        Assert.Equal($"{record}|3|0|{ownerId}|{Day(day)}", afterEdit);
        Assert.True(int.Parse(audit, CultureInfo.InvariantCulture) >= 2, $"57f: the audit log holds {audit} rows for the record.");
        Assert.DoesNotContain("name=\"EmployeeId\"", editPage);
        Assert.DoesNotContain("name=\"AttendanceDate\"", editPage);
        Assert.Equal($"{record}|1|0|{ownerId}|{Day(day)}", afterForged);
        Assert.Equal("", moved);
        Assert.True(Count(onLeaveCreateBody, onLeaveRule) > Count(createForm, onLeaveRule), "57f: «في إجازة» was not refused on the create form.");
        Assert.Equal("", onLeaveCreated);
        Assert.Contains(onLeaveRule, onLeaveEditBody);
        Assert.Equal(afterForged, afterOnLeave);
        Assert.True(leaveMade, $"57f: the injected leave made no record: {leaveRecord}");
        Assert.Contains(leaveMadeRule, editLeaveBody);
        Assert.True(Count(deleteLeaveBody, leaveMadeRule) > Count(leavePage, leaveMadeRule), "57f: the delete of a leave-made record was not refused.");
        Assert.Equal(leaveRecord, leaveAfter);
        Assert.True(givenBack, "57f: the cancelled leave's days are still there.");
        Assert.Equal(afterForged, ownerAtTheEnd);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57g

    [SkippableFact]
    public async Task Ac57g_a_leave_decision_on_a_locked_day_is_a_system_correction_with_a_new_version()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57g");
        var s = await StartAsync(Ac, "57g",
            "AC-LVA-57g: an approved leave (injected) for a day of a closed month makes a system correction, approved at once with no requester and no decider, a new version of the summary and a difference in payroll; its cancellation makes the reverse correction and the day is what it was. The same event delivered twice makes one correction.",
            Br1, Central);
        s.Steps("""
            The scenario reads January 2025 as 57a closed it: the RIF employee's summary, with its two absences (6 and 7 January).
            1. An approved leave of 7 January for the RIF employee is published to the broker (AcceptanceMessagingHelper, until LVA-A),
               twice under one message id, then once more under a new one.
            2. Positive: one system correction for the day («غائب» → «في إجازة»), approved, with no requester and no decider; the day
               is leave-made and keeps «غائب» as what it replaced; the summary's next version has one absence less and one leave day;
               payroll holds a difference of −1.
            3. The leave's cancellation is published: a reverse correction («في إجازة» → «غائب»), the next version, a difference of +1;
               the day is «غائب» again.
            4. Negative: no second correction came from the duplicates (one row per leave event and day).
            Test-only clean-up (psql, payroll_db): the zero adjustments payroll notes for the leave's approval and cancellation are
            deleted, so that no other class's run takes them.
            The reconciler's rule is proved by Lva057LeaveAttendanceTests (its job runs hourly; this class restarts no container).
            """);
        var start = await ReadSummaryAsync(Employee, month);
        AcceptanceGate.RequireData(s.Recorder, start.Version >= 1 && start.Absent >= 1,
            "كانون الثاني 2025 غير مغلق لموظف فرع ريف دمشق (يسبقه AC-LVA-57a)", "January 2025 is not closed for the RIF employee: AC-LVA-57a runs first.");
        var day = new DateOnly(2025, 1, 7);
        var dayBefore = await RecordAsync(Employee, day);
        AcceptanceGate.RequireData(s.Recorder, dayBefore.Split('|').ElementAtOrDefault(1) == "1",
            "يوم 7 كانون الثاني ليس «غائب» لموظف فرع ريف دمشق", "7 January is not «absent» for the RIF employee (a leftover of an earlier run of 57g): run 57a again.");
        var rowsBefore = await RowsAsync(start.Id);
        s.Recorder.WriteBefore(new { scenario = "57g", explicitDomainSnapshot = true, start = start.ToString(), dayBefore, rowsBefore });

        var leave = Guid.NewGuid();
        var correlation = Guid.NewGuid().ToString("D");
        await AcceptanceMessagingHelper.PublishLeaveApprovedAsync(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.EmpBr1Id, leave, day, day, "Annual", correlation, duplicateCount: 2);
        var applied = await WaitAsync(async () => (await ReadSummaryAsync(Employee, month)).Version == start.Version + 1);
        await AcceptanceMessagingHelper.PublishLeaveApprovedAsync(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.EmpBr1Id, leave, day, day, "Annual", correlation);
        await Task.Delay(TimeSpan.FromSeconds(6));
        var afterLeave = await ReadSummaryAsync(Employee, month);
        var dayOnLeave = await RecordAsync(Employee, day);
        var replaced = await Db(Core, $"SELECT COALESCE(\"ReplacedStatus\"::text, '-') || '|' || COALESCE(\"SourceReference\", '-') FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\"='{Employee}' AND \"AttendanceDate\"='{Day(day)}';");
        var corrections = await Db(Core,
            "SELECT COALESCE(string_agg(\"Status\" || '|' || COALESCE(\"OldStatus\"::text, '-') || '|' || COALESCE(\"NewStatus\"::text, '-') || '|' || \"Source\" || '|' || " +
            "(\"RequestedBy\" IS NULL AND \"DecidedBy\" IS NULL)::text || '|' || COALESCE(\"ResultingVersion\"::text, '-') || '|' || \"Reason\", ';' ORDER BY \"CreatedAt\"), '') " +
            $"FROM \"AttendanceCorrections\" WHERE \"EmployeeId\"='{Employee}' AND \"AttendanceDate\"='{Day(day)}' AND \"SourceReference\" LIKE 'LeaveRequestId={leave:D}:%';");
        var refunded = await WaitAsync(async () => (await RowsAsync(start.Id)).EndsWith($"{afterLeave.Version}:-1:false:free", StringComparison.Ordinal));
        var rowsAfterLeave = await RowsAsync(start.Id);

        await AcceptanceMessagingHelper.PublishLeaveCancelledAsync(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.EmpBr1Id, leave, day, day, "Annual", correlation, duplicateCount: 2);
        var reverted = await WaitAsync(async () => (await ReadSummaryAsync(Employee, month)).Version == start.Version + 2);
        var afterCancel = await ReadSummaryAsync(Employee, month);
        var dayAfter = await RecordAsync(Employee, day);
        var charged = await WaitAsync(async () => (await RowsAsync(start.Id)).EndsWith($"{afterCancel.Version}:1:false:free", StringComparison.Ordinal));
        var rowsAfterCancel = await RowsAsync(start.Id);
        var allCorrections = await Db(Core,
            "SELECT COALESCE(string_agg(split_part(\"SourceReference\", ':', 2) || '=' || \"Status\" || '/' || COALESCE(\"ResultingVersion\"::text, '-'), ',' ORDER BY \"CreatedAt\"), '') " +
            $"FROM \"AttendanceCorrections\" WHERE \"EmployeeId\"='{Employee}' AND \"SourceReference\" LIKE 'LeaveRequestId={leave:D}:%';");
        var receipt = await ReceiptAsync(start.Id);
        // Payroll noted the approval twice (two message ids) and the cancellation once.
        await RemoveLeaveNotesAsync(leave, 3);

        await s.AuditAsync(start.Id);
        s.Checks(new
        {
            month = Ym(month),
            leave,
            start = start.ToString(),
            afterLeave = afterLeave.ToString(),
            dayOnLeave,
            replaced,
            corrections,
            rowsAfterLeave,
            afterCancel = afterCancel.ToString(),
            dayAfter,
            rowsAfterCancel,
            allCorrections,
            receipt
        });

        Assert.True(applied, "57g: the approved leave made no new version.");
        Assert.Equal((start.Version + 1, start.Absent - 1, start.OnLeave + 1), (afterLeave.Version, afterLeave.Absent, afterLeave.OnLeave));
        // The day: «في إجازة» (2), made by a leave (2), keeping «غائب» (1) as what it replaced, with the leave's reference.
        Assert.Contains("|2|2|", dayOnLeave);
        Assert.Equal($"1|LeaveRequestId={leave:D}", replaced);
        // One correction: approved (1), absent (1) → on leave (2), by a leave event (1), with no requester and no decider.
        Assert.Equal($"1|1|2|1|true|{afterLeave.Version}|إجازة معتمدة {leave:D}", corrections);
        Assert.True(refunded, $"57g: payroll's rows are {rowsAfterLeave}.");
        Assert.True(reverted, "57g: the cancelled leave made no new version.");
        Assert.Equal((start.Version + 2, start.Absent, start.OnLeave), (afterCancel.Version, afterCancel.Absent, afterCancel.OnLeave));
        // The day is «غائب» (1) again, written by a correction (3).
        Assert.Contains("|1|3|", dayAfter);
        Assert.True(charged, $"57g: payroll's rows are {rowsAfterCancel}.");
        Assert.Equal($"Approved=1/{afterLeave.Version},Cancelled=1/{afterCancel.Version}", allCorrections);
        Assert.Equal($"{afterCancel.Version}|{start.Absent}|{start.Absent}", receipt);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 57h

    [SkippableFact]
    public async Task Ac57h_the_api_closes_and_reads_a_period_as_the_screens_do_and_another_agency_gets_404()
    {
        AcceptanceEnv.RequireEnabled();
        var month = Month("57h");
        var s = await StartAsync(Ac, "57h",
            "AC-LVA-57h: POST …/attendance/periods/{month}/close, GET …/attendance/periods/{month} and GET api/payroll/attendance/receipts give what the screens give (the contract PLAN-59 builds on). outsider-b, of another agency, naming a unit of agency A gets 404 and nothing is written.",
            Br1, Central, Processor, Outsider);
        s.Steps($"""
            {ResetSteps}
            1. br1-mgr fills July 2025 for his sub-unit by the bulk entry («حاضر»).
            2. Negative: outsider-b (agency B) posts the close of July 2025 naming agency A's sub-unit → 404; agency A has no period,
               no summary; agency B has none either.
            3. br1-mgr closes July for the sub-unit through the API: 200 with periodId, status, closedEmployees, skippedAlreadyClosed.
               The same close again: 200 with closedEmployees = 0.
            4. GET …/attendance/periods/2025-07 as br1-mgr: the summaries of his tree at version 1, as the periods page shows the month.
            5. GET api/payroll/attendance/receipts?periodStart=2025-07-01 as payroll-processor: one receipt per summary, at version 1.
            6. The lock is the closed employees' only: br1-mgr still writes by hand a July day of the seeded RIF employee, who sits in
               another directorate of the branch, while the same day of a closed employee is refused.
            """);
        await ResetMonthAsync(s, "57h", month);
        var people = (await TreeEmployeesAsync(RifHr, month)).Split(',', StringSplitOptions.RemoveEmptyEntries);
        AcceptanceGate.RequireData(s.Recorder, people.Length > 0 && !people.Contains(Employee),
            "الوحدة الفرعية لفرع ريف دمشق بلا موظفين أو تضم موظف البذرة", "RIF's sub-unit has no employee, or holds the seeded RIF employee.");
        await BulkAsync(s[Br1], "57h-bulk", RifHr, month, "Present");

        using var outsider = await Api.AttendanceAsync(s.Recorder, Outsider, "57h");
        var (foreign, _, _) = await outsider.PostAsync("close-foreign-agency", $"{PeriodsApi}/{Ym(month)}/close", new { orgUnitId = RifHr });
        var afterForeign = await Db(Core, $"SELECT count(*)::text FROM \"AttendancePeriods\" WHERE \"PeriodStart\"='{Day(month)}';") + "|" + await SummaryCountAsync(month);

        using var branch = await Api.AttendanceAsync(s.Recorder, Br1, "57h");
        var (closed, closedBody, _) = await branch.PostAsync("close", $"{PeriodsApi}/{Ym(month)}/close", new { orgUnitId = RifHr });
        var (again, againBody, _) = await branch.PostAsync("close-again", $"{PeriodsApi}/{Ym(month)}/close", new { orgUnitId = RifHr });
        var (read, readBody, _) = await branch.GetAsync("read", $"{PeriodsApi}/{Ym(month)}");
        var summaries = readBody.GetProperty("summaries").EnumerateArray()
            .Select(x => (Employee: x.GetProperty("employeeId").GetGuid().ToString("D"), Version: x.GetProperty("version").GetInt32(), Working: x.GetProperty("workingDays").GetInt32(), Absent: x.GetProperty("absentDays").GetInt32()))
            .ToList();
        var page = await s[Br1].GetAsync("57h-periods-page", PeriodsUi);
        var stored = await SummaryCountAsync(month);

        // The lock is the closed employees' only: a day of an employee of another directorate is still written by hand.
        var openDay = new DateOnly(2025, 7, 8);
        var (outside, outsideBody) = await s[Br1].PostWithStatusAsync("57h-create-outside-the-closed-unit", $"{AttendanceUi}/Create", $"{AttendanceUi}/Create",
            F(("EmployeeId", Employee), ("AttendanceDate", Day(openDay)), ("Status", "Present"), ("Notes", "حضور موظف من مديرية لم تُغلق")));
        var outsideRecord = await RecordAsync(Employee, openDay);
        var (inside, insideBody) = await s[Br1].PostWithStatusAsync("57h-edit-inside-the-closed-unit",
            $"{AttendanceUi}/Edit/{await RecordIdAsync(people[0], openDay, "57h")}", $"{AttendanceUi}/Create",
            F(("Status", "Remote"), ("Notes", "محاولة على يوم مقفل")));
        var insideRecord = await RecordAsync(people[0], openDay);

        using var processor = await Api.PayrollAsync(s.Recorder, Processor, "57h");
        JsonElement receiptsBody = default;
        var received = await WaitAsync(async () =>
        {
            var (_, body, _) = await processor.GetAsync("receipts", $"api/payroll/attendance/receipts?periodStart={Day(month)}", capture: false);
            receiptsBody = body;
            return body.ValueKind == JsonValueKind.Object && body.GetProperty("count").GetInt32() == people.Length;
        }, 120);
        var receipts = receiptsBody.GetProperty("receipts").EnumerateArray()
            .Select(x => (Employee: x.GetProperty("employeeId").GetGuid().ToString("D"), Version: x.GetProperty("latestVersion").GetInt32(), Absent: x.GetProperty("latestAbsentDays").GetInt32()))
            .ToList();

        s.Checks(new
        {
            month = Ym(month),
            people = people.Length,
            foreign = (int)foreign,
            afterForeign,
            closed = (int)closed,
            closedEmployees = closedBody.GetProperty("closedEmployees").GetInt32(),
            status = Text(closedBody, "status"),
            again = (int)again,
            againClosed = againBody.GetProperty("closedEmployees").GetInt32(),
            outside = (int)outside,
            outsideRecord,
            inside = (int)inside,
            insideRecord,
            againSkipped = againBody.GetProperty("skippedAlreadyClosed").GetInt32(),
            read = (int)read,
            summaries = summaries.Count,
            stored,
            receipts = receipts.Count
        });

        Assert.Equal(HttpStatusCode.NotFound, foreign);
        Assert.Equal("0|0", afterForeign);
        Assert.True(closed == HttpStatusCode.OK, Text(closedBody, "error"));
        Assert.Equal((people.Length, 0), (closedBody.GetProperty("closedEmployees").GetInt32(), closedBody.GetProperty("skippedAlreadyClosed").GetInt32()));
        Assert.True(Guid.TryParse(closedBody.GetProperty("periodId").GetGuid().ToString("D"), out _));
        Assert.Contains(Text(closedBody, "status"), new[] { "PartiallyClosed", "Closed" });
        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal((0, people.Length), (againBody.GetProperty("closedEmployees").GetInt32(), againBody.GetProperty("skippedAlreadyClosed").GetInt32()));
        // The read gives the branch its own tree's summaries: the sub-unit's employees, each at version 1 with July's 23 working days.
        Assert.Equal(HttpStatusCode.OK, read);
        var working = WorkingDays(month).Count;
        Assert.Equal(people.Order(StringComparer.Ordinal), summaries.Select(x => x.Employee).Order(StringComparer.Ordinal));
        Assert.All(summaries, x => Assert.Equal((1, working, 0), (x.Version, x.Working, x.Absent)));
        Assert.Equal(people.Length.ToString(CultureInfo.InvariantCulture), stored);
        Assert.Equal(readBody.GetProperty("closedEmployees").GetInt32(), summaries.Count);
        // The page shows the same month.
        Assert.Contains(Ym(month), page);
        // Outside the closed sub-unit the day is written («حاضر», by hand); inside it the day is locked and stays as the close left it.
        Assert.True(outside == HttpStatusCode.OK, Snip(outsideBody));
        Assert.EndsWith($"|0|0|{Employee}|{Day(openDay)}", outsideRecord);
        Assert.Contains("اليوم ضمن فترة حضور مغلقة", insideBody);
        Assert.EndsWith($"|0|1|{people[0]}|{Day(openDay)}", insideRecord);
        // Payroll: one receipt per summary, the same employees, at version 1.
        Assert.True(received, $"57h: payroll lists {receipts.Count} receipts for {people.Length} summaries.");
        Assert.Equal(people.Order(StringComparer.Ordinal), receipts.Select(x => x.Employee).Order(StringComparer.Ordinal));
        Assert.All(receipts, x => Assert.Equal((1, 0), (x.Version, x.Absent)));
        s.Recorder.WriteResult("Pass");
    }
}
