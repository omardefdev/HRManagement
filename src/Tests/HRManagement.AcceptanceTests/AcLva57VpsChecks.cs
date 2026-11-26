using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-57 (P9, P-15, P-16) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as three
/// people: the administrator (<c>hradmin</c>, or the user named in <c>LVA057_VPS_ADMIN</c>) fills, closes and asks for corrections;
/// <c>e2e-hr-admin</c>, a second administrator, decides them; <c>e2e-payroll-processor</c> reads payroll's side. The password is
/// read into memory only; evidence goes under <c>artifacts/plan57/vps</c> and is never committed.
/// <para>
/// <b>What these checks write on the demo, and nothing else:</b> the attendance of <b>one small unit</b> (at most 20 employees) for
/// <b>one past month no demo run covers</b> (August 2026, or <c>LVA057_VPS_MONTH</c>; the unit <c>HR-ATT</c>, or
/// <c>LVA057_VPS_UNIT</c>), filled by the bulk entry as present and closed; and pairs of corrections on one of its days whose
/// payroll differences cancel each other. A closed period is not reopened, by design: the month and the unit stay closed on the
/// demo, and a second run of the checks finds them so. No demo run is calculated, cancelled or changed, no other attendance record
/// is written, and nothing is left waiting: a correction that still waits when a check ends is rejected with a note.
/// </para>
/// The demo's attendance records of P0's picture (September and October 2026), its runs and its salary lines are compared before
/// and after by checksums, and with the picture when <c>LVA057_VPS_ATTENDANCE_MD5</c>, <c>LVA057_VPS_RUNS_MD5</c> and
/// <c>LVA057_VPS_LINES_MD5</c> carry its values. No name and no amount is written into evidence.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Lva57Decider = "e2e-hr-admin";
    private const string Lva57Api = "/ui/hr-core/api/attendance";
    private const string Lva57Records = "/ui/hr-core/DailyAttendanceRecords";
    private const string Lva57Periods = "/ui/hr-core/AttendancePeriods";
    private const string Lva57Corrections = "/ui/hr-core/AttendanceCorrections";

    private const string Lva57AbsenceReason = "لم يحضر ولم يقدّم عذراً بحسب سجل دوام المديرية.";
    private const string Lva57ReverseReason = "تبيّن أنه كان في مهمة رسمية بكتاب المديرية؛ يُعاد اليوم حاضراً.";
    private const string Lva57ApprovalNote = "مطابق لسجل دوام المديرية.";
    private const string Lva57RejectionNote = "طلب فحص للشاشات لا يُعتمد؛ يبقى اليوم كما أُغلق.";
    private const string Lva57OwnDecisionRefusal = "لا يبتّ مقدّم الطلب في طلبه";
    private const string Lva57LockedDay = "اليوم ضمن فترة حضور مغلقة";

    private static string Lva57Admin => Environment.GetEnvironmentVariable("LVA057_VPS_ADMIN") is { Length: > 0 } user ? user : "hradmin";

    /// <summary>The checks' month: past, with no demo run and none of the demo's seeded attendance.</summary>
    private static DateOnly Lva57Month =>
        DateOnly.ParseExact((Environment.GetEnvironmentVariable("LVA057_VPS_MONTH") is { Length: > 0 } m ? m : "2026-08") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Lva57UnitCode => Environment.GetEnvironmentVariable("LVA057_VPS_UNIT") is { Length: > 0 } code ? code : "HR-ATT";

    private static string Lva57Ym => Lva57Month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Lva57Start => Lva57Month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Lva57End => Lva57Month.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static EvidenceRecorder StartLva57(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"lva57-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan57", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static string Lva57Core(string sql) => Sql("hr_core_db", sql).SingleOrDefault() ?? "";

    private static string Lva57Pay(string sql) => Sql("payroll_db", sql).SingleOrDefault() ?? "";

    /// <summary>The unit of the checks (id|tenant), by its code.</summary>
    private static string[] Lva57Unit() =>
        Lva57Core($"select \"Id\" || '|' || \"TenantId\" from \"OrgUnits\" where \"Code\" = '{Lva57UnitCode}' order by \"Id\" limit 1").Split('|');

    /// <summary>The service's rule of who is in service in the month, on the Employees row <c>e</c> (ASCII, as it goes over ssh).</summary>
    private static string Lva57InService()
    {
        const string ended = "'Terminated','Retired','Resigned','Deceased','Merged','Anonymized'";
        return $"e.\"CurrentStatus\" <> 10 and (e.\"HireDate\" is null or e.\"HireDate\" < ('{Lva57End}'::date + 1)) " +
            "and (e.\"CurrentStatus\" not in (2, 3, 6, 7, 8, 9) or (select (coalesce(" +
            $"max(h.\"ChangedAt\") filter (where h.\"OldStatus\" is null or h.\"OldStatus\" = '' or h.\"OldStatus\" not in ({ended})), min(h.\"ChangedAt\")) " +
            "at time zone 'Asia/Damascus')::date from \"EmploymentStatusHistories\" h " +
            $"where h.\"EmployeeId\" = e.\"Id\" and h.\"NewStatus\" in ({ended})) >= '{Lva57Start}')";
    }

    /// <summary>The employees a close of the unit's tree covers in the month, in a stable order.</summary>
    private static List<string> Lva57People(string unit) =>
        Sql("hr_core_db",
            "select e.\"Id\" from \"Employees\" e join \"OrgUnits\" o on o.\"Id\" = e.\"PrimaryOrgUnitId\" " +
            $"where o.\"MaterializedPath\" like (select \"MaterializedPath\" from \"OrgUnits\" where \"Id\" = '{unit}') || '%' and {Lva57InService()} order by e.\"Id\"").ToList();

    private static string Lva57In(IEnumerable<string> ids) => string.Join(",", ids.Select(id => $"'{id}'"));

    /// <summary>
    /// What no check may move: the demo's attendance records of P0's picture (count:checksum of id, employee, date, status — the
    /// inventory's expression, over the days outside the checks' month), and payroll's runs and lines (the inventory's expressions).
    /// </summary>
    private static (string Attendance, string Runs, string Lines) Lva57DemoData() =>
        (Lva57Core(
            "select count(*) || ':' || coalesce(md5(string_agg(md5(\"Id\"::text || '|' || \"EmployeeId\"::text || '|' || \"AttendanceDate\"::text || '|' || \"Status\"::text), '' order by \"Id\")), '-') " +
            $"from \"DailyAttendanceRecords\" where \"AttendanceDate\" not between '{Lva57Start}' and '{Lva57End}'"),
         Lva57Pay(
            "select count(*) || ':' || coalesce(md5(string_agg(md5(\"Id\"::text || '|' || \"Status\"::text || '|' || \"TotalNetPay\"::text || '|' || \"PeriodStart\"::text), '' order by \"Id\")), '-') from \"PayrollRuns\""),
         Lva57Pay(
            "select count(*) || ':' || coalesce(md5(string_agg(md5(\"Id\"::text || '|' || \"GrossPay\"::text || '|' || \"TotalDeductions\"::text || '|' || \"NetPay\"::text), '' order by \"Id\")), '-') from \"SalaryLines\""));

    /// <summary>An employee's summary of the checks' month: id|version|working|present|absent|unrecorded, or empty.</summary>
    private static string Lva57Summary(string employee) =>
        Lva57Core(
            "select s.\"Id\" || '|' || s.\"Version\" || '|' || s.\"WorkingDays\" || '|' || s.\"PresentDays\" || '|' || s.\"AbsentDays\" || '|' || s.\"UnrecordedWorkingDays\" " +
            "from \"AttendanceEmployeeSummaries\" s join \"AttendancePeriods\" p on p.\"Id\" = s.\"AttendancePeriodId\" " +
            $"where p.\"PeriodStart\" = '{Lva57Start}' and s.\"EmployeeId\" = '{employee}'");

    /// <summary>The payroll rows of a summary as «version:days:carried:applied», and the sum of the free rows' days.</summary>
    private static (string Rows, string FreeDays) Lva57Rows(string summary) =>
        (Lva57Pay(
            "select coalesce(string_agg(\"SourceVersion\" || ':' || \"AbsentDays\" || ':' || \"CarryForward\"::text || ':' || (\"AppliedInRunId\" is not null)::text, ',' order by \"SourceVersion\"), '') " +
            $"from \"AbsenceDeductions\" where \"SourceReference\" like 'ATT:{summary}:%'"),
         Lva57Pay($"select coalesce(sum(\"AbsentDays\"), 0) from \"AbsenceDeductions\" where \"SourceReference\" like 'ATT:{summary}:%' and \"AppliedInRunId\" is null"));

    /// <summary>A signed-in session that sends the antiforgery token of the attendance API (the cookie stays in the client).</summary>
    private static async Task<HttpClient> Lva57ApiAsync(string user, string password, string tokenOf = "periods")
    {
        var (client, _) = await SignInAsync(user, password, "/ui/hr-core/");
        using var issued = await client.GetAsync($"https://{AppDomain}{Lva57Api}/{tokenOf}/antiforgery");
        var body = await issued.Content.ReadAsStringAsync();
        Assert.True(issued.IsSuccessStatusCode, $"The antiforgery token of the attendance API answered {(int)issued.StatusCode} to {user}.");
        using var token = JsonDocument.Parse(body);
        client.DefaultRequestHeaders.Add(token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static Task<(int Status, string Error, string Body)> Lva57PostAsync(HttpClient client, string path, object? body = null) =>
        Pay55PostAsync(client, $"{Lva57Api}/{path}", body ?? new { });

    private static string Lva57IdOf(string body) => Regex.Match(body, "\"id\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"").Groups[1].Value;

    /// <summary>Leaves nothing waiting: every request of the unit's employees in the checks' month that still waits is rejected with a note.</summary>
    private static async Task Lva57CloseAsync(HttpClient decider, IEnumerable<string> people)
    {
        var ids = people.ToList();
        if (ids.Count == 0)
            return;
        foreach (var request in Sql("hr_core_db",
                     $"select \"Id\" from \"AttendanceCorrections\" where \"Status\" = 0 and \"EmployeeId\" in ({Lva57In(ids)}) and \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}'"))
        {
            await Lva57PostAsync(decider, $"corrections/{request}/reject", new { note = Lva57RejectionNote });
        }
    }

    private static string Lva57Waiting(IEnumerable<string> people) =>
        Lva57Core($"select count(*) from \"AttendanceCorrections\" where \"Status\" = 0 and \"EmployeeId\" in ({Lva57In(people)}) and \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}'");

    private static List<DateOnly> Lva57WorkingDays()
    {
        var days = new List<DateOnly>();
        for (var day = Lva57Month; day < Lva57Month.AddMonths(1); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                days.Add(day);
        }

        return days;
    }

    // ---------------------------------------------------------------- 1. the migrations

    [SkippableFact]
    public void AC_LVA_57_vps_the_migrations_ran_the_new_tables_are_under_forced_row_security_and_no_record_run_or_line_changed()
    {
        var recorder = StartLva57("migration",
            "Demo server: the two LVA057 migrations are recorded; the four attendance tables and payroll's receipts are under forced row security; the leave-made records are as many as P0's inventory counted, every other record is «manual» or the checks' own; the attendance records of P0's picture, the runs and the lines are unchanged (checksums); the old close's queue holds nothing.");
        var core = Lva57Core("select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%LVA057_AttendancePeriods'");
        var payroll = Lva57Pay("select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%LVA057_AttendanceHandoff'");
        var coreSecurity = Lva57Core(
            "select string_agg(relname || ':' || relrowsecurity::text || ':' || relforcerowsecurity::text, ',' order by relname) from pg_class " +
            "where relname in ('AttendancePeriods', 'AttendancePeriodClosures', 'AttendanceEmployeeSummaries', 'AttendanceCorrections', 'DailyAttendanceRecords') and relkind = 'r'");
        var payrollSecurity = Lva57Pay(
            "select string_agg(relname || ':' || relrowsecurity::text || ':' || relforcerowsecurity::text, ',' order by relname) from pg_class " +
            "where relname in ('AttendanceSummaryReceipts', 'AbsenceDeductions') and relkind = 'r'");
        var columns = Lva57Core(
            "select count(*) from information_schema.columns where table_name = 'DailyAttendanceRecords' and column_name in ('Source', 'SourceReference', 'ReplacedStatus')") + "|" +
            Lva57Pay("select count(*) from information_schema.columns where table_name = 'AbsenceDeductions' and column_name in ('OriginalPeriodStart', 'OriginalPeriodEnd', 'CarryForward', 'SourceVersion')");
        var indexes = Lva57Core(
            "select count(*) from pg_indexes where indexdef like 'CREATE UNIQUE INDEX%' and indexname in ('IX_AttendanceCorrections_OnePendingPerDay', 'IX_AttendanceCorrections_OnePerLeaveEventDay')");
        // The fill: leave-made records outside the checks' month (the checks make none), and what each record's source is.
        var leaveMade = Lva57Core($"select count(*) from \"DailyAttendanceRecords\" where \"Source\" = 2 and \"AttendanceDate\" not between '{Lva57Start}' and '{Lva57End}'");
        var sources = Lva57Core("select coalesce(string_agg(s || ':' || n, ',' order by s), '') from (select \"Source\" s, count(*) n from \"DailyAttendanceRecords\" group by 1) t");
        var outside = Lva57Core($"select count(*) filter (where \"Source\" <> 0) from \"DailyAttendanceRecords\" where \"AttendanceDate\" not between '{Lva57Start}' and '{Lva57End}'");
        var (attendance, runs, lines) = Lva57DemoData();
        var rabbit = ContainerOf("rabbitmq");
        var (_, queues) = Sh($"docker exec {rabbit} rabbitmqctl -q list_queues name messages consumers 2>/dev/null | grep -i attendance || true");
        var pictureLeaveMade = Environment.GetEnvironmentVariable("LVA057_VPS_LEAVE_MADE");
        var pictureAttendance = Environment.GetEnvironmentVariable("LVA057_VPS_ATTENDANCE_MD5");
        var pictureRuns = Environment.GetEnvironmentVariable("LVA057_VPS_RUNS_MD5");
        var pictureLines = Environment.GetEnvironmentVariable("LVA057_VPS_LINES_MD5");

        recorder.WriteScenarioDbChecks(new
        {
            migrations = $"{core}|{payroll}",
            coreSecurity,
            payrollSecurity,
            columns,
            columnsFormat = "new columns of the attendance records | new columns of the absence rows",
            indexes,
            leaveMade,
            sources,
            sourcesFormat = "source:records (0 manual, 1 bulk, 2 leave, 3 correction); bulk and correction are the checks' own month",
            recordsWithASourceOutsideTheChecksMonth = outside,
            attendance,
            attendanceFormat = "count:checksum of (id, employee, date, status) outside the checks' month, P0's expression",
            runs,
            lines,
            queues = queues.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            comparedWithThePicture = pictureAttendance is not null
        });

        Assert.Equal("1|1", $"{core}|{payroll}");
        Assert.Equal(
            "AttendanceCorrections:true:true,AttendanceEmployeeSummaries:true:true,AttendancePeriodClosures:true:true,AttendancePeriods:true:true,DailyAttendanceRecords:true:true",
            coreSecurity);
        Assert.Equal("AbsenceDeductions:true:true,AttendanceSummaryReceipts:true:true", payrollSecurity);
        Assert.Equal("3|4", columns);
        Assert.Equal("2", indexes);
        // Outside the checks' month a record is manual or leave-made, as the migration left it.
        Assert.Equal(leaveMade, outside);
        // The old close's queue is gone or empty, and the new one is consumed.
        var queueRows = queues.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(q => Regex.Split(q, @"\s+")).ToList();
        Assert.DoesNotContain(queueRows, q => q[0].Contains("attendance-period-closed", StringComparison.Ordinal) && q.Length > 1 && q[1] != "0");
        Assert.Contains(queueRows, q => q[0] == "payroll-web-attendance-summary-published" && q.Length > 2 && q[1] == "0" && q[2] != "0");
        if (pictureLeaveMade is not null) Assert.Equal(pictureLeaveMade, leaveMade);
        if (pictureAttendance is not null) Assert.EndsWith(":" + pictureAttendance, attendance, StringComparison.Ordinal);
        if (pictureRuns is not null) Assert.EndsWith(":" + pictureRuns, runs, StringComparison.Ordinal);
        if (pictureLines is not null) Assert.EndsWith(":" + pictureLines, lines, StringComparison.Ordinal);
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 2. one close

    [SkippableFact]
    public async Task AC_LVA_57_vps_a_small_unit_closes_a_past_month_and_payroll_receives_each_summary()
    {
        var recorder = StartLva57("close",
            "Demo server: the administrator fills one small unit's past month by the bulk entry as present (working days only) and closes it through the API; each employee the close covers has a summary at version 1 or later with no working day unrecorded, payroll holds a receipt for each, a write by hand on a day of the month is refused and writes nothing, and the same close again changes nothing. No demo run, line or older attendance record changed. A second run of the check finds the month closed and proves the same.");
        var password = DemoPassword();
        var unit = Lva57Unit();
        AcceptanceGate.RequireData(recorder, unit.Length == 2, "وحدة الفحص غير موجودة على خادم العرض", $"The demo has no unit with the code {Lva57UnitCode} (LVA057_VPS_UNIT names another).");
        var people = Lva57People(unit[0]);
        AcceptanceGate.RequireData(recorder, people.Count is >= 2 and <= 20,
            "وحدة الفحص ليست صغيرة", $"The unit {Lva57UnitCode} covers {people.Count} employees in {Lva57Ym}: the check closes one small unit (2 to 20).");
        var runsOfTheMonth = Lva57Pay($"select count(*) from \"PayrollRuns\" where \"PeriodStart\"::date <= '{Lva57End}' and \"PeriodEnd\"::date >= '{Lva57Start}' and \"Status\" not in (5, 8)");
        AcceptanceGate.RequireData(recorder, runsOfTheMonth == "0" && Lva57Month.AddMonths(1) <= DateOnly.FromDateTime(DateTime.UtcNow),
            "شهر الفحص له تشغيل رواتب أو لم ينتهِ", $"{Lva57Ym} has a live payroll run on the demo, or has not ended (LVA057_VPS_MONTH names another).");
        var ids = Lva57In(people);
        var demoBefore = Lva57DemoData();
        var working = Lva57WorkingDays();
        var closedBefore = Lva57Core(
            "select count(*) from \"AttendanceEmployeeSummaries\" s join \"AttendancePeriods\" p on p.\"Id\" = s.\"AttendancePeriodId\" " +
            $"where p.\"PeriodStart\" = '{Lva57Start}' and s.\"EmployeeId\" in ({ids})");
        var othersBefore = Lva57Core(
            $"select count(*) from \"DailyAttendanceRecords\" where \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}' and \"EmployeeId\" not in ({ids})");
        using var admin = await Lva57ApiAsync(Lva57Admin, password);

        // The bulk entry, through its own form: working days only; a day that has a record or is locked is skipped.
        var filled = WebUtility.HtmlDecode(await Wf45PostRenderedAsync(admin, $"{Lva57Records}/BulkEntry", "BulkEntry",
            new Dictionary<string, string> { ["orgUnitId"] = unit[0], ["month"] = Lva57Ym, ["defaultStatus"] = "Present" }));
        var onRestDays = Lva57Core(
            $"select count(*) from \"DailyAttendanceRecords\" where \"EmployeeId\" in ({ids}) and \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}' and extract(dow from \"AttendanceDate\") in (5, 6)");
        var missingDays = Lva57Core(
            $"select count(*) from \"Employees\" e cross join generate_series('{Lva57Start}'::date, '{Lva57End}'::date, interval '1 day') d " +
            $"where e.\"Id\" in ({ids}) and extract(dow from d) not in (5, 6) and (e.\"HireDate\" is null or e.\"HireDate\" <= d) " +
            "and not exists (select 1 from \"DailyAttendanceRecords\" r where r.\"EmployeeId\" = e.\"Id\" and r.\"AttendanceDate\" = d::date)");

        var (closed, closeError, closeBody) = await Lva57PostAsync(admin, $"periods/{Lva57Ym}/close", new { orgUnitId = unit[0] });
        var summaries = Lva57Core(
            "select count(*) || '|' || count(*) filter (where s.\"Version\" >= 1 and s.\"UnrecordedWorkingDays\" = 0) || '|' || coalesce(max(s.\"WorkingDays\"), 0) " +
            "from \"AttendanceEmployeeSummaries\" s join \"AttendancePeriods\" p on p.\"Id\" = s.\"AttendancePeriodId\" " +
            $"where p.\"PeriodStart\" = '{Lva57Start}' and s.\"EmployeeId\" in ({ids})");
        var periodStatus = Lva57Core($"select \"Status\" from \"AttendancePeriods\" where \"PeriodStart\" = '{Lva57Start}'");
        // Payroll receives each summary (the outbox and the consumer take a moment).
        var receipts = "";
        for (var i = 0; i < 45; i++)
        {
            receipts = Lva57Pay($"select count(*) from \"AttendanceSummaryReceipts\" where \"PeriodStart\" = '{Lva57Start}' and \"EmployeeId\" in ({ids})");
            if (receipts == people.Count.ToString(CultureInfo.InvariantCulture)) break;
            await Task.Delay(2000);
        }

        // A write by hand on a locked day (a Friday of the month has no record: a create) is refused, and writes nothing.
        var friday = Lva57Month;
        while (friday.DayOfWeek != DayOfWeek.Friday) friday = friday.AddDays(1);
        var recordsBefore = Lva57Core($"select count(*) from \"DailyAttendanceRecords\" where \"EmployeeId\" = '{people[0]}' and \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}'");
        var refused = WebUtility.HtmlDecode(await Wf45PostRenderedAsync(admin, $"{Lva57Records}/Create", "Create",
            new Dictionary<string, string>
            {
                ["EmployeeId"] = people[0],
                ["AttendanceDate"] = friday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Status"] = "Present",
                ["Notes"] = "حضور استثنائي يوم الجمعة"
            }));
        var recordsAfter = Lva57Core($"select count(*) from \"DailyAttendanceRecords\" where \"EmployeeId\" = '{people[0]}' and \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}'");

        // The same close again changes nothing.
        var closures = Lva57Core($"select count(*) from \"AttendancePeriodClosures\" c join \"AttendancePeriods\" p on p.\"Id\" = c.\"AttendancePeriodId\" where p.\"PeriodStart\" = '{Lva57Start}'");
        var (again, _, againBody) = await Lva57PostAsync(admin, $"periods/{Lva57Ym}/close", new { orgUnitId = unit[0] });
        var closuresAfter = Lva57Core($"select count(*) from \"AttendancePeriodClosures\" c join \"AttendancePeriods\" p on p.\"Id\" = c.\"AttendancePeriodId\" where p.\"PeriodStart\" = '{Lva57Start}'");
        var othersAfter = Lva57Core(
            $"select count(*) from \"DailyAttendanceRecords\" where \"AttendanceDate\" between '{Lva57Start}' and '{Lva57End}' and \"EmployeeId\" not in ({ids})");
        var demoAfter = Lva57DemoData();

        recorder.WriteScenarioDbChecks(new
        {
            month = Lva57Ym,
            unit = Lva57UnitCode,
            employees = people.Count,
            workingDays = working.Count,
            closedBeforeThisRun = closedBefore,
            theBulkEntryAnswered = filled.Contains("الإدخال الجماعي: ", StringComparison.Ordinal),
            onRestDays,
            missingDays,
            close = closed,
            closeError,
            closeAnswer = Regex.Replace(closeBody, "\"periodId\"\\s*:\\s*\"[^\"]+\",?", ""),
            summaries,
            summariesFormat = "summaries | at a version with no working day unrecorded | the most working days",
            periodStatus,
            periodStatusFormat = "1 partially closed (other units of the agency are open), 2 closed",
            receipts,
            lockedCreateRefused = refused.Contains(Lva57LockedDay, StringComparison.Ordinal),
            recordsBefore,
            recordsAfter,
            again,
            againAnswer = Regex.Replace(againBody, "\"periodId\"\\s*:\\s*\"[^\"]+\",?", ""),
            closures = $"{closures}|{closuresAfter}",
            otherUnitsRecordsOfTheMonth = $"{othersBefore}|{othersAfter}",
            demoUnchanged = demoBefore == demoAfter
        });

        Assert.Contains("الإدخال الجماعي: ", filled, StringComparison.Ordinal);
        Assert.Equal("0", onRestDays);
        Assert.Equal("0", missingDays);
        Assert.True(closed == 200, $"The close of {Lva57UnitCode} for {Lva57Ym} answered {closed}: {closeError}");
        Assert.Equal($"{people.Count}|{people.Count}|{working.Count}", summaries);
        Assert.Contains(periodStatus, new[] { "1", "2" });
        Assert.Equal(people.Count.ToString(CultureInfo.InvariantCulture), receipts);
        Assert.Contains(Lva57LockedDay, refused, StringComparison.Ordinal);
        Assert.Equal(recordsBefore, recordsAfter);
        Assert.Equal(200, again);
        Assert.Contains("\"closedEmployees\":0", againBody.Replace(" ", ""), StringComparison.Ordinal);
        Assert.Equal(closures, closuresAfter);
        // Nothing but the unit's own month was written: no other unit's record of the month, no older record, no run, no line.
        Assert.Equal(othersBefore, othersAfter);
        Assert.Equal(demoBefore, demoAfter);
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 3. two corrections that cancel each other

    [SkippableFact]
    public async Task AC_LVA_57_vps_two_corrections_cancel_each_other_and_the_requester_does_not_approve_his_own()
    {
        var recorder = StartLva57("corrections",
            "Demo server: on a day of the closed month the administrator asks for «غائب» with a reason; his own approval is refused (400) and changes nothing; the second administrator approves: the summary's next version with one absence more and a difference of +1 in payroll. Then the reverse correction, asked and approved the same way: the next version, a difference of -1 — the two cancel each other, the day is «حاضر» again, and payroll's free days for the summary are what they were. No request is left waiting, and no demo run, line or older record changed.");
        var password = DemoPassword();
        var unit = Lva57Unit();
        AcceptanceGate.RequireData(recorder, unit.Length == 2, "وحدة الفحص غير موجودة على خادم العرض", $"The demo has no unit with the code {Lva57UnitCode}.");
        var people = Lva57People(unit[0]);
        // The employee: one of the unit whose month is closed, preferably one payroll pays.
        var paid = Sql("payroll_db", $"select \"EmployeeId\" from \"EmployeePayProfiles\" where \"IsActive\" and \"EmployeeId\" in ({Lva57In(people.DefaultIfEmpty(Guid.Empty.ToString()))}) order by \"EmployeeId\"").ToHashSet();
        var employee = people.Where(id => Lva57Summary(id).Length > 0).OrderByDescending(paid.Contains).ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        AcceptanceGate.RequireData(recorder, employee is not null,
            "شهر الفحص غير مغلق لوحدة الفحص", $"{Lva57Ym} is not closed for {Lva57UnitCode}: the close check runs first.");
        var day = Lva57WorkingDays()[3];
        var dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Day() => Lva57Core($"select \"Status\" || '|' || \"Source\" from \"DailyAttendanceRecords\" where \"EmployeeId\" = '{employee}' and \"AttendanceDate\" = '{dayText}'");
        using var admin = await Lva57ApiAsync(Lva57Admin, password, "corrections");
        using var decider = await Lva57ApiAsync(Lva57Decider, password, "corrections");
        await Lva57CloseAsync(decider, people);
        AcceptanceGate.RequireData(recorder, Day().StartsWith("0|", StringComparison.Ordinal),
            "يوم الفحص ليس «حاضر»", $"{dayText} is not «present» for the check's employee (an earlier run stopped half-way: approve the reverse correction by hand).");
        var demoBefore = Lva57DemoData();
        var start = Lva57Summary(employee!).Split('|');
        var (summary, version, absent) = (start[0], int.Parse(start[1], CultureInfo.InvariantCulture), int.Parse(start[4], CultureInfo.InvariantCulture));
        var (rowsBefore, freeBefore) = Lva57Rows(summary);
        try
        {
            // 1. «غائب», asked by the administrator; his own approval is refused; the second administrator approves.
            var (asked, askError, askBody) = await Lva57PostAsync(admin, "corrections", new { employeeId = employee, attendanceDate = dayText, newStatus = "Absent", reason = Lva57AbsenceReason });
            var first = Lva57IdOf(askBody);
            Assert.True(asked == 200 && first.Length == 36, $"The request for «absent» answered {asked}: {askError}");
            var (own, ownError, _) = await Lva57PostAsync(admin, $"corrections/{first}/approve", new { note = Lva57ApprovalNote });
            var afterOwn = Lva57Core($"select \"Status\" from \"AttendanceCorrections\" where \"Id\" = '{first}'") + "|" + Day() + "|" + Lva57Summary(employee!).Split('|')[1];
            var (approved, approveError, _) = await Lva57PostAsync(decider, $"corrections/{first}/approve", new { note = Lva57ApprovalNote });
            var afterFirst = Lva57Summary(employee!).Split('|');
            var dayAbsent = Day();
            var firstRow = "";
            for (var i = 0; i < 30 && !firstRow.Contains($"{version + 1}:1:", StringComparison.Ordinal); i++)
            {
                await Task.Delay(2000);
                firstRow = Lva57Rows(summary).Rows;
            }

            // 2. The reverse, the same way.
            var (askedBack, askBackError, askBackBody) = await Lva57PostAsync(admin, "corrections", new { employeeId = employee, attendanceDate = dayText, newStatus = "Present", reason = Lva57ReverseReason });
            var second = Lva57IdOf(askBackBody);
            Assert.True(askedBack == 200 && second.Length == 36, $"The reverse request answered {askedBack}: {askBackError}");
            var (approvedBack, approveBackError, _) = await Lva57PostAsync(decider, $"corrections/{second}/approve", new { note = Lva57ApprovalNote });
            var afterSecond = Lva57Summary(employee!).Split('|');
            var dayBack = Day();
            var rows = "";
            var free = "";
            for (var i = 0; i < 30 && !rows.EndsWith($"{version + 2}:-1:false:false", StringComparison.Ordinal); i++)
            {
                await Task.Delay(2000);
                (rows, free) = Lva57Rows(summary);
            }

            var decisions = Lva57Core(
                "select string_agg(\"Status\" || ':' || coalesce(\"OldStatus\"::text, '-') || '>' || coalesce(\"NewStatus\"::text, '-') || ':v' || coalesce(\"ResultingVersion\"::text, '-') || ':' || (\"RequestedBy\" <> \"DecidedBy\")::text, ',' order by \"ResultingVersion\") " +
                $"from \"AttendanceCorrections\" where \"Id\" in ('{first}', '{second}')");
            var receipt = Lva57Pay($"select \"LatestVersion\" || '|' || \"LatestAbsentDays\" || '|' || \"RecordedAbsentDays\" from \"AttendanceSummaryReceipts\" where \"SummaryId\" = '{summary}'");
            var demoAfter = Lva57DemoData();

            recorder.WriteScenarioDbChecks(new
            {
                month = Lva57Ym,
                unit = Lva57UnitCode,
                day = dayText,
                theEmployeeIsPaid = paid.Contains(employee!),
                versionAtTheStart = version,
                absentAtTheStart = absent,
                ownApproval = own,
                ownApprovalError = ownError,
                afterOwn,
                afterOwnFormat = "request status (0 waits) | day status|source | summary version",
                approved,
                approveError,
                afterFirst = $"{afterFirst[1]}|{afterFirst[4]}",
                dayAbsent,
                firstRow,
                approvedBack,
                approveBackError,
                afterSecond = $"{afterSecond[1]}|{afterSecond[4]}",
                dayBack,
                rowsBefore,
                rows,
                rowsFormat = "version:days:carried:applied in a run",
                freeDays = $"{freeBefore}|{free}",
                decisions,
                decisionsFormat = "status (1 approved):old>new:version:requester is not the decider",
                receipt,
                demoUnchanged = demoBefore == demoAfter
            });

            Assert.Equal(400, own);
            Assert.Contains(Lva57OwnDecisionRefusal, ownError, StringComparison.Ordinal);
            // The request still waits, the day is «حاضر» and the version is the same.
            var unchanged = afterOwn.Split('|');
            Assert.Equal(("0", "0", version.ToString(CultureInfo.InvariantCulture)), (unchanged[0], unchanged[1], unchanged[3]));
            Assert.True(approved == 200, $"The approval by {Lva57Decider} answered {approved}: {approveError}");
            Assert.Equal($"{version + 1}|{absent + 1}", $"{afterFirst[1]}|{afterFirst[4]}");
            Assert.Equal("1|3", dayAbsent);                                   // absent, written by a correction
            Assert.Contains($"{version + 1}:1:false:false", firstRow, StringComparison.Ordinal);
            Assert.True(approvedBack == 200, $"The approval of the reverse answered {approvedBack}: {approveBackError}");
            Assert.Equal($"{version + 2}|{absent}", $"{afterSecond[1]}|{afterSecond[4]}");
            Assert.Equal("0|3", dayBack);                                     // present again
            // The two differences cancel each other: payroll's free days for the summary are what they were.
            Assert.EndsWith($"{version + 1}:1:false:false,{version + 2}:-1:false:false", rows, StringComparison.Ordinal);
            Assert.Equal(freeBefore, free);
            Assert.Equal($"1:0>1:v{version + 1}:true,1:1>0:v{version + 2}:true", decisions);
            Assert.Equal($"{version + 2}|{absent}|{absent}", receipt);
            Assert.Equal(demoBefore, demoAfter);
        }
        finally
        {
            await Lva57CloseAsync(decider, people);
        }

        Assert.Equal("0", Lva57Waiting(people));
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 4. the gate, read only

    [SkippableFact]
    public async Task AC_LVA_57_vps_the_gate_reads_a_demo_run_and_the_receipts_without_changing_anything()
    {
        var recorder = StartLva57("gate",
            "Demo server: attendance-status of an existing demo run answers the gate's mode (Warn), who is in the run's scope and who has no closed attendance for its period — everyone, since the demo closed none of its months; the receipts of the checks' month are as many as its summaries. Reads only: no run, line or audit record changed.");
        var password = DemoPassword();
        var run = Lva57Pay("select \"Id\" from \"PayrollRuns\" where \"RunType\" = 'Regular' and \"Status\" not in (5, 8) and \"PeriodStart\" < '2028-01-01' order by \"PeriodStart\" desc limit 1");
        AcceptanceGate.RequireData(recorder, run.Length == 36, "لا يوجد تشغيل رواتب على خادم العرض", "The demo has no live regular run.");
        var demoBefore = Lva57DemoData();
        var auditBefore = Lva57Pay("select count(*) || ':' || coalesce(md5(string_agg(\"Id\"::text || \"Status\"::text, ',' order by \"Id\")), '-') from \"PayrollAuditRecords\"");
        var (processor, _) = await SignInAsync(Pay56Processor, password, "/ui/payroll/");
        using var _client = processor;

        using var status = await processor.GetAsync($"https://{AppDomain}{Pay56Api}/runs/{run}/attendance-status");
        var statusBody = await status.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(status.IsSuccessStatusCode ? statusBody : "{}");
        var mode = json.RootElement.TryGetProperty("gateMode", out var m) ? m.GetString() : null;
        var inScope = json.RootElement.TryGetProperty("employeesInScope", out var s) ? s.GetInt32() : -1;
        var missing = json.RootElement.TryGetProperty("missingAttendance", out var list) ? list.GetArrayLength() : -1;
        var profiles = Lva57Pay("select count(distinct \"EmployeeId\") from \"EmployeePayProfiles\" where \"IsActive\"");
        var period = Lva57Pay($"select to_char(\"PeriodStart\", 'YYYY-MM') || '|' || \"Status\" from \"PayrollRuns\" where \"Id\" = '{run}'").Split('|');
        var receiptsOfItsPeriod = Lva57Pay(
            $"select count(distinct a.\"EmployeeId\") from \"AttendanceSummaryReceipts\" a join \"PayrollRuns\" r on r.\"Id\" = '{run}' " +
            "where a.\"PeriodStart\" <= r.\"PeriodEnd\"::date and a.\"PeriodEnd\" >= r.\"PeriodStart\"::date");

        using var receipts = await processor.GetAsync($"https://{AppDomain}{Pay56Api}/attendance/receipts?periodStart={Lva57Start}");
        var receiptsBody = await receipts.Content.ReadAsStringAsync();
        using var receiptsJson = JsonDocument.Parse(receipts.IsSuccessStatusCode ? receiptsBody : "{}");
        var listed = receiptsJson.RootElement.TryGetProperty("count", out var c) ? c.GetInt32() : -1;
        var summaries = Lva57Core(
            $"select count(*) from \"AttendanceEmployeeSummaries\" s join \"AttendancePeriods\" p on p.\"Id\" = s.\"AttendancePeriodId\" where p.\"PeriodStart\" = '{Lva57Start}'");
        var demoAfter = Lva57DemoData();
        var auditAfter = Lva57Pay("select count(*) || ':' || coalesce(md5(string_agg(\"Id\"::text || \"Status\"::text, ',' order by \"Id\")), '-') from \"PayrollAuditRecords\"");

        recorder.WriteScenarioDbChecks(new
        {
            runPeriod = period[0],
            runStatus = period.ElementAtOrDefault(1),
            attendanceStatus = (int)status.StatusCode,
            gateMode = mode,
            employeesInScope = inScope,
            activePayProfiles = profiles,
            missingAttendance = missing,
            receiptsOfTheRunsPeriod = receiptsOfItsPeriod,
            receiptsRead = (int)receipts.StatusCode,
            receiptsOfTheChecksMonth = listed,
            summariesOfTheChecksMonth = summaries,
            demoUnchanged = demoBefore == demoAfter,
            auditRecordsUnchanged = auditBefore == auditAfter
        });

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("Warn", mode);
        Assert.Equal(profiles, inScope.ToString(CultureInfo.InvariantCulture));
        // Missing = in scope less who has a receipt of the run's period (nobody: the demo closed none of its run months).
        Assert.Equal(inScope - int.Parse(receiptsOfItsPeriod, CultureInfo.InvariantCulture), missing);
        Assert.True(missing > 0);
        Assert.Equal(HttpStatusCode.OK, receipts.StatusCode);
        Assert.Equal(summaries, listed.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(demoBefore, demoAfter);
        Assert.Equal(auditBefore, auditAfter);
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 5. the screens

    [SkippableFact]
    public async Task AC_LVA_57_vps_the_attendance_screens_open_in_arabic_under_the_enforced_policy_with_names()
    {
        var recorder = StartLva57("screens",
            "Demo server: the periods' list and close form, the corrections' list, a request's form from a locked day and its decision page, an attendance record's page and edit form, the bulk entry, and a run's page with its attendance panel open in Chromium at two sizes with one enforced CSP policy each and no violation; the pages are Arabic and right-to-left, show people by name and never an identifier, the edit form posts neither the employee nor the date and offers no «في إجازة». The request the decision page shows is the check's own and is rejected with a note at the end.");
        var password = DemoPassword();
        var unit = Lva57Unit();
        AcceptanceGate.RequireData(recorder, unit.Length == 2, "وحدة الفحص غير موجودة على خادم العرض", $"The demo has no unit with the code {Lva57UnitCode}.");
        var people = Lva57People(unit[0]);
        var employee = people.FirstOrDefault(id => Lva57Summary(id).Length > 0);
        AcceptanceGate.RequireData(recorder, employee is not null, "شهر الفحص غير مغلق لوحدة الفحص", $"{Lva57Ym} is not closed for {Lva57UnitCode}: the close check runs first.");
        var day = Lva57WorkingDays()[5].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var locked = Lva57Core($"select \"Id\" from \"DailyAttendanceRecords\" where \"EmployeeId\" = '{employee}' and \"AttendanceDate\" = '{day}'");
        // A record that is not locked and not leave-made: one of the demo's own, outside the checks' month.
        var open = Lva57Core(
            $"select r.\"Id\" from \"DailyAttendanceRecords\" r where r.\"AttendanceDate\" not between '{Lva57Start}' and '{Lva57End}' and r.\"Source\" <> 2 " +
            "and not exists (select 1 from \"AttendanceEmployeeSummaries\" s join \"AttendancePeriods\" p on p.\"Id\" = s.\"AttendancePeriodId\" " +
            "where s.\"EmployeeId\" = r.\"EmployeeId\" and r.\"AttendanceDate\" between p.\"PeriodStart\" and p.\"PeriodEnd\") order by r.\"AttendanceDate\" desc, r.\"Id\" limit 1");
        var run = Lva57Pay("select \"Id\" from \"PayrollRuns\" where \"RunType\" = 'Regular' and \"Status\" not in (5, 8) and \"PeriodStart\" < '2028-01-01' order by \"PeriodStart\" desc limit 1");
        AcceptanceGate.RequireData(recorder, locked.Length == 36 && open.Length == 36 && run.Length == 36,
            "لا توجد سجلات للصفحات على خادم العرض", "The demo has no locked record, no open record or no run for the pages.");
        var demoBefore = Lva57DemoData();
        var output = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan57", "vps", "screens");
        var results = new List<Ux067PageResult>();
        var repeats = new List<string>();
        using var admin = await Lva57ApiAsync(Lva57Admin, password, "corrections");
        using var decider = await Lva57ApiAsync(Lva57Decider, password, "corrections");
        var (processor, _) = await SignInAsync(Pay56Processor, password, "/ui/payroll/");
        using var _processor = processor;
        await Lva57CloseAsync(decider, people);
        try
        {
            // What the decision page shows: a request of the check's own, which waits while the pages are read.
            var (asked, askError, askBody) = await Lva57PostAsync(admin, "corrections", new { employeeId = employee, attendanceDate = day, newStatus = "Remote", reason = Lva57ReverseReason });
            var request = Lva57IdOf(askBody);
            Assert.True(asked == 200 && request.Length == 36, $"The request for the decision page answered {asked}: {askError}");
            var requester = Lva57Core($"select \"RequestedBy\" from \"AttendanceCorrections\" where \"Id\" = '{request}'");

            Ux067KeyPage[] adminPages =
            [
                new("attendance-periods", Lva57Periods),
                new("attendance-period-close", $"{Lva57Periods}/Close"),
                new("attendance-corrections", Lva57Corrections),
                new("attendance-correction-form", $"{Lva57Corrections}/Create?employeeId={employee}&date={Lva57WorkingDays()[6]:yyyy-MM-dd}"),
                new("attendance-records", Lva57Records),
                new("attendance-record-details", $"{Lva57Records}/Details/{locked}"),
                new("attendance-record-edit", $"{Lva57Records}/Edit/{open}"),
                new("attendance-bulk-entry", $"{Lva57Records}/BulkEntry"),
            ];
            Ux067KeyPage[] deciderPages = [new("attendance-correction-decision", $"{Lva57Corrections}/Details/{request}")];
            Ux067KeyPage[] processorPages = [new("run-with-its-attendance-panel", $"{Pay56Runs}/Details/{run}")];
            (int Width, int Height)[] sizes = [(1366, 768), (390, 844)];

            // A page load, or the sign-in, fails when a network interface of the machine that runs the check changes under Chromium
            // (PLAN-54's note). Each session is opened once, each page is probed on its own, and a step the network cut is done again.
            async Task<T> OnThisMachinesNetworkAsync<T>(string step, Func<Task<T>> work)
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await work();
                    }
                    catch (Exception ex) when (attempt < 12 && Ux067PageProbe.IsClientNetworkCut(ex))
                    {
                        repeats.Add($"{step}: {ex.Message.Split('\n')[0]}");
                        await Task.Delay(TimeSpan.FromSeconds(3 + ((attempt * 7) % 23)));
                    }
                }
            }

            var playwright = await Playwright.CreateAsync();
            // Over HTTP/3 a connection whose local path changed stalls to the navigation timeout; over TCP it fails fast and is done again.
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--disable-quic"] });
            var states = new List<string>();
            try
            {
                foreach (var (user, pages) in new[] { (Lva57Admin, adminPages), (Lva57Decider, deciderPages), (Pay56Processor, processorPages) })
                {
                    var state = Path.Combine(Path.GetTempPath(), $"lva57-state-{Guid.NewGuid():N}.json");
                    states.Add(state);
                    await OnThisMachinesNetworkAsync($"sign-in {user}", async () =>
                    {
                        await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", user, password, state);
                        return true;
                    });
                    var (probed, again) = await Ux067PageProbe.ProbeEachAsync(
                        browser, $"https://{AppDomain}", state, pages, sizes, Path.Combine(output, "screenshots"), attemptsPerPage: 12);
                    results.AddRange(probed);
                    repeats.AddRange(again);
                }
            }
            finally
            {
                await browser.DisposeAsync();
                playwright.Dispose();
                foreach (var state in states.Where(File.Exists)) File.Delete(state);
            }

            // What the server sends, read as text: Arabic, right-to-left, people by the registry's spans or by name, never an id.
            async Task<string> PageAsync(HttpClient client, string path)
            {
                using var page = await client.GetAsync($"https://{AppDomain}{path}");
                Assert.True(page.IsSuccessStatusCode, $"GET {path} answered {(int)page.StatusCode}");
                return WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            }

            static string Visible(string html) =>
                Regex.Replace(Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase), "<[^>]+>", " ");

            var periodsPage = await PageAsync(admin, Lva57Periods);
            var correctionsPage = await PageAsync(admin, Lva57Corrections);
            var decisionPage = await PageAsync(decider, $"{Lva57Corrections}/Details/{request}");
            var detailsPage = await PageAsync(admin, $"{Lva57Records}/Details/{locked}");
            var editPage = await PageAsync(admin, $"{Lva57Records}/Edit/{open}");
            var runPage = await PageAsync(processor, $"{Pay56Runs}/Details/{run}");
            var editForm = Regex.Matches(editPage, @"<form\b[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(f => f.Value).FirstOrDefault(f => Regex.IsMatch(f, @"action=""[^""]*/Edit/")) ?? "";
            var panel = Regex.Match(runPage, "data-testid=\"run-attendance\".*", RegexOptions.Singleline).Value;

            var failures = results.Where(r => !r.Passes || r.Status != 200)
                .Select(r => $"{r.Name}: HTTP {r.Status}, {r.CspPolicies} policies, {r.Violations.Count} violations {string.Join("; ", r.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))} {r.Note}")
                .ToList();
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "csp-pages.json"), Ux067PageProbe.ToJson(results));
            recorder.WriteScenarioSteps(string.Join("\n", results.Select(r =>
                $"- {r.Name}: HTTP {r.Status}, {r.CspHeaders} CSP header(s), {r.CspPolicies} policies, {r.Violations.Count} violation(s)" + (r.Note is null ? "" : $" ({r.Note})"))));
            recorder.WriteScenarioDbChecks(new
            {
                repeats,
                pages = results.Select(r => new { r.Name, r.Status, r.CspHeaders, r.CspPolicies, violations = r.Violations.Count, r.Passes }),
                rightToLeft = periodsPage.Contains("dir=\"rtl\"", StringComparison.Ordinal),
                arabic = periodsPage.Contains("lang=\"ar\"", StringComparison.Ordinal),
                thePeriodsListShowsTheMonth = Visible(periodsPage).Contains(Lva57Ym, StringComparison.Ordinal),
                theCorrectionsListShowsTheRequest = correctionsPage.Contains($"/Details/{request}", StringComparison.OrdinalIgnoreCase),
                theDecisionPageShowsTheReason = decisionPage.Contains(Lva57ReverseReason, StringComparison.Ordinal),
                theDecisionPageOffersBothDecisions = decisionPage.Contains($"/Approve/{request}", StringComparison.OrdinalIgnoreCase) && decisionPage.Contains($"/Reject/{request}", StringComparison.OrdinalIgnoreCase),
                noIdentifierOnTheDecisionPage = !Visible(decisionPage).Contains(employee!, StringComparison.OrdinalIgnoreCase) && !Visible(decisionPage).Contains(requester, StringComparison.OrdinalIgnoreCase),
                noIdentifierOnTheRecordsPage = !Visible(detailsPage).Contains(employee!, StringComparison.OrdinalIgnoreCase),
                theEditFormPostsNoEmployeeAndNoDate = editForm.Length > 0 && !editForm.Contains("name=\"EmployeeId\"", StringComparison.Ordinal) && !editForm.Contains("name=\"AttendanceDate\"", StringComparison.Ordinal),
                theEditFormOffersNoOnLeave = editForm.Length > 0 && !Regex.IsMatch(editForm, "<option[^>]*value=\"(2|OnLeave)\""),
                theRunsPageHasItsAttendancePanel = panel.Length > 0,
                thePanelShowsNoIdentifier = panel.Length > 0 && !Regex.IsMatch(Visible(panel), "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
            });

            Assert.Equal(adminPages.Length + deciderPages.Length + processorPages.Length, results.Count);   // every page,
            Assert.All(results, r => Assert.Equal(sizes.Length, r.Screenshots.Count));                      // opened at both sizes
            Assert.True(failures.Count == 0, "Screens failing the CSP check:\n" + string.Join("\n", failures));
            Assert.Contains("dir=\"rtl\"", periodsPage, StringComparison.Ordinal);
            Assert.Contains("lang=\"ar\"", periodsPage, StringComparison.Ordinal);
            Assert.Contains("فترات الحضور", Visible(periodsPage), StringComparison.Ordinal);
            Assert.Contains(Lva57Ym, Visible(periodsPage), StringComparison.Ordinal);
            Assert.Contains("تصحيحات الحضور", Visible(correctionsPage), StringComparison.Ordinal);
            Assert.Contains($"/Details/{request}", correctionsPage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Lva57ReverseReason, decisionPage, StringComparison.Ordinal);
            Assert.Contains($"/Approve/{request}", decisionPage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"/Reject/{request}", decisionPage, StringComparison.OrdinalIgnoreCase);
            // Names, not ids: neither the employee's nor the requester's identifier is printed.
            Assert.DoesNotContain(employee!, Visible(decisionPage), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(requester, Visible(decisionPage), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(employee!, Visible(detailsPage), StringComparison.OrdinalIgnoreCase);
            // The edit form: the status and the note only, and never «في إجازة».
            Assert.True(editForm.Length > 0, "The edit page has no edit form.");
            Assert.DoesNotContain("name=\"EmployeeId\"", editForm, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"AttendanceDate\"", editForm, StringComparison.Ordinal);
            Assert.DoesNotMatch("<option[^>]*value=\"(2|OnLeave)\"", editForm);
            // The run's page carries the attendance panel, with names.
            Assert.True(panel.Length > 0, "The run's page has no attendance panel.");
            Assert.DoesNotMatch("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", Visible(panel));
            Assert.Equal(demoBefore, Lva57DemoData());
        }
        finally
        {
            await Lva57CloseAsync(decider, people);
        }

        Assert.Equal("0", Lva57Waiting(people));
        recorder.WriteResult("Pass");
    }
}
