using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-40 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. Read-only: the
/// replay runs as a dry run only (D-40-4). Run after the PLAN-38 server scenario, which is the tour (a leave approval and an
/// ID card): the unsaved counter and the <c>_error</c> queues are read after real traffic.
/// </summary>
public partial class AcOps36VpsBootTests
{
    /// <summary>Faulted <c>ITransactionArchived</c> messages from before PLAN-40 (the state-history defect it fixed).</summary>
    private const int KnownErrorMessages = 3;
    private const string KnownErrorQueue = "hr-contracts-web-transaction-archived_error";

    private static readonly string[] OutboxServices =
    [
        "hr-core-web", "hr-contracts-web", "hr-health-web", "hr-movements-web", "hr-training-web", "payroll-web", "idcard-web",
        "records-archive-web", "admin-logistics-web", "it-infra-web", "dev-analytics-web", "compliance-web", "auth-directory-web",
    ];

    private static EvidenceRecorder StartMsg40(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"msg40-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence", "AC-MSG-40"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo server (hradmin; password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check runs against the demo server.");
        return recorder;
    }

    [SkippableFact]
    public void AC_MSG_40_vps_outbox_tables_migrated_by_the_bootstrap_and_only_mq_users_connected()
    {
        var recorder = StartMsg40("vps-tables", "Demo server: outbox tables in the five services, Training receipts, only mq_* broker users");
        var tables = new Dictionary<string, string>();
        foreach (var database in new[] { "admin_db", "auth_directory_db", "it_infra_db", "archive_db", "analytics_db" })
        {
            var found = string.Join(",", Sql(database,
                "SELECT string_agg(table_name, ',' ORDER BY table_name) FROM information_schema.tables WHERE table_name IN ('InboxState','OutboxMessage','OutboxState')"));
            tables[database] = found;
            Assert.Equal("InboxState,OutboxMessage,OutboxState", found);
        }

        var training = string.Join(",", Sql("hr_training_db", "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'IntegrationMessageReceipts'"));
        Assert.Equal("1", training);

        var rabbit = ContainerOf("rabbitmq");
        var (code, output) = Sh($"docker exec {rabbit} rabbitmqctl list_connections user -q");
        Assert.True(code == 0, output);
        var users = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l != "user").Distinct().Order().ToList();
        Assert.Equal(14, users.Count);
        Assert.All(users, u => Assert.StartsWith("mq_", u, StringComparison.Ordinal));

        recorder.WriteScenarioDbChecks(new { tables, trainingReceipts = training, brokerUsers = users });
        recorder.WriteScenarioSteps("psql on the server: the tables of MSG040_AddMassTransitOutbox, applied by hrm-bootstrap as *_migrator; rabbitmqctl list_connections: only mq_* users.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public void AC_MSG_40_vps_no_unsaved_outbox_message_and_no_new_error_message()
    {
        var recorder = StartMsg40("vps-metrics", "Demo server: outbox_unsaved_messages_total is 0 in the 13 services, _error queues did not grow");
        var samples = new List<object>();
        foreach (var service in OutboxServices)
        {
            var container = ContainerOf(service);
            var (code, output) = Sh($"docker exec {container} sh -c {Q("curl -s http://localhost:8080/metrics | grep '^outbox_unsaved_messages_total' || true")}");
            Assert.True(code == 0, output);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.True(lines.Length > 0, $"{service}: the guard's counter is not exported.");
            var total = lines.Sum(l => double.Parse(l[(l.LastIndexOf('}') + 1)..].Trim().Split(' ')[0], CultureInfo.InvariantCulture));
            samples.Add(new { service, series = lines.Length, total });
            Assert.True(total == 0, $"{service}: {string.Join(" | ", lines)}");
        }

        var rabbit = ContainerOf("rabbitmq");
        var (queuesCode, queues) = Sh($"docker exec {rabbit} rabbitmqctl list_queues name messages -q");
        Assert.True(queuesCode == 0, queues);
        var errors = queues.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('\t', ' '))
            .Where(p => p.Length == 2 && p[0].EndsWith("_error", StringComparison.Ordinal) && p[1] != "0")
            .ToDictionary(p => p[0], p => int.Parse(p[1], CultureInfo.InvariantCulture));
        foreach (var (queue, depth) in errors)
            Assert.True(queue == KnownErrorQueue && depth <= KnownErrorMessages, $"{queue}: {depth} message(s) in an error queue.");

        recorder.WriteScenarioDbChecks(new { samples, errors });
        recorder.WriteScenarioSteps("Inside each container: curl /metrics. rabbitmqctl list_queues: no _error queue grew beyond the before-picture (3 old messages).");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_MSG_40_vps_replay_dry_run_changes_nothing_and_reports_counts()
    {
        var recorder = StartMsg40("vps-replay", "Demo server: replay dry run through the gateway as hradmin; counts recorded, nothing applied (D-40-4)");
        var (client, _) = await SignInAsync("hradmin", DemoPassword(), "/ui/hr-core/");
        var results = new JsonArray();
        var lost = 0;
        var runsBefore = Sql("hr_movements_db", "SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityType\" = 'MessagingReplayRun'")[0];
        using (client)
        {
            foreach (var (service, path) in new[]
                     {
                         ("hr-movements", "replay/promotions"), ("hr-movements", "replay/delegations"),
                         ("hr-core", "replay/asset-outcomes"), ("hr-core", "resync/org-units"),
                     })
            {
                var response = await client.PostAsync($"https://{AppDomain}/api/{service}/api/admin/messaging/{path}?dryRun=true", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode} {body[..Math.Min(body.Length, 300)]}");
                var result = JsonNode.Parse(body)!;
                Assert.True((bool)result["dryRun"]!);
                if (path.StartsWith("replay/", StringComparison.Ordinal))
                    lost += (int)result["count"]!;
                results.Add(result);
            }
        }

        Assert.Equal(runsBefore, Sql("hr_movements_db", "SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityType\" = 'MessagingReplayRun'")[0]);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var report = new JsonObject
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["gateway"] = $"https://{AppDomain}",
            ["dryRun"] = true,
            ["lostEvents"] = lost,
            ["results"] = results
        };
        var path2 = Path.Combine(AcceptanceHarness.RepoRoot, "reports", $"msg040-replay-server-{stamp}.json");
        File.WriteAllText(path2, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        recorder.WriteScenarioDbChecks(new { lostEvents = lost, report = Path.GetFileName(path2) });
        recorder.WriteScenarioSteps("hradmin signs in through the edge; POST replay and resync with dryRun=true; ids and counts in reports/msg040-replay-server-<utc>.json. No MessagingReplayRun row: nothing was applied.");
        recorder.WriteResult("Pass");
    }
}
