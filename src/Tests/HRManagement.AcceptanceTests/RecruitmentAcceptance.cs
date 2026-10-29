using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-33 building blocks shared by AC-HCM-21, AC-HCM-22 and AC-WF-23: persona UI sessions through the gateway, SQL
/// evidence checks, and the recruitment steps the scenarios repeat. Data is located by the ids of
/// <see cref="RecruitmentStates"/> or by SQL on rows the scenario created — never by scraping HTML.
/// </summary>
internal static class RecruitmentAcceptance
{
    public const string Database = "hr_core_db";

    // RecruitmentApplicationStatus / OfferStatus / RecruitmentAppealStatus are stored as integers.
    public const int AppSubmitted = 0, AppUnderReview = 1, AppRejected = 3, AppAssessed = 6, AppRanked = 7, AppOfferExtended = 8,
        AppOfferAccepted = 9;
    public const int OfferDraft = 0, OfferPending = 1, OfferIssued = 2, OfferAccepted = 3;
    public const int AppealPending = 1, AppealDecided = 2;

    public static readonly string[] BlockingStatuses = ["Draft", "PendingApproval", "Approved", "Published"];
    public static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");

    public static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Database, sql);

    public static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Database, sql);

    public static string ReqDetails(object id) => $"ui/hr-core/RecruitmentRequisitions/Details/{id}";
    public static string AppDetails(object id) => $"ui/hr-core/RecruitmentApplications/Details/{id}";
    public static string OfferDetails(object id) => $"ui/hr-core/OfferLetters/Details/{id}";
    public static string AppealDetails(object id) => $"ui/hr-core/RecruitmentAppeals/Details/{id}";
    public static string SessionDetails(object id) => $"ui/hr-core/SelectionCommittees/Details/{id}";

    /// <summary>Starts a scenario in the shared AC folder: recorder keyed by <paramref name="key"/> and the persona sessions.</summary>
    public static async Task<RecruitmentSession> StartAsync(string ac, string key, string scenario, params string[] personas)
    {
        var first = AcceptanceHarness.RequirePersona(personas[0]);
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(first);
        var recorder = new EvidenceRecorder(ac, correlationId) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, string.Join('+', personas));
        var sessions = new Dictionary<string, AcceptanceUi>(StringComparer.Ordinal);
        foreach (var id in personas)
        {
            var persona = AcceptanceHarness.LoadPersonas().FirstOrDefault(p => p.Id == id && p.Enabled);
            AcceptanceGate.RequireData(recorder, persona is not null,
                $"الشخصية {id} غير موجودة في personas.json", $"Persona {id} is missing from personas.json (run scripts/prepare-keycloak-import.ps1).");
            sessions[id] = new AcceptanceUi(await CreateClientAsync(persona!, correlationId), recorder, id, key);
        }

        return new RecruitmentSession(recorder, key, sessions);
    }

    private static async Task<HttpClient> CreateClientAsync(E2ePersona persona, string correlationId)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona, correlationId);
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        return http;
    }

    /// <summary>
    /// The first <c>AC33-POOL-*</c> position (seeded, G5, RIF) without a blocking requisition; Blocked when the pool is
    /// used up (restore it with scripts/acceptance-reset.ps1).
    /// </summary>
    public static async Task<string> FreePoolPositionAsync(RecruitmentSession s)
    {
        var free = await Sql(
            "SELECT p.\"Id\" FROM \"FundedPositions\" p WHERE p.\"PositionCode\" LIKE 'AC33-POOL-%' AND p.\"Status\"='Vacant' AND NOT EXISTS (" +
            "SELECT 1 FROM \"RecruitmentRequisitions\" r WHERE r.\"FundedPositionId\"=p.\"Id\" AND r.\"Status\" IN (" +
            string.Join(",", BlockingStatuses.Select(b => $"'{b}'")) + ")) ORDER BY p.\"PositionCode\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(free, out _),
            "نفدت مناصب AC33-POOL — شغّل scripts/acceptance-reset.ps1",
            "The AC33-POOL positions are used up — run scripts/acceptance-reset.ps1 (PLAN-33 recruitment states).");
        return free;
    }

    /// <summary>central-a creates a draft requisition on a free pool position (UI) and returns its id.</summary>
    public static async Task<string> CreateRequisitionAsync(RecruitmentSession s, string step)
    {
        var position = await FreePoolPositionAsync(s);
        await s["central-a"].PostAsync($"{step}-create", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", Br1Scope), new("FundedPositionId", position), new("JobTitle", $"وظيفة PLAN-33 {s.Key}"),
            new("JustificationNotes", $"PLAN-33 acceptance {s.Key}"), new("LegalSourceId", Syr001LegislationIds.Law50_2004),
            new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)), new("PublishingChannel", "PublicOnly")
        ]);
        var id = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{position}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(id, out _), $"{step}: requisition was not created on pool position {position}.");
        return id;
    }

    public static Task<string> RequisitionStateAsync(string id) =>
        Sql("SELECT r.\"Status\" || '|' || COALESCE(r.\"WorkflowInstanceId\"::text,'') || '|' || COALESCE(i.\"CurrentStepLevel\"::text,'') || '|' || " +
            "COALESCE(i.\"Status\",'') FROM \"RecruitmentRequisitions\" r LEFT JOIN \"WorkflowInstances\" i ON i.\"Id\"=r.\"WorkflowInstanceId\" " +
            $"WHERE r.\"Id\"='{id}';");

    public static Task<string> WorkflowCodeAsync(string instanceId) =>
        Sql($"SELECT d.\"Code\" FROM \"WorkflowInstances\" i JOIN \"WorkflowDefinitions\" d ON d.\"Id\"=i.\"DefinitionId\" WHERE i.\"Id\"='{instanceId}';");

    public static Task ApproveRequisitionAsync(AcceptanceUi ui, string step, string id) =>
        ui.PostAsync(step, $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", $"PLAN-33 {step}")]);

    /// <summary>Draft → submit → br1-mgr step 1 → a-management-approver step 2 → criteria v1 → publish.</summary>
    public static async Task<string> PublishRequisitionAsync(RecruitmentSession s, string step, int closingInDays = 30)
    {
        var id = await CreateRequisitionAsync(s, step);
        await s["central-a"].PostAsync($"{step}-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        await ApproveRequisitionAsync(s["br1-mgr"], $"{step}-approve-1", id);
        await ApproveRequisitionAsync(s["a-management-approver"], $"{step}-approve-2", id);
        Assert.StartsWith("Approved|", await RequisitionStateAsync(id));
        await s["central-a"].PostAsync($"{step}-criteria", "ui/hr-core/RecruitmentAssessmentCriteria/Create", ReqDetails(id), CriteriaForm(id));
        await s["central-a"].PostAsync($"{step}-publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{id}", ReqDetails(id),
            [new("applicationClosingDate", ClosingDate(closingInDays))]);
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{id}';"));
        return id;
    }

    public static string ClosingDate(int days) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static List<KeyValuePair<string, string>> CriteriaForm(string reqId)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", reqId), new("Title", "PLAN-33") };
        (string Key, string Name, string Weight, string Stage)[] items =
            [("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")];
        for (var i = 0; i < items.Length; i++)
        {
            var t = $"r{i}";
            form.Add(new("Items.Index", t));
            form.Add(new($"Items[{t}].Key", items[i].Key));
            form.Add(new($"Items[{t}].NameAr", items[i].Name));
            form.Add(new($"Items[{t}].Weight", items[i].Weight));
            form.Add(new($"Items[{t}].MaxScore", "100"));
            form.Add(new($"Items[{t}].Stage", items[i].Stage));
            form.Add(new($"Items[{t}].IsMandatory", "true"));
        }

        return form;
    }

    /// <summary>central-a enters an HR application on <paramref name="reqId"/> (UI) and returns its id.</summary>
    public static async Task<string> CreateApplicationAsync(RecruitmentSession s, string step, string reqId)
    {
        var name = $"AC33 {s.Key} {Guid.NewGuid():N}"[..24];
        await s["central-a"].PostAsync($"{step}-application", "ui/hr-core/RecruitmentApplications/Create", "ui/hr-core/RecruitmentApplications/Index",
            [new("ApplicantFullName", name), new("RequisitionId", reqId), new("Notes", "PLAN-33")]);
        var id = await Sql($"SELECT \"Id\" FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{reqId}' AND \"ApplicantFullName\"='{name}';");
        Assert.True(Guid.TryParse(id, out _), $"{step}: HR application was not created.");
        return id;
    }

    /// <summary>Payroll bounds of grade G5 in agency A (offer salaries must fall inside them).</summary>
    public static async Task<decimal> MidSalaryAsync(RecruitmentSession s, string grade = "G5")
    {
        var row = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            $"SELECT g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g JOIN \"SalaryScales\" sc ON sc.\"Id\"=g.\"SalaryScaleId\" " +
            $"WHERE g.\"TenantId\"='{AcceptanceSeedIds.AgencyAId}' AND g.\"GradeCode\"='{grade}' AND sc.\"IsActive\" AND sc.\"EffectiveFrom\" <= now() " +
            "AND (sc.\"EffectiveTo\" IS NULL OR sc.\"EffectiveTo\" >= now()) ORDER BY sc.\"EffectiveFrom\" DESC LIMIT 1;");
        var parts = row.Split('|');
        AcceptanceGate.RequireData(s.Recorder, parts.Length == 2,
            $"درجة الرواتب {grade} غير موجودة للجهة A", $"Payroll grade {grade} not found for agency A.");
        return Math.Round((decimal.Parse(parts[0], CultureInfo.InvariantCulture) + decimal.Parse(parts[1], CultureInfo.InvariantCulture)) / 2m, 0);
    }

    public static List<KeyValuePair<string, string>> OfferForm(string appId, decimal salary, string grade = "G5") =>
    [
        new("ApplicationId", appId),
        new("OfferedSalary", salary.ToString(CultureInfo.InvariantCulture)),
        new("GradeCode", grade),
        new("ProposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("ValidityDays", "14")
    ];

    public static Task ApproveOfferAsync(AcceptanceUi ui, string step, string offerId) =>
        ui.PostAsync(step, $"ui/hr-core/OfferLetters/ApproveWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", $"PLAN-33 {step}")]);

    public static Task<string> OfferStateAsync(string offerId) =>
        Sql("SELECT o.\"Status\"::text || '|' || COALESCE(o.\"WorkflowInstanceId\"::text,'') || '|' || COALESCE(i.\"CurrentStepLevel\"::text,'') " +
            $"FROM \"OfferLetters\" o LEFT JOIN \"WorkflowInstances\" i ON i.\"Id\"=o.\"WorkflowInstanceId\" WHERE o.\"Id\"='{offerId}';");

    /// <summary>Work inbox of a persona: the deep links of its item rows and the header counter.</summary>
    public static async Task<WorkInboxView> InboxAsync(AcceptanceUi ui, string step)
    {
        var html = await ui.GetAsync(step, "ui/hr-core/WorkInbox");
        var links = Regex.Matches(html, @"href=""/ui/hr-core(/[^""]+)""\s+class=""btn btn-primary btn-xs""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        var pending = Regex.Match(html, @"(\d+)\s+معلّق");
        return new WorkInboxView(links, pending.Success ? int.Parse(pending.Groups[1].Value, CultureInfo.InvariantCulture) : -1, html);
    }
}

internal sealed record WorkInboxView(IReadOnlyList<string> Links, int PendingBadge, string Html)
{
    public bool Has(string link) => Links.Any(l => l.Equals(link, StringComparison.OrdinalIgnoreCase));
}

internal sealed record RecruitmentSession(EvidenceRecorder Recorder, string Key, IReadOnlyDictionary<string, AcceptanceUi> Personas)
{
    public AcceptanceUi this[string persona] => Personas[persona];

    /// <summary>Per-scenario steps file; the folder's steps.md concatenates them (scenarios share the AC folder).</summary>
    public void Steps(string markdown)
    {
        Recorder.WriteScenarioSteps(markdown);
        var combined = Directory.GetFiles(Recorder.Folder, "steps-*.md")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"## {Path.GetFileNameWithoutExtension(f)[6..]}\n\n{File.ReadAllText(f)}");
        Recorder.WriteSteps(string.Join("\n\n", combined));
    }

    /// <summary>audit.json: the HR.Core audit rows of the entities the scenario touched (action, user, time).</summary>
    public async Task AuditAsync(params string[] entityIds)
    {
        var ids = string.Join(",", entityIds.Where(id => Guid.TryParse(id, out _)).Select(id => $"'{id}'"));
        var rows = ids.Length == 0 ? "" : await RecruitmentAcceptance.Sql(
            "SELECT COALESCE(string_agg(\"EntityType\" || '|' || \"EntityId\" || '|' || \"Action\" || '|' || COALESCE(\"UserId\"::text,'') || '|' || " +
            $"to_char(\"CreatedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS'), E'\\n' ORDER BY \"CreatedAt\"), '') FROM \"AuditLogs\" WHERE \"EntityId\" IN ({ids});");
        Recorder.WriteAudit(new
        {
            scenario = Key,
            explicitAuditQuery = true,
            source = "hr_core_db.AuditLogs",
            entityIds,
            rows = rows.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        });
    }

    /// <summary>Writes after.json and db-checks-&lt;key&gt;.json (and db-checks.json for the folder), flushes timing.</summary>
    public void Checks(object checks)
    {
        Recorder.WriteAfter(new { scenario = Key, checks });
        Recorder.WriteScenarioDbChecks(checks);
        Recorder.WriteDbChecks(new { lastScenario = Key, checks });
        Recorder.FlushTiming();
    }
}

/// <summary>Gateway UI client for one persona: antiforgery-aware form posts with evidence capture (as in AC-HCM-26..28).</summary>
internal sealed class AcceptanceUi(HttpClient http, EvidenceRecorder recorder, string persona, string tag)
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public string Persona => persona;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (_cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await http.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var header in setCookies)
            {
                var pair = header.Split(';', 2)[0];
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var name = pair[..eq].Trim();
                var value = pair[(eq + 1)..].Trim();
                var expired = header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(value) || expired) _cookies.Remove(name);
                else _cookies[name] = value;
            }
        }

        return response;
    }

    private async Task<HttpResponseMessage> FollowRedirectsAsync(HttpResponseMessage resp)
    {
        for (var hops = 0; hops < 3 && (int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null; hops++)
        {
            var location = resp.Headers.Location;
            resp.Dispose();
            resp = await SendAsync(HttpMethod.Get,
                location.IsAbsoluteUri ? location.PathAndQuery.TrimStart('/') : location.OriginalString.TrimStart('/'));
        }

        return resp;
    }

    /// <summary>GET that must answer 200; returns the HTML-decoded body.</summary>
    public async Task<string> GetAsync(string step, string path)
    {
        var (status, body) = await GetWithStatusAsync(step, path);
        Assert.True(status == HttpStatusCode.OK, $"GET {path} as {persona} returned {(int)status}.");
        return body;
    }

    public async Task<(HttpStatusCode Status, string Body)> GetWithStatusAsync(string step, string path)
    {
        var resp = await FollowRedirectsAsync(await SendAsync(HttpMethod.Get, path));
        var body = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode, Snippet(body));
        return (resp.StatusCode, WebUtility.HtmlDecode(body));
    }

    /// <summary>Antiforgery form post (token from <paramref name="tokenPage"/>); must not be denied or fail with 5xx.</summary>
    public async Task<string> PostAsync(string step, string path, string tokenPage, List<KeyValuePair<string, string>> fields, bool withEvidence = false)
    {
        var (status, body) = await PostWithStatusAsync(step, path, tokenPage, fields, withEvidence);
        Assert.True((int)status < 500, $"POST {path} as {persona} returned {(int)status}.");
        Assert.True(status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"POST {path} as {persona} was denied.");
        return body;
    }

    public async Task<(HttpStatusCode Status, string Body)> PostWithStatusAsync(string step, string path, string tokenPage,
        List<KeyValuePair<string, string>> fields, bool withEvidence = false)
    {
        var page = await FollowRedirectsAsync(await SendAsync(HttpMethod.Get, tokenPage));
        var pageHtml = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {tokenPage} as {persona} returned {(int)page.StatusCode}.");
        var token = Regex.Match(pageHtml, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""", RegexOptions.IgnoreCase);
        Assert.True(token.Success, $"No antiforgery token on {tokenPage}.");

        var form = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)) };
        var posted = await SendAsync(HttpMethod.Post, path, OfferOnBehalfEvidence.Content(form, withEvidence));
        var postStatus = (int)posted.StatusCode;
        var redirect = posted.Headers.Location?.ToString();
        var resp = await FollowRedirectsAsync(posted);
        var body = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp($"{tag}-{step}-{persona}", "POST", "/" + path, (int)resp.StatusCode,
            new { fields = fields.Select(f => $"{f.Key}={f.Value}"), postStatus, redirect, final = resp.RequestMessage?.RequestUri?.AbsolutePath });
        return (postStatus is >= 300 and < 400 ? resp.StatusCode : (HttpStatusCode)postStatus, WebUtility.HtmlDecode(body));
    }

    private static string Snippet(string body) => body.Length > 600 ? body[..600] : body;
}
