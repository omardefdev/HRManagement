using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using Xunit.Abstractions;
using Xunit.Sdk;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>The scenarios of a class in the order of their names (53a, 53b, …): 53d needs the month 53a froze.</summary>
public sealed class ScenarioNameOrderer : ITestCaseOrderer
{
    public const string TypeName = "HRManagement.AcceptanceTests.ScenarioNameOrderer";
    public const string AssemblyName = "HRManagement.AcceptanceTests";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);
}

/// <summary>
/// AC-IAM-53a–e (PLAN-53) on the prod-like stack, through the gateway. A salary changes by a request a processor makes and another person
/// approves; the approved change is applied by one applier that carries the unit, the regime and the bank account, and a payroll run of the
/// month holds it. The negatives: the requester cannot approve, an approver cannot request, a payroll user with neither role writes no
/// master data, another agency's approver gets 404, a salary outside the grade's bounds, a mid-month date and a second open request are
/// refused, a change cannot enter a frozen month, and a grade in use cannot be deleted. A grade's bounds change by a request too.
/// The employees are picked from payroll_db: agency A, an active pay profile with a grade that leaves room, and no salary change request
/// yet, so no scenario meets another's employee. Every request a scenario leaves open is cancelled before it ends.
/// 53a freezes the current month for agency A, so a second run of the class on the same stack stops at 53a's precondition: it needs a
/// fresh stack. Nothing stops or recreates a container.
/// </summary>
[RequiredAcceptance]
[TestCaseOrderer(ScenarioNameOrderer.TypeName, ScenarioNameOrderer.AssemblyName)]
public class AcIam53SalaryChangeTests
{
    private const string Ac = "AC-IAM-53";
    private const string Processor = "payroll-processor";
    private const string Approver = "payroll-approver";
    private const string Poster = "a-payroll-poster";
    private const string ForeignApprover = "b-payroll-approver";
    private const string Payroll = "payroll_db";
    private const string Changes = "api/payroll/pay-profile-changes";
    private const string ChangesUi = "ui/payroll/PayProfileChanges";
    private const string ReferenceUi = "ui/payroll/PayrollReferenceChanges";
    private const string LegalReference = "القرار 53 لعام 2026";
    private const int Pending = 0;
    private const int Scheduled = 1;
    private const int Applied = 2;
    private const int Cancelled = 4;

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(90);

    private static Task<string> Db(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll, sql);

    private static Task<long> Count(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll, sql);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>An amount as PostgreSQL writes a numeric(18,2), whatever the culture of the test host.</summary>
    private static string Fixed(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    /// <summary>The first day of the current month on the service's clock (Localization:TimeZone, Asia/Damascus).</summary>
    private static DateOnly MonthStart
    {
        get
        {
            var today = HrLocalTime.ToLocal(DateTime.UtcNow, HrLocalTime.DefaultZone);
            return new DateOnly(today.Year, today.Month, 1);
        }
    }

    /// <summary>The count and an md5 of whole rows (every column): what a refusal must leave as it was.</summary>
    private static Task<string> RowsAsync(string table, string where) =>
        Db($"SELECT count(*)::text || ':' || md5(COALESCE(string_agg(t::text, '|' ORDER BY t.\"Id\"), '')) FROM \"{table}\" t WHERE {where};");

    private static Task<long> StateAsync(string table, string id) => Count($"SELECT \"State\" FROM \"{table}\" WHERE \"Id\"='{id}';");

    // ---------------------------------------------------------------- employees and profiles

    private sealed record Pick(string Employee, string Profile, decimal Salary, string Grade, string GradeCode, decimal Min, decimal Max)
    {
        /// <summary>A salary inside the grade's bounds that differs from the current one by 1000.</summary>
        public decimal Raised => Salary + 1000m <= Max ? Salary + 1000m : Salary - 1000m;
    }

    /// <summary>
    /// An agency-A employee no scenario has used: an active profile that began before this month, a unit, a grade with both bounds
    /// around the salary and room for 1000 either way, and no salary change request at all. The ones with a bank account and a regime
    /// come first. <paramref name="inRun"/> keeps to the employees of that run; <paramref name="noLineIn"/> to those no run of that month holds.
    /// </summary>
    private static async Task<Pick> PickEmployeeAsync(EvidenceRecorder recorder, string step, string? inRun = null, DateOnly? noLineIn = null)
    {
        var seeded = string.Join(",", new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpBr2Id }.Select(id => $"'{id:D}'"));
        var row = await Db(
            "SELECT p.\"EmployeeId\"::text || '|' || p.\"Id\"::text || '|' || p.\"BaseSalary\"::text || '|' || g.\"Id\"::text || '|' || g.\"GradeCode\" || '|' || " +
            "g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"EmployeePayProfiles\" p JOIN \"PayGrades\" g ON g.\"Id\" = p.\"PayGradeId\" " +
            $"WHERE p.\"TenantId\"='{TenantA}' AND p.\"IsActive\" AND p.\"OrgUnitId\" IS NOT NULL AND p.\"EffectiveFrom\" < '{Day(MonthStart)}' " +
            "AND g.\"MaxSalary\" > 0 AND p.\"BaseSalary\" BETWEEN g.\"MinSalary\" AND g.\"MaxSalary\" " +
            "AND (p.\"BaseSalary\" + 1000 <= g.\"MaxSalary\" OR p.\"BaseSalary\" - 1000 >= g.\"MinSalary\") " +
            $"AND p.\"EmployeeId\" NOT IN ({seeded}) " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayProfileChangeRequests\" r WHERE r.\"EmployeeId\" = p.\"EmployeeId\") " +
            (inRun is null ? "" : $"AND EXISTS (SELECT 1 FROM \"SalaryLines\" l WHERE l.\"PayrollRunId\"='{inRun}' AND l.\"EmployeeId\" = p.\"EmployeeId\") ") +
            (noLineIn is not { } month ? "" :
                "AND NOT EXISTS (SELECT 1 FROM \"SalaryLines\" l JOIN \"PayrollRuns\" r ON r.\"Id\" = l.\"PayrollRunId\" WHERE l.\"EmployeeId\" = p.\"EmployeeId\" " +
                $"AND r.\"PeriodStart\" <= '{Day(month.AddMonths(1).AddDays(-1))}' AND r.\"PeriodEnd\" >= '{Day(month)}') ") +
            "ORDER BY (p.\"RegimeCode\" IS NOT NULL) DESC, p.\"EmployeeId\" DESC LIMIT 1;");
        var parts = row.Split('|');
        AcceptanceGate.RequireData(recorder, parts.Length == 7,
            $"{step}: لا يوجد موظف في الجهة A بملف أجر فعّال ودرجة تسمح بالتغيير وبلا طلب سابق",
            $"{step}: no agency-A employee with an active pay profile, a grade that leaves room and no earlier request (a rerun needs a fresh stack).");
        decimal Number(int i) => decimal.Parse(parts[i], CultureInfo.InvariantCulture);
        return new Pick(parts[0], parts[1], Number(2), parts[3], parts[4], Number(5), Number(6));
    }

    /// <summary>The employee's active profile: its id and salary, and that it is the only active one.</summary>
    private static Task<string> ActiveProfileAsync(string employee) =>
        Db("SELECT count(*)::text || '|' || COALESCE(max(\"Id\"::text), '') || '|' || COALESCE(max(\"BaseSalary\")::text, '') " +
           $"FROM \"EmployeePayProfiles\" WHERE \"EmployeeId\"='{employee}' AND \"IsActive\";");

    private static Task<string> ProfilesAsync(string employee) => RowsAsync("EmployeePayProfiles", $"t.\"EmployeeId\"='{employee}'");

    private static Task<long> RequestsAsync(string employee) =>
        Count($"SELECT count(*) FROM \"PayProfileChangeRequests\" WHERE \"EmployeeId\"='{employee}';");

    private static async Task<string> LatestRequestAsync(string employee, string step)
    {
        var id = await Db($"SELECT \"Id\"::text FROM \"PayProfileChangeRequests\" WHERE \"EmployeeId\"='{employee}' ORDER BY \"RequestedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: no salary change request of {employee}.");
        return id;
    }

    // ---------------------------------------------------------------- the API, as one persona

    private sealed class Api(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
    {
        public static async Task<Api> OpenAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")),
                recorder, persona, tag);

        public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(string step, HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json = default;
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                json = JsonSerializer.Deserialize<JsonElement>(text);
            recorder.CaptureHttp($"{tag}-{step}-{persona}", method.Method, "/" + path, (int)response.StatusCode,
                json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json);
            return (response.StatusCode, json);
        }

        public Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string step, string path, object? body = null) =>
            SendAsync(step, HttpMethod.Post, path, body ?? new { });

        public void Dispose() => http.Dispose();
    }

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static object Proposal(string employee, decimal salary, DateOnly from, string reason) => new
    {
        employeeId = employee,
        proposedBaseSalary = salary,
        proposedPayGradeId = (Guid?)null,
        effectiveFrom = Day(from),
        reason,
        legalReference = LegalReference
    };

    /// <summary>A pending request made by the processor through the API; fails the scenario when it is not accepted.</summary>
    private static async Task<string> SubmitAsync(Api processor, string step, string employee, decimal salary, DateOnly from, string reason)
    {
        var (status, body) = await processor.PostAsync(step, Changes, Proposal(employee, salary, from, reason));
        Assert.True(status == HttpStatusCode.Created, $"{step}: the request was not accepted ({(int)status}): {Text(body, "error")}");
        Assert.Equal("PendingApproval", Text(body, "state"));
        return body.GetProperty("requestId").GetGuid().ToString("D");
    }

    /// <summary>Closes a request a scenario left open (pending or scheduled), so the employee has no open request after it.</summary>
    private static async Task CancelAsync(Api processor, string step, string request)
    {
        var state = await StateAsync("PayProfileChangeRequests", request);
        if (state is not (Pending or Scheduled)) return;
        var (status, body) = await processor.PostAsync(step, $"{Changes}/{request}/cancel", new { reason = $"{Ac}: انتهى السيناريو" });
        Assert.True(status == HttpStatusCode.OK, $"{step}: cancelling the request answered {(int)status}: {Text(body, "error")}");
        Assert.Equal(Cancelled, await StateAsync("PayProfileChangeRequests", request));
    }

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

    /// <summary>
    /// The runs of agency A that cover any day of the month, as «kind:status:frozen:lines». The seed holds FinalSettlement and
    /// Correction runs with no salary line in some months: a run of another kind covers only the employees on its lines (P-6), so
    /// they stop no change. <paramref name="kind"/> keeps to one kind.
    /// </summary>
    private static Task<string> RunsOfMonthAsync(DateOnly monthStart, string? kind = null) =>
        Db("SELECT COALESCE(string_agg(r.\"RunType\"::text || ':' || r.\"Status\"::text || ':' || r.\"ResultsFrozen\"::text || ':' || " +
           "(SELECT count(*) FROM \"SalaryLines\" l WHERE l.\"PayrollRunId\" = r.\"Id\")::text, ',' ORDER BY r.\"CreatedAt\"), '') " +
           $"FROM \"PayrollRuns\" r WHERE r.\"TenantId\"='{TenantA}' AND r.\"PeriodStart\" <= '{Day(monthStart.AddMonths(1).AddDays(-1))}' " +
           $"AND r.\"PeriodEnd\" >= '{Day(monthStart)}'" + (kind is null ? ";" : $" AND r.\"RunType\"='{kind}';"));

    // ---------------------------------------------------------------- 53a

    [SkippableFact]
    public async Task Ac53a_a_salary_change_is_requested_approved_by_another_person_applied_whole_and_held_by_the_months_run()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "53a",
            "AC-IAM-53a: the processor requests a raise from the first day of the current month; it waits; he cannot approve it; the approver approves and it is applied to a new profile version that carries the unit, the regime and the bank account; the month's run holds the new salary",
            Processor, Approver);
        s.Steps("""
            1. Precondition, in before.json: no payroll run of the current month (Damascus date) covers the employee — agency A has no
               Regular run of the month, and no run of another kind holds a line of his (the seed's FinalSettlement and Correction runs
               of some months have no lines). A second run of the class on the same stack stops here: 53a freezes the month, so a
               rerun needs a fresh stack.
               Test-only precondition (psql, payroll_db): the employee's seeded pay profile gets a bank account, a bank name, a
               verification time and a regime code; the seed leaves them empty, and the copy must be proved on real values.
            2. payroll-processor opens the salary change form of an agency-A employee and requests a salary inside the grade's bounds
               from the first day of the current month, with a reason and a legal reference → the request's page, state PendingApproval;
               the active pay profile is the same row with the same salary.
            3. Negative: payroll-processor posts the approval of his own request, on the screen and on the API → 403; the request is as
               it was. (No account holds both payroll-processor and payroll-approver; the rule «not the requester» for a user who
               holds both is proved by Iam053SalaryChangeStateMachineTests and Iam053PayrollScopeHttpTests.)
            4. payroll-approver approves on the screen → Applied. The previous version is closed the day before; the new version is the
               only active one, starts on the first day of the month, holds the new salary, and carries the unit, the regime, the bank
               account, the bank name, the verification time and the grade of the previous one. A history row of the previous version
               exists. payroll_db's outbox sequence advanced by the two events (decided, applied) and no outbox row is left.
            5. payroll-processor creates the Regular run of the current month, calculates and validates it → frozen. The run's input
               snapshot of the employee names the new version and its salary, and his line's base component is the new salary for the
               whole month.
            """);
        var (processor, approver) = (s[Processor], s[Approver]);
        using var processorApi = await Api.OpenAsync(s.Recorder, Processor, "53a");

        var month = MonthStart;
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var runsBefore = await RunsOfMonthAsync(month);
        var regularRunsBefore = await RunsOfMonthAsync(month, "Regular");
        AcceptanceGate.RequireData(s.Recorder, regularRunsBefore.Length == 0,
            $"للجهة A تشغيل رواتب اعتيادي يغطي الشهر الجاري ({Day(month)[..7]}): السيناريو يحتاج مكدساً جديداً",
            $"Agency A already has a Regular payroll run covering {Day(month)[..7]} ({regularRunsBefore}): AC-IAM-53a froze the month in an earlier run; start a fresh stack.");
        var pick = await PickEmployeeAsync(s.Recorder, "53a", noLineIn: month);
        // Test-only precondition (psql, payroll_db): the seed's pay profiles have no regime code, so the copy would be proved on an
        // empty value. The employee's profile gets one here; its salary, grade, dates and unit are the seed's. (Since PLAN-55 the
        // bank account is no field of a profile: it is its own entity, and AC-PAY-55 proves it.)
        await Db($"UPDATE \"EmployeePayProfiles\" SET \"RegimeCode\"='law50' WHERE \"Id\"='{pick.Profile}'; SELECT 1;");
        var carried = "\"OrgUnitId\"::text || '|' || COALESCE(\"RegimeCode\",'∅') || '|' || COALESCE(\"PayGradeId\"::text,'∅')";
        var carriedBefore = await Db($"SELECT {carried} FROM \"EmployeePayProfiles\" WHERE \"Id\"='{pick.Profile}';");
        var activeBefore = await ActiveProfileAsync(pick.Employee);
        s.Recorder.WriteBefore(new
        {
            scenario = "53a",
            explicitDomainSnapshot = true,
            month = Day(month),
            runsOfTheMonth = runsBefore,
            runsOfTheMonthFormat = "kind:status:frozen:lines",
            regularRunsOfTheMonth = regularRunsBefore,
            runsHoldingTheEmployeeInTheMonth = 0,
            employee = pick.Employee,
            profile = pick.Profile,
            grade = pick.GradeCode,
            salaryIsInsideBounds = true,
            carriedFields = "OrgUnitId|RegimeCode|PayGradeId",
            carriedFieldsPresent = carriedBefore.Split('|').Select(v => v != "∅").ToArray(),
            carriedFieldsSetByTheTest = "RegimeCode",
            activeBefore
        });

        // 2. The request, on the form.
        var reason = $"{Ac}a: زيادة من أول الشهر";
        var form = $"ui/payroll/EmployeePayProfiles/Create?employeeId={pick.Employee}";
        await processor.GetAsync("53a-form", form);
        var (submitted, submittedBody) = await processor.PostWithStatusAsync("53a-request", "ui/payroll/EmployeePayProfiles/Create", form,
            F(("EmployeeId", pick.Employee), ("ProposedBaseSalary", Money(pick.Raised)), ("ProposedPayGradeId", ""), ("EffectiveFrom", Day(month)),
                ("Reason", reason), ("LegalReference", LegalReference)));
        Assert.True(submitted == HttpStatusCode.OK, $"53a: the request form answered {(int)submitted}.");
        var request = await LatestRequestAsync(pick.Employee, "53a");
        var details = $"{ChangesUi}/Details/{request}";
        var stateAfterRequest = await StateAsync("PayProfileChangeRequests", request);
        var activeAfterRequest = await ActiveProfileAsync(pick.Employee);

        // 3. The requester cannot approve.
        var (ownUi, _) = await processor.PostWithStatusAsync("53a-own-approve", $"{ChangesUi}/Approve/{request}", details, F(("note", "53a")));
        var (ownApi, _) = await processorApi.PostAsync("own-approve-api", $"{Changes}/{request}/approve", new { note = "53a" });
        var stateAfterOwn = await StateAsync("PayProfileChangeRequests", request);
        var activeAfterOwn = await ActiveProfileAsync(pick.Employee);

        // 4. The approver approves.
        var sequenceBefore = await AcMsg40AtomicPublishingTests.OutboxSequenceAsync(Payroll);
        await approver.GetAsync("53a-approver-details", details);
        var (approved, approvedBody) = await approver.PostWithStatusAsync("53a-approve", $"{ChangesUi}/Approve/{request}", details, F(("note", "53a: اعتماد")));
        var stateAfterApproval = await StateAsync("PayProfileChangeRequests", request);
        var sequenceAfter = await AcMsg40AtomicPublishingTests.OutboxSequenceAsync(Payroll);
        var outboxLeft = await AcMsg40AtomicPublishingTests.PollAsync(() => Count("SELECT count(*) FROM \"OutboxMessage\";"), n => n == 0, DeliveryWindow);
        var appliedProfile = await Db($"SELECT COALESCE(\"AppliedProfileId\"::text,'') FROM \"PayProfileChangeRequests\" WHERE \"Id\"='{request}';");
        var activeAfterApproval = await ActiveProfileAsync(pick.Employee);
        var newVersion = await Db(
            "SELECT \"BaseSalary\"::text || '|' || to_char(\"EffectiveFrom\", 'YYYY-MM-DD') || '|' || COALESCE(to_char(\"EffectiveTo\", 'YYYY-MM-DD'),'∅') || '|' || \"IsActive\"::text " +
            $"FROM \"EmployeePayProfiles\" WHERE \"Id\"='{appliedProfile}';");
        var carriedAfter = await Db($"SELECT {carried} FROM \"EmployeePayProfiles\" WHERE \"Id\"='{appliedProfile}';");
        var previousVersion = await Db(
            "SELECT \"BaseSalary\"::text || '|' || COALESCE(to_char(\"EffectiveTo\", 'YYYY-MM-DD'),'∅') || '|' || \"IsActive\"::text " +
            $"FROM \"EmployeePayProfiles\" WHERE \"Id\"='{pick.Profile}';");
        var historyRows = await Count(
            $"SELECT count(*) FROM \"EmployeePayProfileHistories\" WHERE \"EmployeeId\"='{pick.Employee}' AND \"SourceProfileId\"='{pick.Profile}' AND \"BaseSalary\"={Money(pick.Salary)};");
        var requestRow = await Db(
            "SELECT \"RequestedBy\"::text || '|' || COALESCE(\"DecidedBy\"::text,'') || '|' || (\"RequestedBy\" <> \"DecidedBy\")::text || '|' || COALESCE(\"LegalReference\",'') " +
            $"FROM \"PayProfileChangeRequests\" WHERE \"Id\"='{request}';");

        // 5. The month's run.
        // PLAN-54 (found by its pull request's Acceptance run): a class that ran earlier on this stack may have added an
        // employment record after the seed (AC-IAM-50b does), and the service-regime gate then refuses every run of the agency.
        // The seed's classification is applied to such records first; the order of a group's classes changes from run to run.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(TenantA);
        var (created, createdBody) = await processorApi.PostAsync("create-run", "api/payroll/runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = monthEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = DateTime.UtcNow
        });
        Assert.True(created == HttpStatusCode.OK || created == HttpStatusCode.Created, $"53a: creating the month's run answered {(int)created}: {Text(createdBody, "error")}");
        var run = createdBody.GetProperty("id").GetGuid().ToString("D");
        var (calculated, calculatedBody) = await processorApi.PostAsync("calculate", $"api/payroll/runs/{run}/calculate");
        Assert.True(calculated == HttpStatusCode.OK, $"53a: calculating the month's run answered {(int)calculated}: {Text(calculatedBody, "error")}");
        var (validated, validatedBody) = await processorApi.PostAsync("validate", $"api/payroll/runs/{run}/validate");
        Assert.True(validated == HttpStatusCode.OK, $"53a: validating the month's run answered {(int)validated}: {Text(validatedBody, "error")}");
        var (_, freeze) = await processorApi.SendAsync("freeze-status", HttpMethod.Get, $"api/payroll/runs/{run}/freeze-status");
        var frozen = freeze.ValueKind == JsonValueKind.Object && freeze.GetProperty("resultsFrozen").GetBoolean();
        var snapshot = await Db(
            "SELECT COALESCE(max(sn.\"EmployeePayProfileId\"::text || '~' || sn.\"ProfileVersion\"), '') FROM \"PayrollRunInputSnapshots\" sn " +
            $"JOIN \"PayrollRunJobs\" j ON j.\"Id\" = sn.\"PayrollRunJobId\" WHERE j.\"PayrollRunId\"='{run}' AND sn.\"EmployeeId\"='{pick.Employee}';");
        var baseComponent = await Db(
            "SELECT COALESCE(string_agg(c.\"Amount\"::text, ',' ORDER BY c.\"Amount\"), '') FROM \"SalaryLineComponents\" c JOIN \"SalaryLines\" l ON l.\"Id\" = c.\"SalaryLineId\" " +
            $"WHERE l.\"PayrollRunId\"='{run}' AND l.\"EmployeeId\"='{pick.Employee}' AND c.\"ComponentType\"='BaseSalary';");
        var lines = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}' AND \"EmployeeId\"='{pick.Employee}';");

        await AuditAsync(s, request, pick.Profile, appliedProfile, run);
        s.Checks(new
        {
            month = Day(month),
            employee = pick.Employee,
            request,
            submitted = (int)submitted,
            stateAfterRequest,
            activeBefore,
            activeAfterRequest,
            ownApproveOnTheScreen = (int)ownUi,
            ownApproveOnTheApi = (int)ownApi,
            stateAfterOwn,
            approved = (int)approved,
            stateAfterApproval,
            appliedProfile,
            activeAfterApproval,
            newVersionIsTheRaise = newVersion == $"{Fixed(pick.Raised)}|{Day(month)}|∅|true",
            previousVersionIsClosedTheDayBefore = previousVersion == $"{Fixed(pick.Salary)}|{Day(month.AddDays(-1))}|false",
            carriedUnchanged = carriedBefore == carriedAfter,
            historyRows,
            requesterAndApproverDiffer = requestRow.Split('|').ElementAtOrDefault(2),
            legalReferenceKept = requestRow.EndsWith('|' + LegalReference, StringComparison.Ordinal),
            outboxRowsWritten = sequenceAfter - sequenceBefore,
            outboxLeft,
            run,
            frozen,
            snapshotNamesTheNewVersion = snapshot.StartsWith(appliedProfile + "~", StringComparison.Ordinal),
            snapshotHoldsTheNewSalary = snapshot.Contains($"|{Fixed(pick.Raised)}|", StringComparison.Ordinal),
            lines,
            baseComponentIsTheNewSalary = baseComponent == $"{Fixed(pick.Raised)}"
        });

        Assert.Equal(Pending, stateAfterRequest);
        Assert.Equal(activeBefore, activeAfterRequest);
        Assert.Contains("بانتظار", submittedBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, ownUi);
        Assert.Equal(HttpStatusCode.Forbidden, ownApi);
        Assert.Equal(Pending, stateAfterOwn);
        Assert.Equal(activeBefore, activeAfterOwn);
        Assert.True(approved == HttpStatusCode.OK, $"53a: the approval answered {(int)approved}: {Snip(approvedBody)}");
        Assert.Equal(Applied, stateAfterApproval);
        Assert.True(Guid.TryParse(appliedProfile, out _), "53a: the applied request names no profile.");
        Assert.NotEqual(pick.Profile, appliedProfile);
        Assert.Equal($"1|{appliedProfile}|{Fixed(pick.Raised)}", activeAfterApproval);
        Assert.Equal($"{Fixed(pick.Raised)}|{Day(month)}|∅|true", newVersion);
        Assert.Equal($"{Fixed(pick.Salary)}|{Day(month.AddDays(-1))}|false", previousVersion);
        Assert.DoesNotContain("∅", carriedBefore, StringComparison.Ordinal);
        Assert.Equal(carriedBefore, carriedAfter);
        Assert.True(historyRows >= 1, "53a: no history row of the previous version.");
        Assert.Equal("true", requestRow.Split('|').ElementAtOrDefault(2));
        Assert.EndsWith('|' + LegalReference, requestRow, StringComparison.Ordinal);
        Assert.True(sequenceAfter - sequenceBefore >= 2, $"53a: the approval wrote {sequenceAfter - sequenceBefore} outbox rows, not the decided and the applied events.");
        Assert.Equal(0, outboxLeft);
        Assert.True(frozen, "53a: the validated run is not frozen.");
        Assert.Equal(1, lines);
        Assert.StartsWith(appliedProfile + "~", snapshot, StringComparison.Ordinal);
        Assert.Contains($"|{Fixed(pick.Raised)}|", snapshot, StringComparison.Ordinal);
        Assert.Equal($"{Fixed(pick.Raised)}", baseComponent);
        s.Recorder.WriteResult("Pass");
    }

    private static string Snip(string body) => body.Length > 400 ? body[..400] : body;

    // ---------------------------------------------------------------- 53b

    [SkippableFact]
    public async Task Ac53b_an_approver_requests_nothing_a_poster_writes_no_master_data_and_another_agencys_approver_gets_404()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "53b",
            "AC-IAM-53b: payroll-approver cannot request a salary change (form and PUT → 403); a-payroll-poster cannot edit a deduction type or delete a grade (403); b-payroll-approver gets 404 on agency A's request; nothing changes",
            Processor, Approver, Poster, ForeignApprover);
        s.Steps("""
            Precondition: payroll-processor makes a request for an agency-A employee from next month (pending). It is cancelled at the end.
            1. Negative: payroll-approver posts the request form (EmployeePayProfiles/Create) and the PUT alias for another employee
               → 403; that employee has no request and his profiles are as they were.
            2. Negative: a-payroll-poster (the payroll role and payroll-poster only) posts DeductionTypes/Edit and PayGrades/Delete
               → 403; the deduction type, the grade and the reference change requests are as they were.
            3. Negative: b-payroll-approver (agency B) reads agency A's request (API and screen), and approves it on the API and on the
               screen → 404 each; the request is still pending.
            Each form post carries a token of a page its user can open, so the answer is the authorization's, not the token check's.
            """);
        var (approver, poster, foreign) = (s[Approver], s[Poster], s[ForeignApprover]);
        using var processorApi = await Api.OpenAsync(s.Recorder, Processor, "53b");
        using var approverApi = await Api.OpenAsync(s.Recorder, Approver, "53b");
        using var foreignApi = await Api.OpenAsync(s.Recorder, ForeignApprover, "53b");

        var next = MonthStart.AddMonths(1);
        var requested = await PickEmployeeAsync(s.Recorder, "53b");
        var request = await SubmitAsync(processorApi, "precondition-request", requested.Employee, requested.Raised, next, $"{Ac}b: طلب للجهة A");
        try
        {
            var target = await PickEmployeeAsync(s.Recorder, "53b");
            var deduction = await Db($"SELECT \"Id\"::text FROM \"DeductionTypes\" WHERE \"TenantId\"='{TenantA}' ORDER BY \"Id\" LIMIT 1;");
            AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(deduction, out _), "لا يوجد نوع اقتطاع للجهة A", "Agency A has no deduction type.");

            async Task<string> RowsNowAsync() => string.Join(" / ", await Task.WhenAll(
                ProfilesAsync(target.Employee),
                RowsAsync("PayProfileChangeRequests", $"t.\"EmployeeId\"='{target.Employee}'"),
                RowsAsync("PayProfileChangeRequests", $"t.\"Id\"='{request}'"),
                RowsAsync("DeductionTypes", $"t.\"Id\"='{deduction}'"),
                RowsAsync("PayGrades", $"t.\"Id\"='{target.Grade}'"),
                RowsAsync("PayrollReferenceChangeRequests", $"t.\"TenantId\"='{TenantA}'")));
            var before = await RowsNowAsync();
            s.Recorder.WriteBefore(new { scenario = "53b", explicitDomainSnapshot = true, request, target = target.Employee, deduction, grade = target.GradeCode, rows = before });

            var details = $"{ChangesUi}/Details/{request}";
            var (approverForm, _) = await approver.PostWithStatusAsync("53b-approver-form", "ui/payroll/EmployeePayProfiles/Create", details,
                F(("EmployeeId", target.Employee), ("ProposedBaseSalary", Money(target.Raised)), ("EffectiveFrom", Day(next)), ("Reason", $"{Ac}b"),
                    ("LegalReference", LegalReference)));
            var (approverPut, _) = await approverApi.SendAsync("approver-put", HttpMethod.Put, $"api/payroll/employees/{target.Employee}/pay-profile", new
            {
                baseSalary = target.Raised,
                payGradeId = (Guid?)null,
                effectiveFrom = next.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                changeReason = $"{Ac}b",
                legalReference = LegalReference
            });

            var (posterDeduction, _) = await poster.PostWithStatusAsync("53b-poster-deduction", $"ui/payroll/DeductionTypes/Edit/{deduction}", ChangesUi,
                F(("Id", deduction), ("Code", "AC53"), ("NameAr", $"{Ac}b")));
            var (posterGrade, _) = await poster.PostWithStatusAsync("53b-poster-grade", $"ui/payroll/PayGrades/Delete/{target.Grade}", ChangesUi,
                F(("Reason", $"{Ac}b"), ("LegalReference", LegalReference)));

            var (foreignGet, _) = await foreignApi.SendAsync("foreign-get", HttpMethod.Get, $"{Changes}/{request}");
            var (foreignApprove, _) = await foreignApi.PostAsync("foreign-approve", $"{Changes}/{request}/approve", new { note = "53b" });
            var (foreignDetails, _) = await foreign.GetWithStatusAsync("53b-foreign-details", details);
            var (foreignForm, _) = await foreign.PostWithStatusAsync("53b-foreign-approve-form", $"{ChangesUi}/Approve/{request}", ChangesUi, F(("note", "53b")));
            var foreignList = await foreign.GetAsync("53b-foreign-list", ChangesUi);
            var after = await RowsNowAsync();
            var state = await StateAsync("PayProfileChangeRequests", request);

            await AuditAsync(s, request);
            s.Checks(new
            {
                request,
                approverForm = (int)approverForm,
                approverPut = (int)approverPut,
                posterDeduction = (int)posterDeduction,
                posterGrade = (int)posterGrade,
                foreignGet = (int)foreignGet,
                foreignApprove = (int)foreignApprove,
                foreignDetails = (int)foreignDetails,
                foreignForm = (int)foreignForm,
                listedToTheOtherAgency = foreignList.Contains(request, StringComparison.OrdinalIgnoreCase),
                state,
                before,
                after
            });

            Assert.Equal(HttpStatusCode.Forbidden, approverForm);
            Assert.Equal(HttpStatusCode.Forbidden, approverPut);
            Assert.Equal(HttpStatusCode.Forbidden, posterDeduction);
            Assert.Equal(HttpStatusCode.Forbidden, posterGrade);
            Assert.Equal(HttpStatusCode.NotFound, foreignGet);
            Assert.Equal(HttpStatusCode.NotFound, foreignApprove);
            Assert.Equal(HttpStatusCode.NotFound, foreignDetails);
            Assert.Equal(HttpStatusCode.NotFound, foreignForm);
            Assert.DoesNotContain(request, foreignList, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Pending, state);
            Assert.Equal(before, after);
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await CancelAsync(processorApi, "cancel-precondition", request);
        }
    }

    // ---------------------------------------------------------------- 53c

    [SkippableFact]
    public async Task Ac53c_a_salary_outside_the_grades_bounds_a_mid_month_date_and_a_second_open_request_are_refused()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "53c",
            "AC-IAM-53c: a salary above the grade's maximum → 400 naming both bounds; a date in the middle of a month → 400; a second open request of the same employee → 409; no row for any refusal",
            Processor);
        s.Steps("""
            1. Negative: payroll-processor requests, on the form, a salary one above the grade's maximum → 400; the page names the
               grade, its minimum and its maximum; no request row.
            2. Negative: on the API, the same employee with a valid salary from the 15th of next month → 400 («أول يوم من شهر»); no row.
            3. Positive control: a valid request from next month → 201, pending.
            4. Negative: a second request of the same employee → 409 («طلب تغيير راتب مفتوح»); still one row.
            5. The request is cancelled; the employee's pay profiles are as they were through all of it.
            """);
        var processor = s[Processor];
        using var processorApi = await Api.OpenAsync(s.Recorder, Processor, "53c");

        var next = MonthStart.AddMonths(1);
        var pick = await PickEmployeeAsync(s.Recorder, "53c");
        var profilesBefore = await ProfilesAsync(pick.Employee);
        s.Recorder.WriteBefore(new { scenario = "53c", explicitDomainSnapshot = true, employee = pick.Employee, grade = pick.GradeCode, requests = 0, profiles = profilesBefore });

        var form = $"ui/payroll/EmployeePayProfiles/Create?employeeId={pick.Employee}";
        var (outside, outsideBody) = await processor.PostWithStatusAsync("53c-outside-bounds", "ui/payroll/EmployeePayProfiles/Create", form,
            F(("EmployeeId", pick.Employee), ("ProposedBaseSalary", Money(pick.Max + 1m)), ("ProposedPayGradeId", ""), ("EffectiveFrom", Day(next)),
                ("Reason", $"{Ac}c: فوق الحد الأعلى"), ("LegalReference", LegalReference)));
        var rowsAfterOutside = await RequestsAsync(pick.Employee);

        var (midMonth, midMonthBody) = await processorApi.PostAsync("mid-month", Changes, Proposal(pick.Employee, pick.Raised, next.AddDays(14), $"{Ac}c: منتصف الشهر"));
        var rowsAfterMidMonth = await RequestsAsync(pick.Employee);

        var request = await SubmitAsync(processorApi, "first-request", pick.Employee, pick.Raised, next, $"{Ac}c: الطلب الأول");
        try
        {
            var (second, secondBody) = await processorApi.PostAsync("second-request", Changes, Proposal(pick.Employee, pick.Raised, next, $"{Ac}c: الطلب الثاني"));
            var rowsAfterSecond = await RequestsAsync(pick.Employee);
            var profilesAfter = await ProfilesAsync(pick.Employee);

            await AuditAsync(s, request);
            s.Checks(new
            {
                employee = pick.Employee,
                outside = (int)outside,
                outsideNamesTheGrade = outsideBody.Contains($"خارج حدود الدرجة {pick.GradeCode}", StringComparison.Ordinal),
                outsideNamesTheMinimum = outsideBody.Contains($"الحد الأدنى {Money(pick.Min)}", StringComparison.Ordinal),
                outsideNamesTheMaximum = outsideBody.Contains($"الحد الأعلى {Money(pick.Max)}", StringComparison.Ordinal),
                rowsAfterOutside,
                midMonth = (int)midMonth,
                midMonthError = Text(midMonthBody, "error"),
                rowsAfterMidMonth,
                request,
                second = (int)second,
                secondError = Text(secondBody, "error"),
                rowsAfterSecond,
                profilesBefore,
                profilesAfter
            });

            Assert.Equal(HttpStatusCode.BadRequest, outside);
            Assert.Contains($"خارج حدود الدرجة {pick.GradeCode}", outsideBody, StringComparison.Ordinal);
            Assert.Contains($"الحد الأدنى {Money(pick.Min)}", outsideBody, StringComparison.Ordinal);
            Assert.Contains($"الحد الأعلى {Money(pick.Max)}", outsideBody, StringComparison.Ordinal);
            Assert.Equal(0, rowsAfterOutside);
            Assert.Equal(HttpStatusCode.BadRequest, midMonth);
            Assert.Contains("أول يوم من شهر", Text(midMonthBody, "error"), StringComparison.Ordinal);
            Assert.Equal(0, rowsAfterMidMonth);
            Assert.Equal(HttpStatusCode.Conflict, second);
            Assert.Contains("طلب تغيير راتب مفتوح", Text(secondBody, "error"), StringComparison.Ordinal);
            Assert.Equal(1, rowsAfterSecond);
            Assert.Equal(profilesBefore, profilesAfter);
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await CancelAsync(processorApi, "cancel-first-request", request);
        }
    }

    // ---------------------------------------------------------------- 53d

    [SkippableFact]
    public async Task Ac53d_a_change_for_next_month_is_scheduled_and_a_change_cannot_enter_the_frozen_month()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "53d",
            "AC-IAM-53d: an approved request from next month is Scheduled and the active profile is as it was; a request from the month 53a froze cannot be approved (409 naming the month), stays pending, and the run's snapshot is as it was",
            Processor, Approver);
        s.Steps("""
            Precondition: the frozen Regular run of the current month that AC-IAM-53a made (the scenarios run in the order of their names).
            1. Positive: payroll-processor requests a raise from the first day of next month; payroll-approver approves → Scheduled;
               the employee's pay profiles are the same rows (nothing is applied before its date).
            2. Negative: payroll-processor requests a raise from the first day of the current month for an employee of the frozen run
               (the request is accepted: it is only a request). payroll-approver approves → 409, the message names the month and the
               frozen run; the state is PendingApproval; the employee's profiles, his line and his input snapshot in the run are as
               they were.
            3. Both requests are cancelled, so neither employee is left with an open request.
            """);
        using var processorApi = await Api.OpenAsync(s.Recorder, Processor, "53d");
        using var approverApi = await Api.OpenAsync(s.Recorder, Approver, "53d");

        var month = MonthStart;
        var run = await Db(
            $"SELECT COALESCE((SELECT \"Id\"::text FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"ResultsFrozen\" AND \"RunType\"='Regular' " +
            $"AND \"PeriodStart\" <= '{Day(month)}' AND \"PeriodEnd\" >= '{Day(month.AddMonths(1).AddDays(-1))}' ORDER BY \"CreatedAt\" LIMIT 1), '');");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(run, out _),
            "لا يوجد تشغيل مجمّد للشهر الجاري: السيناريو 53a لم ينجح قبل هذا السيناريو",
            "No frozen Regular run of the current month for agency A: AC-IAM-53a must pass before AC-IAM-53d.");

        var future = await PickEmployeeAsync(s.Recorder, "53d-next-month");
        var futureProfiles = await ProfilesAsync(future.Employee);
        var scheduled = await SubmitAsync(processorApi, "request-next-month", future.Employee, future.Raised, month.AddMonths(1), $"{Ac}d: من الشهر القادم");
        string? blocked = null;
        try
        {
            var (approveFuture, approveFutureBody) = await approverApi.PostAsync("approve-next-month", $"{Changes}/{scheduled}/approve", new { note = "53d" });
            var scheduledState = await StateAsync("PayProfileChangeRequests", scheduled);
            var futureProfilesAfter = await ProfilesAsync(future.Employee);

            var frozen = await PickEmployeeAsync(s.Recorder, "53d-frozen-month", run);
            Task<string> RunRowsAsync() => Db(
                "SELECT (SELECT count(*)::text || ':' || md5(COALESCE(string_agg(l::text, '|' ORDER BY l.\"Id\"), '')) FROM \"SalaryLines\" l " +
                $"WHERE l.\"PayrollRunId\"='{run}' AND l.\"EmployeeId\"='{frozen.Employee}') || ' / ' || " +
                "(SELECT count(*)::text || ':' || md5(COALESCE(string_agg(sn::text, '|' ORDER BY sn.\"Id\"), '')) FROM \"PayrollRunInputSnapshots\" sn " +
                $"JOIN \"PayrollRunJobs\" j ON j.\"Id\" = sn.\"PayrollRunJobId\" WHERE j.\"PayrollRunId\"='{run}' AND sn.\"EmployeeId\"='{frozen.Employee}') || ' / ' || " +
                $"(SELECT \"Status\"::text || ':' || \"ResultsFrozen\"::text || ':' || \"TotalNetPay\"::text FROM \"PayrollRuns\" WHERE \"Id\"='{run}');");
            var frozenProfiles = await ProfilesAsync(frozen.Employee);
            var runBefore = await RunRowsAsync();
            s.Recorder.WriteBefore(new
            {
                scenario = "53d",
                explicitDomainSnapshot = true,
                month = Day(month),
                run,
                scheduledEmployee = future.Employee,
                frozenEmployee = frozen.Employee,
                futureProfiles,
                frozenProfiles,
                runRows = runBefore
            });

            blocked = await SubmitAsync(processorApi, "request-frozen-month", frozen.Employee, frozen.Raised, month, $"{Ac}d: في الشهر المجمّد");
            var (approveFrozen, approveFrozenBody) = await approverApi.PostAsync("approve-frozen-month", $"{Changes}/{blocked}/approve", new { note = "53d" });
            var blockedState = await StateAsync("PayProfileChangeRequests", blocked);
            var blockedApplied = await Db($"SELECT COALESCE(\"AppliedProfileId\"::text,'') FROM \"PayProfileChangeRequests\" WHERE \"Id\"='{blocked}';");
            var frozenProfilesAfter = await ProfilesAsync(frozen.Employee);
            var runAfter = await RunRowsAsync();
            var refusal = Text(approveFrozenBody, "error");

            await AuditAsync(s, scheduled, blocked);
            s.Checks(new
            {
                month = Day(month),
                run,
                scheduled,
                approveNextMonth = (int)approveFuture,
                approveNextMonthState = Text(approveFutureBody, "state"),
                scheduledState,
                futureProfilesUnchanged = futureProfiles == futureProfilesAfter,
                blocked,
                approveFrozenMonth = (int)approveFrozen,
                refusal,
                refusalState = Text(approveFrozenBody, "state"),
                blockedState,
                blockedApplied,
                frozenProfilesUnchanged = frozenProfiles == frozenProfilesAfter,
                runBefore,
                runAfter
            });

            Assert.Equal(HttpStatusCode.OK, approveFuture);
            Assert.Equal("Scheduled", Text(approveFutureBody, "state"));
            Assert.Equal(Scheduled, scheduledState);
            Assert.Equal(futureProfiles, futureProfilesAfter);
            Assert.Equal(HttpStatusCode.Conflict, approveFrozen);
            Assert.Contains(month.ToString("yyyy/MM", CultureInfo.InvariantCulture), refusal, StringComparison.Ordinal);
            Assert.Contains("مجمّد", refusal, StringComparison.Ordinal);
            Assert.Equal("PendingApproval", Text(approveFrozenBody, "state"));
            Assert.Equal(Pending, blockedState);
            Assert.Equal("", blockedApplied);
            Assert.Equal(frozenProfiles, frozenProfilesAfter);
            Assert.Equal(runBefore, runAfter);
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await CancelAsync(processorApi, "cancel-scheduled", scheduled);
            if (blocked is not null) await CancelAsync(processorApi, "cancel-blocked", blocked);
        }
    }

    // ---------------------------------------------------------------- 53e

    private sealed record GradeRow(string Id, string Scale, string Code, string Name, decimal Base, decimal Min, decimal Max)
    {
        public List<KeyValuePair<string, string>> Form(decimal max, string reason) =>
            F(("SalaryScaleId", Scale), ("GradeCode", Code), ("GradeNameAr", Name), ("BaseSalary", Money(Base)), ("MinSalary", Money(Min)),
                ("MaxSalary", Money(max)), ("Reason", reason), ("LegalReference", LegalReference));
    }

    private static async Task<GradeRow?> ReadGradeAsync(string where)
    {
        var row = await Db(
            "SELECT g.\"Id\"::text || '|' || g.\"SalaryScaleId\"::text || '|' || g.\"GradeCode\" || '|' || g.\"GradeNameAr\" || '|' || g.\"BaseSalary\"::text || '|' || " +
            $"g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g WHERE {where} ORDER BY length(g.\"GradeCode\") DESC, g.\"GradeCode\" DESC LIMIT 1;");
        var parts = row.Split('|');
        if (parts.Length != 7) return null;
        decimal Number(int i) => decimal.Parse(parts[i], CultureInfo.InvariantCulture);
        return new GradeRow(parts[0], parts[1], parts[2], parts[3], Number(4), Number(5), Number(6));
    }

    private static async Task<string> LatestReferenceRequestAsync(string grade, string step)
    {
        var id = await Db($"SELECT \"Id\"::text FROM \"PayrollReferenceChangeRequests\" WHERE \"EntityId\"='{grade}' ORDER BY \"RequestedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: no reference change request of grade {grade}.");
        return id;
    }

    /// <summary>A user on a page (PLAN-67): the span the browser fills with his name from the gateway's registry, by his id.</summary>
    private static string UserSpan(string userId) => $"data-hr-user-id=\"{userId}\"";

    /// <summary>Digits only: an amount is found on a page whatever separators its culture writes.</summary>
    private static string Digits(string text) => Regex.Replace(text, @"[^\d]", "");

    [SkippableFact]
    public async Task Ac53e_a_grades_maximum_changes_by_a_request_another_person_approves_and_a_grade_in_use_is_not_deleted()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "53e",
            "AC-IAM-53e: the processor requests a higher maximum for a grade with a legal reference; the old maximum stays until payroll-approver approves; the requester cannot approve; a grade that pay profiles name is not deleted",
            Processor, Approver);
        s.Steps("""
            The grade: one of agency A that pay profiles name, with no open request, and not G5 (the recruitment classes read G5).
            1. payroll-processor posts the grade's edit form with a maximum 1000 higher, a reason and a legal reference → the request's
               page, pending; the grade row is as it was.
            2. Negative: payroll-processor posts the approval of his own request → 403; still pending, the grade as it was.
            3. payroll-approver approves → applied; the grade's maximum is the new one, its details page shows it, and the request's
               page and row hold the requester, the approver (another person) and the legal reference. (A page names a user by the
               span the browser fills from the gateway's registry, PLAN-67; the row holds both names as text.)
            4. Negative: payroll-processor posts the grade's delete request → 409 with «لا تُحذف الدرجة»; the grade remains, and no
               request row is added.
            5. The maximum is put back the same way (a request, approved by payroll-approver), so the seed's grade is as it was.
            """);
        var (processor, approver) = (s[Processor], s[Approver]);

        var grade = await ReadGradeAsync(
            $"g.\"TenantId\"='{TenantA}' AND g.\"GradeCode\" <> 'G5' AND g.\"MaxSalary\" > 0 " +
            "AND EXISTS (SELECT 1 FROM \"EmployeePayProfiles\" p WHERE p.\"PayGradeId\" = g.\"Id\") " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayrollReferenceChangeRequests\" r WHERE r.\"EntityId\" = g.\"Id\" AND r.\"State\" = 0)");
        AcceptanceGate.RequireData(s.Recorder, grade is not null, "لا توجد درجة للجهة A عليها ملفات أجر وبلا طلب مفتوح", "Agency A has no grade in use without an open request.");
        var g = grade!;
        var raised = g.Max + 1000m;
        Task<string> GradeValuesAsync() => Db(
            "SELECT \"SalaryScaleId\"::text || '|' || \"GradeCode\" || '|' || \"GradeNameAr\" || '|' || \"BaseSalary\"::text || '|' || \"MinSalary\"::text || '|' || \"MaxSalary\"::text " +
            $"FROM \"PayGrades\" WHERE \"Id\"='{g.Id}';");
        Task<long> RequestRowsAsync() => Count($"SELECT count(*) FROM \"PayrollReferenceChangeRequests\" WHERE \"EntityId\"='{g.Id}';");
        var valuesBefore = await GradeValuesAsync();
        var rowBefore = await RowsAsync("PayGrades", $"t.\"Id\"='{g.Id}'");
        s.Recorder.WriteBefore(new { scenario = "53e", explicitDomainSnapshot = true, grade = g.Id, code = g.Code, values = valuesBefore, row = rowBefore });

        var edit = $"ui/payroll/PayGrades/Edit/{g.Id}";
        var gradeDetails = $"ui/payroll/PayGrades/Details/{g.Id}";
        try
        {
            // 1. The request.
            var (submitted, submittedBody) = await processor.PostWithStatusAsync("53e-request", edit, edit, g.Form(raised, $"{Ac}e: رفع الحد الأعلى"));
            Assert.True(submitted == HttpStatusCode.OK, $"53e: the grade's edit form answered {(int)submitted}: {Snip(submittedBody)}");
            var request = await LatestReferenceRequestAsync(g.Id, "53e");
            var requestDetails = $"{ReferenceUi}/Details/{request}";
            var stateAfterRequest = await StateAsync("PayrollReferenceChangeRequests", request);
            var rowAfterRequest = await RowsAsync("PayGrades", $"t.\"Id\"='{g.Id}'");

            // 2. The requester cannot approve.
            var (own, _) = await processor.PostWithStatusAsync("53e-own-approve", $"{ReferenceUi}/Approve/{request}", requestDetails, F(("note", "53e")));
            var stateAfterOwn = await StateAsync("PayrollReferenceChangeRequests", request);
            var rowAfterOwn = await RowsAsync("PayGrades", $"t.\"Id\"='{g.Id}'");

            // 3. Another person approves.
            await approver.GetAsync("53e-approver-details", requestDetails);
            var (approved, approvedBody) = await approver.PostWithStatusAsync("53e-approve", $"{ReferenceUi}/Approve/{request}", requestDetails, F(("note", "53e: اعتماد")));
            var stateAfterApproval = await StateAsync("PayrollReferenceChangeRequests", request);
            var maxAfterApproval = await Db($"SELECT \"MaxSalary\"::text FROM \"PayGrades\" WHERE \"Id\"='{g.Id}';");
            var gradePage = await processor.GetAsync("53e-grade-details", gradeDetails);
            var requestPage = await processor.GetAsync("53e-request-details", requestDetails);
            var requestRow = await Db(
                "SELECT \"RequestedBy\"::text || '|' || COALESCE(\"DecidedBy\"::text,'') || '|' || (\"RequestedBy\" <> \"DecidedBy\")::text || '|' || " +
                "COALESCE(\"RequestedByName\",'') || '|' || COALESCE(\"DecidedByName\",'') || '|' || COALESCE(\"LegalReference\",'') " +
                $"FROM \"PayrollReferenceChangeRequests\" WHERE \"Id\"='{request}';");
            var names = requestRow.Split('|');

            // 4. A grade in use is not deleted.
            var delete = $"ui/payroll/PayGrades/Delete/{g.Id}";
            var requestsBeforeDelete = await RequestRowsAsync();
            var (deleted, deletedBody) = await processor.PostWithStatusAsync("53e-delete-in-use", delete, delete,
                F(("Reason", $"{Ac}e: حذف درجة مستعملة"), ("LegalReference", LegalReference)));
            var requestsAfterDelete = await RequestRowsAsync();
            var gradeRemains = await Count($"SELECT count(*) FROM \"PayGrades\" WHERE \"Id\"='{g.Id}';");

            await AuditAsync(s, request, g.Id);
            s.Checks(new
            {
                grade = g.Id,
                code = g.Code,
                request,
                submitted = (int)submitted,
                stateAfterRequest,
                gradeUnchangedBeforeApproval = rowBefore == rowAfterRequest,
                ownApprove = (int)own,
                stateAfterOwn,
                gradeUnchangedAfterOwn = rowBefore == rowAfterOwn,
                approved = (int)approved,
                stateAfterApproval,
                maximumIsTheNewOne = maxAfterApproval == $"{Fixed(raised)}",
                gradePageShowsTheNewMaximum = Digits(gradePage).Contains(Digits(Money(raised)), StringComparison.Ordinal),
                requesterAndApproverDiffer = names.ElementAtOrDefault(2),
                requestPageShowsTheReference = requestPage.Contains(LegalReference, StringComparison.Ordinal),
                rowHoldsBothNames = names.Length == 6 && names[3].Length > 0 && names[4].Length > 0,
                requestPageNamesTheRequester = names.Length == 6 && requestPage.Contains(UserSpan(names[0]), StringComparison.OrdinalIgnoreCase),
                requestPageNamesTheApprover = names.Length == 6 && requestPage.Contains(UserSpan(names[1]), StringComparison.OrdinalIgnoreCase),
                deleteInUse = (int)deleted,
                deleteSaysWhy = deletedBody.Contains("لا تُحذف الدرجة", StringComparison.Ordinal),
                requestsBeforeDelete,
                requestsAfterDelete,
                gradeRemains
            });

            Assert.Contains("بانتظار", submittedBody, StringComparison.Ordinal);
            Assert.Equal(0, stateAfterRequest);
            Assert.Equal(rowBefore, rowAfterRequest);
            Assert.Equal(HttpStatusCode.Forbidden, own);
            Assert.Equal(0, stateAfterOwn);
            Assert.Equal(rowBefore, rowAfterOwn);
            Assert.True(approved == HttpStatusCode.OK, $"53e: the approval answered {(int)approved}: {Snip(approvedBody)}");
            Assert.Equal(1, stateAfterApproval);
            Assert.Equal($"{Fixed(raised)}", maxAfterApproval);
            Assert.Contains(Digits(Money(raised)), Digits(gradePage), StringComparison.Ordinal);
            Assert.Equal(6, names.Length);
            Assert.Equal("true", names[2]);
            Assert.Equal(LegalReference, names[5]);
            Assert.Contains(LegalReference, requestPage, StringComparison.Ordinal);
            Assert.True(names[3].Length > 0 && names[4].Length > 0, "53e: the request's row does not hold the requester's and the approver's names.");
            Assert.True(requestPage.Contains(UserSpan(names[0]), StringComparison.OrdinalIgnoreCase), "53e: the request's page does not name the requester.");
            Assert.True(requestPage.Contains(UserSpan(names[1]), StringComparison.OrdinalIgnoreCase), "53e: the request's page does not name the approver.");
            Assert.Equal(HttpStatusCode.Conflict, deleted);
            Assert.Contains("لا تُحذف الدرجة", deletedBody, StringComparison.Ordinal);
            Assert.Equal(requestsBeforeDelete, requestsAfterDelete);
            Assert.Equal(1, gradeRemains);
        }
        finally
        {
            // The seed's grade goes back to its values, the same way: an open request is cancelled, a changed maximum is requested back
            // and approved by the other person.
            var open = await Db($"SELECT COALESCE((SELECT \"Id\"::text FROM \"PayrollReferenceChangeRequests\" WHERE \"EntityId\"='{g.Id}' AND \"State\"=0 LIMIT 1), '');");
            if (open.Length > 0)
                await processor.PostWithStatusAsync("53e-restore-cancel", $"{ReferenceUi}/Cancel/{open}", $"{ReferenceUi}/Details/{open}", F(("reason", $"{Ac}e: انتهى السيناريو")));
            if (await GradeValuesAsync() != valuesBefore)
            {
                await processor.PostWithStatusAsync("53e-restore-request", edit, edit, g.Form(g.Max, $"{Ac}e: إعادة الحد الأعلى"));
                var restore = await LatestReferenceRequestAsync(g.Id, "53e-restore");
                await approver.PostWithStatusAsync("53e-restore-approve", $"{ReferenceUi}/Approve/{restore}", $"{ReferenceUi}/Details/{restore}", F(("note", "53e: إعادة")));
            }
        }

        var valuesAfter = await GradeValuesAsync();
        s.Recorder.WriteAfter(new { scenario = "53e", valuesBefore, valuesAfter, restored = valuesBefore == valuesAfter });
        Assert.Equal(valuesBefore, valuesAfter);
        s.Recorder.WriteResult("Pass");
    }
}
