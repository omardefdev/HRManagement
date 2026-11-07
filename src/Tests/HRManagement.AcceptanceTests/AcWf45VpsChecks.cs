using System.Net;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-45 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password is read
/// from the server's .env into memory only. Evidence goes under <c>artifacts/plan45/vps</c>: it is never committed. The checks leave assets
/// named «PLAN-45 server check» on the demo server: one held, one lost, one free.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Wf45Marker = "PLAN-45 server check";
    private const string Wf45Maker = "e2e-logistics-maker";
    private const string Wf45Checker = "e2e-logistics-checker";
    private const string Wf45Manager = "e2e-checker-mgmt";
    private const string Wf45Proof = "PLAN-45-SERVER-CHECK";

    private static EvidenceRecorder StartWf45(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"wf45-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan45", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static async Task<string> Wf45PageAsync(HttpClient client, string path)
    {
        using var page = await client.GetAsync($"https://{AppDomain}{path}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, $"GET {path} answered {(int)page.StatusCode}");
        return html;
    }

    /// <summary>Posts the form the page renders for <paramref name="action"/>: its URL, its hidden fields, its token.</summary>
    private static async Task<string> Wf45PostRenderedAsync(
        HttpClient client, string page, string action, Dictionary<string, string>? extra = null)
    {
        var html = await Wf45PageAsync(client, page);
        var form = Regex.Matches(html, @"<form\b[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .FirstOrDefault(f => Regex.IsMatch(f, $@"action=""[^""]*/{action}[^""]*"""));
        Assert.True(form is not null, $"{page} has no {action} form.");

        var path = WebUtility.HtmlDecode(Regex.Match(form!, @"action=""([^""]+)""").Groups[1].Value);
        var fields = Regex.Matches(form, @"<input\b[^>]*type=""hidden""[^>]*>", RegexOptions.IgnoreCase)
            .Select(m => (Name: Regex.Match(m.Value, @"name=""([^""]+)""").Groups[1].Value, Value: Regex.Match(m.Value, @"value=""([^""]*)""").Groups[1].Value))
            .Where(f => f.Name.Length > 0)
            .GroupBy(f => f.Name)
            .ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.Last().Value));
        foreach (var (key, value) in extra ?? [])
            fields[key] = value;

        var (status, _, body) = await PostFormAsync(client, path, fields);
        Assert.True(status < 500, $"POST {path} answered {status}");
        return body;
    }

    private static async Task<string> Wf45AssetAsync(HttpClient maker, string suffix)
    {
        var name = $"{Wf45Marker} {suffix} {Guid.NewGuid():N}";
        const string create = "/ui/admin-logistics/Assets/CreateLegacy";
        await PostFormAsync(maker, create, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(maker, create),
            ["Name"] = name,
            ["Category"] = "IT",
            ["Brand"] = "Demo",
            ["PurchaseDate"] = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Value"] = "50000",
            ["Status"] = "InStorage",
            ["LegacyEntryReason"] = "PLAN-45: an asset for the server check of the custody chain"
        });

        var row = Sql("admin_db", $"select \"Id\", \"IsPrivileged\" from \"Assets\" where \"Name\" = '{name}'");
        Assert.True(row.Length == 1, "The asset was not created.");
        Assert.EndsWith("|t", row[0], StringComparison.Ordinal);
        return row[0].Split('|')[0];
    }

    private static async Task<T> Wf45PollAsync<T>(Func<T> read, Func<T, bool> done, int seconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var value = read();
        while (!done(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            value = read();
        }

        return value;
    }

    [SkippableFact]
    public void AC_WF_45_vps_the_migration_the_definitions_and_the_exemption()
    {
        var recorder = StartWf45("vps-schema", "Demo server: the migration applied, both codes on the chain, the exemption lifted");

        var migration = Sql("admin_db", "select \"MigrationId\" from \"__EFMigrationsHistory\" where \"MigrationId\" like '%WF045_AssetApprovalLoop'");
        Assert.Single(migration);
        var columns = Sql("admin_db", "select count(*) from information_schema.columns where table_name = 'AssetLossReports' and column_name in ('ApprovalStartAttempts','NeedsIntervention','WithdrawnAt')").Single();
        Assert.Equal("3", columns);
        Assert.Equal("t", Sql("admin_db", "select relforcerowsecurity from pg_class where oid = '\"IdempotencyRecords\"'::regclass").Single());

        var versions = Sql("hr_core_db", "select \"Code\", \"Version\", \"Status\", \"IsPublished\", \"RequireDistinctApprovers\" from \"WorkflowDefinitions\" where \"Code\" like 'ASSET-%' order by 1, 2");
        Assert.Equal(
            ["ASSET-ASSIGN-APPROVAL|1|Retired|f|t", "ASSET-ASSIGN-APPROVAL|2|Published|t|t", "ASSET-LOSS-APPROVAL|1|Retired|f|t", "ASSET-LOSS-APPROVAL|2|Published|t|t"],
            versions);
        var steps = Sql("hr_core_db", "select \"StepsJson\" from \"WorkflowDefinitions\" where \"Code\" like 'ASSET-%' and \"IsPublished\"");
        Assert.All(steps, s =>
        {
            Assert.Contains("admin-logistics", s, StringComparison.Ordinal);
            Assert.Contains("hr-management-approver", s, StringComparison.Ordinal);
        });

        var (code, settings) = Sh($"docker exec {ContainerOf("hr-core-web")} sh -c {Q("grep -c AssetLossReport /app/appsettings.Production.json || true")}");
        Assert.True(code == 0, settings);
        Assert.Equal("0", settings.Trim());

        recorder.WriteScenarioDbChecks(new { migration, versions });
        recorder.WriteScenarioSteps("psql: the migration is recorded, IdempotencyRecords is under forced row security, both codes have v1 Retired and v2 Published with the chain; the running settings name no AssetLossReport.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_WF_45_vps_custody_loss_and_withdrawal_through_the_chain()
    {
        var recorder = StartWf45("vps-journey", "Demo server: a handover and a loss through both steps from the inbox, a withdrawal, and the refusals");
        var password = DemoPassword();
        var employee = Sql("hr_core_db", "select \"Id\" from \"Employees\" order by \"Id\" limit 1").Single();

        var (maker, _) = await SignInAsync(Wf45Maker, password, "/ui/admin-logistics/");
        var (checker, _) = await SignInAsync(Wf45Checker, password, "/ui/admin-logistics/");
        var (manager, _) = await SignInAsync(Wf45Manager, password);
        using (maker)
        using (checker)
        using (manager)
        {
            async Task ApproveAsync(HttpClient who, string instance)
            {
                var inbox = await Wf45PageAsync(who, "/ui/hr-core/WorkInbox");
                Assert.Contains($"/ui/hr-core/WorkflowApprovals/Details/{instance}", inbox, StringComparison.OrdinalIgnoreCase);
                await Wf45PostRenderedAsync(who, $"/ui/hr-core/WorkflowApprovals/Details/{instance}", "ApproveWorkflow", new Dictionary<string, string> { ["Comments"] = Wf45Marker });
            }

            // Custody.
            var heldAsset = await Wf45AssetAsync(maker, "held");
            await Wf45PostRenderedAsync(maker, $"/ui/admin-logistics/Assets/Details/{heldAsset}", "AssignToEmployee", new Dictionary<string, string>
            {
                ["employeeId"] = employee,
                ["handoverCondition"] = "Good",
                ["evidenceRef"] = Wf45Proof
            });
            var held = Sql("admin_db", $"select \"Id\", \"Status\", \"WorkflowInstanceId\" from \"AssetAssignments\" where \"AssetId\" = '{heldAsset}'").Single().Split('|');
            Assert.Equal("0", held[1]);
            var heldInstance = held[2];

            // The requester and the wrong role.
            var refused = await Wf45PageAsync(maker, $"/ui/hr-core/WorkflowApprovals/Details/{heldInstance}");
            Assert.DoesNotContain("/ApproveWorkflow", refused, StringComparison.Ordinal);
            using (var employees = await checker.GetAsync($"https://{AppDomain}/ui/hr-core/Employees"))
                Assert.Equal(HttpStatusCode.Forbidden, employees.StatusCode);

            await ApproveAsync(checker, heldInstance);
            await ApproveAsync(manager, heldInstance);
            var active = await Wf45PollAsync(
                () => Sql("admin_db", $"select \"Status\" from \"AssetAssignments\" where \"Id\" = '{held[0]}'").Single(), s => s == "1");
            Assert.Equal("1", active);
            Assert.Equal("0", Sql("admin_db", $"select \"Status\" from \"Assets\" where \"Id\" = '{heldAsset}'").Single());

            // Loss.
            var lostAsset = await Wf45AssetAsync(maker, "lost");
            await Wf45PostRenderedAsync(maker, $"/ui/admin-logistics/Assets/Details/{lostAsset}", "MarkLost", new Dictionary<string, string>
            {
                ["reason"] = "Theft",
                ["context"] = "InStorage",
                ["notes"] = Wf45Marker,
                ["evidenceRef"] = Wf45Proof
            });
            var report = Sql("admin_db", $"select \"Id\", \"Status\", \"WorkflowInstanceId\" from \"AssetLossReports\" where \"AssetId\" = '{lostAsset}'").Single().Split('|');
            Assert.Equal("0", report[1]);
            await ApproveAsync(checker, report[2]);
            await ApproveAsync(manager, report[2]);
            var confirmed = await Wf45PollAsync(
                () => Sql("admin_db", $"select \"Status\" from \"AssetLossReports\" where \"Id\" = '{report[0]}'").Single(), s => s == "1");
            Assert.Equal("1", confirmed);
            Assert.Equal("4", Sql("admin_db", $"select \"Status\" from \"Assets\" where \"Id\" = '{lostAsset}'").Single());

            // Withdrawal.
            var freeAsset = await Wf45AssetAsync(maker, "free");
            var freePage = $"/ui/admin-logistics/Assets/Details/{freeAsset}";
            await Wf45PostRenderedAsync(maker, freePage, "AssignToEmployee", new Dictionary<string, string>
            {
                ["employeeId"] = employee,
                ["handoverCondition"] = "Good",
                ["evidenceRef"] = Wf45Proof
            });
            var withdrawn = Sql("admin_db", $"select \"Id\", \"WorkflowInstanceId\" from \"AssetAssignments\" where \"AssetId\" = '{freeAsset}'").Single().Split('|');
            await Wf45PostRenderedAsync(maker, freePage, "WithdrawApproval", new Dictionary<string, string> { ["reason"] = "PLAN-45 server check, withdrawn" });
            Assert.Equal("5", Sql("admin_db", $"select \"Status\" from \"AssetAssignments\" where \"Id\" = '{withdrawn[0]}'").Single());
            Assert.Equal("Cancelled", Sql("hr_core_db", $"select \"Status\" from \"WorkflowInstances\" where \"Id\" = '{withdrawn[1]}'").Single());
            Assert.Equal("0", Sql("admin_db", $"select count(*) from \"AssetAssignments\" where \"AssetId\" = '{freeAsset}' and \"ReturnedAt\" is null").Single());

            recorder.WriteScenarioDbChecks(new { heldAsset, heldInstance, active, lostAsset, lossInstance = report[2], confirmed, freeAsset, withdrawnInstance = withdrawn[1] });
            recorder.WriteScenarioSteps(
                $"1. {Wf45Maker} creates a privileged asset and hands it over; {Wf45Checker} and {Wf45Manager} approve from the inbox: Active, InUse.\n" +
                $"2. Negative: the requester sees no approve form; {Wf45Checker} gets 403 on /ui/hr-core/Employees.\n" +
                "3. A loss the same way: Confirmed, Lost.\n" +
                "4. A withdrawal: Withdrawn, the instance Cancelled, the asset free (psql).");
            recorder.WriteResult("Pass");
        }
    }

    [SkippableFact]
    public void AC_WF_45_vps_the_job_runs_and_its_metrics_exist()
    {
        var recorder = StartWf45("vps-job", "Demo server: the job's cycle in the log, and both metrics");
        var container = ContainerOf("admin-logistics-web");

        var (logCode, log) = Sh($"docker logs --tail 2000 {container} 2>&1 | grep -a 'Asset approval start: ' | tail -1");
        Assert.True(logCode == 0 && log.Contains("tenant(s)", StringComparison.Ordinal), $"No cycle of the job in the log: {log}");

        var (metricCode, metrics) = Sh($"docker exec {container} sh -c {Q("curl -s http://localhost:8080/metrics | grep '^asset_approval_' || true")}");
        Assert.True(metricCode == 0, metrics);
        Assert.Contains("asset_approval_starting_total", metrics, StringComparison.Ordinal);
        Assert.Contains("asset_approval_needs_intervention_total", metrics, StringComparison.Ordinal);

        recorder.WriteScenarioDbChecks(new { log = log.Trim(), metrics = metrics.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) });
        recorder.WriteScenarioSteps("docker logs of admin-logistics-web holds a cycle of the job; /metrics inside the container holds both gauges.");
        recorder.WriteResult("Pass");
    }
}
