using System.Net;
using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-51 (P8) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password is
/// read from the server's .env into memory only; evidence goes under <c>artifacts/plan51/vps</c> and is never committed. The checks create
/// nothing on the server: their POSTs are refused before any write (404 or 403), and onboarding is a dry run that the gateway refuses.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private static EvidenceRecorder StartIam51(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"iam51-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan51", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static async Task<(int Status, string Body)> Iam51SendAsync(HttpClient client, HttpMethod method, string path, string? correlation = null)
    {
        using var request = new HttpRequestMessage(method, $"https://{AppDomain}{path}");
        if (correlation is not null)
            request.Headers.Add("X-Correlation-ID", correlation);
        if (method == HttpMethod.Post)
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "1" });
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A column of the gateway's audit row of a correlation id, once its queue has written it (within 30 s).</summary>
    private static async Task<string> Iam51AuditAsync(string correlation, string column)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var value = Sql("gateway_audit_db",
                $"select coalesce({column}::text, '') from gateway_audit_entries where \"CorrelationId\" = '{correlation}' order by \"Id\" desc limit 1").SingleOrDefault() ?? "";
            if (value.Length > 0 || DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
                return value;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    [SkippableFact]
    public async Task AC_IAM_51_vps_self_service_is_closed_to_everything_but_its_own_controllers()
    {
        var recorder = StartIam51("ess", "Demo server: POST /ui/ess/Shifts/Create and GET /ui/ess/api/internal/.../lifecycle-flags answer 404 with Denied:EssNotExposed; /ui/ess/EssPortal reaches the self-service policy (403 for a user without hr-employee)");
        var password = DemoPassword();
        var (admin, _) = await SignInAsync("hradmin", password);
        var (compliance, _) = await SignInAsync("e2e-compliance", password);
        using (admin)
        using (compliance)
        {
            var employee = Sql("hr_core_db", "select \"Id\" from \"Employees\" order by \"NationalId\", \"Id\" limit 1").Single();
            var createCorrelation = Guid.NewGuid().ToString("N");
            var flagsCorrelation = Guid.NewGuid().ToString("N");
            var shiftsBefore = Sql("hr_core_db", "select count(*) from \"Shifts\"").Single();
            var (create, _) = await Iam51SendAsync(admin, HttpMethod.Post, "/ui/ess/Shifts/Create", createCorrelation);
            var (flags, _) = await Iam51SendAsync(admin, HttpMethod.Get, $"/ui/ess/api/internal/employees/{employee}/lifecycle-flags", flagsCorrelation);
            var (portal, _) = await Iam51SendAsync(compliance, HttpMethod.Get, "/ui/ess/EssPortal");
            Assert.Equal(404, create);
            Assert.Equal(404, flags);
            Assert.Equal(403, portal);
            Assert.Equal(shiftsBefore, Sql("hr_core_db", "select count(*) from \"Shifts\"").Single());
            var createDecision = await Iam51AuditAsync(createCorrelation, "\"Decision\"");
            var flagsDecision = await Iam51AuditAsync(flagsCorrelation, "\"Decision\"");
            Assert.Equal("Denied:EssNotExposed", createDecision);
            Assert.Equal("Denied:EssNotExposed", flagsDecision);
            recorder.WriteScenarioDbChecks(new { essShiftsCreate = create, essInternalFlags = flags, essPortalWithoutEmployeeRole = portal, createDecision, flagsDecision });
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_51_vps_the_audit_is_for_its_owners_with_their_own_agencys_rows()
    {
        var recorder = StartIam51("audit", "Demo server: e2e-hr-admin gets 403 on /Home/Audit; e2e-compliance gets 200 and only rows of its own agency");
        var password = DemoPassword();
        var complianceTenant = Sql("keycloak_db",
            "select a.value from user_attribute a join user_entity u on u.id = a.user_id join realm r on r.id = u.realm_id " +
            "where r.name = 'hrmanagement' and u.username = 'e2e-compliance' and a.name = 'tenant_id'").SingleOrDefault() ?? "";
        AcceptanceGate.RequireData(recorder, Guid.TryParse(complianceTenant, out _), "المستخدم بلا tenant_id", "e2e-compliance has no tenant_id on the live realm.");

        var (hrAdmin, _) = await SignInAsync("e2e-hr-admin", password);
        var (compliance, _) = await SignInAsync("e2e-compliance", password);
        using (hrAdmin)
        using (compliance)
        {
            var (adminAudit, _) = await Iam51SendAsync(hrAdmin, HttpMethod.Get, "/Home/Audit");
            var (adminSearch, _) = await Iam51SendAsync(hrAdmin, HttpMethod.Get, "/ops/audit-search");
            var (complianceAudit, _) = await Iam51SendAsync(compliance, HttpMethod.Get, "/Home/Audit");
            var (complianceSearch, body) = await Iam51SendAsync(compliance, HttpMethod.Get, "/ops/audit-search?timeRange=24h");
            Assert.Equal(403, adminAudit);
            Assert.Equal(403, adminSearch);
            Assert.Equal(200, complianceAudit);
            Assert.Equal(200, complianceSearch);

            using var doc = JsonDocument.Parse(body);
            var ids = doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
            var foreign = ids.Count == 0
                ? "0"
                : Sql("gateway_audit_db",
                    $"select count(*) from gateway_audit_entries where \"Id\" in ({string.Join(',', ids)}) and \"TenantId\" is distinct from '{complianceTenant}'").Single();
            var otherRowsExist = Sql("gateway_audit_db",
                $"select count(*) from gateway_audit_entries where \"OccurredAtUtc\" > now() - interval '24 hours' and \"TenantId\" is distinct from '{complianceTenant}'").Single();
            Assert.Equal("0", foreign);
            recorder.WriteScenarioDbChecks(new { adminAudit, adminSearch, complianceAudit, complianceSearch, rowsSeen = ids.Count, rowsOfAnotherAgencyOrNone = foreign, rowsOutsideItsAgencyIn24h = otherRowsExist });
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_51_vps_onboarding_is_the_platforms_and_apply_file_is_gone()
    {
        var recorder = StartIam51("onboarding", "Demo server: hradmin gets 403 on api/onboarding/agencies/apply?dryRun=true, and apply-file answers 404");
        var password = DemoPassword();
        var (admin, _) = await SignInAsync("hradmin", password);
        using (admin)
        {
            var (apply, _) = await Iam51SendAsync(admin, HttpMethod.Post, "/api/hr-core/api/onboarding/agencies/apply?dryRun=true");
            var (applyFile, _) = await Iam51SendAsync(admin, HttpMethod.Post, "/api/hr-core/api/onboarding/agencies/apply-file?path=/etc/hostname&dryRun=true");
            Assert.Equal(403, apply);
            Assert.Equal(404, applyFile);
            recorder.WriteScenarioDbChecks(new { apply, applyFile });
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public void AC_IAM_51_vps_demo_mode_keeps_hr_admin_on_the_payroll_and_clinical_policies()
    {
        var recorder = StartIam51("d-iam-4", "Demo server: Deployment__Mode=demo in Payroll and HR.Health, so D-IAM-4's exclusion (listed in their Production files) is off; shown from the running configuration, no payroll posted");
        var result = new Dictionary<string, object>();
        foreach (var service in new[] { "payroll-web", "hr-health-web" })
        {
            var container = ContainerOf(service);
            var (envCode, env) = Sh($"docker exec {container} printenv Deployment__Mode");
            var (_, excludedEnv) = Sh($"docker exec {container} sh -c {Q("printenv | grep -c '^Authorization__AdminExcludedPolicies' || true")}");
            var (fileCode, file) = Sh($"docker exec {container} sh -c {Q("grep -c AdminExcludedPolicies /app/appsettings.Production.json || true")}");
            Assert.True(envCode == 0, $"{service}: printenv failed");
            Assert.Equal("demo", env.Trim());
            Assert.Equal("0", excludedEnv.Trim());
            result[service] = new { deploymentMode = env.Trim(), excludedByEnvironment = excludedEnv.Trim(), listedInProductionFile = fileCode == 0 ? file.Trim() : "?" };
        }

        recorder.WriteScenarioDbChecks(result);
        recorder.WriteResult("Pass");
    }
}
