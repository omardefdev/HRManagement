using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-39 (P7, S-1) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>), after the in-place move: Keycloak connects as its
/// own role, which cannot reach hr_core_db, and owns every object of keycloak_db (AC-OPS-39e); the reconciled realm has
/// the Demo profile's production security settings; and a protected national ID still shows, in the same signed-in
/// session, after <c>docker compose up -d --force-recreate</c> of every service (AC-OPS-39f on the server: the HR.Core
/// key-ring canary passed on the existing data and the gateway's key ring kept the session). Secrets stay on the
/// server or in memory; the evidence (<c>acceptance-evidence/AC-OPS-39/vps/</c>) holds names, counts and states.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private static EvidenceRecorder StartOps39(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"ops39-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence", "AC-OPS-39"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo server (hradmin; password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check runs against the demo server only.");
        return recorder;
    }

    /// <summary>kcadm in the keycloak container; the admin password reaches its prompt on stdin there.</summary>
    private static string Kcadm(string args)
    {
        var kc = ContainerOf("keycloak");
        var (code, output) = Sh($"docker exec -i {kc} sh -c {Q("{ printf '%s\\n' \"$KEYCLOAK_ADMIN_PASSWORD\"; sleep 2; } | timeout 60 /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user \"$KEYCLOAK_ADMIN\" > /dev/null 2>&1 && /opt/keycloak/bin/kcadm.sh " + args)} < /dev/null");
        Assert.True(code == 0, $"kcadm {args.Split(' ')[0]} failed: {output}");
        return output.Trim();
    }

    [SkippableFact]
    public void AC_OPS_39_vps_e_Keycloak_on_its_own_role_with_the_production_security_settings()
    {
        var recorder = StartOps39("vps-39e", "Demo server: Keycloak as keycloak (no hr_core_db), keycloak_db owned by keycloak, realm security settings reconciled");

        var users = Sql("postgres", "SELECT COALESCE(string_agg(DISTINCT usename, ','), '') FROM pg_stat_activity WHERE datname = 'keycloak_db' AND backend_type = 'client backend' AND application_name <> 'psql'").FirstOrDefault() ?? "";
        Assert.Equal("keycloak", users);
        var owners = Sql("keycloak_db",
            "SELECT pg_get_userbyid(c.relowner) || '=' || count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' GROUP BY c.relowner ORDER BY 1");
        Assert.Single(owners);
        Assert.StartsWith("keycloak=", owners[0], StringComparison.Ordinal);
        Assert.Equal("keycloak", Sql("postgres", "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'keycloak_db'").Single());

        // Negative: keycloak's own password (read from Keycloak's environment on the server, never printed) on hr_core_db.
        var kc = ContainerOf("keycloak");
        var pg = ContainerOf("postgresql");
        var (deniedCode, denied) = Sh($"docker exec {kc} printenv KC_DB_PASSWORD | docker exec -i {pg} sh -c {Q("PGPASSWORD=\"$(cat)\" psql -X -h postgresql -U keycloak -d hr_core_db -Atc 'SELECT 1'")}");
        Assert.NotEqual(0, deniedCode);
        Assert.Contains("permission denied for database", denied);

        var realm = Kcadm("get realms/hrmanagement --fields sslRequired,bruteForceProtected,failureFactor,waitIncrementSeconds,maxFailureWaitSeconds,permanentLockout,passwordPolicy --format csv --noquotes");
        Assert.Equal("external,true,5,900,900,false,length(12) and notUsername and passwordHistory(3)", realm);
        var gateway = Kcadm("get clients -r hrmanagement -q clientId=hr-gateway-client --fields directAccessGrantsEnabled,standardFlowEnabled --format csv --noquotes");
        Assert.Equal("false,true", gateway);
        var serviceFlags = Kcadm("get clients -r hrmanagement --fields clientId,standardFlowEnabled,directAccessGrantsEnabled,implicitFlowEnabled --format csv --noquotes")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l.StartsWith("svc-", StringComparison.Ordinal)).ToList();
        Assert.All(serviceFlags, l => Assert.EndsWith(",false,false,false", l, StringComparison.Ordinal));

        recorder.WriteScenarioSteps("1. pg_stat_activity on keycloak_db: the only client user is keycloak; every relation and the database belong to keycloak.\n" +
                                    "2. Negative: psql as keycloak (its own password, read on the server) to hr_core_db -> permission denied for database.\n" +
                                    "3. kcadm: sslRequired external, brute force (5 failures, 900 s), password policy; gateway without password grant; no svc-* client with a browser or password flow.");
        recorder.WriteScenarioDbChecks(new { keycloakDbUsers = users, owners, realm, gatewayDirectAccessGrantsAndStandardFlow = gateway, serviceClients = serviceFlags.Count });
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_OPS_39_vps_f_A_protected_national_id_shows_after_recreating_every_service()
    {
        var recorder = StartOps39("vps-39f", "Demo server: an employee's national ID shows in the same session after docker compose up -d --force-recreate");

        var employee = Sql("hr_core_db", "SELECT \"Id\" FROM \"Employees\" WHERE \"NationalId\" LIKE 'CfDJ8%' ORDER BY \"Id\" LIMIT 1").Single();
        var (client, _) = await SignInAsync("hradmin", DemoPassword(), "/ui/hr-core/");
        string? Shown(string html) => Regex.Match(html, @"(?<![\d])\d{11}(?![\d])").Success ? Regex.Match(html, @"(?<![\d])\d{11}(?![\d])").Value : null;
        var beforePage = await client.GetAsync($"https://{AppDomain}/ui/hr-core/Employees/Details/{employee}");
        var beforeHtml = await beforePage.Content.ReadAsStringAsync();
        Assert.True(beforePage.IsSuccessStatusCode, $"details before: {(int)beforePage.StatusCode}");
        var before = Shown(beforeHtml);
        Assert.False(string.IsNullOrEmpty(before), "no national ID on the details page before the recreate");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (upCode, upOutput) = Sh(Compose("up -d --force-recreate"), 1_200_000);
        Assert.True(upCode == 0, $"up --force-recreate failed: {upOutput[^Math.Min(upOutput.Length, 2000)..]}");
        HttpResponseMessage? afterPage = null;
        for (var i = 0; i < 120; i++)
        {
            try
            {
                afterPage = await client.GetAsync($"https://{AppDomain}/ui/hr-core/Employees/Details/{employee}");
                if (afterPage.IsSuccessStatusCode && afterPage.RequestMessage!.RequestUri!.Host.Equals(AppDomain, StringComparison.OrdinalIgnoreCase))
                    break;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        var recreateSeconds = (int)sw.Elapsed.TotalSeconds;
        Assert.NotNull(afterPage);
        Assert.True(afterPage!.IsSuccessStatusCode, $"details after: {(int)afterPage.StatusCode}");
        Assert.Equal(AppDomain, afterPage.RequestMessage!.RequestUri!.Host, ignoreCase: true);   // the same session, no new sign-in
        var after = Shown(await afterPage.Content.ReadAsStringAsync());
        Assert.Equal(before, after);

        var hrCore = ContainerOf("hr-core-web");
        var canary = Sh($"docker logs {hrCore} 2>&1 | grep -m1 'Key-ring canary of HR.Core'").Output.Trim();
        Assert.Contains("a protected national ID decrypts with the key ring at /var/hrm/dpkeys", canary);
        var reconcile = Sh($"docker logs $(docker ps -aq --filter label=com.docker.compose.project={Project} --filter label=com.docker.compose.service=keycloak-reconcile | head -1) 2>&1 | tail -1").Output.Trim();

        recorder.WriteScenarioSteps("1. hradmin signs in through the edge; an employee with a protected national ID (CfDJ8... at rest) shows its 11-digit ID.\n" +
                                    "2. docker compose up -d --force-recreate (every service of the chain).\n" +
                                    "3. The same browser session opens the same employee: no new sign-in (gateway key ring kept), the same ID shown (HR.Core key ring kept); HR.Core's canary line after the recreate.");
        recorder.WriteScenarioDbChecks(new
        {
            employeeId = employee,
            shownBefore = true,
            sameAfter = true,
            sessionKept = true,
            recreateSeconds,
            canary = Regex.Replace(canary, @"^\S+\s+\S+\s+", ""),
            reconcile = Regex.Replace(reconcile, @"^\S+\s+", ""),
        });
        recorder.WriteResult("Pass");
    }
}
