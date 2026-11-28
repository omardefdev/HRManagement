using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-60 (AC-SEP-60): one persona's gateway session as a browser keeps it — its cookies, so the flash message of a redirect reaches
/// the page it lands on — and the posts a person makes: the form a page renders (<c>data-sep-action</c>), or an action posted with the
/// token of a page the person can open (a refusal is the server's, not a missing button's).
/// </summary>
internal sealed class ScreenSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly EvidenceRecorder _recorder;

    private ScreenSession(HttpClient http, EvidenceRecorder recorder, string persona)
    {
        _http = http;
        _recorder = recorder;
        Persona = persona;
    }

    public string Persona { get; }

    public static async Task<ScreenSession> OpenAsync(string persona, EvidenceRecorder recorder) =>
        new(await AcS2s42TrustBoundaryTests.UiClientAsync(persona), recorder, persona);

    public async Task<ScreenPage> GetAsync(string step, string path)
    {
        using var response = await _http.GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        _recorder.CaptureHttp($"{step}-{Persona}", "GET", "/" + path.TrimStart('/'), (int)response.StatusCode);
        return new ScreenPage(response.StatusCode, response.Headers.Location?.OriginalString, html, AcS2s42TrustBoundaryTests.Alerts(html));
    }

    /// <summary>
    /// Posts <paramref name="fields"/> to <paramref name="path"/> with the antiforgery token <paramref name="tokenPage"/> renders. With
    /// <paramref name="requireToken"/> false, a page the persona cannot open sends no token (the refusal under test comes first).
    /// </summary>
    public async Task<ScreenPage> PostAsync(string step, string tokenPage, string path, IReadOnlyDictionary<string, string>? fields = null,
        bool requireToken = true)
    {
        string? token = null;
        using (var page = await _http.GetAsync(tokenPage))
        {
            var raw = await page.Content.ReadAsStringAsync();
            if (page.StatusCode == HttpStatusCode.OK)
                token = AcMsg40AtomicPublishingTests.RequireAntiforgeryToken(raw);
            else
                Assert.False(requireToken, $"GET {tokenPage} as {Persona}: {(int)page.StatusCode}");
            _recorder.CaptureHttp($"{step}-token-{Persona}", "GET", "/" + tokenPage.TrimStart('/'), (int)page.StatusCode);
        }

        return await SendAsync(step, path, fields, token);
    }

    /// <summary>The form <paramref name="page"/> renders for <paramref name="sepAction"/> (and the item's step), posted as rendered.</summary>
    public async Task<ScreenPage> PostRenderedAsync(string step, string page, string sepAction, string? item = null,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var form = await FindRenderedAsync(page, sepAction, item);
        Assert.True(form is not null, $"{page} renders no {sepAction}{(item is null ? "" : $" ({item})")} form for {Persona}.");
        var (url, hidden) = form!.Value;
        foreach (var (key, value) in fields ?? new Dictionary<string, string>())
            hidden[key] = value;
        return await SendAsync(step, url, hidden, token: null);
    }

    /// <summary>Whether <paramref name="page"/> renders the <paramref name="sepAction"/> form for this persona.</summary>
    public async Task<bool> RendersAsync(string page, string sepAction, string? item = null) =>
        await FindRenderedAsync(page, sepAction, item) is not null;

    private async Task<(string Url, Dictionary<string, string> Fields)?> FindRenderedAsync(string page, string sepAction, string? item)
    {
        using var response = await _http.GetAsync(page);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {page} as {Persona}: {(int)response.StatusCode}");
        foreach (Match form in Regex.Matches(raw, @"<form\b[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var open = Regex.Match(form.Value, @"<form\b[^>]*>", RegexOptions.IgnoreCase).Value;
            if (!open.Contains($"data-sep-action=\"{sepAction}\"", StringComparison.Ordinal))
                continue;
            if (item is not null && !open.Contains($"data-sep-form-step=\"{item}\"", StringComparison.Ordinal))
                continue;

            // \s: the panel's own data-sep-action="..." also contains 'action="' (the tag helper writes the real one after it).
            var url = WebUtility.HtmlDecode(Regex.Match(open, @"\saction=""([^""]+)""").Groups[1].Value);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match input in Regex.Matches(form.Value, @"<input\b[^>]*type=""hidden""[^>]*>", RegexOptions.IgnoreCase))
            {
                var name = Regex.Match(input.Value, @"name=""([^""]+)""");
                if (name.Success)
                    fields[name.Groups[1].Value] = WebUtility.HtmlDecode(Regex.Match(input.Value, @"value=""([^""]*)""").Groups[1].Value);
            }

            return (url, fields);
        }

        return null;
    }

    private async Task<ScreenPage> SendAsync(string step, string path, IReadOnlyDictionary<string, string>? fields, string? token)
    {
        var form = new Dictionary<string, string>(fields ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (token is not null)
            form["__RequestVerificationToken"] = token;
        var target = path.StartsWith('/') ? path : "/" + path;
        using var posted = await _http.PostAsync(target, new FormUrlEncodedContent(form));
        var status = posted.StatusCode;
        var location = posted.Headers.Location?.OriginalString;
        var html = WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync());
        if ((int)status is >= 300 and < 400 && location is not null)
        {
            using var landed = await _http.GetAsync(location);
            html = WebUtility.HtmlDecode(await landed.Content.ReadAsStringAsync());
        }

        var alerts = AcS2s42TrustBoundaryTests.Alerts(html);
        _recorder.CaptureHttp($"{step}-{Persona}", "POST", target, (int)status, new
        {
            fields = form.Keys.Where(k => k != "__RequestVerificationToken").Select(k => $"{k}={form[k]}"),
            location,
            alerts
        });
        return new ScreenPage(status, location, html, alerts);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>A post's own status and redirect, and the page it lands on (HTML-decoded) with its alert boxes.</summary>
internal sealed record ScreenPage(HttpStatusCode Status, string? Location, string Html, string Alerts)
{
    public bool Says(string text) => Html.Contains(text, StringComparison.Ordinal);
}

/// <summary>One AC-SEP-60 scenario's evidence, in the shared folder: the eight elements of each scenario under its key.</summary>
internal sealed class Sep60Scenario
{
    private Sep60Scenario(EvidenceRecorder recorder, string key)
    {
        Recorder = recorder;
        Key = key;
    }

    public EvidenceRecorder Recorder { get; }

    public string Key { get; }

    public static async Task<Sep60Scenario> StartAsync(string key, string scenario, params string[] personas)
    {
        AcceptanceEnv.RequireEnabled();
        var first = AcceptanceHarness.RequirePersona(personas[0]);
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(first);
        var recorder = new EvidenceRecorder(SeparationJourney.Ac, correlationId) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, string.Join('+', personas));
        AcceptanceGate.RequireEnabled(recorder);
        foreach (var id in personas.Append(AcceptanceLeaverFixture.TemplatePersona))
        {
            var persona = AcceptanceHarness.LoadPersonas().FirstOrDefault(p => p.Id == id && p.Enabled);
            AcceptanceGate.RequireData(recorder, persona is not null,
                $"الشخصية {id} غير موجودة في personas.json", $"Persona {id} is missing from personas.json (run scripts/prepare-keycloak-import.ps1).");
        }

        return new Sep60Scenario(recorder, key);
    }

    public void Before(object snapshot) => Recorder.WriteBefore(new { scenario = Key, snapshot });

    /// <summary>Per-scenario steps file; the folder's steps.md concatenates them.</summary>
    public void Steps(string markdown)
    {
        Recorder.WriteScenarioSteps(markdown);
        var combined = Directory.GetFiles(Recorder.Folder, "steps-*.md")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"## {Path.GetFileNameWithoutExtension(f)[6..]}\n\n{File.ReadAllText(f)}");
        Recorder.WriteSteps(string.Join("\n\n", combined));
    }

    /// <summary>audit.json: HR.Contracts' audit rows of the scenario's records, decisions and contracts (action, user, time).</summary>
    public async Task AuditAsync(params Guid?[] entityIds)
    {
        var ids = string.Join(",", entityIds.OfType<Guid>().Select(id => $"'{id:D}'"));
        var rows = ids.Length == 0 ? "" : await SeparationJourney.ReadAsync(SeparationJourney.Contracts,
            "SELECT COALESCE(string_agg(\"EntityType\" || '|' || \"EntityId\" || '|' || \"Action\" || '|' || COALESCE(\"UserId\"::text,'') || '|' || " +
            $"to_char(\"CreatedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS'), E'\\n' ORDER BY \"CreatedAt\"), '') FROM \"AuditLogs\" WHERE \"EntityId\" IN ({ids});");
        Recorder.WriteAudit(new
        {
            scenario = Key,
            explicitAuditQuery = true,
            source = "hr_contracts_db.AuditLogs",
            entityIds = entityIds.OfType<Guid>(),
            rows = rows.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        });
    }

    /// <summary>after.json and db-checks-&lt;key&gt;.json (and db-checks.json for the folder), and the timing.</summary>
    public void Checks(object checks)
    {
        Recorder.WriteAfter(new { scenario = Key, checks });
        Recorder.WriteScenarioDbChecks(checks);
        Recorder.WriteDbChecks(new { lastScenario = Key, checks });
        Recorder.FlushTiming();
    }
}

/// <summary>
/// PLAN-60 (AC-SEP-60): a separation by the screens — HR.Contracts' pages for the record, HR.Core's inbox for its approval. psql only
/// reads (the ids a redirect does not carry, the states the assertions name); no step writes SQL.
/// </summary>
internal static class SeparationJourney
{
    public const string Ac = "AC-SEP-60";
    public const string Contracts = "hr_contracts_db";
    public const string Core = "hr_core_db";
    public const string Payroll = "payroll_db";

    public const string Maker = "a-contracts-maker";
    public const string Checker = "a-contracts-checker";
    public const string CoreStep = "central-a";
    public const string Management = "a-management-approver";
    public const string ComplianceStep = "a-compliance";

    public const string StandardCode = "SEPARATION-APPROVAL";
    public const string SensitiveCode = "SEPARATION-APPROVAL-SENSITIVE";
    public const string CleanupReason = "AC-SEP-60: نسخة اختبار لا تُنفَّذ، تُلغى في نهاية السيناريو";

    private static readonly TimeSpan OutcomeWindow = TimeSpan.FromSeconds(30);

    public static string Details(Guid record) => $"ui/hr-contracts/SeparationRecords/Details/{record:D}";

    public static string ActionPath(string action, Guid record) => $"ui/hr-contracts/SeparationRecords/{action}/{record:D}";

    public static async Task<string> ReadAsync(string database, string sql) =>
        (await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql)).Trim();

    public static Task<long> CountAsync(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);

    /// <summary>Today in Damascus (D-SEP-6: the execution's date is the capital's, as the server reads it).</summary>
    public static DateOnly TodayInDamascus()
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Damascus");
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.CreateCustomTimeZone("Damascus", TimeSpan.FromHours(3), "Damascus", "Damascus");
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    /// <summary>The persona's user id (the token's subject): the actor HR.Contracts records.</summary>
    public static async Task<Guid> PersonaIdAsync(string persona)
    {
        var (token, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona(persona));
        return Guid.Parse(AcceptanceHarness.ParseJwt(token).Subject);
    }

    /// <summary>The record's actors and reasons: created|submitted|decided|executed by, and the rejection reason.</summary>
    public static async Task<(string CreatedBy, string SubmittedBy, string DecidedBy, string ExecutedBy, string RejectionReason)> ActorsAsync(Guid record)
    {
        var row = await ReadAsync(Contracts,
            "SELECT COALESCE(\"CreatedBy\"::text,'') || '|' || COALESCE(\"SubmittedBy\"::text,'') || '|' || COALESCE(\"DecidedBy\"::text,'') || '|' || " +
            $"COALESCE(\"ExecutedBy\"::text,'') || '|' || COALESCE(\"RejectionReason\",'') FROM \"SeparationRecords\" WHERE \"Id\"='{record:D}';");
        var parts = row.Split('|', 5);
        return (parts[0], parts[1], parts[2], parts[3], parts.ElementAtOrDefault(4) ?? "");
    }

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static Dictionary<string, string> RecordFields(Guid employee, Guid? contract, string type, DateOnly effective, string reason) => new()
    {
        ["EmployeeId"] = employee.ToString("D"),
        ["ContractId"] = contract?.ToString("D") ?? "",
        ["SeparationType"] = type,
        ["EffectiveDate"] = Iso(effective),
        ["Reason"] = reason
    };

    /// <summary>The maker's record on <c>SeparationRecords/Create</c>; its id (the create redirects to the list) read back by the copy.</summary>
    public static async Task<Guid> CreateAsync(ScreenSession maker, LeaverCopy copy, string type, DateOnly effective, string step = "create",
        ICollection<Guid>? track = null)
    {
        var page = await maker.PostAsync(step, "ui/hr-contracts/SeparationRecords/Create", "ui/hr-contracts/SeparationRecords/Create",
            RecordFields(copy.EmployeeId, copy.ContractId, type, effective, $"AC-SEP-60 {copy.Label}"));
        var id = await OpenRecordOfAsync(copy.EmployeeId);
        Assert.True(id is not null, $"No separation record for {copy.Label}: {(int)page.Status} {page.Alerts} {ValidationErrors(page.Html)}");
        // The isolation rule: the record is known to the cleanup from its creation, whatever fails after.
        track?.Add(id!.Value);
        return id!.Value;
    }

    public static async Task<Guid?> OpenRecordOfAsync(Guid employee)
    {
        var id = await ReadAsync(Contracts,
            $"SELECT \"Id\" FROM \"SeparationRecords\" WHERE \"EmployeeId\"='{employee:D}' AND \"Status\"<>'Cancelled' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        return Guid.TryParse(id, out var record) ? record : null;
    }

    public static Task<long> RecordsOfAsync(Guid employee) =>
        CountAsync(Contracts, $"SELECT count(*) FROM \"SeparationRecords\" WHERE \"EmployeeId\"='{employee:D}' AND \"Status\"<>'Cancelled';");

    /// <summary>The validation summary of a form that came back (a refusal of Create or Edit).</summary>
    public static string ValidationErrors(string html) =>
        string.Join(" | ", Regex.Matches(html, @"validation-summary-errors[^>]*>(.*?)</div>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", " ").Trim()));

    public static async Task<(string Status, Guid? Instance)> StateAsync(Guid record)
    {
        var row = await ReadAsync(Contracts,
            $"SELECT \"Status\" || '|' || COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"SeparationRecords\" WHERE \"Id\"='{record:D}';");
        var parts = row.Split('|');
        return (parts[0], parts.Length > 1 && Guid.TryParse(parts[1], out var instance) ? instance : null);
    }

    public static async Task<string> StatusAsync(Guid record) => (await StateAsync(record)).Status;

    /// <summary>Polls the record's status until it is one of <paramref name="expected"/> or the window closes; answers the last read.</summary>
    public static async Task<string> PollStatusAsync(Guid record, TimeSpan window, params string[] expected)
    {
        var deadline = DateTime.UtcNow + window;
        var status = await StatusAsync(record);
        while (!expected.Contains(status, StringComparer.Ordinal) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            status = await StatusAsync(record);
        }

        return status;
    }

    /// <summary>HR.Core's instance: its status, its current step and its code.</summary>
    public static async Task<(string Status, int Step, string Code)> InstanceAsync(Guid instance)
    {
        var row = await ReadAsync(Core,
            "SELECT i.\"Status\"::text || '|' || COALESCE(i.\"CurrentStepLevel\"::text,'') || '|' || d.\"Code\" FROM \"WorkflowInstances\" i " +
            $"JOIN \"WorkflowDefinitions\" d ON d.\"Id\"=i.\"DefinitionId\" WHERE i.\"Id\"='{instance:D}';");
        var parts = row.Split('|');
        Assert.True(parts.Length == 3, $"No workflow instance {instance:D}: '{row}'");
        return (parts[0], int.TryParse(parts[1], out var step) ? step : 0, parts[2]);
    }

    /// <summary>The maker submits from the record's page → PendingApproval with an engine instance.</summary>
    public static async Task<Guid> SubmitAsync(ScreenSession maker, Guid record, string step = "submit")
    {
        var page = await maker.PostRenderedAsync(step, Details(record), "submit");
        var (status, instance) = await StateAsync(record);
        Assert.True(status == "PendingApproval" && instance is not null, $"Submit: {status} {page.Alerts}");
        return instance!.Value;
    }

    /// <summary>The two steps of the standard chain, each from the holder's inbox: central-a (hr-core), then the management approver.</summary>
    public static async Task ApproveStandardAsync(Guid instance, EvidenceRecorder recorder)
    {
        await CustodyJourney.ApproveFromInboxAsync(CoreStep, instance, recorder);
        Assert.Equal(2, (await InstanceAsync(instance)).Step);
        await CustodyJourney.ApproveFromInboxAsync(Management, instance, recorder);
    }

    /// <summary>The outcome reaches the record (HR.Core's outbox, then HR.Contracts' consumer) within the window; answers the delay.</summary>
    public static async Task<long> AwaitStatusAsync(Guid record, string expected, TimeSpan? window = null)
    {
        var watch = Stopwatch.StartNew();
        var status = await PollStatusAsync(record, window ?? OutcomeWindow, expected);
        Assert.True(status == expected, $"The record is {status}, not {expected}, after {watch.Elapsed.TotalSeconds:0}s.");
        return watch.ElapsedMilliseconds;
    }

    /// <summary>A record created, submitted and approved through both standard steps: Approved.</summary>
    public static async Task<(Guid Record, Guid Instance)> ApprovedAsync(ScreenSession maker, LeaverCopy copy, DateOnly effective,
        EvidenceRecorder recorder, ICollection<Guid> track, string type = "Resignation")
    {
        var record = await CreateAsync(maker, copy, type, effective, track: track);
        var instance = await SubmitAsync(maker, record);
        await ApproveStandardAsync(instance, recorder);
        await AwaitStatusAsync(record, "Approved");
        return (record, instance);
    }

    /// <summary>The checker issues the decision from the record's page; answers its number.</summary>
    public static async Task<string> IssueDecisionAsync(ScreenSession checker, Guid record)
    {
        var page = await checker.PostRenderedAsync("issue-decision", Details(record), "issue-decision");
        var number = await DecisionNumberAsync(record);
        Assert.True(number.Length > 0, $"No decision: {page.Alerts}");
        return number;
    }

    public static Task<string> DecisionNumberAsync(Guid record) =>
        ReadAsync(Contracts,
            "SELECT COALESCE(d.\"DecisionNumber\",'') FROM \"SeparationRecords\" r LEFT JOIN \"AdministrativeDecisions\" d ON d.\"Id\"=r.\"DecisionId\" " +
            $"WHERE r.\"Id\"='{record:D}';");

    /// <summary>A manual item completed on the page with its evidence reference.</summary>
    public static Task<ScreenPage> CompleteItemAsync(ScreenSession session, Guid record, string item, string evidence) =>
        session.PostRenderedAsync($"complete-{item}", Details(record), "complete", item,
            new Dictionary<string, string> { ["evidenceRef"] = evidence, ["notes"] = "AC-SEP-60" });

    /// <summary>An item's state: completed|excepted|requested-by|approved-by.</summary>
    public static Task<string> ItemAsync(Guid record, string item) =>
        ReadAsync(Contracts,
            "SELECT \"IsCompleted\"::text || '|' || (\"ExceptionApprovedBy\" IS NOT NULL)::text || '|' || COALESCE(\"ExceptionRequestedBy\"::text,'') || '|' || " +
            "COALESCE(\"ExceptionApprovedBy\"::text,'') || '|' || COALESCE(\"EvidenceRef\",'') FROM \"SeparationChecklists\" " +
            $"WHERE \"SeparationRecordId\"='{record:D}' AND \"Step\"='{item}';");

    /// <summary>The execution, posted with the record page's token (a refusal is the server's, whether or not the page offers it).</summary>
    public static Task<ScreenPage> ExecuteAsync(ScreenSession session, Guid record, string step = "execute") =>
        session.PostAsync(step, Details(record), ActionPath("Execute", record));

    /// <summary>The contract the copy holds: status|end date.</summary>
    public static Task<string> ContractAsync(Guid contract) =>
        ReadAsync(Contracts, $"SELECT \"Status\"::text || '|' || COALESCE(\"EndDate\"::text,'') FROM \"Contracts\" WHERE \"Id\"='{contract:D}';");

    public static Task<long> SagasOfAsync(Guid record) =>
        CountAsync(Contracts, $"SELECT count(*) FROM \"SeparationSagaStates\" WHERE \"SeparationRecordId\"='{record:D}';");

    /// <summary>The saga's row after an execution: it appears once HR.Contracts consumes its own outbox message, so it is polled.</summary>
    public static Task<long> SagaStartedAsync(Guid record) =>
        CustodyJourney.PollAsync(() => SagasOfAsync(record), 1, TimeSpan.FromSeconds(30));

    /// <summary>The decision's number: DEC-SEP-&lt;year&gt;-#####, the year HR.Core's counter reads (UTC's; Damascus's differs on 31 December evening).</summary>
    public static bool IsDecisionNumber(string number, DateOnly today) =>
        Regex.IsMatch(number, $"^DEC-SEP-({today.Year}|{DateTime.UtcNow.Year})-\\d{{5}}$");

    /// <summary>
    /// The saga's end on this stack, recorded (the parties' truth is PLAN-62's): the record's status once it leaves Executing, or after
    /// the window, with the saga's state and the parties it waits for, completed, or last faulted.
    /// </summary>
    public static async Task<object> SagaEndAsync(Guid record, TimeSpan window)
    {
        var status = await PollStatusAsync(record, window, "Completed", "AwaitingIntervention");
        var saga = await ReadAsync(Contracts,
            "SELECT \"CurrentState\" || '|' || \"PendingParties\" || '|' || \"CompletedParties\" || '|' || COALESCE(\"LastFaultParty\",'') " +
            $"FROM \"SeparationSagaStates\" WHERE \"SeparationRecordId\"='{record:D}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        var parts = saga.Split('|');
        return new
        {
            recordStatus = status,
            sagaState = parts.ElementAtOrDefault(0),
            pendingParties = parts.ElementAtOrDefault(1),
            completedParties = parts.ElementAtOrDefault(2),
            lastFaultParty = parts.ElementAtOrDefault(3)
        };
    }

    /// <summary>
    /// The isolation rule: every record the scenario did not execute is cancelled with a reason by the checker (a pending one is withdrawn
    /// by the cancellation first), and its instance has ended. Answers what could not be cleaned, for the test to assert after its body.
    /// </summary>
    public static Task<List<string>> CancelLeftoversAsync(EvidenceRecorder recorder, IEnumerable<Guid> records) =>
        CancelLeftoversAsync(recorder, records.Select(r => (Guid?)r).ToArray());

    public static async Task<List<string>> CancelLeftoversAsync(EvidenceRecorder recorder, params Guid?[] records)
    {
        var problems = new List<string>();
        var open = new List<Guid>();
        foreach (var record in records.OfType<Guid>().Distinct())
        {
            var status = await StatusAsync(record);
            if (status is "Draft" or "PendingApproval" or "Rejected" or "Approved")
                open.Add(record);
        }

        if (open.Count == 0)
            return problems;

        using var checker = await ScreenSession.OpenAsync(Checker, recorder);
        foreach (var record in open)
        {
            try
            {
                var page = await checker.PostAsync("cleanup-cancel", Details(record), ActionPath("Cancel", record),
                    new Dictionary<string, string> { ["reason"] = CleanupReason });
                var (status, instance) = await StateAsync(record);
                if (status != "Cancelled")
                    problems.Add($"{record:D} is {status} after the cancellation: {page.Alerts}");
                if (instance is Guid id && (await InstanceAsync(id)).Status is "NotStarted" or "InProgress")
                    problems.Add($"{record:D}: instance {id:D} has not ended.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems.Add($"{record:D}: {ex.Message}");
            }
        }

        recorder.CaptureHttp("cleanup", "SUMMARY", "/cleanup", problems.Count == 0 ? 200 : 500, new { cancelled = open, problems });
        return problems;
    }
}
