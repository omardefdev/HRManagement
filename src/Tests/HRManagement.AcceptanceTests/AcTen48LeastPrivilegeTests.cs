using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-TEN-48 (PLAN-48 P-3) on the least-privilege stack: HR.Core connects as hr_core_app (no superuser, no BYPASSRLS) under forced
/// row security, and national_shared_read was applied by the stack's own migration path. Agency B reads the national settings
/// through the running services: its recruitment snapshot holds the national rules and they decide; its code policy is the
/// national one, with its prefix. The start of a national workflow definition from an agency session is proved on PostgreSQL by
/// Ten048NationalRlsIntegrationTests (OwnOrNational through EF as a non-owner role).
/// </summary>
[RequiredAcceptance]
public class AcTen48LeastPrivilegeTests
{
    /// <summary>psql as the administrator in the hrm-leastpriv stack's postgres (the acceptance helper looks in hrm-acceptance).</summary>
    private static Task<string> Sql(string sql) => AcOps38FirstBootTests.AdminSqlAsync("hr_core_db", sql);

    private static async Task<HttpClient> ClientAsync(string personaId)
    {
        var persona = AcceptanceHarness.RequirePersona(personaId);
        var (_, correlation) = await AcceptanceHarness.LoginAsync(persona);
        return await AcceptanceHarness.CreatePolicyLifecycleClientAsync(persona, correlation);
    }

    private static async Task<(HttpStatusCode Status, string Text)> SendAsync(HttpClient http, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        using var response = await http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task An_agency_reads_the_national_settings_as_hr_core_app_under_forced_row_security()
    {
        var recorder = new EvidenceRecorder("AC-TEN-48", Guid.NewGuid().ToString("D")) { ScenarioKey = "48-leastpriv" };
        recorder.WriteMetadata("The national read through the running services, as hr_core_app under forced row security", "b-approver, outsider-b");
        AcceptanceGate.RequireEnabled(recorder);

        // The stack really enforces row security for HR.Core, and the migration path applied the read policy on the ten tables.
        var role = (await Sql("""SELECT rolsuper || '|' || rolbypassrls FROM pg_roles WHERE rolname = 'hr_core_app';""")).Trim();
        Assert.Equal("false|false", role);
        var sessions = long.Parse((await Sql("""SELECT count(*) FROM pg_stat_activity WHERE datname = 'hr_core_db' AND usename = 'hr_core_app';""")).Trim(), CultureInfo.InvariantCulture);
        Assert.True(sessions > 0, "HR.Core holds no hr_core_app session on hr_core_db");
        var policies = long.Parse((await Sql("""SELECT count(*) FROM pg_policies WHERE policyname = 'national_shared_read' AND cmd = 'SELECT';""")).Trim(), CultureInfo.InvariantCulture);
        Assert.Equal(10, policies);
        var forced = (await Sql("""SELECT relforcerowsecurity FROM pg_class WHERE relname = 'PolicyTemplates' AND relkind = 'r';""")).Trim();
        Assert.Equal("t", forced);

        using var approver = await ClientAsync("b-approver");
        using var author = await ClientAsync("outsider-b");

        var (snapshot, snapshotText) = await SendAsync(approver, HttpMethod.Get, $"api/hr-core/api/policies/snapshots/{PolicyDomains.CoreRecruitment}");
        Assert.True(snapshot == HttpStatusCode.OK, $"snapshot -> {(int)snapshot} {snapshotText}");
        var rules = JsonDocument.Parse(snapshotText).RootElement.GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.Contains("R-REQ-SUBMIT", rules);
        Assert.Contains("R-REQ-PUBLISH-DENY", rules);

        // The gateway does not route an approver to evaluate, so agency B's hr-core user asks for the decision.
        var (evaluated, evaluatedText) = await SendAsync(author, HttpMethod.Post, "api/hr-core/api/policies/evaluate", new
        {
            domain = PolicyDomains.CoreRecruitment,
            subjectType = "RecruitmentRequisition",
            action = "Publish",
            facts = new Dictionary<string, string> { ["action"] = "Publish", ["requisitionStatus"] = "Draft", ["positionVacant"] = "true" }
        });
        Assert.True(evaluated == HttpStatusCode.OK, $"evaluate -> {(int)evaluated} {evaluatedText}");
        var decision = JsonDocument.Parse(evaluatedText).RootElement;
        Assert.False(decision.GetProperty("allowed").GetBoolean());
        Assert.Contains("R-REQ-PUBLISH-DENY", decision.GetProperty("matchedRuleIds").EnumerateArray().Select(e => e.GetString()));

        var nextYear = DateTime.UtcNow.Year + 1;
        var (preview, previewText) = await SendAsync(author, HttpMethod.Post, "api/hr-core/api/codes/preview", new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.RecruitmentRequisition,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BudgetYear"] = nextYear.ToString(CultureInfo.InvariantCulture) },
            Preview = true
        });
        Assert.True(preview == HttpStatusCode.OK, $"preview -> {(int)preview} {previewText}");
        var code = JsonDocument.Parse(previewText).RootElement.GetProperty("code").GetString()!;
        Assert.Matches($@"^REQ-{nextYear}-\d{{5}}$", code);

        recorder.WriteScenarioDbChecks(new { hrCoreApp = role, hrCoreAppSessions = sessions, nationalSharedReadPolicies = policies, policyTemplatesForced = forced, rules, denied = true, preview = code });
        recorder.WriteScenarioSteps(
            "1. By psql: hr_core_app is neither superuser nor BYPASSRLS, HR.Core holds sessions as hr_core_app, national_shared_read (FOR SELECT) is on the ten tables, PolicyTemplates is FORCE ROW LEVEL SECURITY.\n" +
            "2. b-approver (agency B) reads GET api/policies/snapshots/Core.Recruitment: the national rules; outsider-b (agency B) evaluates a publish of an unapproved requisition: denied by the national R-REQ-PUBLISH-DENY.\n" +
            "3. outsider-b (agency B) previews a requisition number for the next budget year: REQ-<year>-##### from the national code policy.");
        recorder.WriteResult("Pass");
    }
}
