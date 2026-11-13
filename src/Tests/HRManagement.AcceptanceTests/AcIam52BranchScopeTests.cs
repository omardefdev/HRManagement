using System.Globalization;
using System.Net;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-IAM-52a–e (PLAN-52) on the prod-like stack, through the gateway. A branch manager (<c>br1-mgr</c>, RIF) keeps to his branch on every
/// screen he reaches: the foreign records are made inside the test by <c>br2-mgr</c> (ALEP) or <c>central-a</c>, and each one answers 404 to
/// him on its details and on every POST by its id, is absent from his lists and his export, and stays as it was (its row, read before and
/// after). The central reader sees them, so their absence is the scope's answer. A scope that names no unit is refused (403).
/// Two kinds of record have no short path in the UI, so the test writes them as a stated precondition: an appointment transaction (only a
/// recruitment hire makes one, AC-HCM-29 proves that path) and an internal vacancy with its application (a requisition published internally,
/// AC-HCM-26). Nothing stops or recreates a container.
/// </summary>
[RequiredAcceptance]
public class AcIam52BranchScopeTests
{
    private const string Ac = "AC-IAM-52";
    private const string Br1 = "br1-mgr";
    private const string Br2 = "br2-mgr";
    private const string Central = "central-a";
    private const string LogisticsChecker = "a-logistics-checker";
    private const string InvalidScope = "negative-invalid-scope";
    private const string Core = "hr_core_db";
    private const string ContractsDb = "hr_contracts_db";
    private const string LogisticsDb = "admin_db";

    private static readonly string Rif = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string Alep = AcceptanceSeedIds.Br2OrgUnitId.ToString("D");
    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string RifEmployee = AcceptanceSeedIds.EmpBr1Id.ToString("D");
    private static readonly string AlepEmployee = AcceptanceSeedIds.EmpBr2Id.ToString("D");

    private static Task<string> Db(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Minute(DateTime time) => time.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    /// <summary>The rows a negative must leave alone: their count and an md5 of their whole text (every column, UpdatedAt included).</summary>
    private static Task<string> RowsAsync(string database, string table, string where) =>
        Db(database, $"SELECT count(*)::text || ':' || md5(COALESCE(string_agg(t::text, '|' ORDER BY t.\"Id\"), '')) FROM \"{table}\" t WHERE {where};");

    private static async Task<string> IdAsync(string database, string table, string where, string step)
    {
        var id = await Db(database, $"SELECT \"Id\"::text FROM \"{table}\" WHERE {where} ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: no {table} row where {where}.");
        return id;
    }

    private static bool Shows(string body, params string[] tokens) =>
        tokens.Any(t => body.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>The statuses a scenario collects; asserted together at the end, so the evidence holds every answer.</summary>
    private sealed class Answers
    {
        private readonly List<(string Step, int Expected, int Actual)> _rows = [];

        public void Add(string step, int expected, HttpStatusCode actual) => _rows.Add((step, expected, (int)actual));

        public object Evidence => _rows.Select(r => new { r.Step, r.Expected, r.Actual }).ToList();

        public void AssertAll()
        {
            var wrong = _rows.Where(r => r.Expected != r.Actual).Select(r => $"{r.Step}: expected {r.Expected}, got {r.Actual}").ToList();
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }
    }

    private static async Task GetAsync(Answers answers, AcceptanceUi ui, string step, string path, int expected) =>
        answers.Add(step, expected, (await ui.GetWithStatusAsync(step, path)).Status);

    private static async Task PostAsync(Answers answers, AcceptanceUi ui, string step, string path, string tokenPage, int expected,
        List<KeyValuePair<string, string>> fields) =>
        answers.Add(step, expected, (await ui.PostWithStatusAsync(step, path, tokenPage, fields)).Status);

    // ---------------------------------------------------------------- 52a

    private sealed record BranchRecords(
        string Marker, string Diwan, string Statistic, string Inspection, string Attendance, string AttendanceDate,
        string Roster, string Assignment, string KeyPosition, string Plan, string Candidate);

    /// <summary>An attendance day with no record of the employee yet (the table has years of seeded days).</summary>
    private static async Task<string> FreeAttendanceDayAsync(string employee)
    {
        for (var i = 0; i < 60; i++)
        {
            var day = Day(Today.AddDays(-Random.Shared.Next(30, 700)));
            if (await Db(Core, $"SELECT count(*)::text FROM \"DailyAttendanceRecords\" WHERE \"EmployeeId\"='{employee}' AND \"AttendanceDate\"='{day}';") == "0")
                return day;
        }

        throw new InvalidOperationException($"No free attendance day for {employee}.");
    }

    /// <summary>The records of one branch, made through its manager's own screens.</summary>
    private static async Task<BranchRecords> CreateBranchRecordsAsync(AcceptanceUi ui, string side, string unit, string employee, string shift, string tag)
    {
        var marker = $"AC52-{side}-{tag}";
        var step = $"52a-{side.ToLowerInvariant()}";

        await ui.PostAsync($"{step}-diwan", "ui/hr-core/DiwanCorrespondences/Create", "ui/hr-core/DiwanCorrespondences/Create",
            F(("ReferenceNumber", marker), ("Subject", $"مكاتبة {marker}"), ("Direction", "Inbound"), ("ExternalParty", Ac), ("OrgUnitId", unit)));
        var diwan = await IdAsync(Core, "DiwanCorrespondences", $"\"ReferenceNumber\"='{marker}'", step);

        await ui.PostAsync($"{step}-statistic", "ui/hr-core/HrStatisticReports/Create", "ui/hr-core/HrStatisticReports/Create",
            F(("Title", marker), ("PeriodStart", Day(Today.AddDays(-30))), ("PeriodEnd", Day(Today)), ("OrgUnitId", unit), ("MetricsSummary", Ac)));
        var statistic = await IdAsync(Core, "HrStatisticReports", $"\"Title\"='{marker}'", step);

        await ui.PostAsync($"{step}-inspection", "ui/hr-core/FieldInspectionVisits/Create", "ui/hr-core/FieldInspectionVisits/Create",
            F(("Title", marker), ("Location", Ac), ("VisitDate", Day(Today)), ("OrgUnitId", unit), ("Summary", Ac)));
        var inspection = await IdAsync(Core, "FieldInspectionVisits", $"\"Title\"='{marker}'", step);

        var day = await FreeAttendanceDayAsync(employee);
        await ui.PostAsync($"{step}-attendance", "ui/hr-core/DailyAttendanceRecords/Create", "ui/hr-core/DailyAttendanceRecords/Create",
            F(("EmployeeId", employee), ("AttendanceDate", day), ("Status", "Present"), ("Notes", marker)));
        var attendance = await IdAsync(Core, "DailyAttendanceRecords", $"\"Notes\"='{marker}'", step);

        await ui.PostAsync($"{step}-roster", "ui/hr-core/Rosters/Create", "ui/hr-core/Rosters/Create",
            F(("Title", marker), ("OrgUnitId", unit), ("PeriodStart", Day(Today)), ("PeriodEnd", Day(Today.AddDays(30)))));
        var roster = await IdAsync(Core, "ShiftRosters", $"\"Title\"='{marker}'", step);
        await ui.PostAsync($"{step}-assignment", "ui/hr-core/Rosters/AddAssignment", $"ui/hr-core/Rosters/Details/{roster}",
            F(("rosterId", roster), ("shiftId", shift), ("employeeId", employee), ("workDate", Day(Today.AddDays(1))), ("overtimeHours", "0")));
        var assignment = await IdAsync(Core, "ShiftAssignments", $"\"ShiftRosterId\"='{roster}'", step);

        await ui.PostAsync($"{step}-key-position", "ui/hr-core/KeyPositions/Create", "ui/hr-core/KeyPositions/Create",
            F(("Title", marker), ("OrgUnitId", unit), ("Criticality", "3")));
        var keyPosition = await IdAsync(Core, "KeyPositions", $"\"Title\"='{marker}'", step);
        await ui.PostAsync($"{step}-plan", "ui/hr-core/SuccessionPlans/Create", "ui/hr-core/SuccessionPlans/Create",
            F(("KeyPositionId", keyPosition), ("Year", Today.Year.ToString(CultureInfo.InvariantCulture)), ("Notes", marker)));
        var plan = await IdAsync(Core, "SuccessionPlans", $"\"KeyPositionId\"='{keyPosition}'", step);
        await ui.PostAsync($"{step}-candidate", "ui/hr-core/SuccessionPlans/AddCandidate", $"ui/hr-core/SuccessionPlans/Details/{plan}",
            F(("successionPlanId", plan), ("employeeId", employee), ("latestEvaluationScore", "80"), ("yearsInService", "5"), ("meetsQualification", "true")));
        var candidate = await IdAsync(Core, "SuccessorCandidates", $"\"SuccessionPlanId\"='{plan}'", step);

        return new BranchRecords(marker, diwan, statistic, inspection, attendance, day, roster, assignment, keyPosition, plan, candidate);
    }

    /// <summary>The foreign rows and the rows a refused write would have added, read before and after the negatives.</summary>
    private static async Task<Dictionary<string, string>> ForeignRowsAsync(BranchRecords alep, BranchRecords rif, string refusedMarker)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["diwan"] = await RowsAsync(Core, "DiwanCorrespondences", $"t.\"Id\"='{alep.Diwan}'"),
            ["statistic"] = await RowsAsync(Core, "HrStatisticReports", $"t.\"Id\"='{alep.Statistic}'"),
            ["inspection"] = await RowsAsync(Core, "FieldInspectionVisits", $"t.\"Id\"='{alep.Inspection}'"),
            ["attendance"] = await RowsAsync(Core, "DailyAttendanceRecords", $"t.\"EmployeeId\"='{AlepEmployee}'"),
            ["ownAttendance"] = await RowsAsync(Core, "DailyAttendanceRecords", $"t.\"Id\"='{rif.Attendance}'"),
            ["roster"] = await RowsAsync(Core, "ShiftRosters", $"t.\"Id\"='{alep.Roster}'"),
            ["assignments"] = await RowsAsync(Core, "ShiftAssignments", $"t.\"ShiftRosterId\" IN ('{alep.Roster}','{rif.Roster}')"),
            ["keyPosition"] = await RowsAsync(Core, "KeyPositions", $"t.\"Id\"='{alep.KeyPosition}'"),
            ["plans"] = await RowsAsync(Core, "SuccessionPlans", $"t.\"KeyPositionId\"='{alep.KeyPosition}'"),
            ["candidates"] = await RowsAsync(Core, "SuccessorCandidates", $"t.\"SuccessionPlanId\"='{alep.Plan}'"),
        };
        foreach (var (name, table, column) in new[]
                 {
                     ("refusedDiwan", "DiwanCorrespondences", "ReferenceNumber"), ("refusedStatistic", "HrStatisticReports", "Title"),
                     ("refusedInspection", "FieldInspectionVisits", "Title"), ("refusedRoster", "ShiftRosters", "Title"),
                     ("refusedKeyPosition", "KeyPositions", "Title"), ("refusedAttendance", "DailyAttendanceRecords", "Notes"),
                     ("refusedShift", "Shifts", "Code")
                 })
            rows[name] = await Db(Core, $"SELECT count(*)::text FROM \"{table}\" WHERE \"{column}\" LIKE '{refusedMarker}%';");
        return rows;
    }

    [SkippableFact]
    public async Task Ac52a_a_branch_manager_keeps_to_his_branch_on_diwan_statistics_inspections_attendance_rosters_and_succession()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "52a", "AC-IAM-52a: Diwan, statistics, inspections, attendance, rosters and succession — br1-mgr lists and makes RIF's; ALEP's answer 404, are absent from his lists and his export, and stay as they were",
            Br1, Br2, Central, InvalidScope);
        s.Steps("""
            1. central-a adds a shift to the catalogue (central, D-52-3) for the roster assignments.
            2. br2-mgr (ALEP) and br1-mgr (RIF) each make, through their own screens, a Diwan correspondence, a statistics report and an
               inspection visit of their branch, an attendance record of their branch's employee, a roster with an assignment of that
               employee, and a key position with a succession plan and a candidate.
            3. Positive: br1-mgr's lists of the six screens show RIF's records; his employee export (Reports/ExportEmployeesCsv, the export
               a branch manager reaches here: the six screens have none) holds the RIF employee.
            4. Negative: the same lists show ALEP's records to central-a and not to br1-mgr; his export has no ALEP employee.
            5. Negative: br1-mgr, on ALEP's records — attendance details, edit (GET and POST), delete, create for the ALEP employee, and his
               own record moved to the ALEP employee; roster details, add and remove an assignment, delete; key position edit (GET and POST)
               and delete; succession plan details, add and remove a candidate, delete, a plan on ALEP's key position; Diwan,
               statistics, inspection, roster and key position created in ALEP's unit, Diwan for the ALEP employee; and an ALEP employee
               assigned on his own RIF roster → 404 each. The shift catalogue refuses his write (403).
            6. The rows of ALEP's records (count and md5 of every column), his own attendance row, and the rows a refused write would add are
               read by psql before and after: identical, and no refused row exists.
            7. negative-invalid-scope (branch-manager with a scope that names no unit) → 403 on the attendance details and the roster list.
            """);

        var tag = Tag();
        var (br1, br2, central) = (s[Br1], s[Br2], s[Central]);

        var shiftCode = $"AC52-{tag}";
        await central.PostAsync("52a-shift", "ui/hr-core/Shifts/Create", "ui/hr-core/Shifts/Create",
            F(("Code", shiftCode), ("NameAr", $"وردية {tag}"), ("ShiftType", "Day"), ("StartTime", "08:00"), ("EndTime", "16:00"), ("IsActive", "true")));
        var shift = await IdAsync(Core, "Shifts", $"\"Code\"='{shiftCode}'", "52a-shift");

        var alep = await CreateBranchRecordsAsync(br2, "ALEP", Alep, AlepEmployee, shift, tag);
        var rif = await CreateBranchRecordsAsync(br1, "RIF", Rif, RifEmployee, shift, tag);

        // Lists: RIF's shown to br1-mgr, ALEP's shown to central-a and not to br1-mgr.
        var lists = new List<object>();
        async Task ListAsync(string name, string ownPath, string[] own, string foreignPath, string[] foreign)
        {
            var mine = await br1.GetAsync($"52a-list-{name}-own", ownPath);
            var theirs = await br1.GetAsync($"52a-list-{name}-foreign", foreignPath);
            var centrally = await central.GetAsync($"52a-list-{name}-central", foreignPath);
            var row = new { name, ownPath, foreignPath, ownShown = Shows(mine, own), foreignShown = Shows(theirs, foreign), foreignShownCentrally = Shows(centrally, foreign) };
            lists.Add(row);
            Assert.True(row.ownShown, $"52a {name}: br1-mgr's list does not show his branch's record.");
            Assert.False(row.foreignShown, $"52a {name}: br1-mgr's list shows the other branch's record.");
            Assert.True(row.foreignShownCentrally, $"52a {name}: central-a's list does not show the ALEP record.");
        }

        await ListAsync("diwan", $"ui/hr-core/DiwanCorrespondences?q={tag}", [rif.Marker], $"ui/hr-core/DiwanCorrespondences?q={tag}", [alep.Marker]);
        await ListAsync("statistics", $"ui/hr-core/HrStatisticReports?q={tag}", [rif.Marker], $"ui/hr-core/HrStatisticReports?q={tag}", [alep.Marker]);
        await ListAsync("inspections", $"ui/hr-core/FieldInspectionVisits?q={tag}", [rif.Marker], $"ui/hr-core/FieldInspectionVisits?q={tag}", [alep.Marker]);
        // The attendance list shows the employee's name, the day and the status: each day was free for its employee before the test.
        var rifName = await Db(Core, $"SELECT \"FullName\" FROM \"Employees\" WHERE \"Id\"='{RifEmployee}';");
        var alepName = await Db(Core, $"SELECT \"FullName\" FROM \"Employees\" WHERE \"Id\"='{AlepEmployee}';");
        await ListAsync("attendance",
            $"ui/hr-core/DailyAttendanceRecords?dateFrom={rif.AttendanceDate}&dateTo={rif.AttendanceDate}", [rifName],
            $"ui/hr-core/DailyAttendanceRecords?dateFrom={alep.AttendanceDate}&dateTo={alep.AttendanceDate}", [alepName]);
        await ListAsync("rosters", $"ui/hr-core/Rosters?q={tag}", [rif.Marker], $"ui/hr-core/Rosters?q={tag}", [alep.Marker]);
        await ListAsync("key-positions", $"ui/hr-core/KeyPositions?q={tag}", [rif.Marker], $"ui/hr-core/KeyPositions?q={tag}", [alep.Marker]);
        await ListAsync("succession", $"ui/hr-core/SuccessionPlans?year={Today.Year}", [rif.Plan, rif.Marker],
            $"ui/hr-core/SuccessionPlans?year={Today.Year}", [alep.Plan, alep.Marker]);

        var (exportStatus, export) = await br1.PostWithStatusAsync("52a-export", "ui/hr-core/Reports/ExportEmployeesCsv", "ui/hr-core/Reports", []);
        Assert.Equal(HttpStatusCode.OK, exportStatus);
        Assert.Contains(RifEmployee, export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AlepEmployee, export, StringComparison.OrdinalIgnoreCase);

        // Negatives, every row read before and after.
        var refused = $"AC52-X-{tag}";
        var before = await ForeignRowsAsync(alep, rif, refused);
        s.Recorder.WriteBefore(new { scenario = "52a", explicitDomainSnapshot = true, rows = before });

        var answers = new Answers();
        const string attendancePage = "ui/hr-core/DailyAttendanceRecords";
        await GetAsync(answers, br1, "attendance-details", $"ui/hr-core/DailyAttendanceRecords/Details/{alep.Attendance}", 404);
        await GetAsync(answers, br1, "attendance-edit-get", $"ui/hr-core/DailyAttendanceRecords/Edit/{alep.Attendance}", 404);
        await PostAsync(answers, br1, "attendance-edit", $"ui/hr-core/DailyAttendanceRecords/Edit/{alep.Attendance}", attendancePage, 404,
            F(("Id", alep.Attendance), ("EmployeeId", AlepEmployee), ("AttendanceDate", alep.AttendanceDate), ("Status", "Absent"), ("Notes", refused)));
        await PostAsync(answers, br1, "attendance-delete", $"ui/hr-core/DailyAttendanceRecords/Delete/{alep.Attendance}", attendancePage, 404, []);
        await PostAsync(answers, br1, "attendance-create-foreign-employee", "ui/hr-core/DailyAttendanceRecords/Create", attendancePage, 404,
            F(("EmployeeId", AlepEmployee), ("AttendanceDate", await FreeAttendanceDayAsync(AlepEmployee)), ("Status", "Present"), ("Notes", refused)));
        await PostAsync(answers, br1, "attendance-edit-own-to-foreign-employee", $"ui/hr-core/DailyAttendanceRecords/Edit/{rif.Attendance}", attendancePage, 404,
            F(("Id", rif.Attendance), ("EmployeeId", AlepEmployee), ("AttendanceDate", rif.AttendanceDate), ("Status", "Present"), ("Notes", refused)));

        const string rostersPage = "ui/hr-core/Rosters";
        await GetAsync(answers, br1, "roster-details", $"ui/hr-core/Rosters/Details/{alep.Roster}", 404);
        await PostAsync(answers, br1, "roster-add-assignment", "ui/hr-core/Rosters/AddAssignment", rostersPage, 404,
            F(("rosterId", alep.Roster), ("shiftId", shift), ("employeeId", RifEmployee), ("workDate", Day(Today.AddDays(2))), ("overtimeHours", "0")));
        await PostAsync(answers, br1, "roster-remove-assignment", "ui/hr-core/Rosters/RemoveAssignment", rostersPage, 404,
            F(("id", alep.Assignment), ("rosterId", alep.Roster)));
        await PostAsync(answers, br1, "roster-delete", $"ui/hr-core/Rosters/Delete/{alep.Roster}", rostersPage, 404, []);
        await PostAsync(answers, br1, "own-roster-foreign-employee", "ui/hr-core/Rosters/AddAssignment", rostersPage, 404,
            F(("rosterId", rif.Roster), ("shiftId", shift), ("employeeId", AlepEmployee), ("workDate", Day(Today.AddDays(2))), ("overtimeHours", "0")));
        await PostAsync(answers, br1, "roster-create-foreign-unit", "ui/hr-core/Rosters/Create", rostersPage, 404,
            F(("Title", refused), ("OrgUnitId", Alep), ("PeriodStart", Day(Today)), ("PeriodEnd", Day(Today.AddDays(30)))));

        const string keyPositionsPage = "ui/hr-core/KeyPositions";
        await GetAsync(answers, br1, "key-position-edit-get", $"ui/hr-core/KeyPositions/Edit/{alep.KeyPosition}", 404);
        await PostAsync(answers, br1, "key-position-edit", $"ui/hr-core/KeyPositions/Edit/{alep.KeyPosition}", keyPositionsPage, 404,
            F(("Id", alep.KeyPosition), ("Title", refused), ("OrgUnitId", Alep), ("Criticality", "1")));
        await PostAsync(answers, br1, "key-position-delete", $"ui/hr-core/KeyPositions/Delete/{alep.KeyPosition}", keyPositionsPage, 404, []);
        await PostAsync(answers, br1, "key-position-create-foreign-unit", "ui/hr-core/KeyPositions/Create", keyPositionsPage, 404,
            F(("Title", refused), ("OrgUnitId", Alep), ("Criticality", "3")));

        const string plansPage = "ui/hr-core/SuccessionPlans";
        await GetAsync(answers, br1, "plan-details", $"ui/hr-core/SuccessionPlans/Details/{alep.Plan}", 404);
        await PostAsync(answers, br1, "plan-add-candidate", "ui/hr-core/SuccessionPlans/AddCandidate", plansPage, 404,
            F(("successionPlanId", alep.Plan), ("employeeId", RifEmployee), ("latestEvaluationScore", "70"), ("yearsInService", "3"), ("meetsQualification", "true")));
        await PostAsync(answers, br1, "plan-remove-candidate", "ui/hr-core/SuccessionPlans/RemoveCandidate", plansPage, 404,
            F(("id", alep.Candidate), ("successionPlanId", alep.Plan)));
        await PostAsync(answers, br1, "plan-delete", $"ui/hr-core/SuccessionPlans/Delete/{alep.Plan}", plansPage, 404, []);
        await PostAsync(answers, br1, "plan-create-on-foreign-key-position", "ui/hr-core/SuccessionPlans/Create", plansPage, 404,
            F(("KeyPositionId", alep.KeyPosition), ("Year", (Today.Year + 1).ToString(CultureInfo.InvariantCulture)), ("Notes", refused)));

        await PostAsync(answers, br1, "diwan-create-foreign-unit", "ui/hr-core/DiwanCorrespondences/Create", "ui/hr-core/DiwanCorrespondences", 404,
            F(("ReferenceNumber", refused), ("Subject", refused), ("Direction", "Inbound"), ("OrgUnitId", Alep)));
        await PostAsync(answers, br1, "diwan-create-foreign-employee", "ui/hr-core/DiwanCorrespondences/Create", "ui/hr-core/DiwanCorrespondences", 404,
            F(("ReferenceNumber", refused + "-E"), ("Subject", refused), ("Direction", "Inbound"), ("RelatedEmployeeId", AlepEmployee)));
        await PostAsync(answers, br1, "statistic-create-foreign-unit", "ui/hr-core/HrStatisticReports/Create", "ui/hr-core/HrStatisticReports", 404,
            F(("Title", refused), ("PeriodStart", Day(Today.AddDays(-30))), ("PeriodEnd", Day(Today)), ("OrgUnitId", Alep)));
        await PostAsync(answers, br1, "inspection-create-foreign-unit", "ui/hr-core/FieldInspectionVisits/Create", "ui/hr-core/FieldInspectionVisits", 404,
            F(("Title", refused), ("VisitDate", Day(Today)), ("OrgUnitId", Alep)));
        await PostAsync(answers, br1, "shift-create-central-only", "ui/hr-core/Shifts/Create", "ui/hr-core/Shifts", 403,
            F(("Code", refused), ("NameAr", refused), ("ShiftType", "Day"), ("StartTime", "08:00"), ("EndTime", "16:00"), ("IsActive", "true")));

        await GetAsync(answers, s[InvalidScope], "invalid-scope-attendance-details", $"ui/hr-core/DailyAttendanceRecords/Details/{rif.Attendance}", 403);
        await GetAsync(answers, s[InvalidScope], "invalid-scope-rosters", "ui/hr-core/Rosters", 403);

        var after = await ForeignRowsAsync(alep, rif, refused);
        var unchanged = before.Keys.Where(k => before[k] == after[k]).ToList();
        var refusedRows = after.Where(kv => kv.Key.StartsWith("refused", StringComparison.Ordinal) && kv.Value != "0").Select(kv => kv.Key).ToList();
        await s.AuditAsync(alep.Diwan, alep.Statistic, alep.Inspection, alep.Attendance, alep.Roster, alep.KeyPosition, alep.Plan, rif.Attendance);
        s.Checks(new { shift, rif, alep, lists, exportHasOwnEmployee = true, exportHasForeignEmployee = false, answers = answers.Evidence, before, after, refusedRows });

        answers.AssertAll();
        Assert.True(unchanged.Count == before.Count, "52a: rows changed: " + string.Join(", ", before.Keys.Except(unchanged)));
        Assert.Empty(refusedRows);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 52b

    /// <summary>An active employee of the branch with no appointment transaction and no HR.Contracts snapshot yet.</summary>
    private static async Task<(string Employee, string Unit)> FreshEmployeeAsync(EvidenceRecorder r, string branch)
    {
        var rows = await Db(Core,
            "SELECT string_agg(x.\"Id\"::text || '|' || x.\"PrimaryOrgUnitId\"::text, ',') FROM (SELECT e.\"Id\", e.\"PrimaryOrgUnitId\" FROM \"Employees\" e " +
            "JOIN \"OrgUnits\" u ON u.\"Id\"=e.\"PrimaryOrgUnitId\" " +
            $"WHERE e.\"TenantId\"='{TenantA}' AND e.\"CurrentStatus\"=0 AND u.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{branch}') || '%' " +
            "ORDER BY e.\"Id\" DESC LIMIT 40) x;");
        foreach (var row in rows.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = row.Split('|');
            if (await Db(ContractsDb, $"SELECT count(*)::text FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{parts[0]}';") == "0"
                && await Db(ContractsDb, $"SELECT count(*)::text FROM \"EmployeeOrgUnitSnapshots\" WHERE \"EmployeeId\"='{parts[0]}';") == "0")
                return (parts[0], parts[1]);
        }

        AcceptanceGate.RequireData(r, false, $"لا يوجد موظف نشط بلا معاملة تعيين في الفرع {branch}",
            $"No active employee of {branch} without an appointment transaction; run scripts/acceptance-reset.ps1.");
        return default;
    }

    [SkippableFact]
    public async Task Ac52b_a_branch_manager_follows_his_branchs_hire_transaction_and_another_branchs_answers_404()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "52b", "AC-IAM-52b: the hire wizard — br1-mgr moves a RIF appointment to his allowed step; an ALEP one answers 404 on every step, is absent from his list and stays as it was",
            Br1, Br2, Central);
        s.Steps("""
            Test-only precondition (psql, hr_contracts_db): an appointment transaction is made only by a recruitment hire (ICandidateAccepted;
            AC-HCM-29 proves that path). The test writes one InProcess appointment for an active RIF employee and one for an active ALEP
            employee (neither has a transaction yet), each with the employee snapshot EmployeeCreated writes for a hire.
            1. Positive: br1-mgr's HireWizard list shows the RIF transaction; Step2 opens (200); Step3SubmitForApproval moves it to
               PendingApproval; Step3Approve is the management approver's (403, his role) and the state stays PendingApproval.
            2. br2-mgr's list shows the ALEP transaction and not the RIF one; central-a opens the ALEP Step2 (200).
            3. Negative: br1-mgr → the ALEP transaction's Step2, Step3, Step4 (GET), IssueAppointmentDecision, Step3SubmitForApproval,
               Step3Execute and CompleteMedicalStep (POST) → 404 each; his list does not show it; Index?employeeId=<ALEP employee> keeps
               QA-034's 403.
            4. The ALEP transaction's row, its checklist and state history, and the decisions are read before and after: identical.
            5. The two transactions and snapshots the test wrote are deleted when it ends.
            """);
        var (br1, br2, central) = (s[Br1], s[Br2], s[Central]);

        var (rifEmployee, rifUnit) = await FreshEmployeeAsync(s.Recorder, Rif);
        var (alepEmployee, alepUnit) = await FreshEmployeeAsync(s.Recorder, Alep);
        var units = new[] { Rif, Alep, rifUnit, alepUnit }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var snapshots = await Db(ContractsDb, $"SELECT count(*)::text FROM \"OrgUnitSnapshots\" WHERE \"Id\" IN ({string.Join(",", units.Select(u => $"'{u}'"))});");
        AcceptanceGate.RequireData(s.Recorder, snapshots == units.Count.ToString(CultureInfo.InvariantCulture),
            "لقطات الوحدات غير موجودة في hr_contracts_db", "HR.Contracts has no OrgUnitSnapshots of the two branches (HR.Core's start-up re-sync did not run).");

        var rifTxn = Guid.NewGuid().ToString("D");
        var alepTxn = Guid.NewGuid().ToString("D");
        await Db(ContractsDb,
            "INSERT INTO \"EmployeeOrgUnitSnapshots\" (\"EmployeeId\",\"TenantId\",\"PrimaryOrgUnitId\") VALUES " +
            $"('{rifEmployee}','{TenantA}','{rifUnit}'),('{alepEmployee}','{TenantA}','{alepUnit}');" +
            "INSERT INTO \"ServiceTransactions\" (\"Id\",\"TenantId\",\"EmployeeId\",\"TransactionType\",\"State\",\"Notes\",\"CreatedAt\",\"UpdatedAt\") VALUES " +
            $"('{rifTxn}','{TenantA}','{rifEmployee}','Appointment','InProcess','{Ac} test-only precondition',now(),now())," +
            $"('{alepTxn}','{TenantA}','{alepEmployee}','Appointment','InProcess','{Ac} test-only precondition',now(),now());");

        try
        {
            const string wizard = "ui/hr-contracts/HireWizard";
            var mine = await br1.GetAsync("52b-list-br1", wizard);
            var theirs = await br2.GetAsync("52b-list-br2", wizard);
            Assert.Contains(rifTxn, mine, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(alepTxn, theirs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(rifTxn, theirs, StringComparison.OrdinalIgnoreCase);
            await central.GetAsync("52b-central-step2", $"{wizard}/Step2?transactionId={alepTxn}");

            await br1.GetAsync("52b-own-step2", $"{wizard}/Step2?transactionId={rifTxn}");
            await br1.PostAsync("52b-own-submit", $"{wizard}/Step3SubmitForApproval", wizard, F(("transactionId", rifTxn)));
            var ownState = await Db(ContractsDb, $"SELECT \"State\" FROM \"ServiceTransactions\" WHERE \"Id\"='{rifTxn}';");
            var (approveStatus, _) = await br1.PostWithStatusAsync("52b-own-approve", $"{wizard}/Step3Approve", wizard, F(("transactionId", rifTxn)));
            var ownStateAfterApprove = await Db(ContractsDb, $"SELECT \"State\" FROM \"ServiceTransactions\" WHERE \"Id\"='{rifTxn}';");

            async Task<string> ForeignAsync() => string.Join(" / ", await Task.WhenAll(
                RowsAsync(ContractsDb, "ServiceTransactions", $"t.\"Id\"='{alepTxn}'"),
                RowsAsync(ContractsDb, "HireChecklists", $"t.\"ServiceTransactionId\"='{alepTxn}'"),
                RowsAsync(ContractsDb, "ServiceTransactionStateHistories", $"t.\"TransactionId\"='{alepTxn}'"),
                Db(ContractsDb, "SELECT count(*)::text FROM \"AdministrativeDecisions\";")));
            var before = await ForeignAsync();
            s.Recorder.WriteBefore(new { scenario = "52b", explicitDomainSnapshot = true, foreign = before });

            var answers = new Answers();
            foreach (var page in new[] { "Step2", "Step3", "Step4" })
                await GetAsync(answers, br1, $"foreign-{page.ToLowerInvariant()}", $"{wizard}/{page}?transactionId={alepTxn}", 404);
            foreach (var action in new[] { "IssueAppointmentDecision", "Step3SubmitForApproval", "Step3Execute" })
                await PostAsync(answers, br1, $"foreign-{action}", $"{wizard}/{action}", wizard, 404, F(("transactionId", alepTxn)));
            await PostAsync(answers, br1, "foreign-CompleteMedicalStep", $"{wizard}/CompleteMedicalStep", wizard, 404,
                F(("transactionId", alepTxn), ("evidenceRef", Ac)));
            await GetAsync(answers, br1, "foreign-employee-list", $"{wizard}?employeeId={alepEmployee}", 403);
            var listAfter = await br1.GetAsync("52b-list-br1-after", wizard);
            var after = await ForeignAsync();

            s.Checks(new
            {
                rifEmployee,
                alepEmployee,
                rifTxn,
                alepTxn,
                ownState,
                approveStatus = (int)approveStatus,
                ownStateAfterApprove,
                answers = answers.Evidence,
                foreignListed = Shows(mine, alepTxn) || Shows(listAfter, alepTxn),
                before,
                after
            });

            Assert.Equal("PendingApproval", ownState);
            Assert.Equal(HttpStatusCode.Forbidden, approveStatus);
            Assert.Equal("PendingApproval", ownStateAfterApprove);
            answers.AssertAll();
            Assert.False(Shows(mine, alepTxn) || Shows(listAfter, alepTxn), "52b: br1-mgr's hire list shows the ALEP transaction.");
            Assert.Equal(before, after);
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            // The two transactions are the test's own precondition: they go, so no later scenario takes them for a journey's appointment
            // (AC-INT-37b reads the newest appointment of the last hours).
            await Db(ContractsDb,
                $"DELETE FROM \"ServiceTransactionStateHistories\" WHERE \"TransactionId\" IN ('{rifTxn}','{alepTxn}');" +
                $"DELETE FROM \"HireChecklists\" WHERE \"ServiceTransactionId\" IN ('{rifTxn}','{alepTxn}');" +
                $"DELETE FROM \"ServiceTransactions\" WHERE \"Id\" IN ('{rifTxn}','{alepTxn}');" +
                $"DELETE FROM \"EmployeeOrgUnitSnapshots\" WHERE \"EmployeeId\" IN ('{rifEmployee}','{alepEmployee}');");
        }
    }

    // ---------------------------------------------------------------- 52c

    [SkippableFact]
    public async Task Ac52c_a_branch_manager_sees_his_branchs_supply_receipts_and_maintenance_and_writes_no_catalogue()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "52c", "AC-IAM-52c: logistics — br1-mgr lists RIF's supply orders, receipts and maintenance; an ALEP order answers 404 and stays as it was; the inventory and vendor catalogues refuse his writes (403)",
            Br1, Br2, LogisticsChecker);
        s.Steps("""
            1. br1-mgr orders supplies for RIF; a-logistics-checker (central) approves; br1-mgr fulfils it and records the receipt; br1-mgr
               records a maintenance of the RIF custody asset (AcceptanceAssetBr1).
            2. br2-mgr orders supplies for ALEP.
            3. Positive: br1-mgr's supply order, receipt and maintenance lists show RIF's records; br2-mgr's order list shows ALEP's.
            4. Negative: his supply order list does not show the ALEP order; the ALEP order's details, edit (GET and POST), add line, cancel,
               fulfil and the receipt form for it → 404; an order in ALEP's unit → 404; an inventory item and a vendor → 403 (D-52-3).
            5. The ALEP order's row, its lines and receipts, and the rows a refused write would add are read before and after: identical.
            """);
        var tag = Tag();
        var (br1, br2, checker) = (s[Br1], s[Br2], s[LogisticsChecker]);
        const string orders = "ui/admin-logistics/SupplyOrders";

        var rifItems = $"AC52-RIF-{tag}";
        await br1.PostAsync("52c-rif-order", $"{orders}/Create", $"{orders}/Create",
            F(("OrgUnitId", Rif), ("Items", rifItems), ("OrderedAt", Minute(DateTime.UtcNow))));
        var rifOrder = await IdAsync(LogisticsDb, "SupplyOrders", $"\"Items\"='{rifItems}'", "52c-rif-order");
        await checker.PostAsync("52c-rif-approve", $"{orders}/Approve/{rifOrder}", $"{orders}/Details/{rifOrder}", []);
        await br1.PostAsync("52c-rif-fulfil", $"{orders}/Fulfill/{rifOrder}", $"{orders}/Details/{rifOrder}", []);
        await br1.PostAsync("52c-rif-receipt", "ui/admin-logistics/ReceiptRecords/CreateFromOrder",
            $"ui/admin-logistics/ReceiptRecords/CreateFromOrder?supplyOrderId={rifOrder}",
            F(("SupplyOrderId", rifOrder), ("ReceivedAt", Minute(DateTime.UtcNow))));
        var rifReceipt = await IdAsync(LogisticsDb, "ReceiptRecords", $"\"SupplyOrderId\"='{rifOrder}'", "52c-rif-receipt");
        var asset = AcceptanceSeedIds.AcceptanceAssetBr1Id.ToString("D");
        await br1.PostAsync("52c-rif-maintenance", "ui/admin-logistics/MaintenanceRecords/Create", "ui/admin-logistics/MaintenanceRecords/Create",
            F(("AssetId", asset), ("MaintenanceType", "Preventive"), ("PerformedAt", Minute(DateTime.UtcNow)), ("PerformedBy", Ac), ("Cost", "10"), ("Notes", rifItems)));
        var rifMaintenance = await IdAsync(LogisticsDb, "MaintenanceRecords", $"\"Notes\"='{rifItems}'", "52c-rif-maintenance");

        var alepItems = $"AC52-ALEP-{tag}";
        await br2.PostAsync("52c-alep-order", $"{orders}/Create", $"{orders}/Create",
            F(("OrgUnitId", Alep), ("Items", alepItems), ("OrderedAt", Minute(DateTime.UtcNow))));
        var alepOrder = await IdAsync(LogisticsDb, "SupplyOrders", $"\"Items\"='{alepItems}'", "52c-alep-order");

        var orderList = await br1.GetAsync("52c-list-orders", orders);
        var receiptList = await br1.GetAsync("52c-list-receipts", "ui/admin-logistics/ReceiptRecords");
        var maintenanceList = await br1.GetAsync("52c-list-maintenance", "ui/admin-logistics/MaintenanceRecords");
        var theirList = await br2.GetAsync("52c-list-orders-br2", orders);

        var refused = $"AC52-X-{tag}";
        async Task<string> ForeignAsync() => string.Join(" / ", await Task.WhenAll(
            RowsAsync(LogisticsDb, "SupplyOrders", $"t.\"Id\"='{alepOrder}'"),
            RowsAsync(LogisticsDb, "SupplyOrderLines", $"t.\"SupplyOrderId\"='{alepOrder}'"),
            RowsAsync(LogisticsDb, "ReceiptRecords", $"t.\"SupplyOrderId\"='{alepOrder}'"),
            Db(LogisticsDb, $"SELECT count(*)::text FROM \"SupplyOrders\" WHERE \"Items\"='{refused}';"),
            Db(LogisticsDb, $"SELECT count(*)::text FROM \"InventoryItems\" WHERE \"Name\"='{refused}';"),
            Db(LogisticsDb, $"SELECT count(*)::text FROM \"Vendors\" WHERE \"Name\"='{refused}';")));
        var before = await ForeignAsync();
        s.Recorder.WriteBefore(new { scenario = "52c", explicitDomainSnapshot = true, foreign = before });

        var answers = new Answers();
        await GetAsync(answers, br1, "order-details", $"{orders}/Details/{alepOrder}", 404);
        await GetAsync(answers, br1, "order-edit-get", $"{orders}/Edit/{alepOrder}", 404);
        await PostAsync(answers, br1, "order-edit", $"{orders}/Edit/{alepOrder}", orders, 404,
            F(("Id", alepOrder), ("OrgUnitId", Alep), ("Items", refused), ("OrderedAt", Minute(DateTime.UtcNow))));
        await PostAsync(answers, br1, "order-add-line", $"{orders}/AddLine/{alepOrder}", orders, 404,
            F(("sku", refused), ("qtyRequested", "1"), ("unitCost", "1"), ("isCapitalizable", "false")));
        await PostAsync(answers, br1, "order-cancel", $"{orders}/Cancel/{alepOrder}", orders, 404, []);
        await PostAsync(answers, br1, "order-fulfil", $"{orders}/Fulfill/{alepOrder}", orders, 404, []);
        await GetAsync(answers, br1, "receipt-form", $"ui/admin-logistics/ReceiptRecords/CreateFromOrder?supplyOrderId={alepOrder}", 404);
        await PostAsync(answers, br1, "order-create-foreign-unit", $"{orders}/Create", orders, 404,
            F(("OrgUnitId", Alep), ("Items", refused), ("OrderedAt", Minute(DateTime.UtcNow))));
        await PostAsync(answers, br1, "inventory-create", "ui/admin-logistics/InventoryItems/Create", "ui/admin-logistics/InventoryItems", 403,
            F(("Name", refused), ("Quantity", "1")));
        await PostAsync(answers, br1, "vendor-create", "ui/admin-logistics/Vendors/Create", "ui/admin-logistics/Vendors", 403,
            F(("Name", refused)));
        var after = await ForeignAsync();

        s.Checks(new
        {
            rifOrder,
            rifReceipt,
            rifMaintenance,
            alepOrder,
            lists = new
            {
                ordersOwn = Shows(orderList, rifOrder),
                ordersForeign = Shows(orderList, alepOrder),
                receiptsOwn = Shows(receiptList, rifReceipt),
                maintenanceOwn = Shows(maintenanceList, rifMaintenance),
                ordersForeignShownToBr2 = Shows(theirList, alepOrder)
            },
            answers = answers.Evidence,
            before,
            after
        });

        Assert.True(Shows(orderList, rifOrder), "52c: br1-mgr's supply order list does not show the RIF order.");
        Assert.True(Shows(receiptList, rifReceipt), "52c: br1-mgr's receipt list does not show the RIF receipt.");
        Assert.True(Shows(maintenanceList, rifMaintenance), "52c: br1-mgr's maintenance list does not show the RIF maintenance.");
        Assert.True(Shows(theirList, alepOrder), "52c: br2-mgr's list does not show the ALEP order.");
        Assert.False(Shows(orderList, alepOrder), "52c: br1-mgr's supply order list shows the ALEP order.");
        answers.AssertAll();
        Assert.Equal(before, after);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 52d

    private static async Task<(HttpStatusCode Status, List<(string Id, string? Unit)> Items)> InstancesAsync(AcceptanceUi ui, string step, string path)
    {
        var (status, body) = await ui.GetWithStatusAsync(step, path);
        var items = new List<(string, string?)>();
        if (status == HttpStatusCode.OK)
        {
            using var json = JsonDocument.Parse(body);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var unit = item.TryGetProperty("scopeOrgUnitId", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                items.Add((item.GetProperty("id").GetString()!, unit));
            }
        }

        return (status, items);
    }

    [SkippableFact]
    public async Task Ac52d_the_central_reader_lists_every_workflow_instance_and_a_branch_manager_his_branchs_only()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "52d", "AC-IAM-52d: api/workflow-instances — central-a lists every instance; br1-mgr's list holds RIF's only (no ALEP, no central), and an ALEP or central instance answers 404",
            Central, Br1, InvalidScope);
        s.Steps("""
            1. central-a raises a requisition on a vacant ALEP position, scoped to ALEP, and submits it: its approval instance is stamped
               with an ALEP unit (psql).
            2. central-a submits an establishment change (as AC-POL-49 does): its instance has no unit, so it is central (D-52-1).
            3. Positive: central-a's list (entityType=RecruitmentRequisition) holds the ALEP instance, and Get answers 200 for it and for the
               central one; br1-mgr's list is not empty.
            4. Negative: br1-mgr's lists (all types, and requisitions) hold no instance whose unit is outside RIF or empty (psql on every id),
               and not the ALEP one; Get and History of the ALEP instance, and Get of the central instance → 404.
            5. negative-invalid-scope → 403 on the list.
            6. The ALEP requisition is cancelled afterwards (best effort, recorded).
            """);
        var (central, br1) = (s[Central], s[Br1]);

        var position = await Db(Core,
            "SELECT p.\"Id\"::text FROM \"FundedPositions\" p JOIN \"OrgUnits\" u ON u.\"Id\"=p.\"OrgUnitId\" " +
            $"WHERE p.\"TenantId\"='{TenantA}' AND p.\"Status\"='Vacant' AND u.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{Alep}') || '%' " +
            "AND NOT EXISTS (SELECT 1 FROM \"RecruitmentRequisitions\" r WHERE r.\"FundedPositionId\"=p.\"Id\" AND r.\"Status\" IN (" +
            string.Join(",", BlockingStatuses.Select(b => $"'{b}'")) + ")) ORDER BY p.\"PositionCode\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(position, out _), "لا يوجد منصب شاغر في فرع حلب", "No vacant ALEP funded position without a requisition; run scripts/acceptance-reset.ps1.");
        var rifInstances = await Db(Core,
            "SELECT count(*)::text FROM \"WorkflowInstances\" i JOIN \"OrgUnits\" u ON u.\"Id\"=i.\"ScopeOrgUnitId\" " +
            $"WHERE i.\"TenantId\"='{TenantA}' AND u.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{Rif}') || '%';");
        AcceptanceGate.RequireData(s.Recorder, rifInstances != "0",
            "لا توجد نسخ مسارات لفرع ريف دمشق", "The seed holds no RIF-stamped workflow instance.");

        // A central instance: an establishment change (a type with no unit, so central, D-52-1), as AC-POL-49 submits it.
        var (_, correlation) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona(Central));
        using (var centralApi = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona(Central), correlation))
        {
            var change = await EstablishmentChangeFlow.SubmitSampleChangeAsync(centralApi, s.Recorder, $"{Ac}-52d");
            s.Recorder.CaptureHttp("52d-central-change", "POST", "/api/hr-core/api/establishment/changes/submit", 200, new { change });
        }

        var centralInstance = await Db(Core,
            $"SELECT COALESCE((SELECT \"Id\"::text FROM \"WorkflowInstances\" WHERE \"TenantId\"='{TenantA}' AND \"ScopeOrgUnitId\" IS NULL ORDER BY \"StartedAt\" DESC LIMIT 1), '');");
        Assert.True(Guid.TryParse(centralInstance, out _), "52d: the establishment change started no central workflow instance.");

        var title = $"AC52-ALEP-{Tag()}";
        await central.PostAsync("52d-requisition", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", Alep), new("FundedPositionId", position), new("JobTitle", title), new("JustificationNotes", title),
            new("LegalSourceId", Syr001LegislationIds.Law50_2004), new("BudgetYear", Today.Year.ToString(CultureInfo.InvariantCulture)),
            new("PublishingChannel", "PublicOnly")
        ]);
        var requisition = await IdAsync(Core, "RecruitmentRequisitions", $"\"JustificationNotes\"='{title}'", "52d-requisition");
        await central.PostAsync("52d-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{requisition}", ReqDetails(requisition), []);
        var instance = await Db(Core, $"SELECT COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{requisition}';");
        Assert.True(Guid.TryParse(instance, out _), "52d: the submitted requisition has no workflow instance.");
        var stampedInAlep = await Db(Core,
            "SELECT count(*)::text FROM \"WorkflowInstances\" i JOIN \"OrgUnits\" u ON u.\"Id\"=i.\"ScopeOrgUnitId\" " +
            $"WHERE i.\"Id\"='{instance}' AND u.\"MaterializedPath\" LIKE (SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{Alep}') || '%';");

        const string api = "api/hr-core/api/workflow-instances";
        var (centralStatus, centralList) = await InstancesAsync(central, "52d-central-list", $"{api}?entityType=RecruitmentRequisition");
        var (centralGet, _) = await central.GetWithStatusAsync("52d-central-get", $"{api}/{instance}");
        var (centralGetCentral, _) = await central.GetWithStatusAsync("52d-central-get-central", $"{api}/{centralInstance}");
        var (allStatus, br1All) = await InstancesAsync(br1, "52d-br1-list", api);
        var (reqStatus, br1Requisitions) = await InstancesAsync(br1, "52d-br1-list-requisitions", $"{api}?entityType=RecruitmentRequisition");
        var listed = br1All.Concat(br1Requisitions).Select(i => i.Id).Distinct().ToList();
        var outside = listed.Count == 0 ? "0" : await Db(Core,
            "SELECT count(*)::text FROM \"WorkflowInstances\" i LEFT JOIN \"OrgUnits\" u ON u.\"Id\"=i.\"ScopeOrgUnitId\" " +
            $"WHERE i.\"Id\" IN ({string.Join(",", listed.Select(id => $"'{id}'"))}) AND (u.\"Id\" IS NULL OR u.\"MaterializedPath\" NOT LIKE " +
            $"(SELECT \"MaterializedPath\" FROM \"OrgUnits\" WHERE \"Id\"='{Rif}') || '%');");

        var answers = new Answers();
        await GetAsync(answers, br1, "foreign-get", $"{api}/{instance}", 404);
        await GetAsync(answers, br1, "foreign-history", $"{api}/{instance}/history", 404);
        await GetAsync(answers, br1, "central-get", $"{api}/{centralInstance}", 404);
        await GetAsync(answers, s[InvalidScope], "invalid-scope-list", api, 403);

        var (cancelStatus, _) = await central.PostWithStatusAsync("52d-cleanup-cancel", $"ui/hr-core/RecruitmentRequisitions/Cancel/{requisition}", ReqDetails(requisition),
            F(("reason", $"{Ac} 52d clean-up")));
        var finalState = await RequisitionStateAsync(requisition);

        s.Checks(new
        {
            requisition,
            instance,
            stampedInAlep,
            centralInstance,
            rifInstances,
            centralList = new { status = (int)centralStatus, count = centralList.Count, holdsAlep = centralList.Any(i => i.Id == instance) },
            centralGet = (int)centralGet,
            centralGetCentral = (int)centralGetCentral,
            br1Lists = new { all = (int)allStatus, requisitions = (int)reqStatus, count = listed.Count, outsideRif = outside, holdsAlep = listed.Contains(instance) },
            answers = answers.Evidence,
            cleanup = new { status = (int)cancelStatus, finalState }
        });

        Assert.Equal("1", stampedInAlep);
        Assert.Equal(HttpStatusCode.OK, centralStatus);
        Assert.Contains(centralList, i => i.Id == instance);
        Assert.Equal(HttpStatusCode.OK, centralGet);
        Assert.Equal(HttpStatusCode.OK, centralGetCentral);
        Assert.Equal(HttpStatusCode.OK, allStatus);
        Assert.Equal(HttpStatusCode.OK, reqStatus);
        Assert.NotEmpty(br1All);
        Assert.Equal("0", outside);
        Assert.DoesNotContain(instance, listed);
        answers.AssertAll();
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 52e

    [SkippableFact]
    public async Task Ac52e_another_branchs_internal_vacancy_and_its_application_answer_404()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "52e", "AC-IAM-52e: internal recruitment — br1-mgr's PromoteFromInternal on an ALEP application and the ALEP vacancy's details answer 404; nothing changes",
            Br1, Central);
        s.Steps("""
            Test-only precondition (psql, hr_core_db): an internal vacancy comes from a requisition published internally (AC-HCM-26 proves
            that path). The test copies the seeded vacancy (its unit is an HQ section) into a RIF vacancy, and into an ALEP vacancy with a
            copy of the seeded application for the ALEP employee; neither copy has a requisition.
            1. Positive: br1-mgr opens the RIF vacancy (200) and his marketplace list shows it; central-a opens the ALEP vacancy (200) and
               lists it.
            2. Negative: br1-mgr → the ALEP vacancy's details, PromoteFromInternal with its application, and ChangeApplicationStatus on it
               → 404; his marketplace list does not show it.
            3. Central control: central-a's PromoteFromInternal on the same application is not a 404 (the action refuses an application
               with no requisition), so the 404 is the scope's answer.
            4. The vacancy and application rows and the recruitment applications are read before and after: identical.
            5. The copies the test wrote are deleted when it ends.
            """);
        var (br1, central) = (s[Br1], s[Central]);

        var tag = Tag();
        var vacancy = Guid.NewGuid().ToString("D");
        var ownVacancy = Guid.NewGuid().ToString("D");
        var application = Guid.NewGuid().ToString("D");
        var title = $"AC52-ALEP-{tag}";
        var ownTitle = $"AC52-RIF-{tag}";
        var seededVacancy = AcceptanceSeedIds.Br1InternalVacancyId.ToString("D");
        var seededApplication = AcceptanceSeedIds.Br1InternalApplicationId.ToString("D");
        AcceptanceGate.RequireData(s.Recorder, await Db(Core, $"SELECT count(*)::text FROM \"InternalApplications\" WHERE \"Id\"='{seededApplication}';") == "1",
            "الترشح الداخلي المزروع غير موجود", "The seeded internal vacancy and application are missing; run scripts/acceptance-reset.ps1.");
        static string CopyVacancy(string table, string seeded, string id, string unit, string title) =>
            $"CREATE TEMP TABLE {table} AS SELECT * FROM \"InternalVacancies\" WHERE \"Id\"='{seeded}';" +
            $"UPDATE {table} SET \"Id\"='{id}', \"OrgUnitId\"='{unit}', \"Title\"='{title}', \"RequisitionId\"=NULL, \"IsDerivedFromRequisition\"=false, \"CreatedAt\"=now(), \"UpdatedAt\"=now();" +
            $"INSERT INTO \"InternalVacancies\" SELECT * FROM {table};";
        await Db(Core,
            CopyVacancy("v52", seededVacancy, vacancy, Alep, title) + CopyVacancy("r52", seededVacancy, ownVacancy, Rif, ownTitle) +
            $"CREATE TEMP TABLE a52 AS SELECT * FROM \"InternalApplications\" WHERE \"Id\"='{seededApplication}';" +
            $"UPDATE a52 SET \"Id\"='{application}', \"InternalVacancyId\"='{vacancy}', \"EmployeeId\"='{AlepEmployee}', \"RequisitionId\"=NULL, " +
            "\"PromotedRecruitmentApplicationId\"=NULL, \"CreatedAt\"=now(), \"UpdatedAt\"=now();" +
            "INSERT INTO \"InternalApplications\" SELECT * FROM a52; SELECT 1;");

        try
        {
            const string market = "ui/hr-core/TalentMarketplace";
            await br1.GetAsync("52e-own-vacancy", $"{market}/Details/{ownVacancy}");
            await central.GetAsync("52e-central-vacancy", $"{market}/Details/{vacancy}");
            var centralList = await central.GetAsync("52e-central-list", market);
            var mine = await br1.GetAsync("52e-br1-list", market);
            Assert.True(Shows(mine, ownTitle), "52e: br1-mgr's marketplace list does not show the RIF vacancy.");

            async Task<string> RowsNowAsync() => string.Join(" / ", await Task.WhenAll(
                RowsAsync(Core, "InternalVacancies", $"t.\"Id\"='{vacancy}'"),
                RowsAsync(Core, "InternalApplications", $"t.\"Id\"='{application}'"),
                Db(Core, "SELECT count(*)::text FROM \"RecruitmentApplications\";")));
            var before = await RowsNowAsync();
            s.Recorder.WriteBefore(new { scenario = "52e", explicitDomainSnapshot = true, foreign = before });

            var answers = new Answers();
            await GetAsync(answers, br1, "foreign-vacancy-details", $"{market}/Details/{vacancy}", 404);
            await PostAsync(answers, br1, "foreign-promote", "ui/hr-core/RecruitmentApplications/PromoteFromInternal", market, 404,
                F(("internalApplicationId", application)));
            await PostAsync(answers, br1, "foreign-change-status", $"{market}/ChangeApplicationStatus", market, 404,
                F(("id", application), ("vacancyId", vacancy), ("status", "UnderReview")));
            var (centralPromote, _) = await central.PostWithStatusAsync("52e-central-promote", "ui/hr-core/RecruitmentApplications/PromoteFromInternal", market,
                F(("internalApplicationId", application)));
            var after = await RowsNowAsync();

            s.Checks(new
            {
                vacancy,
                ownVacancy,
                application,
                title,
                ownListed = Shows(mine, ownTitle),
                listedCentrally = Shows(centralList, title),
                listedToBr1 = Shows(mine, title),
                answers = answers.Evidence,
                centralPromote = (int)centralPromote,
                before,
                after
            });

            Assert.True(Shows(centralList, title), "52e: central-a's marketplace list does not show the ALEP vacancy.");
            Assert.False(Shows(mine, title), "52e: br1-mgr's marketplace list shows the ALEP vacancy.");
            answers.AssertAll();
            Assert.NotEqual(HttpStatusCode.NotFound, centralPromote);
            Assert.Equal(before, after);
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            // The copies are the test's own precondition: they go when the scenario ends.
            await Db(Core,
                $"DELETE FROM \"InternalApplications\" WHERE \"Id\"='{application}';" +
                $"DELETE FROM \"InternalVacancies\" WHERE \"Id\" IN ('{vacancy}','{ownVacancy}');");
        }
    }
}
