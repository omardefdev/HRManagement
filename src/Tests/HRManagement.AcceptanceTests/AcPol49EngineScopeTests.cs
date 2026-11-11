using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>One row of HR.Core's policy ledger, with its decision read back.</summary>
internal sealed record PolicyLedgerRow(
    Guid Id, string Domain, string Action, string Mode, string? Caller, Guid Tenant, Guid? Entity, string? Subject, string FactsHash,
    Guid? Instance, Guid? Actor, PolicyDecision Decision);

/// <summary>PLAN-49: what the two AC-POL-49 classes share: the ledger read, the facts hash, the container logs.</summary>
internal static class PolicyScopeJourney
{
    public const string Ac = "AC-POL-49";
    public const string OutOfScopeMessage = "المجال خارج نطاق المحرك";
    public const string SchemaRefusal = "حقل غير مسموح";
    public static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    public static readonly string[] Governed = ["Assets.Custody", "Contracts.Leave", "Core.Establishment", "Core.Recruitment", "IDCard.Lifecycle"];

    private static readonly JsonSerializerOptions DecisionOptions = new() { PropertyNameCaseInsensitive = true };

    public static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    public static Task<string> Sql(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    public static async Task<string> Scalar(string database, string sql) => (await Sql(database, sql)).Trim();

    public static Task<long> Count(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);

    /// <summary>
    /// The clock the ledger rows carry: the containers share it, and the test machine's may differ by seconds. Taken before an
    /// action, it bounds "the rows this action wrote" (the acceptance tests run one at a time).
    /// </summary>
    public static Task<string> ClockAsync() =>
        Scalar("hr_core_db", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS.US');");

    public static async Task<List<PolicyLedgerRow>> LedgerAsync(string where)
    {
        var json = await Scalar("hr_core_db", $"""
            SELECT COALESCE(json_agg(json_build_object(
                'id', l."Id", 'domain', l."Domain", 'action', l."Action", 'mode', l."Mode", 'caller', l."CallerClientId", 'tenant', l."TenantId",
                'entity', l."EntityId", 'subject', l."SubjectType", 'factsHash', l."FactsHash", 'instance', l."WorkflowInstanceId",
                'actor', l."ActorUserId", 'decision', l."DecisionJson") ORDER BY l."OccurredAt", l."Id"), '[]'::json)::text
            FROM "PolicyEnforcementLedgers" l WHERE {where};
            """);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(row => new PolicyLedgerRow(
            row.GetProperty("id").GetGuid(),
            row.GetProperty("domain").GetString()!,
            row.GetProperty("action").GetString()!,
            row.GetProperty("mode").GetString()!,
            row.GetProperty("caller").GetString(),
            row.GetProperty("tenant").GetGuid(),
            NullableGuid(row.GetProperty("entity")),
            row.GetProperty("subject").GetString(),
            row.GetProperty("factsHash").GetString()!,
            NullableGuid(row.GetProperty("instance")),
            NullableGuid(row.GetProperty("actor")),
            JsonSerializer.Deserialize<PolicyDecision>(row.GetProperty("decision").GetString()!, DecisionOptions)!)).ToList();
    }

    private static Guid? NullableGuid(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();

    /// <summary>
    /// The ledger keeps a hash of the facts it decided on, not the facts (PolicyOrchestrator.HashFacts): the first 16 hex digits
    /// of the SHA-256 of "key=value" pairs in key order. Computed here over what a facts builder sends, it says which facts a row
    /// was decided on.
    /// </summary>
    public static string FactsHash(IReadOnlyDictionary<string, string> facts)
    {
        var canonical = string.Join("|", facts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    /// <summary>What every Shadow row of an owner service must say, whatever it decided.</summary>
    public static void AssertShadowRow(PolicyLedgerRow row, string domain, string action, string? caller)
    {
        Assert.Equal(domain, row.Domain);
        Assert.Equal(action, row.Action);
        Assert.Equal("Shadow", row.Mode);
        Assert.Equal(caller, row.Caller);
        Assert.Equal(TenantA, row.Tenant);
        // Shadow records and starts nothing.
        Assert.Null(row.Instance);
        // The facts matched the schema: a request with a key the schema lacks is refused whole, with this message.
        Assert.DoesNotContain(row.Decision.Explanations, e => e.Contains(SchemaRefusal, StringComparison.Ordinal));
        Assert.DoesNotContain(SchemaRefusal, row.Decision.DenyReason ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The rule keys of the effective national version of a domain.</summary>
    public static async Task<string[]> EffectiveNationalRulesAsync(string domain) =>
        (await Sql("hr_core_db", $"""
            SELECT r."RuleKey" FROM "PolicyRules" r WHERE r."PolicyTemplateVersionId" = (
                SELECT v."Id" FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
                WHERE t."Domain" = '{domain}' AND t."Scope" = 'National' AND v."Status" = 'Published'
                  AND (v."EffectiveFrom" IS NULL OR v."EffectiveFrom" <= now()) AND (v."EffectiveTo" IS NULL OR v."EffectiveTo" > now())
                ORDER BY v."VersionNumber" DESC LIMIT 1)
            ORDER BY r."RuleKey";
            """)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static async Task<Guid> PersonaUserIdAsync(string persona)
    {
        var (token, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona(persona));
        return Guid.Parse(AcceptanceHarness.ParseJwt(token).Subject);
    }

    /// <summary>
    /// PLAN-49 (the plan's first risk): what a Shadow call costs the action that makes it. The client times every call to HR.Core
    /// and writes one line; these are the lines of a service since <paramref name="sinceUtc"/> for a domain, in milliseconds.
    /// </summary>
    public static async Task<List<long>> ClientCallMillisecondsAsync(string service, string domain, DateTime sinceUtc)
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service);
        var (_, logs) = await AcceptanceMessagingHelper.RunProcessAsync(
            "docker", "logs", "--since", sinceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), container);
        return Regex.Matches(logs, $@"policy_client_call domain={Regex.Escape(domain)} action=\w+ mode=Shadow outcome=ok elapsed_ms=(\d+)")
            .Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
    }

    /// <summary>
    /// What HR.Core's last start-up logged about the published rules its validator refuses: the national part (rules of effective
    /// versions), and the agencies' overrides, read apart.
    /// </summary>
    public static async Task<(int NationalInForce, int NationalTemplatesRead, int OverridesInForce, int AgencyTenantsRead, List<(Guid Tenant, string RuleKey, string Codes)> OverrideRules)> StartUpInventoryAsync()
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-core-web");
        var (_, logs) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "logs", container);
        return ParseStartUpInventory(logs);
    }

    /// <summary>
    /// The last start-up of a log, both parts by their place in it, the way scripts/pol047-validate.ps1 reads them. One start-up
    /// writes the national part (a line for each refused rule of an effective version, then its summary) and then the agencies'
    /// part (its lines, then its own summary); a national line that follows an agencies' line, or a national line that ended a
    /// part, begins another start-up. A part that could not be taken leaves no summary: the numbers of the start-up before must
    /// not stand for it (the last review of PLAN-49), so it fails here.
    /// </summary>
    public static (int NationalInForce, int NationalTemplatesRead, int OverridesInForce, int AgencyTenantsRead, List<(Guid Tenant, string RuleKey, string Codes)> OverrideRules) ParseStartUpInventory(string logs)
    {
        const string nationalPrefix = "PLAN-47 validator inventory";
        const string overridePrefix = "PLAN-49 agency override inventory";
        var entries = logs.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(nationalPrefix, StringComparison.Ordinal) || line.Contains(overridePrefix, StringComparison.Ordinal))
            .Select(line => (National: line.Contains(nationalPrefix, StringComparison.Ordinal), Text: line))
            .ToList();
        Assert.True(entries.Any(entry => entry.National), "hr-core-web logged no 'PLAN-47 validator inventory' line at start-up.");

        var nationalEnd = new Regex(@"validator inventory: (\d+ rule\(s\)|the inventory could not be taken)");
        var begin = 0;
        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i].National && (!entries[i - 1].National || nationalEnd.IsMatch(entries[i - 1].Text)))
                begin = i;
        }

        var nationalLines = entries.Skip(begin).Where(entry => entry.National).Select(entry => entry.Text).ToList();
        var overrideLines = entries.Skip(begin).Where(entry => !entry.National).Select(entry => entry.Text).ToList();

        Assert.DoesNotContain(nationalLines, line => line.Contains("could not be taken", StringComparison.Ordinal));
        var national = nationalLines
            .Select(line => Regex.Match(line, @"validator inventory: (\d+) rule\(s\) of effective versions the validator refuses; \d+ more in superseded versions \(history, unchanged\); (\d+) national template\(s\) read"))
            .LastOrDefault(match => match.Success);
        Assert.True(national is not null, "The last start-up of hr-core-web wrote no national inventory summary (or the summary of an older release).");

        Assert.DoesNotContain(overrideLines, line => line.Contains("could not be taken", StringComparison.Ordinal) || line.Contains("not taken", StringComparison.Ordinal));
        var overrides = overrideLines
            .Select(line => Regex.Match(line, @"agency override inventory: (\d+) rule\(s\) of overrides in force the validator refuses, in \d+ of (\d+) agency tenant\(s\) read"))
            .LastOrDefault(match => match.Success);
        Assert.True(overrides is not null, "The last start-up of hr-core-web wrote no 'PLAN-49 agency override inventory' summary.");

        var rules = overrideLines
            .Select(line => Regex.Match(line, @"agency override inventory: \S+ tenant ([0-9a-fA-F-]{36}) v\d+ \([0-9a-fA-F-]{36}\) rule (\S+): (\S+)"))
            .Where(match => match.Success)
            .Select(match => (Tenant: Guid.Parse(match.Groups[1].Value), RuleKey: match.Groups[2].Value, Codes: match.Groups[3].Value))
            .ToList();
        return (int.Parse(national!.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(national.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(overrides!.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(overrides.Groups[2].Value, CultureInfo.InvariantCulture), rules);
    }

    public static object LatencySummary(IReadOnlyCollection<long> milliseconds) => milliseconds.Count == 0
        ? new { calls = 0 }
        : new { calls = milliseconds.Count, minMs = milliseconds.Min(), maxMs = milliseconds.Max(), averageMs = Math.Round(milliseconds.Average(), 1), all = milliseconds };
}

/// <summary>
/// AC-POL-49a..e and g (PLAN-49) on the prod-like stack, where the five governed domains run in Shadow as in Production: what the
/// policy engine governs is one catalog (the page, the drafts, the validator), every governed domain has a call site that leaves
/// a Shadow ledger row in the caller's tenant with facts that match the schema, and nothing a rule says changes an outcome or
/// starts a workflow. It stops and recreates no container. AC-POL-49f and the start-up refusal of 49a need another configuration:
/// <see cref="AcPol49EnforceOverlayTests"/>.
/// </summary>
[RequiredAcceptance]
public class AcPol49EngineScopeTests
{
    private const string Contracts = "svc-hr-contracts";
    private const string Logistics = "svc-admin-logistics";
    private const string IdCard = "svc-idcard";
    private static readonly Guid TenantA = PolicyScopeJourney.TenantA;

    private static Task<string> Sql(string database, string sql) => PolicyScopeJourney.Sql(database, sql);

    private static Task<string> Scalar(string database, string sql) => PolicyScopeJourney.Scalar(database, sql);

    private static Task<long> Count(string database, string sql) => PolicyScopeJourney.Count(database, sql);

    private static async Task<HttpClient> PolicyClientAsync(string personaId)
    {
        var persona = AcceptanceHarness.RequirePersona(personaId);
        var (_, correlation) = await AcceptanceHarness.LoginAsync(persona);
        return await AcceptanceHarness.CreatePolicyLifecycleClientAsync(persona, correlation);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(HttpClient http, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            json = JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json, text);
    }

    private static IEnumerable<string> Strings(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetString() ?? string.Empty)
            : [];

    private static IEnumerable<string> IssueCodes(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array
            ? issues.EnumerateArray().Select(i => i.GetProperty("code").GetString() ?? string.Empty)
            : [];

    // ------------------------------------------------------------------------------------------------ 49a

    [SkippableFact]
    public async Task Ac49a_The_policy_page_shows_what_the_engine_governs_and_a_domain_out_of_scope_takes_no_draft()
    {
        var r = PolicyScopeJourney.Recorder("49a", "The policy page: five governed domains in Shadow, fifteen out of scope with their reasons; no draft for IT.Tickets", "central-a");
        AcceptanceGate.RequireEnabled(r);
        using var ui = await AcS2s42TrustBoundaryTests.UiClientAsync("central-a");

        var (status, page) = await CustodyJourney.OpenAsync(ui, "ui/hr-core/PolicyTemplates");
        r.CaptureHttp("policy-page", "GET", "/ui/hr-core/PolicyTemplates", (int)status);
        Assert.Equal(HttpStatusCode.OK, status);
        var rows = Regex.Matches(page, @"<tr data-testid=""policy-domain-row"" data-domain=""([^""]+)"" data-status=""([^""]+)"" data-mode=""([^""]+)"">(.*?)</tr>", RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value, m => (Status: m.Groups[2].Value, Mode: m.Groups[3].Value, Html: m.Groups[4].Value), StringComparer.Ordinal);

        // The page is the catalog: every domain once.
        Assert.Equal(PolicyDomains.All.OrderBy(d => d, StringComparer.Ordinal), rows.Keys.OrderBy(d => d, StringComparer.Ordinal));
        var governed = rows.Where(row => row.Value.Status == "governed").Select(row => row.Key).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        var outOfScope = rows.Where(row => row.Value.Status == "out-of-scope").Select(row => row.Key).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        Assert.Equal(PolicyScopeJourney.Governed, governed);
        Assert.Equal(15, outOfScope.Length);
        foreach (var domain in governed)
        {
            Assert.Equal("Shadow", rows[domain].Mode);
            Assert.Contains("محكوم", rows[domain].Html, StringComparison.Ordinal);
            // central-a may author: a governed domain offers a draft.
            Assert.Contains(@"data-testid=""domain-draft-link""", rows[domain].Html, StringComparison.Ordinal);
        }

        foreach (var domain in outOfScope)
        {
            Assert.Equal("Off", rows[domain].Mode);
            Assert.Contains("خارج النطاق", rows[domain].Html, StringComparison.Ordinal);
            Assert.Contains(PolicyDomainCatalog.Find(domain)!.OutOfScopeReasonAr!, rows[domain].Html, StringComparison.Ordinal);
            Assert.DoesNotContain(@"data-testid=""domain-draft-link""", rows[domain].Html, StringComparison.Ordinal);
        }

        // What HR.Core said about its own data when it started: no rule of an effective national version is one the validator
        // refuses. The agencies' overrides are read apart, each tenant in its own session: this stack seeds agencies B and C
        // with a layer written before the ceiling, on purpose (AC-TEN-48e), and nothing else may show there.
        var inventory = await PolicyScopeJourney.StartUpInventoryAsync();
        Assert.Equal(0, inventory.NationalInForce);
        // "None refused" of templates it did read: a session that is nobody's reads none and would print the same zero.
        Assert.True(inventory.NationalTemplatesRead >= governed.Length, $"the national inventory read {inventory.NationalTemplatesRead} template(s)");
        Assert.True(inventory.AgencyTenantsRead >= 3, $"the agency override inventory read {inventory.AgencyTenantsRead} tenant(s)");
        // Exactly that layer, rule by rule (the second review of PLAN-49): a walk that read the tenants and found nothing in them
        // would pass a check that only bounds what may be listed.
        var seededLayer = new[] { AcceptanceSeedIds.AgencyBId, AcceptanceSeedIds.AgencyCId }
            .SelectMany(tenant => new[] { "ACC-R-OLD-FIRST-WF", "ACC-R-OLD-UNPRIV", "ACC-R-PRIV-WORKFLOW" }.Select(key => $"{tenant:D} {key}"))
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToList();
        var listed = inventory.OverrideRules
            .Select(rule => $"{rule.Tenant:D} {rule.RuleKey}")
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(seededLayer, listed);
        Assert.Equal(seededLayer.Count, inventory.OverridesInForce);

        // Negative: no draft for a domain out of scope, on the page and in the API, and nothing is written.
        const string retired = "IT.Tickets";
        Assert.Contains(retired, outOfScope);
        var templatesBefore = await Count("hr_core_db", $"""SELECT count(*) FROM "PolicyTemplates" WHERE "Domain" = '{retired}';""");
        var versionsBefore = await Count("hr_core_db", $"""SELECT count(*) FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId" WHERE t."Domain" = '{retired}';""");

        using var form = await ui.GetAsync($"ui/hr-core/PolicyTemplates/CreateDraft?domain={retired}&subjectType=Ticket");
        r.CaptureHttp("draft-form-out-of-scope", "GET", $"/ui/hr-core/PolicyTemplates/CreateDraft?domain={retired}", (int)form.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, form.StatusCode);
        var (_, after) = await CustodyJourney.OpenAsync(ui, "ui/hr-core/PolicyTemplates");
        Assert.Contains(PolicyScopeJourney.OutOfScopeMessage, AcS2s42TrustBoundaryTests.Alerts(after), StringComparison.Ordinal);

        // The form of a governed domain, posted for the domain out of scope (what a forged or stale page would send).
        var (_, governedForm) = await CustodyJourney.OpenAsync(ui, "ui/hr-core/PolicyTemplates/CreateDraft?domain=Assets.Custody&subjectType=Asset");
        using var posted = await ui.PostAsync("ui/hr-core/PolicyTemplates/CreateDraft", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AcMsg40AtomicPublishingTests.RequireAntiforgeryToken(governedForm),
            ["domain"] = retired,
            ["subjectType"] = "Ticket",
            ["name"] = "AC-POL-49a",
            ["scope"] = "TenantOverride"
        }));
        r.CaptureHttp("draft-post-out-of-scope", "POST", "/ui/hr-core/PolicyTemplates/CreateDraft", (int)posted.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.EndsWith("/PolicyTemplates", posted.Headers.Location!.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        var (_, afterPost) = await CustodyJourney.OpenAsync(ui, "ui/hr-core/PolicyTemplates");
        Assert.Contains(PolicyScopeJourney.OutOfScopeMessage, AcS2s42TrustBoundaryTests.Alerts(afterPost), StringComparison.Ordinal);

        using var api = await PolicyClientAsync("central-a");
        var (apiStatus, apiBody, apiText) = await SendAsync(api, HttpMethod.Post, "api/hr-core/api/policies/drafts",
            new { domain = retired, subjectType = "Ticket", name = "AC-POL-49a" });
        r.CaptureHttp("draft-api-out-of-scope", "POST", "/api/hr-core/api/policies/drafts", (int)apiStatus, apiText);
        Assert.True(apiStatus == HttpStatusCode.BadRequest, $"draft of {retired} -> {(int)apiStatus} {apiText}");
        Assert.Contains(PolicyScopeJourney.OutOfScopeMessage, apiBody.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Contains("DOMAIN_OUT_OF_SCOPE", IssueCodes(apiBody));

        Assert.Equal(templatesBefore, await Count("hr_core_db", $"""SELECT count(*) FROM "PolicyTemplates" WHERE "Domain" = '{retired}';"""));
        Assert.Equal(versionsBefore, await Count("hr_core_db", $"""SELECT count(*) FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId" WHERE t."Domain" = '{retired}';"""));

        r.WriteScenarioDbChecks(new
        {
            governed = governed.Select(d => new { domain = d, mode = rows[d].Mode }),
            outOfScope = outOfScope.Select(d => new { domain = d, mode = rows[d].Mode }),
            negative = new { domain = retired, pageForm = (int)form.StatusCode, pagePost = (int)posted.StatusCode, api = (int)apiStatus, issues = IssueCodes(apiBody), templatesBefore, versionsBefore },
            startUpInventory = new
            {
                nationalRulesOfEffectiveVersionsRefused = inventory.NationalInForce,
                nationalTemplatesRead = inventory.NationalTemplatesRead,
                agencyTenantsRead = inventory.AgencyTenantsRead,
                agencyOverrideRulesRefused = inventory.OverrideRules.Select(rule => $"{rule.Tenant:D} {rule.RuleKey} {rule.Codes}")
            }
        });
        r.WriteScenarioSteps(
            "1. central-a opens ui/hr-core/PolicyTemplates: the catalog table lists all 20 domains once; the five governed ones (Assets.Custody, Contracts.Leave, Core.Establishment, Core.Recruitment, IDCard.Lifecycle) say «محكوم» with the mode Shadow and offer a draft; the fifteen others say «خارج النطاق» with the mode Off, the catalog's reason, and no draft link.\n" +
            "2. Negative: the draft form of IT.Tickets redirects to the page with «المجال خارج نطاق المحرك»; the form of a governed domain posted for IT.Tickets is refused the same way; POST api/policies/drafts for IT.Tickets -> 400 DOMAIN_OUT_OF_SCOPE with the same message.\n" +
            "3. By psql: no template and no version of IT.Tickets was written.\n" +
            "4. hr-core-web's start-up log: the validator inventory lists 0 rules of effective national versions; the agency override inventory (each tenant read in its own session) lists only the pre-ceiling layer this stack seeds in agencies B and C on purpose (AC-TEN-48e), and nothing in agency A.\n" +
            "The start-up refusal of a mode for a domain out of scope is AC-POL-49a-boot (AcPol49EnforceOverlayTests).");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 49b

    private const string OverlapRefusal = "توجد إجازة معتمدة متداخلة مع هذه الفترة.";
    private const string RefusedNotesPrefix = "مرفوض آلياً: ";

    private static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> LeaveFacts(int days, string type, string action, bool? self = null) =>
        PolicyDomainFactsBuilders.ForLeaveRequest(Guid.Empty, null, days, type, action, self).Facts;

    private static async Task<string> WaitForLeaveAsync(string notesMarker)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        string id;
        do
        {
            id = await Scalar("hr_contracts_db", $"""SELECT "Id" FROM "LeaveRequests" WHERE "Notes" LIKE '%{notesMarker}%' ORDER BY "CreatedAt" DESC LIMIT 1;""");
            if (Guid.TryParse(id, out _))
                return id;
            await Task.Delay(TimeSpan.FromSeconds(2));
        } while (DateTime.UtcNow < deadline);

        return id;
    }

    [SkippableFact]
    public async Task Ac49b_Leave_passes_the_engine_from_the_HR_page_the_portal_an_edit_and_an_approval()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(PolicyScopeJourney.Ac, "49b",
            "Leave: created by HR, requested on the self-service portal, edited, approved: four Shadow ledger rows; an overlapping portal request is created Rejected",
            "a-contracts-maker", "a-contracts-checker", "employee-a");
        var run = Guid.NewGuid().ToString("N")[..8];
        var started = DateTime.UtcNow.AddSeconds(-5);

        // The employee of the portal's user (the token's employee_id), so the portal's requests and HR's are the same person's.
        var (employeeToken, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona("employee-a"));
        var employeeClaim = AcceptanceHarness.ParseJwt(employeeToken).Claims.FirstOrDefault(c => c.Type is "employee_id" or "employeeId")?.Value;
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(employeeClaim, out var employee),
            "رمز employee-a لا يحمل employee_id", "The token of employee-a carries no employee_id claim.");
        var maker = await PolicyScopeJourney.PersonaUserIdAsync("a-contracts-maker");
        var checker = await PolicyScopeJourney.PersonaUserIdAsync("a-contracts-checker");

        // A free fortnight starting on a Sunday at least three weeks ahead (Friday and Saturday are not working days), with no
        // approved leave of this employee in it: the scenario repeats on a stack that already ran it.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = today.AddDays(21);
        while (first.DayOfWeek != DayOfWeek.Sunday)
            first = first.AddDays(1);
        for (var attempt = 0; attempt < 26; attempt++, first = first.AddDays(14))
        {
            if (await Count("hr_contracts_db", $"""
                    SELECT count(*) FROM "LeaveRequests" WHERE "EmployeeId" = '{employee:D}' AND "Status" = 'Approved'
                      AND "StartDate" <= '{Date(first.AddDays(13))}' AND "EndDate" >= '{Date(first)}';
                    """) == 0)
                break;
        }

        var hrStart = first;
        var portalStart = first.AddDays(7);

        // Test-only precondition: the approval debits the annual balance of the leave's year. The seed gives a balance to the
        // first employees of the agency only, and only for the year it ran in.
        await Sql("hr_contracts_db", $"""
            INSERT INTO "LeaveBalances" ("Id","TenantId","EmployeeId","LeaveType","Year","TotalEntitlement","Used","CreatedAt","UpdatedAt")
            SELECT gen_random_uuid(), '{TenantA:D}', '{employee:D}', 'Annual', {hrStart.Year}, 30, 0, now(), now()
            WHERE NOT EXISTS (SELECT 1 FROM "LeaveBalances" WHERE "TenantId" = '{TenantA:D}' AND "EmployeeId" = '{employee:D}' AND "LeaveType" = 'Annual' AND "Year" = {hrStart.Year});
            SELECT 1;
            """);
        var remaining = await Count("hr_contracts_db", $"""SELECT "TotalEntitlement" - "Used" FROM "LeaveBalances" WHERE "EmployeeId" = '{employee:D}' AND "LeaveType" = 'Annual' AND "Year" = {hrStart.Year};""");
        AcceptanceGate.RequireData(s.Recorder, remaining >= 2,
            "رصيد الإجازة السنوية لموظف البوابة أقل من يومين", "The portal employee has fewer than two annual leave days left (scripts/acceptance-reset.ps1).");

        // ---- 1. HR records a leave of three working days (Sunday to Tuesday).
        var hrMarker = $"AC-POL-49b-hr-{run}";
        var created = await s["a-contracts-maker"].PostAsync("create", "ui/hr-contracts/LeaveRequests/Create", "ui/hr-contracts/LeaveRequests/Create",
        [
            new("EmployeeId", employee.ToString("D")), new("LeaveType", "Annual"), new("StartDate", Date(hrStart)),
            new("EndDate", Date(hrStart.AddDays(2))), new("DurationDays", "3"), new("Notes", hrMarker)
        ]);
        var leave = await Scalar("hr_contracts_db", $"""SELECT "Id" FROM "LeaveRequests" WHERE "Notes" = '{hrMarker}';""");
        Assert.True(Guid.TryParse(leave, out var leaveId), $"49b: the leave was not created: {AcS2s42TrustBoundaryTests.Alerts(created)}");

        // ---- 2. The employee asks for two days on the portal (the week after): HR.Core publishes, HR.Contracts creates.
        var portalMarker = $"AC-POL-49b-ess-{run}";
        await s["employee-a"].PostAsync("portal", "ui/hr-core/EssPortal/Leave", "ui/hr-core/EssPortal/Leave",
        [
            new("LeaveType", "Annual"), new("StartDate", Date(portalStart)), new("EndDate", Date(portalStart.AddDays(1))), new("Notes", portalMarker)
        ]);
        var portalLeave = await WaitForLeaveAsync(portalMarker);
        Assert.True(Guid.TryParse(portalLeave, out var portalLeaveId), "49b: the portal request did not reach HR.Contracts within 90 s.");
        Assert.Equal($"Pending|2|{employee:D}", await Scalar("hr_contracts_db",
            $"""SELECT "Status" || '|' || "DurationDays" || '|' || "EmployeeId" FROM "LeaveRequests" WHERE "Id" = '{portalLeaveId:D}';"""));

        // ---- 3. HR shortens its leave to two days (Sunday and Monday).
        var edited = await s["a-contracts-maker"].PostAsync("edit", $"ui/hr-contracts/LeaveRequests/Edit/{leaveId:D}", $"ui/hr-contracts/LeaveRequests/Edit/{leaveId:D}",
        [
            new("Id", leaveId.ToString("D")), new("EmployeeId", employee.ToString("D")), new("LeaveType", "Annual"), new("StartDate", Date(hrStart)),
            new("EndDate", Date(hrStart.AddDays(1))), new("DurationDays", "2"), new("Notes", hrMarker)
        ]);
        Assert.True("Pending|2" == await Scalar("hr_contracts_db", $"""SELECT "Status" || '|' || "DurationDays" FROM "LeaveRequests" WHERE "Id" = '{leaveId:D}';"""),
            $"49b: the edit was not saved: {AcS2s42TrustBoundaryTests.Alerts(edited)}");

        // ---- 4. Another person approves it.
        var approved = await s["a-contracts-checker"].PostAsync("approve", $"ui/hr-contracts/LeaveRequests/Approve/{leaveId:D}",
            $"ui/hr-contracts/LeaveRequests?employeeId={employee:D}", []);
        Assert.True("Approved" == await Scalar("hr_contracts_db", $"""SELECT "Status" FROM "LeaveRequests" WHERE "Id" = '{leaveId:D}';"""),
            $"49b: the leave was not approved: {AcS2s42TrustBoundaryTests.Alerts(approved)}");

        // ---- The four rows: Shadow, asked by HR.Contracts, in the agency's tenant, decided on facts that match the schema.
        var rows = await PolicyScopeJourney.LedgerAsync($"""l."EntityId" IN ('{leaveId:D}','{portalLeaveId:D}')""");
        Assert.Equal(4, rows.Count);
        var hrCreate = Assert.Single(rows, row => row.Entity == leaveId && row.Action == "Create");
        var hrUpdate = Assert.Single(rows, row => row.Entity == leaveId && row.Action == "Update");
        var hrApprove = Assert.Single(rows, row => row.Entity == leaveId && row.Action == "Approve");
        var portalCreate = Assert.Single(rows, row => row.Entity == portalLeaveId && row.Action == "Create");
        foreach (var row in rows)
        {
            PolicyScopeJourney.AssertShadowRow(row, PolicyDomains.ContractsLeave, row.Action, Contracts);
            Assert.Equal("LeaveRequest", row.Subject);
            Assert.True(row.Decision.Allowed, $"{row.Action}: {row.Decision.DenyReason}");
        }

        Assert.Equal(PolicyScopeJourney.FactsHash(LeaveFacts(3, "Annual", "Create")), hrCreate.FactsHash);
        Assert.Equal(PolicyScopeJourney.FactsHash(LeaveFacts(2, "Annual", "Update")), hrUpdate.FactsHash);
        // The portal sends calendar days; HR.Contracts states working days, as the HR page does.
        Assert.Equal(PolicyScopeJourney.FactsHash(LeaveFacts(2, "Annual", "Create")), portalCreate.FactsHash);
        // The approval says who approves whose leave: not his own, not one he recorded.
        Assert.Equal(PolicyScopeJourney.FactsHash(LeaveFacts(2, "Annual", "Approve", self: false)), hrApprove.FactsHash);
        Assert.NotEqual(PolicyScopeJourney.FactsHash(LeaveFacts(2, "Annual", "Approve", self: true)), hrApprove.FactsHash);
        Assert.DoesNotContain("R-LEAVE-SELF", hrApprove.Decision.MatchedRuleIds);
        Assert.Equal(maker, hrCreate.Actor);
        Assert.Equal(maker, hrUpdate.Actor);
        Assert.Equal(checker, hrApprove.Actor);

        // ---- Negative 1: sixty working days of maternity leave pass (the placeholder limit of thirty is gone, and Shadow blocks nothing).
        var other = await Scalar("hr_core_db", $"""SELECT "Id" FROM "Employees" WHERE "TenantId" = '{TenantA:D}' AND "Id" <> '{employee:D}' ORDER BY "CreatedAt", "Id" LIMIT 1;""");
        Assert.True(Guid.TryParse(other, out var otherEmployee), "49b: no second employee in agency A.");
        var maternityMarker = $"AC-POL-49b-maternity-{run}";
        var maternityStart = first.AddDays(28);
        var maternityEnd = maternityStart.AddDays(81);
        var maternity = await s["a-contracts-maker"].PostAsync("maternity", "ui/hr-contracts/LeaveRequests/Create", "ui/hr-contracts/LeaveRequests/Create",
        [
            new("EmployeeId", otherEmployee.ToString("D")), new("LeaveType", "Maternity"), new("StartDate", Date(maternityStart)),
            new("EndDate", Date(maternityEnd)), new("DurationDays", "60"), new("Notes", maternityMarker)
        ]);
        var maternityLeave = await Scalar("hr_contracts_db", $"""SELECT "Id" || '|' || "Status" || '|' || "DurationDays" FROM "LeaveRequests" WHERE "Notes" = '{maternityMarker}';""");
        Assert.True(maternityLeave.EndsWith("|Pending|60", StringComparison.Ordinal), $"49b: sixty days of maternity leave: '{maternityLeave}' {AcS2s42TrustBoundaryTests.Alerts(maternity)}");
        var maternityRow = Assert.Single(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{maternityLeave.Split('|')[0]}'"""));
        PolicyScopeJourney.AssertShadowRow(maternityRow, PolicyDomains.ContractsLeave, "Create", Contracts);
        Assert.Equal(PolicyScopeJourney.FactsHash(LeaveFacts(60, "Maternity", "Create")), maternityRow.FactsHash);
        // The engine itself no longer refuses it: its own decision is an allow.
        Assert.True(maternityRow.Decision.Allowed, maternityRow.Decision.DenyReason);
        var leaveRules = await PolicyScopeJourney.EffectiveNationalRulesAsync(PolicyDomains.ContractsLeave);
        Assert.DoesNotContain("R-LEAVE-MAX", leaveRules);
        Assert.DoesNotContain("R-LEAVE-LONG", leaveRules);
        Assert.Contains("R-LEAVE-SELF", leaveRules);

        // ---- Negative 2: a portal request over the approved leave is created Rejected, with the reason, and the engine is not asked.
        var overlapMarker = $"AC-POL-49b-ess-overlap-{run}";
        await s["employee-a"].PostAsync("portal-overlap", "ui/hr-core/EssPortal/Leave", "ui/hr-core/EssPortal/Leave",
        [
            new("LeaveType", "Annual"), new("StartDate", Date(hrStart)), new("EndDate", Date(hrStart.AddDays(1))), new("Notes", overlapMarker)
        ]);
        var overlapLeave = await WaitForLeaveAsync(overlapMarker);
        Assert.True(Guid.TryParse(overlapLeave, out var overlapLeaveId), "49b: the overlapping portal request was dropped: no row within 90 s.");
        var overlap = (await Scalar("hr_contracts_db", $"""SELECT "Status" || '|' || replace("Notes", E'\n', ' / ') FROM "LeaveRequests" WHERE "Id" = '{overlapLeaveId:D}';""")).Split('|', 2);
        Assert.Equal("Rejected", overlap[0]);
        Assert.StartsWith(RefusedNotesPrefix + OverlapRefusal, overlap[1], StringComparison.Ordinal);
        Assert.Contains(overlapMarker, overlap[1], StringComparison.Ordinal);
        Assert.Empty(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{overlapLeaveId:D}'"""));
        var overlapTransaction = await Scalar("hr_contracts_db", $"""
            SELECT t."State" FROM "ServiceTransactions" t JOIN "LeaveRequests" l ON l."ServiceTransactionId" = t."Id" WHERE l."Id" = '{overlapLeaveId:D}';
            """);
        // Closed as a rejection by HR leaves one: nothing is left waiting on a request nobody will decide.
        Assert.Equal("Cancelled", overlapTransaction);

        var latency = await PolicyScopeJourney.ClientCallMillisecondsAsync("hr-contracts-web", PolicyDomains.ContractsLeave, started);
        Assert.True(latency.Count >= 5, $"49b: {latency.Count} timed policy calls in hr-contracts-web's log; five were made.");

        s.Steps(
            "Profile: prod-like (PolicyEngine:Contracts.Leave=Shadow on hr-contracts-web and hr-core-web, as the Production files).\n" +
            "Test-only precondition (SQL): an annual leave balance for the portal's employee in the leave's year, when the seed gave none.\n" +
            "1. a-contracts-maker records an annual leave of three working days for the employee of employee-a (the HR page).\n" +
            "2. employee-a asks for two days on the self-service portal (ui/hr-core/EssPortal/Leave): HR.Core publishes ILeaveRequested, HR.Contracts creates the request Pending with its working days.\n" +
            "3. a-contracts-maker edits the first leave down to two days. 4. a-contracts-checker approves it.\n" +
            "5. psql (hr_core_db): four ledger rows, Mode Shadow, CallerClientId svc-hr-contracts, the agency's tenant, each an allow with no «حقل غير مسموح»; the facts hash of each equals the hash of the facts its builder sends (Create 3 days, Create 2 days from the portal, Update 2 days, Approve 2 days with isSelfApproval=false).\n" +
            "6. Negative: sixty working days of maternity leave are recorded (Pending); the engine's own decision is an allow, and the effective Contracts.Leave version holds R-LEAVE-SELF and neither R-LEAVE-MAX nor R-LEAVE-LONG.\n" +
            "7. Negative: a portal request over the approved leave is created Rejected with «مرفوض آلياً: توجد إجازة معتمدة متداخلة مع هذه الفترة.» at the head of its notes; no ledger row for it (the local validation refused before the engine was asked).\n" +
            "8. hr-contracts-web's log: the duration of each Shadow call to HR.Core (policy_client_call ... elapsed_ms=).");
        s.Checks(new
        {
            employee,
            leave = new { id = leaveId, from = Date(hrStart), status = "Approved" },
            portalLeave = new { id = portalLeaveId, from = Date(portalStart), status = "Pending", workingDays = 2 },
            ledger = rows.Select(row => new { row.Entity, row.Action, row.Mode, row.Caller, row.Tenant, row.FactsHash, allowed = row.Decision.Allowed, matched = row.Decision.MatchedRuleIds }),
            maternity = new { row = maternityLeave, allowedByTheEngine = maternityRow.Decision.Allowed, effectiveLeaveRules = leaveRules },
            overlap = new { id = overlapLeaveId, status = overlap[0], notes = overlap[1], ledgerRows = 0, transactionState = overlapTransaction },
            shadowCallMilliseconds = PolicyScopeJourney.LatencySummary(latency)
        });
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 49c

    [SkippableFact]
    public async Task Ac49c_A_processed_card_leaves_a_Shadow_row_and_no_rule_starts_an_approval()
    {
        var r = PolicyScopeJourney.Recorder("49c", "A card request is processed: a Shadow ledger row from svc-idcard; the policy starts no instance; R-ID-PROCESS is gone", "a-idcard, a-idcard-2");
        AcceptanceGate.RequireEnabled(r);
        var started = DateTime.UtcNow.AddSeconds(-5);

        // The approval of the card is PLAN-46's: submitted by one officer, approved by another, through HR.Core's engine.
        var (request, instance) = await CrossServiceJourney.SubmittedCardAsync();
        await CustodyJourney.ApproveFromInboxAsync("a-idcard-2", instance, r);
        Assert.Equal(CrossServiceJourney.CardApproved, await CustodyJourney.PollAsync(() => CrossServiceJourney.CardStatusAsync(request), CrossServiceJourney.CardApproved));
        var cardInstancesBefore = await Count("hr_core_db", $"""SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{request:D}';""");
        var instancesBefore = await Count("hr_core_db", """SELECT count(*) FROM "WorkflowInstances";""");
        Assert.Equal(1, cardInstancesBefore);
        Assert.Empty(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{request:D}'"""));

        using var requester = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard");
        var page = await CustodyJourney.PostFormAsync(requester, CrossServiceJourney.CardPage(request), "ProcessRequest");
        r.CaptureHttp("process", "POST", $"/ui/idcard/CardRequests/ProcessRequest/{request:D}", 200);

        var row = Assert.Single(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{request:D}'"""));
        PolicyScopeJourney.AssertShadowRow(row, PolicyDomains.IDCardLifecycle, "Process", IdCard);
        Assert.Equal("CardRequest", row.Subject);
        Assert.Equal(await PolicyScopeJourney.PersonaUserIdAsync("a-idcard"), row.Actor);
        Assert.Equal(PolicyScopeJourney.FactsHash(PolicyDomainFactsBuilders.ForIDCardRequest(request, null, "New", "Process").Facts), row.FactsHash);
        Assert.True(row.Decision.Allowed, row.Decision.DenyReason);

        // Negative: the policy started nothing for the card (its one instance is the approval of PLAN-46), and holds no rule that could.
        Assert.DoesNotContain(PolicyEffect.TriggerWorkflow, row.Decision.Effects);
        Assert.Equal(cardInstancesBefore, await Count("hr_core_db", $"""SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{request:D}';"""));
        Assert.Equal(instancesBefore, await Count("hr_core_db", """SELECT count(*) FROM "WorkflowInstances";"""));
        var cardRules = await PolicyScopeJourney.EffectiveNationalRulesAsync(PolicyDomains.IDCardLifecycle);
        Assert.DoesNotContain("R-ID-PROCESS", cardRules);
        Assert.Equal(0, await Count("hr_core_db", $"""
            SELECT count(*) FROM "PolicyRules" r JOIN "PolicyTemplateVersions" v ON v."Id" = r."PolicyTemplateVersionId" JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
            WHERE t."Domain" = '{PolicyDomains.IDCardLifecycle}' AND v."Status" = 'Published' AND r."ThenJson" LIKE '%TriggerWorkflow%'
              AND (v."EffectiveTo" IS NULL OR v."EffectiveTo" > now());
            """));
        var requestStatus = await CrossServiceJourney.CardStatusAsync(request);
        var latency = await PolicyScopeJourney.ClientCallMillisecondsAsync("idcard-web", PolicyDomains.IDCardLifecycle, started);
        Assert.NotEmpty(latency);

        r.WriteScenarioDbChecks(new
        {
            request,
            approvalInstance = instance,
            ledger = new { row.Action, row.Mode, row.Caller, row.Tenant, row.FactsHash, allowed = row.Decision.Allowed, explanations = row.Decision.Explanations },
            cardInstancesBefore,
            cardInstancesAfter = cardInstancesBefore,
            instancesBefore,
            effectiveCardRules = cardRules,
            requestStatusAfterProcess = requestStatus,
            alerts = AcS2s42TrustBoundaryTests.Alerts(page),
            shadowCallMilliseconds = PolicyScopeJourney.LatencySummary(latency)
        });
        r.WriteScenarioSteps(
            "1. a-idcard submits a card request and a-idcard-2 approves it from the inbox (PLAN-46): Approved, one HR.Core instance.\n" +
            "2. a-idcard processes the approved request: one ledger row for it, Mode Shadow, CallerClientId svc-idcard, the agency's tenant, an allow; its facts hash is the hash of {purpose, action} as the builder sends them.\n" +
            "3. Negative: the row carries no TriggerWorkflow; the request still has exactly its one approval instance and the total of instances did not move (psql); the effective IDCard.Lifecycle version holds no R-ID-PROCESS and no rule that triggers a workflow.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 49d

    private const string CapitalCategory = "أصول ثابتة";
    private const string MissingProofMessage = "أصل مميز: أرفق ملف المحضر أو اكتب مرجع دليل";

    [SkippableFact]
    public async Task Ac49d_A_privileged_handover_goes_as_in_Off_with_a_Shadow_row_and_creating_an_asset_triggers_nothing()
    {
        var r = PolicyScopeJourney.Recorder("49d", "Custody in Shadow: evidence and the approval chain as in Off, the server's effects in the ledger; no TriggerWorkflow on Create", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);
        var started = DateTime.UtcNow.AddSeconds(-5);
        var maker = await PolicyScopeJourney.PersonaUserIdAsync("a-logistics-maker");
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-maker");

        // ---- A privileged asset (setup by SQL, as PLAN-42 and PLAN-45 do); the handover goes through the page.
        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        var (_, withoutEvidence) = await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, AcceptanceSeedIds.EmpBr1Id);
        Assert.Equal(0, await Count("admin_db", $"""SELECT count(*) FROM "AssetAssignments" WHERE "AssetId" = '{asset:D}';"""));
        // The form's own message (AssetDetailsFormValidation.MissingProofMessage), shown beside the handover form.
        Assert.Contains(MissingProofMessage, withoutEvidence, StringComparison.Ordinal);

        var (_, withEvidence) = await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, AcceptanceSeedIds.EmpBr1Id, evidence: "AC-POL-49d");
        var (assignment, instance) = await CustodyJourney.AssignmentOfAsync(asset);
        Assert.True(instance != Guid.Empty, $"The handover started no approval: {AcS2s42TrustBoundaryTests.Alerts(withEvidence)}");
        Assert.Equal(CustodyJourney.Pending, await CustodyJourney.AssignmentStatusAsync(assignment));
        Assert.Equal("ASSET-ASSIGN-APPROVAL", await RecruitmentAcceptance.WorkflowCodeAsync(instance.ToString("D")));

        var assign = Assert.Single(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{assignment:D}'"""));
        PolicyScopeJourney.AssertShadowRow(assign, PolicyDomains.AssetsCustody, "Assign", Logistics);
        Assert.Equal("AssetAssignment", assign.Subject);
        Assert.Equal(maker, assign.Actor);
        // What the server would have decided: evidence, and the handover approval.
        Assert.True(assign.Decision.Allowed, assign.Decision.DenyReason);
        Assert.Contains(PolicyEffect.RequireEvidence, assign.Decision.Effects);
        Assert.Contains(PolicyEffect.TriggerWorkflow, assign.Decision.Effects);
        Assert.Equal("ASSET-ASSIGN-APPROVAL", assign.Decision.GetWorkflowCode());

        // ---- Negative: a capital asset worth 6000 is created through the page.
        var clock = await PolicyScopeJourney.ClockAsync();
        var name = $"AC-POL-49d {Guid.NewGuid():N}"[..28];
        var createPage = "ui/admin-logistics/Assets/CreateLegacy";
        var (_, form) = await CustodyJourney.OpenAsync(http, createPage);
        using var posted = await http.PostAsync(createPage, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AcMsg40AtomicPublishingTests.RequireAntiforgeryToken(form),
            ["Name"] = name,
            ["Category"] = CapitalCategory,
            ["Brand"] = "PLAN-49",
            ["PurchaseDate"] = DateTime.UtcNow.Date.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Value"] = "6000",
            ["Status"] = "InStorage",
            ["LegacyEntryReason"] = "PLAN-49 acceptance: a legacy capital asset entered to read the engine's Shadow decision on creation"
        }));
        r.CaptureHttp("create-capital-asset", "POST", "/" + createPage, (int)posted.StatusCode);
        var createdAsset = await Scalar("admin_db", $"""SELECT "Id" || '|' || "IsPrivileged"::text || '|' || "Value"::text FROM "Assets" WHERE "Name" = '{name}';""");
        Assert.True(createdAsset.Split('|').Length == 3, $"The asset was not created: {(int)posted.StatusCode} {AcS2s42TrustBoundaryTests.Alerts(WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync()))}");
        // As in Off: the static thresholds say a capital asset above 5000 is privileged.
        Assert.Equal("true", createdAsset.Split('|')[1]);

        var creation = await PolicyScopeJourney.LedgerAsync($"""l."Domain" = '{PolicyDomains.AssetsCustody}' AND l."OccurredAt" >= '{clock}' AND l."Action" IN ('Classify','Create')""");
        var classify = Assert.Single(creation, row => row.Action == "Classify");
        var create = Assert.Single(creation, row => row.Action == "Create");
        foreach (var row in creation)
        {
            PolicyScopeJourney.AssertShadowRow(row, PolicyDomains.AssetsCustody, row.Action, Logistics);
            Assert.Equal("Asset", row.Subject);
            Assert.Equal(maker, row.Actor);
            Assert.True(row.Decision.Allowed, row.Decision.DenyReason);
        }

        Assert.Equal("true", classify.Decision.GetSetFieldValue("isPrivileged"));
        // The asset rules are bound to the handover: creating a privileged asset no longer fires the handover approval.
        Assert.DoesNotContain(PolicyEffect.TriggerWorkflow, create.Decision.Effects);
        Assert.DoesNotContain(create.Decision.EffectDetails, e => e.Effect == PolicyEffect.TriggerWorkflow);
        Assert.Equal(0, await Count("hr_core_db", $"""
            SELECT count(*) FROM "PolicyEnforcementLedgers" WHERE "Domain" = '{PolicyDomains.AssetsCustody}' AND "Action" = 'Create'
              AND "OccurredAt" >= '{clock}' AND "DecisionJson" LIKE '%workflowCode%';
            """));
        var custodyRules = await Sql("hr_core_db", $"""
            SELECT r."RuleKey" || ' ' || r."WhenJson" FROM "PolicyRules" r WHERE r."ThenJson" LIKE '%TriggerWorkflow%' AND r."PolicyTemplateVersionId" = (
                SELECT v."Id" FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
                WHERE t."Domain" = '{PolicyDomains.AssetsCustody}' AND t."Scope" = 'National' AND v."Status" = 'Published'
                  AND (v."EffectiveFrom" IS NULL OR v."EffectiveFrom" <= now()) AND (v."EffectiveTo" IS NULL OR v."EffectiveTo" > now())
                ORDER BY v."VersionNumber" DESC LIMIT 1)
            ORDER BY r."RuleKey";
            """);
        var triggers = custodyRules.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(triggers);
        Assert.All(triggers, rule => Assert.Contains(@"{""field"":""action"",""operator"":""Equals"",""value"":""Assign""}", rule, StringComparison.Ordinal));

        var latency = await PolicyScopeJourney.ClientCallMillisecondsAsync("admin-logistics-web", PolicyDomains.AssetsCustody, started);
        Assert.True(latency.Count >= 4, $"49d: {latency.Count} timed policy calls in admin-logistics-web's log; four were made.");

        r.WriteScenarioDbChecks(new
        {
            handover = new { asset, assignment, instance, status = "PendingApproval", ledger = new { assign.Mode, assign.Caller, assign.Tenant, effects = assign.Decision.Effects.Select(e => e.ToString()), workflow = assign.Decision.GetWorkflowCode(), matched = assign.Decision.MatchedRuleIds } },
            creation = new
            {
                asset = createdAsset,
                classify = new { classify.Mode, classify.Caller, isPrivileged = classify.Decision.GetSetFieldValue("isPrivileged"), matched = classify.Decision.MatchedRuleIds },
                create = new { create.Mode, create.Caller, effects = create.Decision.Effects.Select(e => e.ToString()), matched = create.Decision.MatchedRuleIds },
                nationalTriggerRules = triggers
            },
            shadowCallMilliseconds = PolicyScopeJourney.LatencySummary(latency)
        });
        r.WriteScenarioSteps(
            "Profile: prod-like (PolicyEngine:Assets.Custody=Shadow on admin-logistics-web and hr-core-web, as the Production files).\n" +
            "1. a-logistics-maker hands over a privileged asset without evidence: refused with the evidence message, no assignment. With evidence: the assignment is PendingApproval with an ASSET-ASSIGN-APPROVAL instance, as in Off.\n" +
            "2. psql (hr_core_db): one ledger row for the assignment, Mode Shadow, CallerClientId svc-admin-logistics, the agency's tenant; the server's decision carries RequireEvidence and TriggerWorkflow ASSET-ASSIGN-APPROVAL.\n" +
            "3. Negative: a-logistics-maker creates a legacy asset of the category «أصول ثابتة» worth 6000: it is privileged, as the static thresholds of Off say. The ledger holds one Classify row (the server too says isPrivileged=true) and one Create row, both Shadow; the Create row carries no TriggerWorkflow, and no Create row since the creation names a workflow.\n" +
            "4. psql: every rule of the effective national custody version that triggers a workflow is bound to action Equals Assign.\n" +
            "5. admin-logistics-web's log: the duration of each Shadow call to HR.Core.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 49e

    private static async Task<Guid> ImportSampleChangeAsync(HttpClient central, EvidenceRecorder recorder, string step)
    {
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-sample.csv");
        await using var csv = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var file = new StreamContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(file, "file", "job-catalog-sample.csv");
        using var response = await central.PostAsync("api/hr-core/api/establishment/job-catalog/import?mode=Standard", upload);
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp(step, "POST", "/api/hr-core/api/establishment/job-catalog/import?mode=Standard", (int)response.StatusCode);
        Assert.True(response.IsSuccessStatusCode, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("changeRequestId").GetGuid();
    }

    [SkippableFact]
    public async Task Ac49e_An_establishment_submission_records_the_engines_decision_and_a_branch_manager_is_refused_before_it()
    {
        var r = PolicyScopeJourney.Recorder("49e", "Establishment in Shadow: the central submission's ledger row with the actor's real scope; a branch manager is refused before the engine; the deny rule by simulation", "central-a, br1-mgr");
        AcceptanceGate.RequireEnabled(r);
        var centralId = await PolicyScopeJourney.PersonaUserIdAsync("central-a");
        using var central = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("central-a"), Guid.NewGuid().ToString("D"));
        using var branch = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("br1-mgr"), Guid.NewGuid().ToString("D"));

        // ---- The central submission goes on by its workflow, and the engine's decision is recorded.
        var change = await EstablishmentChangeFlow.SubmitSampleChangeAsync(central, r, "AC-POL-49e");
        var state = (await Scalar("hr_core_db", $"""
            SELECT c."Status" || '|' || c."ChangeType" || '|' || d."Code" || '|' || i."Status"
            FROM "EstablishmentChangeRequests" c JOIN "WorkflowInstances" i ON i."Id" = c."WorkflowInstanceId" JOIN "WorkflowDefinitions" d ON d."Id" = i."DefinitionId"
            WHERE c."Id" = '{change:D}';
            """)).Split('|');
        Assert.Equal(["PendingApproval", "ESTABLISHMENT-CHANGE-APPROVAL", "InProgress"], [state[0], state[2], state[3]]);

        var row = Assert.Single(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{change:D}'"""));
        PolicyScopeJourney.AssertShadowRow(row, PolicyDomains.CoreEstablishment, "SubmitForApproval", caller: null);
        Assert.Equal("EstablishmentChangeRequest", row.Subject);
        Assert.Equal(centralId, row.Actor);
        Assert.True(row.Decision.Allowed, row.Decision.DenyReason);
        Assert.Contains("R-EST-SUBMIT", row.Decision.MatchedRuleIds);
        Assert.DoesNotContain("R-EST-DENY-BRANCH", row.Decision.MatchedRuleIds);
        Assert.Equal("ESTABLISHMENT-CHANGE-APPROVAL", row.Decision.GetWorkflowCode());
        // The facts say who submitted: an actor not limited to a branch (the request was a draft when the engine was asked).
        Dictionary<string, string> Facts(bool branchScoped) =>
            PolicyDomainFactsBuilders.ForEstablishmentChange(change, null, "SubmitForApproval", state[1], "Draft", actorIsBranchScoped: branchScoped).Facts;
        Assert.Equal(PolicyScopeJourney.FactsHash(Facts(false)), row.FactsHash);
        Assert.NotEqual(PolicyScopeJourney.FactsHash(Facts(true)), row.FactsHash);

        // ---- Negative: a branch manager cannot submit; the guard refuses him before the service, so the engine is not asked.
        var draft = await ImportSampleChangeAsync(central, r, "import-second-change");
        Assert.Equal("Draft", await Scalar("hr_core_db", $"""SELECT "Status" FROM "EstablishmentChangeRequests" WHERE "Id" = '{draft:D}';"""));
        var submitUrl = $"api/hr-core/api/establishment/changes/{draft:D}/submit";
        using var refused = await branch.PostAsJsonAsync(submitUrl, new { legalSourceId = Syr001LegislationIds.Law50_2004, decisionReference = "AC-POL-49e-branch" });
        r.CaptureHttp("submit-as-branch-manager", "POST", "/" + submitUrl, (int)refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Draft|", await Scalar("hr_core_db", $"""SELECT "Status" || '|' || COALESCE("WorkflowInstanceId"::text, '') FROM "EstablishmentChangeRequests" WHERE "Id" = '{draft:D}';"""));
        Assert.Empty(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{draft:D}'"""));

        // ---- The deny rule matches a branch-scoped actor: asked of the engine directly, on the facts the service would send.
        using var policy = await PolicyClientAsync("central-a");
        async Task<JsonElement> EvaluateAsync(bool branchScoped)
        {
            var (status, body, text) = await SendAsync(policy, HttpMethod.Post, "api/hr-core/api/policies/evaluate", new
            {
                domain = PolicyDomains.CoreEstablishment,
                subjectType = "EstablishmentChangeRequest",
                action = "SubmitForApproval",
                facts = Facts(branchScoped)
            });
            Assert.True(status == HttpStatusCode.OK, $"evaluate -> {(int)status} {text}");
            return body;
        }

        var asBranch = await EvaluateAsync(branchScoped: true);
        Assert.False(asBranch.GetProperty("allowed").GetBoolean());
        Assert.Contains("R-EST-DENY-BRANCH", Strings(asBranch, "matchedRuleIds"));
        Assert.Equal("تغييرات الملاك للموارد البشرية المركزية فقط", asBranch.GetProperty("denyReason").GetString());
        var asCentral = await EvaluateAsync(branchScoped: false);
        Assert.True(asCentral.GetProperty("allowed").GetBoolean());
        Assert.DoesNotContain("R-EST-DENY-BRANCH", Strings(asCentral, "matchedRuleIds"));

        r.WriteScenarioDbChecks(new
        {
            submitted = new { change, status = state[0], changeType = state[1], workflow = state[2], ledger = new { row.Mode, row.Caller, row.Tenant, row.Actor, row.FactsHash, matched = row.Decision.MatchedRuleIds, allowed = row.Decision.Allowed } },
            branchManager = new { change = draft, status = (int)refused.StatusCode, stillDraft = true, ledgerRows = 0 },
            simulation = new { branchScoped = new { allowed = false, matched = Strings(asBranch, "matchedRuleIds") }, central = new { allowed = true, matched = Strings(asCentral, "matchedRuleIds") } }
        });
        r.WriteScenarioSteps(
            "Adapted from the plan (PLAN-49 decision 7): both establishment controllers refuse a branch-scoped actor with 403 before the service, and that guard stays. So the recorded submission is the central actor's, and the deny rule is shown on the engine itself.\n" +
            "1. central-a imports the sample job catalog and submits the change: PendingApproval with an ESTABLISHMENT-CHANGE-APPROVAL instance (the submission goes on by its workflow).\n" +
            "2. psql (hr_core_db): one ledger row for the change, Mode Shadow, no caller client (the domain runs inside HR.Core), the agency's tenant, the actor central-a; R-EST-SUBMIT matched and R-EST-DENY-BRANCH did not; the facts hash is the hash of the builder's facts with hasOrgScope=false (and not with true).\n" +
            "3. Negative: br1-mgr submits a second, draft change -> 403; it stays a draft with no instance, and no ledger row is written for it.\n" +
            "4. POST api/policies/evaluate on the same facts with hasOrgScope=true: denied by R-EST-DENY-BRANCH with its reason; with false: allowed, the deny rule not matched.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 49g

    private static object Rule(string key, string when, string then) =>
        new { ruleKey = key, name = "AC-POL-49 " + key, priority = 40, whenJson = when, thenJson = then };

    private static async Task<Guid> OpenDomainDraftAsync(HttpClient author, string domain, string subjectType)
    {
        // One draft per template: a draft an earlier scenario (or the seed) left open is discarded first.
        foreach (var open in (await Sql("hr_core_db", $"""
                     SELECT v."Id" FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
                     WHERE t."TenantId" = '{TenantA:D}' AND t."Domain" = '{domain}' AND t."Scope" = 'TenantOverride' AND v."Status" = 'Draft';
                     """)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (discarded, _, discardText) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{open}/discard");
            Assert.True(discarded == HttpStatusCode.OK, $"discard {open} -> {(int)discarded} {discardText}");
        }

        var (status, body, text) = await SendAsync(author, HttpMethod.Post, "api/hr-core/api/policies/drafts",
            new { domain, subjectType, name = $"AC-POL-49g {domain}" });
        Assert.True(status == HttpStatusCode.OK, $"draft of {domain} -> {(int)status} {text}");
        return body.GetProperty("id").GetGuid();
    }

    [SkippableFact]
    public async Task Ac49g_A_rule_inside_its_domains_capabilities_is_accepted_and_one_outside_them_is_refused()
    {
        var r = PolicyScopeJourney.Recorder("49g", "Capabilities: a custody rule bound to Assign is accepted; an unconsumed effect, an orphan workflow and a foreign field are refused when added", "central-a");
        AcceptanceGate.RequireEnabled(r);
        using var author = await PolicyClientAsync("central-a");

        var custody = await OpenDomainDraftAsync(author, PolicyDomains.AssetsCustody, "Asset");
        var leave = await OpenDomainDraftAsync(author, PolicyDomains.ContractsLeave, "LeaveRequest");
        var card = await OpenDomainDraftAsync(author, PolicyDomains.IDCardLifecycle, "CardRequest");
        try
        {
            async Task<long> RulesAsync(Guid version) =>
                await Count("hr_core_db", $"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{version:D}';""");
            var before = new Dictionary<Guid, long> { [custody] = await RulesAsync(custody), [leave] = await RulesAsync(leave), [card] = await RulesAsync(card) };

            // Positive: evidence for a handover, bound to the action that consumes it.
            var (accepted, _, acceptedText) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{custody:D}/rules",
                Rule("ACC-P49-EVIDENCE",
                    """{"all":[{"field":"action","operator":"Equals","value":"Assign"},{"field":"category","operator":"Equals","value":"AC-POL-49g"}]}""",
                    """[{"effect":"RequireEvidence"}]"""));
            r.CaptureHttp("custody-evidence-assign", "POST", $"/api/hr-core/api/policies/versions/{custody:D}/rules", (int)accepted, acceptedText);
            Assert.True(accepted == HttpStatusCode.OK, $"RequireEvidence bound to Assign -> {(int)accepted} {acceptedText}");
            Assert.Equal(before[custody] + 1, await RulesAsync(custody));

            // Negatives: each is outside what the catalog gives the domain.
            var refusals = new List<object>();
            foreach (var (version, domain, key, when, then, code, path) in new[]
                     {
                         (leave, PolicyDomains.ContractsLeave, "ACC-P49-DUAL",
                             """{"all":[{"field":"action","operator":"Equals","value":"Approve"}]}""",
                             """[{"effect":"RequireDualApproval"}]""",
                             "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect"),
                         (card, PolicyDomains.IDCardLifecycle, "ACC-P49-ORPHAN",
                             """{"all":[{"field":"action","operator":"Equals","value":"Process"}]}""",
                             """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ID-CARD-ISSUANCE"}}]""",
                             "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect"),
                         (custody, PolicyDomains.AssetsCustody, "ACC-P49-VALUE",
                             """{"all":[{"field":"action","operator":"Equals","value":"Classify"}]}""",
                             """[{"effect":"SetField","params":{"field":"value","value":"1"}}]""",
                             "THEN_SETFIELD_NOT_ALLOWED", "$.then[0].params.field")
                     })
            {
                var rulesBefore = await RulesAsync(version);
                var (status, body, text) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{version:D}/rules", Rule(key, when, then));
                r.CaptureHttp($"refused-{key}", "POST", $"/api/hr-core/api/policies/versions/{version:D}/rules", (int)status, text);
                Assert.True(status == HttpStatusCode.BadRequest, $"{key} -> {(int)status} {text}");
                var issues = body.GetProperty("issues").EnumerateArray()
                    .Select(i => (Code: i.GetProperty("code").GetString()!, Path: i.GetProperty("jsonPath").GetString()!, Message: i.GetProperty("messageAr").GetString()!)).ToList();
                var issue = Assert.Single(issues, i => i.Code == code && i.Path == path);
                Assert.Contains(domain, issue.Message, StringComparison.Ordinal);
                Assert.Matches("[؀-ۿ]", body.GetProperty("error").GetString());
                Assert.Equal(rulesBefore, await RulesAsync(version));
                refusals.Add(new { domain, key, status = (int)status, code, path, message = issue.Message });
            }

            r.WriteScenarioDbChecks(new { drafts = new { custody, leave, card }, accepted = new { key = "ACC-P49-EVIDENCE", status = (int)accepted }, refusals });
            r.WriteScenarioSteps(
                "1. central-a opens a draft of agency A's override in Assets.Custody, Contracts.Leave and IDCard.Lifecycle (POST api/policies/drafts).\n" +
                "2. Positive: a custody rule «action Equals Assign and category Equals AC-POL-49g -> RequireEvidence» is accepted (one more rule in the draft).\n" +
                "3. Negatives, each 400 with an Arabic message that names the domain, and no new row (psql): RequireDualApproval in Contracts.Leave (THEN_EFFECT_NOT_ALLOWED: nobody consumes it); TriggerWorkflow ID-CARD-ISSUANCE in IDCard.Lifecycle (THEN_EFFECT_NOT_ALLOWED: the card's approval starts through the owner's contract, never from a rule); SetField value in Assets.Custody (THEN_SETFIELD_NOT_ALLOWED: only isPrivileged may be set).\n" +
                "4. The three drafts are discarded: nothing of this scenario is published.");
            r.WriteResult("Pass");
        }
        finally
        {
            foreach (var version in new[] { custody, leave, card })
                await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{version:D}/discard");
        }
    }
}
