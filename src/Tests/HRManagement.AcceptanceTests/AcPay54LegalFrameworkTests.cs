using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-PAY-54a–f (PLAN-54) on the prod-like stack, through the gateway. An agency reaches a payroll calculation by a legal framework
/// it imports from a JSON pack: a processor imports it, a second person approves it, a third attests it, the approver or the attestor
/// publishes it, and the run of its first month is pinned to it. The negatives: a malformed pack writes nothing; the approver does
/// not attest, an editor does not approve or publish, nothing is published from a draft; an interim value that cites an exception the
/// catalog does not hold stops the approval; a version is not published into a posted period; and a manual allowance waits for a
/// second person and enters no calculation before that.
/// The class uses its own codes (<c>LAW_50_2004_AC54*</c>) and its own months (2031-01 to 2031-03, 2031-06, 2031-07): a version of 2031 changes
/// nothing before 2031 for any other class on the stack. Its steps run in order (54c needs 54a's version and run). A second run of the
/// class on the same stack finds its code already there and stops at 54a's precondition: it needs a fresh stack. Nothing stops or
/// recreates a container.
/// <para>
/// <b>A dated limit (PLAN-54, P-10):</b> 54a needs the two STR-002 exceptions the interim pack cites
/// (<c>EX-STR2-TAX-INTERIM</c>, <c>EX-STR2-LAW50-UNCERTIFIED</c>) to be active. Both end on 2027-01-31 in
/// <c>docs/str-legal/control-catalog.json</c>. From 2027-02-01 this class fails at its first check, on purpose, until the owner
/// renews or replaces those exceptions: that is an STR-002 decision, not a test's.
/// </para>
/// </summary>
[RequiredAcceptance]
[TestCaseOrderer(ScenarioNameOrderer.TypeName, ScenarioNameOrderer.AssemblyName)]
public class AcPay54LegalFrameworkTests
{
    private const string Ac = "AC-PAY-54";
    private const string Processor = "payroll-processor";
    private const string Approver = "payroll-approver";
    private const string Attestor = "a-payroll-approver-2";
    /// <summary>The agency's hr-admin: he holds Payroll.MasterData and Payroll.Approve, so he can be an editor who also has the cycle's policies.</summary>
    private const string Admin = "a-directory-checker";
    private const string Payroll = "payroll_db";
    private const string Frameworks = "api/payroll/frameworks";
    private const string Code = "LAW_50_2004_AC54";
    private const string TaxException = "EX-STR2-TAX-INTERIM";
    private const string Law50Exception = "EX-STR2-LAW50-UNCERTIFIED";

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly DateOnly FirstMonth = new(2031, 1, 1);
    private static readonly DateOnly SecondMonth = new(2031, 2, 1);
    private static readonly DateOnly ThirdMonth = new(2031, 3, 1);
    private static readonly DateOnly ManualInputMonth = new(2031, 6, 1);
    private static readonly DateOnly NextMonth = new(2031, 7, 1);

    private static Task<string> Db(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll, sql);

    private static Task<long> Count(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll, sql);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    private static string PackPath => Path.Combine(AcceptanceHarness.RepoRoot, "docs", "syr", "payroll-framework-packs", "law50-interim.json");

    private static string InterimPack() => File.ReadAllText(PackPath);

    private static string PackWith(Action<JsonObject> change)
    {
        var node = JsonNode.Parse(InterimPack())!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    // ---------------------------------------------------------------- the API, as one persona

    private sealed class Api(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
    {
        public static async Task<Api> OpenAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")),
                recorder, persona, tag);

        public async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(string step, HttpMethod method, string path, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json = default;
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                json = JsonSerializer.Deserialize<JsonElement>(text);
            // The evidence keeps the answer, never a pack: an exported pack is summarised by its size.
            object captured = path.EndsWith("/export", StringComparison.Ordinal)
                ? new { exportedBytes = Encoding.UTF8.GetByteCount(text) }
                : json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", method.Method, "/" + path, (int)response.StatusCode, captured);
            return (response.StatusCode, json, text);
        }

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(string step, string path, object? body = null) =>
            SendAsync(step, HttpMethod.Post, path, new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json"));

        /// <summary>The import's envelope, built as text: the pack is sent as the file holds it.</summary>
        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> ImportAsync(string step, string packJson, string code, DateOnly effectiveFrom) =>
            SendAsync(step, HttpMethod.Post, $"{Frameworks}/import", new StringContent(
                "{\"pack\":" + packJson + ",\"code\":\"" + code + "\",\"effectiveFrom\":\"" + Day(effectiveFrom) + "\"}", Encoding.UTF8, "application/json"));

        public void Dispose() => http.Dispose();
    }

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Snip(string body) => body.Length > 400 ? body[..400] : body;

    /// <summary>A version's place in the cycle, as one line: what a refusal must leave as it was.</summary>
    private static Task<string> VersionStateAsync(string id) => Db(
        "SELECT c.\"PublicationStatus\" || '|' || c.\"IsActive\"::text || '|' || COALESCE(c.\"ContentHash\",'∅') || '|' || COALESCE(c.\"ApprovedBy\"::text,'∅') || '|' || " +
        "COALESCE(c.\"PublishedBy\"::text,'∅') || '|' || to_char(c.\"EffectiveFrom\", 'YYYY-MM-DD') || '|' || COALESCE(to_char(c.\"EffectiveTo\", 'YYYY-MM-DD'),'∅') || '|' || " +
        "(SELECT count(*) FROM \"LegalAttestations\" a WHERE a.\"PayrollConfigurationId\" = c.\"Id\" AND a.\"Status\" = 'Active')::text " +
        $"FROM \"PayrollConfigurations\" c WHERE c.\"Id\"='{id}';");

    private static Task<string> StatusAsync(string id) => Db($"SELECT \"PublicationStatus\" FROM \"PayrollConfigurations\" WHERE \"Id\"='{id}';");

    /// <summary>The rows a framework command may write, counted for agency A.</summary>
    private static Task<string> FrameworkRowsAsync() => Db(
        "SELECT (SELECT count(*) FROM \"PayrollConfigurations\" WHERE \"TenantId\"='" + TenantA + "')::text || '|' || " +
        "(SELECT count(*) FROM \"TaxBrackets\" WHERE \"TenantId\"='" + TenantA + "')::text || '|' || " +
        "(SELECT count(*) FROM \"StatutoryDeductionRules\" WHERE \"TenantId\"='" + TenantA + "')::text || '|' || " +
        "(SELECT count(*) FROM \"OfficialHolidays\" WHERE \"TenantId\"='" + TenantA + "')::text || '|' || " +
        "(SELECT count(*) FROM \"DisbursementPriorityRules\" WHERE \"TenantId\"='" + TenantA + "')::text || '|' || " +
        "(SELECT count(*) FROM \"PayrollConfigurationFieldSources\" WHERE \"TenantId\"='" + TenantA + "')::text || '|' || " +
        "(SELECT count(*) FROM \"LegalDecisionRecords\" WHERE \"TenantId\"='" + TenantA + "')::text;");

    private static async Task<string> ImportDraftAsync(Api processor, string step, string packJson, string code, DateOnly effectiveFrom)
    {
        var (status, body, text) = await processor.ImportAsync(step, packJson, code, effectiveFrom);
        Assert.True(status == HttpStatusCode.Created, $"{step}: the import answered {(int)status}: {Snip(text)}");
        Assert.Equal("Draft", Text(body, "status"));
        return body.GetProperty("frameworkId").GetGuid().ToString("D");
    }

    private static async Task ActAsync(Api api, string step, string id, string act)
    {
        var (status, _, text) = await api.PostAsync(step, $"{Frameworks}/{id}/{act}");
        Assert.True(status == HttpStatusCode.OK, $"{step}: {act} answered {(int)status}: {Snip(text)}");
    }

    /// <summary>A refused act: 400 with an Arabic message that holds <paramref name="expected"/>, and the version as it was.</summary>
    private static async Task<string> RefusedAsync(Api api, string step, string id, string act, string expected)
    {
        var before = await VersionStateAsync(id);
        var (status, body, text) = await api.PostAsync(step, $"{Frameworks}/{id}/{act}");
        var error = Text(body, "error");
        Assert.True(status == HttpStatusCode.BadRequest, $"{step}: {act} answered {(int)status}, expected 400: {Snip(text)}");
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.Equal(before, await VersionStateAsync(id));
        return error;
    }

    private static async Task<string> CreateAndCalculateAsync(Api processor, string tag, DateOnly month)
    {
        var (created, createdBody, createdText) = await processor.PostAsync($"{tag}-create-run", "api/payroll/runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = month.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = DateTime.UtcNow
        });
        Assert.True(created is HttpStatusCode.OK or HttpStatusCode.Created, $"{tag}: creating the run of {Day(month)} answered {(int)created}: {Snip(createdText)}");
        var run = createdBody.GetProperty("id").GetGuid().ToString("D");
        await CalculateAsync(processor, tag, run);
        return run;
    }

    private static async Task CalculateAsync(Api processor, string tag, string run)
    {
        var (calculated, _, text) = await processor.PostAsync($"{tag}-calculate", $"api/payroll/runs/{run}/calculate");
        Assert.True(calculated == HttpStatusCode.OK, $"{tag}: calculating the run answered {(int)calculated}: {Snip(text)}");
    }

    /// <summary>The number of lines of a run and an md5 of every line and component (the employee, the kind, the name, the amount).</summary>
    private static Task<string> RunResultAsync(string run) => Db(
        "SELECT (SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='" + run + "')::text || ':' || md5(COALESCE(string_agg(" +
        "l.\"EmployeeId\"::text || '|' || c.\"ComponentType\"::text || '|' || c.\"ComponentName\" || '|' || c.\"Amount\"::text, ';' " +
        "ORDER BY l.\"EmployeeId\", c.\"ComponentType\"::text, c.\"ComponentName\", c.\"Amount\"), '')) " +
        "FROM \"SalaryLines\" l JOIN \"SalaryLineComponents\" c ON c.\"SalaryLineId\" = l.\"Id\" WHERE l.\"PayrollRunId\"='" + run + "';");

    private static Task<string> PinAsync(string run) => Db(
        $"SELECT COALESCE(\"PayrollConfigurationId\"::text,'∅') || '|' || COALESCE(\"ConfigContentHash\",'∅') FROM \"PayrollRuns\" WHERE \"Id\"='{run}';");

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

    // ---------------------------------------------------------------- P-10: the dated limit

    /// <summary>
    /// The two exceptions the interim pack cites, read from the catalog the services load. When one is no longer active the class
    /// fails here, naming them and the date: it never takes another path, and it never edits the catalog.
    /// </summary>
    private static (string Tax, string Law50) RequireActiveExceptions()
    {
        var today = DateOnly.FromDateTime(HrLocalTime.ToLocal(DateTime.UtcNow, HrLocalTime.DefaultZone));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AcceptanceHarness.RepoRoot, "docs", "str-legal", "control-catalog.json")));
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var exception in catalog.RootElement.GetProperty("exceptions").EnumerateArray())
        {
            var id = exception.GetProperty("exceptionId").GetString() ?? "";
            if (id is not (TaxException or Law50Exception)) continue;
            var status = exception.GetProperty("status").GetString();
            var expiresOn = exception.TryGetProperty("expiresOn", out var value) ? value.GetString() : null;
            var active = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                         && DateOnly.TryParseExact(expiresOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastDay)
                         && today <= lastDay;
            found[id] = $"{status} until {expiresOn}" + (active ? "" : " (NOT ACTIVE)");
        }

        var tax = found.GetValueOrDefault(TaxException, "missing from the catalog (NOT ACTIVE)");
        var law50 = found.GetValueOrDefault(Law50Exception, "missing from the catalog (NOT ACTIVE)");
        Assert.True(
            !tax.Contains("NOT ACTIVE", StringComparison.Ordinal) && !law50.Contains("NOT ACTIVE", StringComparison.Ordinal),
            $"AC-PAY-54a needs the STR-002 exceptions the interim legal pack cites to be active on {Day(today)} (Damascus). " +
            $"{TaxException}: {tax}. {Law50Exception}: {law50}. By design (D-PAY-1, PLAN-54 P-10) an interim payroll framework is neither " +
            "approved, published nor calculated after its exceptions end. Renewing or replacing these exceptions in " +
            "docs/str-legal/control-catalog.json is the owner's STR-002 decision; this class does not take another path.");
        return (tax, law50);
    }

    // ---------------------------------------------------------------- 54a

    [SkippableFact]
    public async Task Ac54a_a_framework_is_imported_approved_attested_and_published_by_three_people_and_its_first_month_is_calculated()
    {
        AcceptanceEnv.RequireEnabled();
        var (taxException, law50Exception) = RequireActiveExceptions();
        var s = await StartAsync(Ac, "54a",
            "AC-PAY-54a: a processor imports the interim legal pack under a new code from 2031-01-01; a second person approves it, a third attests it, the approver publishes it; the run of 2031-01 is calculated and pinned to it. A pack with a bracket of 120% answers 400 and writes no row.",
            Processor, Approver, Attestor);
        s.Steps("""
            0. The dated limit (P-10): the two exceptions the pack cites, EX-STR2-TAX-INTERIM and EX-STR2-LAW50-UNCERTIFIED, are active
               today in docs/str-legal/control-catalog.json (both end on 2027-01-31). After that date this step fails by design.
            1. Precondition, in before.json: agency A has no version of the code LAW_50_2004_AC54 (a second run of the class on the
               same stack stops here: it needs a fresh stack), and no payroll run of 2031-01.
            2. Negative: payroll-processor posts the pack with the third bracket at 120% → 400, the error names
               $.taxBrackets[2].ratePercent in Arabic, and no row of any framework table is written.
            3. payroll-processor imports docs/syr/payroll-framework-packs/law50-interim.json with the code LAW_50_2004_AC54 and the
               effective date 2031-01-01 (POST api/payroll/frameworks/import, with the antiforgery token) → 201, a Draft of
               fingerprint v2 with its five brackets, four statutory deductions, four holidays, three priority rules and nineteen
               field sources, every value interim under one of the two exceptions.
            4. payroll-approver approves → Approved, with a fingerprint. a-payroll-approver-2 attests → an active attestation of that
               fingerprint that names both exceptions. payroll-approver publishes → Published and active, from 2031-01-01. Three
               different people: the importer, the approver, the attestor.
            5. payroll-processor creates the Regular run of 2031-01 and calculates it → the run is pinned to the version
               (provenance.payrollConfigurationId and the fingerprint), its provenance says «إطار مؤقت — استثناء …», its job's log
               too, and a line holds the four statutory deductions of the version.
            """);

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "54a");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "54a");
        using var attestor = await Api.OpenAsync(s.Recorder, Attestor, "54a");

        // 1. Preconditions.
        var existing = await Count($"SELECT count(*) FROM \"PayrollConfigurations\" WHERE \"TenantId\"='{TenantA}' AND \"Code\" LIKE '{Code}%';");
        var runsOfTheMonth = await Count(
            $"SELECT count(*) FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\" <= '2031-01-31' AND \"PeriodEnd\" >= '2031-01-01';");
        var rowsBefore = await FrameworkRowsAsync();
        s.Recorder.WriteBefore(new
        {
            scenario = "54a",
            explicitDomainSnapshot = true,
            exceptions = new Dictionary<string, string> { [TaxException] = taxException, [Law50Exception] = law50Exception },
            versionsOfTheCode = existing,
            runsOf2031_01 = runsOfTheMonth,
            frameworkRowsOfAgencyA = rowsBefore,
            frameworkRowsFormat = "versions|brackets|statutory deductions|holidays|priority rules|field sources|decisions"
        });
        AcceptanceGate.RequireData(s.Recorder, existing == 0,
            $"للجهة A نسخة سابقة بالرمز {Code}: هذه الفئة تحتاج مكدساً جديداً", $"Agency A already holds a version of {Code}: this class needs a fresh stack (it ran before on this one).");
        AcceptanceGate.RequireData(s.Recorder, runsOfTheMonth == 0,
            "للجهة A تشغيل رواتب في 2031-01", "Agency A already holds a payroll run of 2031-01: this class needs a fresh stack.");

        // 2. A malformed pack writes nothing.
        var (malformed, malformedBody, _) = await processor.ImportAsync("malformed-pack", PackWith(p => p["taxBrackets"]![2]!["ratePercent"] = 120), Code, FirstMonth);
        var malformedPath = malformedBody.ValueKind == JsonValueKind.Object && malformedBody.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
            ? errors[0].GetProperty("path").GetString()
            : null;
        var rowsAfterMalformed = await FrameworkRowsAsync();

        // 3. The import.
        var id = await ImportDraftAsync(processor, "import", InterimPack(), Code, FirstMonth);
        var draft = await Db(
            "SELECT c.\"PublicationStatus\" || '|' || c.\"HashSchemaVersion\"::text || '|' || c.\"TenantId\"::text || '|' || to_char(c.\"EffectiveFrom\", 'YYYY-MM-DD') || '|' || " +
            "(SELECT count(*) FROM \"TaxBrackets\" b WHERE b.\"PayrollConfigurationId\" = c.\"Id\")::text || '|' || " +
            "(SELECT count(*) FROM \"StatutoryDeductionRules\" d WHERE d.\"PayrollConfigurationId\" = c.\"Id\")::text || '|' || " +
            "(SELECT count(*) FROM \"OfficialHolidays\" h WHERE h.\"PayrollConfigurationId\" = c.\"Id\")::text || '|' || " +
            "(SELECT count(*) FROM \"DisbursementPriorityRules\" r WHERE r.\"PayrollConfigurationId\" = c.\"Id\")::text || '|' || " +
            "(SELECT count(*) FROM \"PayrollConfigurationFieldSources\" f WHERE f.\"PayrollConfigurationId\" = c.\"Id\")::text " +
            $"FROM \"PayrollConfigurations\" c WHERE c.\"Id\"='{id}';");
        // Every value of the version: interim, with a reference, under one of the two exceptions. COALESCE: a missing exception
        // or reference must be counted, not skipped by a NULL comparison.
        static string NotSourced(string reference) =>
            "(\"LegalStatus\" <> 'Interim' OR COALESCE(\"InterimExceptionId\", '') NOT IN ('" + TaxException + "','" + Law50Exception + "') OR COALESCE(\"" + reference + "\", '') = '')";
        var unclassified = await Count(
            "SELECT (SELECT count(*) FROM \"TaxBrackets\" WHERE \"PayrollConfigurationId\"='" + id + "' AND " + NotSourced("LegalReference") + ") + " +
            "(SELECT count(*) FROM \"StatutoryDeductionRules\" WHERE \"PayrollConfigurationId\"='" + id + "' AND " + NotSourced("LegalReference") + ") + " +
            "(SELECT count(*) FROM \"OfficialHolidays\" WHERE \"PayrollConfigurationId\"='" + id + "' AND " + NotSourced("LegalReference") + ") + " +
            "(SELECT count(*) FROM \"DisbursementPriorityRules\" WHERE \"PayrollConfigurationId\"='" + id + "' AND " + NotSourced("LegalSource") + ") + " +
            "(SELECT count(*) FROM \"PayrollConfigurationFieldSources\" WHERE \"PayrollConfigurationId\"='" + id + "' AND " + NotSourced("LegalReference") + ");");

        // 4. The cycle, by three people.
        await ActAsync(approver, "approve", id, "approve");
        var afterApproval = await VersionStateAsync(id);
        await ActAsync(attestor, "legal-attest", id, "legal-attest");
        var attestation = await Db(
            "SELECT \"Status\" || '|' || \"ContentHash\" || '|' || \"InterimExceptionIdsJson\" FROM \"LegalAttestations\" WHERE \"PayrollConfigurationId\"='" + id + "' AND \"Status\"='Active';");
        await ActAsync(approver, "publish", id, "publish");
        var published = await VersionStateAsync(id);
        var people = await Db(
            "SELECT (SELECT string_agg(DISTINCT e.\"UserId\"::text, ',') FROM \"PayrollConfigurationEditors\" e WHERE e.\"PayrollConfigurationId\" = c.\"Id\") || '|' || c.\"ApprovedBy\"::text || '|' || " +
            "(SELECT a.\"AttestedBy\"::text FROM \"LegalAttestations\" a WHERE a.\"PayrollConfigurationId\" = c.\"Id\" AND a.\"Status\"='Active') || '|' || c.\"PublishedBy\"::text " +
            $"FROM \"PayrollConfigurations\" c WHERE c.\"Id\"='{id}';");
        var hash = published.Split('|')[2];

        // 5. The first month.
        var run = await CreateAndCalculateAsync(processor, "54a", FirstMonth);
        var pin = await PinAsync(run);
        var (provenanceStatus, provenance, _) = await processor.SendAsync("provenance", HttpMethod.Get, $"api/payroll/runs/{run}/provenance");
        var lines = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}';");
        var statutoryOnALine = await Db(
            "SELECT COALESCE(string_agg(c.\"ComponentName\", ',' ORDER BY c.\"ComponentName\"), '') FROM \"SalaryLineComponents\" c " +
            "WHERE c.\"SalaryLineId\" = (SELECT \"Id\" FROM \"SalaryLines\" WHERE \"PayrollRunId\"='" + run + "' ORDER BY \"EmployeeId\" LIMIT 1) " +
            "AND c.\"ReferenceId\" IN (SELECT \"Id\" FROM \"StatutoryDeductionRules\" WHERE \"PayrollConfigurationId\"='" + id + "');");
        var jobLog = await Db($"SELECT COALESCE(max(\"Log\"), '') FROM \"PayrollRunJobs\" WHERE \"PayrollRunId\"='{run}';");
        var editors = people.Split('|')[0];
        var approvedBy = people.Split('|')[1];
        var attestedBy = people.Split('|')[2];

        await AuditAsync(s, id, run);
        s.Checks(new
        {
            malformedPack = (int)malformed,
            malformedPackErrorPath = malformedPath,
            noRowAfterTheMalformedPack = rowsAfterMalformed == rowsBefore,
            version = id,
            draft,
            draftFormat = "status|fingerprint schema|agency|effective from|brackets|statutory deductions|holidays|priority rules|field sources",
            valuesNotInterimOrWithoutAnException = unclassified,
            afterApproval,
            attestation,
            published,
            versionFormat = "status|active|fingerprint|approved by|published by|effective from|effective to|active attestations",
            threeDifferentPeople = !editors.Contains(approvedBy, StringComparison.Ordinal) && !editors.Contains(attestedBy, StringComparison.Ordinal) && approvedBy != attestedBy,
            run,
            runIsPinnedToTheVersion = pin == $"{id}|{hash}",
            provenanceConfigId = provenance.ValueKind == JsonValueKind.Object ? provenance.GetProperty("payrollConfigurationId").GetString() : null,
            provenanceInterimIndicator = Text(provenance, "interimIndicator"),
            lines,
            statutoryOnALine,
            jobLog
        });

        Assert.Equal(HttpStatusCode.BadRequest, malformed);
        Assert.Equal("$.taxBrackets[2].ratePercent", malformedPath);
        Assert.Equal(rowsBefore, rowsAfterMalformed);
        Assert.Equal($"Draft|2|{TenantA}|2031-01-01|5|4|4|3|19", draft);
        Assert.Equal(0, unclassified);
        Assert.StartsWith("Approved|false|", afterApproval, StringComparison.Ordinal);
        Assert.Equal(64, afterApproval.Split('|')[2].Length);
        Assert.StartsWith($"Active|{afterApproval.Split('|')[2]}|", attestation, StringComparison.Ordinal);
        Assert.Contains(TaxException, attestation, StringComparison.Ordinal);
        Assert.Contains(Law50Exception, attestation, StringComparison.Ordinal);
        Assert.StartsWith("Published|true|", published, StringComparison.Ordinal);
        Assert.EndsWith("|2031-01-01|∅|1", published, StringComparison.Ordinal);
        Assert.DoesNotContain(approvedBy, editors, StringComparison.Ordinal);
        Assert.DoesNotContain(attestedBy, editors, StringComparison.Ordinal);
        Assert.NotEqual(approvedBy, attestedBy);
        Assert.Equal(approvedBy, people.Split('|')[3]);
        Assert.Equal($"{id}|{hash}", pin);
        Assert.Equal(HttpStatusCode.OK, provenanceStatus);
        Assert.Equal(id, provenance.GetProperty("payrollConfigurationId").GetString());
        Assert.Equal(hash, provenance.GetProperty("configContentHash").GetString());
        Assert.StartsWith("إطار مؤقت — استثناء ", Text(provenance, "interimIndicator"), StringComparison.Ordinal);
        Assert.Contains(TaxException, Text(provenance, "interimIndicator"), StringComparison.Ordinal);
        Assert.True(lines > 0, "54a: the run of 2031-01 has no line.");
        Assert.Equal(4, statutoryOnALine.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("إطار مؤقت", jobLog, StringComparison.Ordinal);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 54b

    [SkippableFact]
    public async Task Ac54b_the_approver_does_not_attest_an_editor_neither_approves_nor_publishes_and_nothing_is_published_from_a_draft()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "54b",
            "AC-PAY-54b: a publication from a draft, an approval by an editor, an attestation by the approver and a publication by an editor each answer 400 with an Arabic message and change nothing.",
            Processor, Approver, Attestor, Admin);
        s.Steps("""
            1. payroll-processor imports the interim pack under the code LAW_50_2004_AC54B from 2031-01-01 → Draft. The agency's
               hr-admin (a-directory-checker, who holds Payroll.MasterData and Payroll.Approve) sets its effective date → he is an
               editor of the version, like the importer.
            2. Negative: payroll-approver publishes the draft → 400 «لا يُنشر إلا إصدار معتمد»; the version is as it was.
            3. Negative: the editor with the approval policy approves → 400 «لا يعتمد النسخة من أنشأها أو استوردها أو عدّلها».
               payroll-processor (the importer) posts the approval → 403: he holds no approval policy at all.
            4. payroll-approver approves → Approved.
            5. Negative: payroll-approver attests the version he approved → 400 «لا يصادق قانونياً على النسخة من اعتمدها»; no
               attestation is written. The editor attests → 400.
            6. a-payroll-approver-2 attests → an active attestation.
            7. Negative: the editor publishes → 400 «لا ينشر النسخة من أنشأها أو استوردها أو عدّلها»; the version stays Approved.
               It is left approved, attested and unpublished: it governs nothing.
            """);

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "54b");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "54b");
        using var attestor = await Api.OpenAsync(s.Recorder, Attestor, "54b");
        using var editor = await Api.OpenAsync(s.Recorder, Admin, "54b");
        const string code = Code + "B";
        var existing = await Count($"SELECT count(*) FROM \"PayrollConfigurations\" WHERE \"TenantId\"='{TenantA}' AND \"Code\"='{code}';");
        s.Recorder.WriteBefore(new { scenario = "54b", explicitDomainSnapshot = true, versionsOfTheCode = existing });
        AcceptanceGate.RequireData(s.Recorder, existing == 0,
            $"للجهة A نسخة سابقة بالرمز {code}: هذه الفئة تحتاج مكدساً جديداً", $"Agency A already holds a version of {code}: this class needs a fresh stack.");

        var id = await ImportDraftAsync(processor, "import", InterimPack(), code, FirstMonth);
        var (moved, _, movedText) = await editor.SendAsync("editor-sets-the-date", HttpMethod.Put, $"{Frameworks}/{id}/effective-from",
            new StringContent("{\"effectiveFrom\":\"2031-01-01\"}", Encoding.UTF8, "application/json"));
        Assert.True(moved == HttpStatusCode.OK, $"54b: the hr-admin could not edit the draft ({(int)moved}): {Snip(movedText)}");
        var editors = await Count($"SELECT count(DISTINCT \"UserId\") FROM \"PayrollConfigurationEditors\" WHERE \"PayrollConfigurationId\"='{id}';");

        var publishFromDraft = await RefusedAsync(approver, "publish-from-draft", id, "publish", "لا يُنشر إلا إصدار معتمد");
        var editorApproves = await RefusedAsync(editor, "editor-approves", id, "approve", "لا يعتمد النسخة من أنشأها أو استوردها أو عدّلها");
        var stateBeforeImporter = await VersionStateAsync(id);
        var (importerApproves, _, _) = await processor.PostAsync("importer-approves", $"{Frameworks}/{id}/approve");
        var stateAfterImporter = await VersionStateAsync(id);

        await ActAsync(approver, "approve", id, "approve");
        var approverAttests = await RefusedAsync(approver, "approver-attests", id, "legal-attest", "لا يصادق قانونياً على النسخة من اعتمدها");
        var editorAttests = await RefusedAsync(editor, "editor-attests", id, "legal-attest", "لا يصادق قانونياً على النسخة من أنشأها أو استوردها أو عدّلها");
        var attestationsAfterRefusals = await Count($"SELECT count(*) FROM \"LegalAttestations\" WHERE \"PayrollConfigurationId\"='{id}';");

        await ActAsync(attestor, "legal-attest", id, "legal-attest");
        var editorPublishes = await RefusedAsync(editor, "editor-publishes", id, "publish", "لا ينشر النسخة من أنشأها أو استوردها أو عدّلها");
        var final = await VersionStateAsync(id);

        await AuditAsync(s, id);
        s.Checks(new
        {
            version = id,
            editors,
            publishFromDraft,
            editorApproves,
            importerApprovesStatus = (int)importerApproves,
            versionUnchangedByTheImporter = stateBeforeImporter == stateAfterImporter,
            approverAttests,
            editorAttests,
            attestationsAfterRefusals,
            editorPublishes,
            final,
            versionFormat = "status|active|fingerprint|approved by|published by|effective from|effective to|active attestations"
        });

        Assert.Equal(2, editors);
        Assert.Equal(HttpStatusCode.Forbidden, importerApproves);
        Assert.Equal(stateBeforeImporter, stateAfterImporter);
        Assert.Equal(0, attestationsAfterRefusals);
        Assert.StartsWith("Approved|false|", final, StringComparison.Ordinal);
        Assert.EndsWith("|∅|2031-01-01|∅|1", final, StringComparison.Ordinal);          // not published, one active attestation
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 54c

    [SkippableFact]
    public async Task Ac54c_a_second_version_has_a_new_fingerprint_and_the_first_still_governs_its_month_and_the_general_table_moves_nothing()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "54c",
            "AC-PAY-54c: a second version of the framework changes a statutory percentage from 2031-02-01 and has a new fingerprint; the run of 2031-01 stays pinned to the first version; a statutory row put in the general deduction types table changes nothing in a new calculation of the second version.",
            Processor, Approver, Attestor);
        s.Steps("""
            1. Precondition, in before.json: 54a's version of LAW_50_2004_AC54 is Published and its run of 2031-01 is calculated
               and pinned to it. The run's result (the number of lines and an md5 of every component) is recorded.
            2. payroll-processor exports the version (GET api/payroll/frameworks/{id}/export), changes DED-SOC's employee share from
               7 to 8 in the pack, and imports it under the same code from 2031-02-01 → Draft, version 2. payroll-approver approves,
               a-payroll-approver-2 attests and publishes (the attestor may publish) → Published. Its fingerprint differs from the
               first version's; the first version is Superseded, ends on 2031-01-31, and keeps its fingerprint.
            3. The run of 2031-01, read again: pinned to the first version, with the same fingerprint and the same result.
            4. payroll-processor creates and calculates the run of 2031-02 → pinned to version 2; DED-SOC on a line is 8% of its
               gross pay.
            5. Negative: a statutory row is put in the general deduction types table for agency A, 50% of gross (test-only, psql:
               the table's screens no longer create a statutory type; an old database may hold one). payroll-processor creates and
               calculates the run of 2031-03, a new snapshot of the same version 2 (a calculated run is frozen and is not calculated
               again) → pinned to version 2, no component of any line comes from the general row, and the result equals February's
               to the unit: the same people, the same version, nothing read from the general table. The run of 2031-01, read once
               more, is as it was. The row is deleted at the end, whatever happens.
            """);

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "54c");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "54c");
        using var attestor = await Api.OpenAsync(s.Recorder, Attestor, "54c");

        var first = await Db($"SELECT COALESCE(max(\"Id\"::text), '') FROM \"PayrollConfigurations\" WHERE \"TenantId\"='{TenantA}' AND \"Code\"='{Code}' AND \"VersionNumber\"=1;");
        var firstState = first.Length == 0 ? "" : await VersionStateAsync(first);
        var january = await Db(
            $"SELECT COALESCE(max(\"Id\"::text), '') FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\"='2031-01-01' AND \"RunType\"='Regular' AND \"PayrollConfigurationId\"='{(first.Length == 0 ? Guid.Empty.ToString() : first)}';");
        var januaryResult = january.Length == 0 ? "" : await RunResultAsync(january);
        var januaryPin = january.Length == 0 ? "" : await PinAsync(january);
        s.Recorder.WriteBefore(new { scenario = "54c", explicitDomainSnapshot = true, firstVersion = first, firstState, january, januaryPin, januaryResult });
        AcceptanceGate.RequireData(s.Recorder, firstState.StartsWith("Published|true|", StringComparison.Ordinal) && january.Length > 0,
            "نسخة 54a ليست منشورة أو تشغيل 2031-01 غير موجود: شغّل 54a أولاً على مكدس جديد",
            "54a's version is not published or its run of 2031-01 is missing: 54c runs after 54a, on a fresh stack.");
        var firstHash = firstState.Split('|')[2];

        // 2. Version 2: the exported pack with DED-SOC at 8, from 2031-02-01.
        var (exported, _, exportedText) = await processor.SendAsync("export", HttpMethod.Get, $"{Frameworks}/{first}/export");
        Assert.True(exported == HttpStatusCode.OK, $"54c: the export answered {(int)exported}.");
        var pack = JsonNode.Parse(exportedText)!.AsObject();
        var soc = pack["statutoryDeductions"]!.AsArray().Single(d => d!["code"]!.GetValue<string>() == "DED-SOC")!;
        var socBefore = soc["employeePercent"]!.GetValue<decimal>();
        soc["employeePercent"] = 8;
        var second = await ImportDraftAsync(processor, "import-v2", pack.ToJsonString(), Code, SecondMonth);
        await ActAsync(approver, "approve-v2", second, "approve");
        await ActAsync(attestor, "legal-attest-v2", second, "legal-attest");
        await ActAsync(attestor, "publish-v2", second, "publish");
        var secondState = await VersionStateAsync(second);
        var firstAfter = await VersionStateAsync(first);
        var secondNumber = await Count($"SELECT \"VersionNumber\" FROM \"PayrollConfigurations\" WHERE \"Id\"='{second}';");

        // 3. January again.
        var januaryPinAfterV2 = await PinAsync(january);
        var januaryResultAfterV2 = await RunResultAsync(january);

        // 4. February is version 2's.
        var february = await CreateAndCalculateAsync(processor, "54c-february", SecondMonth);
        var februaryPin = await PinAsync(february);
        var socOnALine = await Db(
            "SELECT l.\"GrossPay\"::text || '|' || abs(c.\"Amount\")::text FROM \"SalaryLineComponents\" c JOIN \"SalaryLines\" l ON l.\"Id\" = c.\"SalaryLineId\" " +
            "WHERE l.\"PayrollRunId\"='" + february + "' AND c.\"ReferenceId\" = (SELECT \"Id\" FROM \"StatutoryDeductionRules\" WHERE \"PayrollConfigurationId\"='" + second + "' AND \"Code\"='DED-SOC') " +
            "ORDER BY l.\"EmployeeId\" LIMIT 1;");

        // 5. The general table moves nothing.
        var februaryResult = await RunResultAsync(february);
        var legacyRow = Guid.NewGuid().ToString("D");
        string januaryPinAfterEdit, januaryResultAfterEdit, march, marchPin, marchResult;
        long fromTheGeneralRow;
        try
        {
            await Db(
                "INSERT INTO \"DeductionTypes\" (\"Id\",\"TenantId\",\"CreatedAt\",\"UpdatedAt\",\"Code\",\"NameAr\",\"IsStatutory\",\"Percentage\") " +
                $"VALUES ('{legacyRow}','{TenantA}',now(),now(),'AC54-LEGACY-STAT','اقتطاع نظامي قديم لاختبار القبول',true,50);");
            march = await CreateAndCalculateAsync(processor, "54c-march", ThirdMonth);
            marchPin = await PinAsync(march);
            marchResult = await RunResultAsync(march);
            fromTheGeneralRow = await Count(
                "SELECT count(*) FROM \"SalaryLineComponents\" c JOIN \"SalaryLines\" l ON l.\"Id\" = c.\"SalaryLineId\" " +
                $"WHERE l.\"PayrollRunId\"='{march}' AND (c.\"ReferenceId\"='{legacyRow}' OR c.\"ComponentName\"='اقتطاع نظامي قديم لاختبار القبول');");
            januaryPinAfterEdit = await PinAsync(january);
            januaryResultAfterEdit = await RunResultAsync(january);
        }
        finally
        {
            await Db($"DELETE FROM \"DeductionTypes\" WHERE \"Id\"='{legacyRow}';");
        }

        var legacyRowLeft = await Count($"SELECT count(*) FROM \"DeductionTypes\" WHERE \"Id\"='{legacyRow}';");
        var gross = decimal.Parse(socOnALine.Split('|')[0], CultureInfo.InvariantCulture);
        var socAmount = decimal.Parse(socOnALine.Split('|')[1], CultureInfo.InvariantCulture);

        await AuditAsync(s, first, second, january, february, march);
        s.Checks(new
        {
            firstVersion = first,
            secondVersion = second,
            secondNumber,
            socEmployeePercentBefore = socBefore,
            socEmployeePercentAfter = 8,
            firstFingerprint = firstHash,
            secondFingerprint = secondState.Split('|')[2],
            firstAfter,
            secondState,
            versionFormat = "status|active|fingerprint|approved by|published by|effective from|effective to|active attestations",
            january,
            januaryPinBefore = januaryPin,
            januaryPinAfterV2,
            januaryPinAfterEdit,
            januaryResultBefore = januaryResult,
            januaryResultAfterV2,
            januaryResultAfterEdit,
            runResultFormat = "lines:md5(employee|kind|name|amount of every component)",
            february,
            februaryPin,
            februaryResult,
            march,
            marchPin,
            marchResult,
            componentsFromTheGeneralRowInMarch = fromTheGeneralRow,
            socOnALine,
            socOnALineFormat = "gross|DED-SOC amount",
            legacyRowLeft
        });

        Assert.Equal(7m, socBefore);
        Assert.Equal(2, secondNumber);
        Assert.StartsWith("Published|true|", secondState, StringComparison.Ordinal);
        Assert.EndsWith("|2031-02-01|∅|1", secondState, StringComparison.Ordinal);
        Assert.NotEqual(firstHash, secondState.Split('|')[2]);
        Assert.StartsWith($"Superseded|false|{firstHash}|", firstAfter, StringComparison.Ordinal);
        Assert.Contains("|2031-01-01|2031-01-31|", firstAfter, StringComparison.Ordinal);
        Assert.Equal($"{first}|{firstHash}", januaryPin);
        Assert.Equal(januaryPin, januaryPinAfterV2);
        Assert.Equal(januaryResult, januaryResultAfterV2);
        Assert.Equal($"{second}|{secondState.Split('|')[2]}", februaryPin);
        // 8% of gross pay, to the version's rounding: a unit of tolerance tells 8% from 7% on any salary.
        Assert.True(Math.Abs(gross * 8m / 100m - socAmount) <= 1m, $"54c: DED-SOC on a line of February is {socAmount} for a gross pay of {gross}; expected 8%.");
        Assert.Equal(februaryPin, marchPin);                                            // March is version 2's too
        Assert.Equal(0, fromTheGeneralRow);
        Assert.Equal(februaryResult, marchResult);                                      // a 50% row would have halved every net pay
        Assert.Equal(januaryPin, januaryPinAfterEdit);
        Assert.Equal(januaryResult, januaryResultAfterEdit);
        Assert.Equal(0, legacyRowLeft);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 54d

    [SkippableFact]
    public async Task Ac54d_an_interim_value_that_cites_an_exception_the_catalog_does_not_hold_stops_the_approval()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "54d",
            "AC-PAY-54d: a pack whose interim value cites an exception that does not exist is imported as a draft and refused at approval with a message that names the value and the exception.",
            Processor, Approver);
        s.Steps("""
            1. payroll-processor imports the interim pack, with DED-SOC citing the exception EX-AC54-NOT-IN-CATALOG, under the code
               LAW_50_2004_AC54D from 2031-01-01 → Draft (the import checks the form of an exception number; whether it is in the
               catalog and active is the approval's question).
            2. Negative: payroll-approver approves → 400; the message names the value («الخصم النظامي DED-SOC») and the exception
               («EX-AC54-NOT-IN-CATALOG»); the version stays a Draft with no fingerprint and no approver.
            """);

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "54d");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "54d");
        const string code = Code + "D";
        const string unknown = "EX-AC54-NOT-IN-CATALOG";
        var existing = await Count($"SELECT count(*) FROM \"PayrollConfigurations\" WHERE \"TenantId\"='{TenantA}' AND \"Code\"='{code}';");
        s.Recorder.WriteBefore(new { scenario = "54d", explicitDomainSnapshot = true, versionsOfTheCode = existing });
        AcceptanceGate.RequireData(s.Recorder, existing == 0,
            $"للجهة A نسخة سابقة بالرمز {code}: هذه الفئة تحتاج مكدساً جديداً", $"Agency A already holds a version of {code}: this class needs a fresh stack.");

        var id = await ImportDraftAsync(processor, "import", PackWith(p =>
            p["statutoryDeductions"]!.AsArray().Single(d => d!["code"]!.GetValue<string>() == "DED-SOC")!["interimExceptionId"] = unknown), code, FirstMonth);
        var refusal = await RefusedAsync(approver, "approve", id, "approve", unknown);
        var final = await VersionStateAsync(id);

        await AuditAsync(s, id);
        s.Checks(new { version = id, refusal, final, versionFormat = "status|active|fingerprint|approved by|published by|effective from|effective to|active attestations" });

        Assert.Contains("الخصم النظامي DED-SOC", refusal, StringComparison.Ordinal);
        Assert.Contains(unknown, refusal, StringComparison.Ordinal);
        Assert.Equal("Draft|false|∅|∅|∅|2031-01-01|∅|0", final);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 54e

    [SkippableFact]
    public async Task Ac54e_a_manual_allowance_waits_for_a_second_person_and_enters_no_calculation_before_that()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "54e",
            "AC-PAY-54e: a processor enters an allowance by hand; it waits and the month's run does not hold it; he cannot approve it; the approver approves it and the next month's run holds it. A posted run id on a manual deduction's form is ignored.",
            Processor, Approver);
        s.Steps("""
            1. Precondition, in before.json: an agency-A employee with an active pay profile and no manual input yet; an allowance
               type of agency A; a posted run of agency A (for the negative); no payroll run of 2031-06 or 2031-07 for agency A
               (the months this scenario takes for itself).
            2. payroll-processor opens the allowance form and enters an allowance for June and July 2031 with a reason → the list says
               «بانتظار الاعتماد»; the row is PendingApproval, of origin Manual, entered by him.
            3. payroll-processor creates and calculates the run of 2031-06 → the employee's line holds no component of the
               allowance.
            4. Negative: payroll-processor posts the approval of his own allowance → 403 (he holds no approval policy); the row is
               as it was.
            5. Negative: payroll-processor enters a deduction with a posted run's id in the field AppliedInRunId (and
               ApprovalStatus=Approved) → the row is PendingApproval with no run: the fields are not the form's and are ignored.
               payroll-approver rejects it with a reason → Rejected.
            6. payroll-approver approves the allowance from the list → Approved, by another person than its author.
            7. payroll-processor creates and calculates the next run, 2031-07 (June's run is calculated and frozen: it is not
               calculated again) → the employee's line holds the allowance, and his gross pay is June's plus its amount.
            """);

        var processorUi = s[Processor];
        var approverUi = s[Approver];
        using var processor = await Api.OpenAsync(s.Recorder, Processor, "54e");
        const string allowancesUi = "ui/payroll/AllowanceRecords";
        const string deductionsUi = "ui/payroll/DeductionRecords";
        const string reason = "قرار الوزير 54 لعام 2031 — بدل مهمة استثنائية";
        const decimal amount = 5432.10m;

        var employee = await Db(
            "SELECT COALESCE(max(p.\"EmployeeId\"::text), '') FROM \"EmployeePayProfiles\" p WHERE p.\"TenantId\"='" + TenantA + "' AND p.\"IsActive\" " +
            "AND NOT EXISTS (SELECT 1 FROM \"AllowanceRecords\" a WHERE a.\"EmployeeId\" = p.\"EmployeeId\" AND a.\"Origin\"='Manual') " +
            "AND NOT EXISTS (SELECT 1 FROM \"DeductionRecords\" d WHERE d.\"EmployeeId\" = p.\"EmployeeId\" AND d.\"Origin\"='Manual');");
        var type = await Db($"SELECT COALESCE(min(\"Id\"::text), '') FROM \"CompensationTypes\" WHERE \"TenantId\"='{TenantA}';");
        var postedRun = await Db($"SELECT COALESCE(max(\"Id\"::text), '') FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"=2;");
        var runsOfJune = await Count(
            $"SELECT count(*) FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"PeriodStart\" <= '2031-07-31' AND \"PeriodEnd\" >= '2031-06-01';");
        s.Recorder.WriteBefore(new { scenario = "54e", explicitDomainSnapshot = true, employee, allowanceType = type, postedRun, runsOf2031_06_and_07 = runsOfJune });
        AcceptanceGate.RequireData(s.Recorder, employee.Length > 0 && type.Length > 0 && postedRun.Length > 0,
            "لا موظف بملف أجر فعّال بلا مدخل يدوي، أو لا نوع بدل، أو لا تشغيل مرحّل للجهة A",
            "No agency-A employee with an active pay profile and no manual input, or no allowance type, or no posted run.");
        AcceptanceGate.RequireData(s.Recorder, runsOfJune == 0,
            "للجهة A تشغيل رواتب في 2031-06 أو 2031-07", "Agency A already holds a payroll run of 2031-06 or 2031-07: this class needs a fresh stack.");

        // 2. The allowance, by hand.
        var (entered, enteredBody) = await processorUi.PostWithStatusAsync("54e-enter-allowance", $"{allowancesUi}/Create", $"{allowancesUi}/Create", F(
            ("EmployeeId", employee), ("AllowanceTypeId", type), ("Amount", "5432.10"), ("EffectiveFrom", "2031-06-01"), ("EffectiveTo", "2031-07-31"),
            ("Reason", reason)));
        var allowance = await Db($"SELECT COALESCE(max(\"Id\"::text), '') FROM \"AllowanceRecords\" WHERE \"EmployeeId\"='{employee}' AND \"Reason\"='{reason}';");
        Assert.True(allowance.Length > 0, $"54e: the allowance was not entered ({(int)entered}): {Snip(enteredBody)}");
        const string allowanceRow =
            "SELECT \"ApprovalStatus\" || '|' || \"Origin\" || '|' || (\"CreatedBy\" IS NOT NULL)::text || '|' || COALESCE(\"ApprovedBy\"::text,'∅') || '|' || \"Amount\"::text FROM \"AllowanceRecords\" WHERE \"Id\"='";
        var afterEntry = await Db(allowanceRow + allowance + "';");
        var list = await processorUi.GetAsync("54e-list", allowancesUi);

        // 3. The month's calculation does not hold it.
        var run = await CreateAndCalculateAsync(processor, "54e", ManualInputMonth);
        const string lineSql =
            "SELECT l.\"GrossPay\"::text || '|' || (SELECT count(*) FROM \"SalaryLineComponents\" c WHERE c.\"SalaryLineId\" = l.\"Id\" AND c.\"ReferenceId\"='";
        var lineBefore = await Db(lineSql + allowance + "')::text FROM \"SalaryLines\" l WHERE l.\"PayrollRunId\"='" + run + "' AND l.\"EmployeeId\"='" + employee + "';");

        // 4. His own approval.
        var (own, _) = await processorUi.PostWithStatusAsync("54e-own-approve", $"{allowancesUi}/Approve/{allowance}", $"{allowancesUi}/Create", F());
        var afterOwn = await Db(allowanceRow + allowance + "';");

        // 5. A deduction with a posted run id on the form.
        const string deductionReason = "استرداد مبلغ صُرف خطأً — AC-PAY-54e";
        await processorUi.PostWithStatusAsync("54e-enter-deduction", $"{deductionsUi}/Create", $"{deductionsUi}/Create", F(
            ("EmployeeId", employee), ("DeductionType", "Other"), ("Amount", "123.45"), ("Reason", deductionReason),
            ("AppliedInRunId", postedRun), ("ApprovalStatus", "Approved"), ("Origin", "System")));
        var deduction = await Db($"SELECT COALESCE(max(\"Id\"::text), '') FROM \"DeductionRecords\" WHERE \"EmployeeId\"='{employee}' AND \"Reason\"='{deductionReason}';");
        Assert.True(deduction.Length > 0, "54e: the deduction was not entered.");
        var deductionRow = await Db(
            $"SELECT \"ApprovalStatus\" || '|' || \"Origin\" || '|' || COALESCE(\"AppliedInRunId\"::text,'∅') FROM \"DeductionRecords\" WHERE \"Id\"='{deduction}';");
        var (rejected, _) = await approverUi.PostWithStatusAsync("54e-reject-deduction", $"{deductionsUi}/Reject/{deduction}", deductionsUi, F(("note", "لا سند للاسترداد")));
        var deductionAfter = await Db($"SELECT \"ApprovalStatus\" || '|' || COALESCE(\"DecisionNote\",'') FROM \"DeductionRecords\" WHERE \"Id\"='{deduction}';");

        // 6. The second person approves the allowance.
        var approverList = await approverUi.GetAsync("54e-approver-list", allowancesUi);
        var (approved, approvedBody) = await approverUi.PostWithStatusAsync("54e-approve", $"{allowancesUi}/Approve/{allowance}", allowancesUi, F());
        var afterApproval = await Db(
            "SELECT \"ApprovalStatus\" || '|' || \"Origin\" || '|' || (\"ApprovedBy\" IS NOT NULL AND \"ApprovedBy\" <> \"CreatedBy\")::text FROM \"AllowanceRecords\" WHERE \"Id\"='" + allowance + "';");

        // 7. The next run holds it; June's, frozen, still does not.
        var nextRun = await CreateAndCalculateAsync(processor, "54e-next-month", NextMonth);
        var lineAfter = await Db(lineSql + allowance + "')::text FROM \"SalaryLines\" l WHERE l.\"PayrollRunId\"='" + nextRun + "' AND l.\"EmployeeId\"='" + employee + "';");
        var juneAgain = await Db(lineSql + allowance + "')::text FROM \"SalaryLines\" l WHERE l.\"PayrollRunId\"='" + run + "' AND l.\"EmployeeId\"='" + employee + "';");
        var grossBefore = decimal.Parse(lineBefore.Split('|')[0], CultureInfo.InvariantCulture);
        var grossAfter = decimal.Parse(lineAfter.Split('|')[0], CultureInfo.InvariantCulture);

        await AuditAsync(s, allowance, deduction, run, nextRun);
        s.Checks(new
        {
            employee,
            allowance,
            afterEntry,
            allowanceFormat = "approval status|origin|has an author|approved by|amount",
            listSaysPending = list.Contains("بانتظار الاعتماد", StringComparison.Ordinal),
            run,
            lineBefore,
            lineFormat = "gross|components of the allowance",
            ownApproval = (int)own,
            afterOwn,
            deduction,
            deductionRow,
            deductionFormat = "approval status|origin|applied in run",
            postedRunSentOnTheForm = postedRun,
            rejected = (int)rejected,
            deductionAfter,
            approverSeesTheApprovalForm = approverList.Contains($"AllowanceRecords/Approve/{allowance}", StringComparison.OrdinalIgnoreCase),
            approved = (int)approved,
            afterApproval,
            nextRun,
            lineAfter,
            juneAgain,
            grossRoseBy = grossAfter - grossBefore
        });

        Assert.Equal("PendingApproval|Manual|true|∅|5432.10", afterEntry);
        Assert.Contains("بانتظار الاعتماد", list, StringComparison.Ordinal);
        Assert.EndsWith("|0", lineBefore, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, own);
        Assert.Equal(afterEntry, afterOwn);
        Assert.Equal("PendingApproval|Manual|∅", deductionRow);
        Assert.True((int)rejected < 400, $"54e: the rejection answered {(int)rejected}.");
        Assert.Equal("Rejected|لا سند للاسترداد", deductionAfter);
        Assert.Contains($"AllowanceRecords/Approve/{allowance}", approverList, StringComparison.OrdinalIgnoreCase);
        Assert.True((int)approved < 400, $"54e: the approval answered {(int)approved}: {Snip(approvedBody)}");
        Assert.Equal("Approved|Manual|true", afterApproval);
        Assert.EndsWith("|1", lineAfter, StringComparison.Ordinal);
        Assert.Equal(amount, grossAfter - grossBefore);
        Assert.Equal(lineBefore, juneAgain);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 54f

    [SkippableFact]
    public async Task Ac54f_a_version_is_not_published_into_a_posted_period_of_its_regime()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "54f",
            "AC-PAY-54f: a version whose effective date falls in a period that already has a posted run is approved and attested, and its publication answers 400 (D-PAY-13).",
            Processor, Approver, Attestor);
        s.Steps("""
            1. Precondition, in before.json: agency A holds a posted payroll run whose period ends on or after 2026-03-01 (the seed's
               posted Regular runs; they are pinned to no version, so they count for every regime).
            2. payroll-processor imports the interim pack under the code LAW_50_2004_AC54F from 2026-03-01 → Draft.
               payroll-approver approves; a-payroll-approver-2 attests.
            3. Negative: payroll-approver publishes → 400; the message names the date and the posted period's end; the version stays
               Approved, attested and unpublished, and no version of agency A changed.
            """);

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "54f");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "54f");
        using var attestor = await Api.OpenAsync(s.Recorder, Attestor, "54f");
        const string code = Code + "F";
        var from = new DateOnly(2026, 3, 1);
        var existing = await Count($"SELECT count(*) FROM \"PayrollConfigurations\" WHERE \"TenantId\"='{TenantA}' AND \"Code\"='{code}';");
        var latestPosted = await Db(
            "SELECT COALESCE(to_char(max(r.\"PeriodEnd\"), 'YYYY-MM-DD'), '') FROM \"PayrollRuns\" r WHERE r.\"TenantId\"='" + TenantA + "' AND r.\"Status\"=2 " +
            "AND (r.\"PayrollConfigurationId\" IS NULL OR r.\"PayrollConfigurationId\" IN (SELECT \"Id\" FROM \"PayrollConfigurations\" WHERE \"RegimeCode\"='LAW_50_2004'));");
        const string othersSql =
            "SELECT md5(COALESCE(string_agg(\"Id\"::text || \"PublicationStatus\" || \"IsActive\"::text || COALESCE(\"EffectiveTo\"::text,''), ';' ORDER BY \"Id\"), '')) " +
            "FROM \"PayrollConfigurations\" WHERE \"Code\" <> '";
        var othersBefore = await Db(othersSql + code + $"' AND \"TenantId\"='{TenantA}';");
        s.Recorder.WriteBefore(new { scenario = "54f", explicitDomainSnapshot = true, versionsOfTheCode = existing, latestPostedPeriodEnd = latestPosted, effectiveFrom = Day(from) });
        AcceptanceGate.RequireData(s.Recorder, existing == 0,
            $"للجهة A نسخة سابقة بالرمز {code}: هذه الفئة تحتاج مكدساً جديداً", $"Agency A already holds a version of {code}: this class needs a fresh stack.");
        AcceptanceGate.RequireData(s.Recorder, string.CompareOrdinal(latestPosted, Day(from)) >= 0,
            "لا تشغيل مرحّل للجهة A تنتهي فترته في 2026-03-01 أو بعده", "Agency A holds no posted run whose period ends on or after 2026-03-01.");

        var id = await ImportDraftAsync(processor, "import", InterimPack(), code, from);
        await ActAsync(approver, "approve", id, "approve");
        await ActAsync(attestor, "legal-attest", id, "legal-attest");
        var refusal = await RefusedAsync(approver, "publish", id, "publish", "تشغيل رواتب مرحّل");
        var final = await VersionStateAsync(id);
        var othersAfter = await Db(othersSql + code + $"' AND \"TenantId\"='{TenantA}';");

        await AuditAsync(s, id);
        s.Checks(new
        {
            version = id,
            latestPostedPeriodEnd = latestPosted,
            refusal,
            final,
            versionFormat = "status|active|fingerprint|approved by|published by|effective from|effective to|active attestations",
            otherVersionsUnchanged = othersBefore == othersAfter
        });

        Assert.Contains("2026-03-01", refusal, StringComparison.Ordinal);
        Assert.Contains(latestPosted, refusal, StringComparison.Ordinal);
        Assert.StartsWith("Approved|false|", final, StringComparison.Ordinal);
        Assert.EndsWith("|∅|2026-03-01|∅|1", final, StringComparison.Ordinal);
        Assert.Equal(othersBefore, othersAfter);
        s.Recorder.WriteResult("Pass");
    }
}
