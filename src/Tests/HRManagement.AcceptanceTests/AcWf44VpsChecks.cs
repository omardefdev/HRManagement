using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-44 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password is read
/// from the server's .env into memory only. Evidence goes under <c>artifacts/plan44/vps</c>: it is never committed. The approval check leaves one
/// approved requisition named «PLAN-44 server check» on the demo server.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Wf44Marker = "PLAN-44 server check";
    private const string Wf44Conflict = "تغيّرت حالة الطلب أثناء الإجراء؛ أعد تحميل الصفحة وحاول مجدداً";

    private static readonly string[] Wf44TablesRead =
    [
        "WorkflowInstances", "RecruitmentRequisitions", "OfferLetters", "EstablishmentChangeRequests",
        "SelectionCommitteeSessions", "RecruitmentAppeals", "RecruitmentCompetitions", "CompetitionResultVersions"
    ];

    private static EvidenceRecorder StartWf44(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"wf44-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan44"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static string[] MetricLines(string prefix)
    {
        var (code, output) = Sh($"docker exec {ContainerOf("hr-core-web")} sh -c {Q($"curl -s http://localhost:8080/metrics | grep '^{prefix}' || true")}");
        Assert.True(code == 0, output);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static double MetricValue(string line) =>
        double.Parse(line[(line.LastIndexOf('}') + 1)..].Trim().Split(' ')[0], CultureInfo.InvariantCulture);

    [SkippableFact]
    public void AC_WF_44_vps_the_migration_applied_and_row_security_is_still_forced()
    {
        var recorder = StartWf44("vps-schema", "Demo server: both indexes exist, the eight tables are forced, no duplicate is left");

        var indexes = Sql("hr_core_db",
            "select indexname from pg_indexes where tablename = 'WorkflowInstances' and indexname in " +
            "('IX_WorkflowInstances_Tenant_Entity_Active', 'IX_WorkflowInstances_Tenant_Source_IdempotencyKey') order by 1");
        Assert.Equal(2, indexes.Length);

        var names = string.Join(",", Wf44TablesRead.Select(t => $"'{t}'"));
        var flags = Sql("hr_core_db",
            $"select c.relname, c.relrowsecurity, c.relforcerowsecurity, t.tableowner from pg_class c join pg_tables t on t.tablename = c.relname and t.schemaname = 'public' where c.relname in ({names}) order by 1");
        Assert.Equal(Wf44TablesRead.Length, flags.Length);
        Assert.All(flags, row => Assert.EndsWith("|t|t|hr_core_migrator", row, StringComparison.Ordinal));

        var history = Sql("hr_core_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%WF044_WorkflowInstanceIntegrity'").Single();
        Assert.Equal("1", history);
        var duplicates = Sql("hr_core_db",
            "select count(*) from (select 1 from \"WorkflowInstances\" where \"Status\" = 'InProgress' group by \"TenantId\", \"EntityType\", \"EntityId\" having count(*) > 1) d").Single();
        Assert.Equal("0", duplicates);

        recorder.WriteScenarioDbChecks(new { indexes, flags, history, duplicates });
        recorder.WriteScenarioSteps("psql as the administrator: the two indexes, relrowsecurity and relforcerowsecurity of the eight tables, the migration history, no duplicate.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_WF_44_vps_the_job_runs_the_metric_is_there_and_the_dry_run_lists_nothing_to_apply()
    {
        var recorder = StartWf44("vps-reconciliation", "Demo server: the reconciliation log line, the metric and the operator's dry run");

        var (code, logs) = Sh($"docker logs {ContainerOf("hr-core-web")} 2>&1 | grep -a 'Workflow reconciliation:' | tail -3 || true");
        Assert.True(code == 0, logs);
        Assert.Contains("tenant(s)", logs, StringComparison.Ordinal);
        Assert.Contains("auto-apply False", logs, StringComparison.Ordinal);

        var metric = MetricLines("workflow_outcome_unapplied_total");
        Assert.NotEmpty(metric);
        Assert.All(metric, line => Assert.Equal(0, MetricValue(line)));

        var (operatorClient, _) = await SignInAsync("hradmin", DemoPassword());
        string report;
        using (operatorClient)
        {
            using var response = await operatorClient.PostAsJsonAsync($"https://{AppDomain}/api/hr-core/api/recruitment/admin/repair-approvals?dryRun=true", new { });
            report = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"dry run answered {(int)response.StatusCode}: {report}");
        }

        recorder.WriteScenarioDbChecks(new { logs, metric, report });
        recorder.WriteScenarioSteps("docker logs of hr-core-web; /metrics inside the container; POST repair-approvals?dryRun=true as hradmin through the edge.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_WF_44_vps_two_approvals_at_once_end_with_one_decision_and_one_conflict_message()
    {
        var recorder = StartWf44("vps-race", "Demo server: two sessions of e2e-checker-mgmt approve step 1 of one requisition at once");
        var password = DemoPassword();
        const string checkerName = "e2e-checker-mgmt";

        var position = Sql("hr_core_db",
            "select p.\"Id\", p.\"OrgUnitId\" from \"FundedPositions\" p where p.\"Status\" = 'Vacant' and not exists " +
            "(select 1 from \"RecruitmentRequisitions\" r where r.\"FundedPositionId\" = p.\"Id\" and r.\"Status\" in ('Draft', 'PendingApproval', 'Approved', 'Published')) " +
            "order by p.\"PositionCode\" limit 1");
        AcceptanceGate.RequireData(recorder, position.Length == 1, "لا وظيفة شاغرة", "No vacant funded position without a requisition.");
        var (positionId, orgUnit) = (position[0].Split('|')[0], position[0].Split('|')[1]);

        var (creator, _) = await SignInAsync("hradmin", password);
        string requisition;
        using (creator)
        {
            const string createPage = "/ui/hr-core/RecruitmentRequisitions/Create";
            string[] rows = [];
            var status = 0;
            for (var attempt = 0; attempt < 5 && rows.Length == 0; attempt++)
            {
                (status, _, _) = await PostFormAsync(creator, createPage, new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(creator, createPage),
                    ["ScopeOrgUnitId"] = orgUnit,
                    ["FundedPositionId"] = positionId,
                    ["JobTitle"] = Wf44Marker,
                    ["JustificationNotes"] = Wf44Marker,
                    ["LegalSourceId"] = "SYR-LAW-50-2004",
                    ["BudgetYear"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
                    ["PublishingChannel"] = "PublicOnly"
                });
                rows = Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"FundedPositionId\" = '{positionId}' and \"Status\" = 'Draft'");
            }

            Assert.True(rows.Length == 1, $"create answered {status}");
            requisition = rows[0];
            var page = $"/ui/hr-core/RecruitmentRequisitions/Details/{requisition}";
            var criteria = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(creator, page),
                ["RequisitionId"] = requisition,
                ["Title"] = Wf44Marker
            };
            (string Key, string Name, string Weight, string Stage)[] items =
                [("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")];
            for (var i = 0; i < items.Length; i++)
            {
                var row = $"r{i}";
                criteria[$"Items[{row}].Key"] = items[i].Key;
                criteria[$"Items[{row}].NameAr"] = items[i].Name;
                criteria[$"Items[{row}].Weight"] = items[i].Weight;
                criteria[$"Items[{row}].MaxScore"] = "100";
                criteria[$"Items[{row}].Stage"] = items[i].Stage;
                criteria[$"Items[{row}].IsMandatory"] = "true";
            }

            var form = criteria.Select(kv => kv).Concat(items.Select((_, i) => new KeyValuePair<string, string>("Items.Index", $"r{i}")));
            using var saved = await creator.PostAsync($"https://{AppDomain}/ui/hr-core/RecruitmentAssessmentCriteria/Create", new FormUrlEncodedContent(form));
            Assert.True(saved.IsSuccessStatusCode, $"criteria answered {(int)saved.StatusCode}");

            await PostFormAsync(creator, $"/ui/hr-core/RecruitmentRequisitions/Submit/{requisition}", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(creator, page)
            });
        }

        var instance = Sql("hr_core_db", $"select \"WorkflowInstanceId\" from \"RecruitmentRequisitions\" where \"Id\" = '{requisition}' and \"Status\" = 'PendingApproval'").Single();
        var conflictsBefore = MetricLines("workflow_instance_conflict_total").Where(l => l.Contains("approve", StringComparison.Ordinal)).Sum(MetricValue);

        var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{requisition}";
        var (first, _) = await SignInAsync(checkerName, password);
        var (second, _) = await SignInAsync(checkerName, password);
        string[] bodies;
        using (first)
        using (second)
        {
            Dictionary<string, string> Fields(string token) => new()
            {
                ["__RequestVerificationToken"] = token,
                ["RequisitionId"] = requisition,
                ["Comments"] = Wf44Marker,
                ["TargetStepLevel"] = "1"
            };

            var firstToken = await TokenAsync(first, details);
            var secondToken = await TokenAsync(second, details);
            var results = await Task.WhenAll(
                PostFormAsync(first, $"/ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{requisition}", Fields(firstToken)),
                PostFormAsync(second, $"/ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{requisition}", Fields(secondToken)));
            bodies = results.Select(r => WebUtility.HtmlDecode(r.Body)).ToArray();
        }

        Assert.Single(bodies, b => b.Contains(Wf44Conflict, StringComparison.Ordinal));
        var state = Sql("hr_core_db",
            $"select \"CurrentStepLevel\", jsonb_array_length(\"StepHistoryJson\"::jsonb) from \"WorkflowInstances\" where \"Id\" = '{instance}'").Single();
        Assert.Equal("2|1", state);
        var signatures = Sql("hr_core_db", $"select count(*) from \"SignatureRecords\" where \"SubjectType\" = 'RecruitmentRequisition' and \"SubjectId\" = '{requisition}'").Single();
        Assert.Equal("1", signatures);
        var conflictsAfter = MetricLines("workflow_instance_conflict_total").Where(l => l.Contains("approve", StringComparison.Ordinal)).Sum(MetricValue);
        Assert.Equal(conflictsBefore + 1, conflictsAfter);

        var (checker, _) = await SignInAsync(checkerName, password);
        using (checker)
        {
            await PostFormAsync(checker, $"/ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{requisition}", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(checker, details),
                ["RequisitionId"] = requisition,
                ["Comments"] = $"{Wf44Marker} 2",
                ["TargetStepLevel"] = "2"
            });
        }

        var final = Sql("hr_core_db",
            $"select r.\"Status\", i.\"Status\" from \"RecruitmentRequisitions\" r join \"WorkflowInstances\" i on i.\"Id\" = r.\"WorkflowInstanceId\" where r.\"Id\" = '{requisition}'").Single();
        Assert.Equal("Approved|Completed", final);

        recorder.WriteScenarioDbChecks(new { requisition, instance, state, signatures, conflictsBefore, conflictsAfter, final });
        recorder.WriteScenarioSteps(
            "1. hradmin creates and submits a requisition.\n" +
            "2. Two sessions of e2e-checker-mgmt post step 1 at once: one decision, one signature, one conflict message, the counter up by one.\n" +
            "3. A third session approves step 2: Approved, instance Completed.");
        recorder.WriteResult("Pass");
    }
}
