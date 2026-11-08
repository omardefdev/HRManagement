using System.Net;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-46 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password is read
/// from the server's .env into memory only. Evidence goes under <c>artifacts/plan46/vps</c>: it is never committed. The checks leave, on the
/// demo server, a processed card request and a withdrawn one, and an approved warning whose notes say «PLAN-46 server check».
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Wf46Marker = "PLAN-46 server check";
    private const string Wf46CardMaker = "e2e-idcard-maker";
    private const string Wf46CardChecker = "e2e-idcard-checker";
    private const string Wf46ComplianceMaker = "e2e-compliance";
    private const string Wf46ComplianceChecker = "e2e-compliance-checker";
    private const string Wf46Authority = "e2e-checker-mgmt";

    private static EvidenceRecorder StartWf46(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"wf46-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan46", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    [SkippableFact]
    public void AC_WF_46_vps_the_migrations_the_old_actions_and_the_definitions()
    {
        var recorder = StartWf46("vps-schema", "Demo server: both migrations, the actions published before approved and never again, the definitions");

        Assert.Single(Sql("idcard_db", "select \"MigrationId\" from \"__EFMigrationsHistory\" where \"MigrationId\" like '%WF046_CardRequestApproval'"));
        Assert.Single(Sql("compliance_db", "select \"MigrationId\" from \"__EFMigrationsHistory\" where \"MigrationId\" like '%WF046_DisciplinaryApproval'"));
        Assert.Equal("t", Sql("compliance_db", "select relforcerowsecurity from pg_class where oid = '\"DisciplinaryActions\"'::regclass").Single());
        Assert.Equal("t", Sql("idcard_db", "select relforcerowsecurity from pg_class where oid = '\"CardRequests\"'::regclass").Single());

        // Every action published before PLAN-46 is approved-before-engine, stamped, and reached payroll at most once.
        var old = Sql("compliance_db",
            "select count(*), count(*) filter (where \"ApprovalStatus\" = 2 and \"OutcomePublishedAt\" = \"CreatedAt\") from \"DisciplinaryActions\" where \"ApprovedBeforeEngine\"").Single().Split('|');
        Assert.Equal(old[0], old[1]);
        var oldIds = Sql("compliance_db", "select \"Id\" from \"DisciplinaryActions\" where \"ApprovedBeforeEngine\"");
        var twice = oldIds.Count(id => int.Parse(Sql("payroll_db",
            $"select count(*) from \"PayrollAdjustments\" where \"AdjustmentReason\" like '%DisciplinaryAction-{id}%'").Single(), System.Globalization.CultureInfo.InvariantCulture) > 1);
        Assert.Equal(0, twice);

        var versions = Sql("hr_core_db",
            "select \"Code\", \"Version\", \"Status\", \"IsPublished\" from \"WorkflowDefinitions\" where \"Code\" in ('ID-CARD-ISSUANCE','DISCIPLINARY-APPROVAL') order by 1, 2");
        Assert.Equal(
            ["DISCIPLINARY-APPROVAL|1|Retired|f", "DISCIPLINARY-APPROVAL|2|Published|t", "ID-CARD-ISSUANCE|1|Retired|f", "ID-CARD-ISSUANCE|2|Published|t"],
            versions);
        Assert.Equal("0", Sql("hr_core_db",
            "select count(*) from \"WorkflowDefinitions\" where \"Code\" in ('HIRE-WIZARD','TRANSFER-APPROVAL','IT-TICKET','SEPARATION-WIZARD') and \"IsPublished\"").Single());

        var (code, settings) = Sh($"docker exec {ContainerOf("hr-core-web")} sh -c {Q("grep -c -E '\"(SeparationRecord|DisciplinaryAction|CardRequest|Ticket)\"' /app/appsettings.Production.json || true")}");
        Assert.True(code == 0, settings);
        Assert.Equal("0", settings.Trim());

        recorder.WriteScenarioDbChecks(new { oldActions = old[0], publishedTwice = twice, versions });
        recorder.WriteScenarioSteps(
            "psql: both WF046 migrations are recorded and both tables keep forced row security; every action from before is Approved, approved-before-engine and stamped,\n" +
            "and none has more than one payroll record; card and discipline have v1 Retired and v2 Published; the four orphans are unpublished; the running settings exempt no type.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_WF_46_vps_a_card_through_the_card_office_and_a_withdrawal()
    {
        var recorder = StartWf46("vps-card", "Demo server: a card request submitted, approved by the second card officer from the inbox, processed; a withdrawal");
        var password = DemoPassword();

        var (maker, _) = await SignInAsync(Wf46CardMaker, password, "/ui/idcard/");
        var (checker, _) = await SignInAsync(Wf46CardChecker, password, "/ui/idcard/");
        using (maker)
        using (checker)
        {
            async Task<string> DraftAsync()
            {
                var employee = Guid.NewGuid().ToString("D");
                const string create = "/ui/idcard/CardRequests/Create";
                await PostFormAsync(maker, create, new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(maker, create),
                    ["EmployeeId"] = employee,
                    ["RequestDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["Purpose"] = "0"
                });
                return Sql("idcard_db", $"select \"Id\" from \"CardRequests\" where \"EmployeeId\" = '{employee}'").Single();
            }

            var request = await DraftAsync();
            var page = $"/ui/idcard/CardRequests/Details/{request}";
            await Wf45PostRenderedAsync(maker, page, "Submit");
            var submitted = Sql("idcard_db", $"select \"Status\", \"WorkflowInstanceId\" from \"CardRequests\" where \"Id\" = '{request}'").Single().Split('|');
            Assert.Equal("3", submitted[0]);
            var instance = submitted[1];

            // The requester sees no approve form; the second officer approves from the inbox.
            Assert.DoesNotContain("/ApproveWorkflow", await Wf45PageAsync(maker, $"/ui/hr-core/WorkflowApprovals/Details/{instance}"), StringComparison.Ordinal);
            using (var employees = await checker.GetAsync($"https://{AppDomain}/ui/hr-core/Employees"))
                Assert.Equal(HttpStatusCode.Forbidden, employees.StatusCode);
            Assert.Contains($"/ui/hr-core/WorkflowApprovals/Details/{instance}", await Wf45PageAsync(checker, "/ui/hr-core/WorkInbox"), StringComparison.OrdinalIgnoreCase);
            await Wf45PostRenderedAsync(checker, $"/ui/hr-core/WorkflowApprovals/Details/{instance}", "ApproveWorkflow", new Dictionary<string, string> { ["Comments"] = Wf46Marker });

            var approved = await Wf45PollAsync(() => Sql("idcard_db", $"select \"Status\" from \"CardRequests\" where \"Id\" = '{request}'").Single(), s => s == "4");
            Assert.Equal("4", approved);
            await Wf45PostRenderedAsync(maker, page, "ProcessRequest");
            var card = Sql("idcard_db", $"select c.\"Status\" from \"IssuedCards\" c join \"CardRequests\" r on r.\"IssuedCardId\" = c.\"Id\" where r.\"Id\" = '{request}'").Single();
            Assert.Equal("0", card);

            // A withdrawal returns a pending request to draft.
            var withdrawn = await DraftAsync();
            var withdrawnPage = $"/ui/idcard/CardRequests/Details/{withdrawn}";
            await Wf45PostRenderedAsync(maker, withdrawnPage, "Submit");
            var withdrawnInstance = Sql("idcard_db", $"select \"WorkflowInstanceId\" from \"CardRequests\" where \"Id\" = '{withdrawn}'").Single();
            await Wf45PostRenderedAsync(maker, withdrawnPage, "Withdraw");
            Assert.Equal("0", Sql("idcard_db", $"select \"Status\" from \"CardRequests\" where \"Id\" = '{withdrawn}'").Single());
            Assert.Equal("Cancelled", Sql("hr_core_db", $"select \"Status\" from \"WorkflowInstances\" where \"Id\" = '{withdrawnInstance}'").Single());

            recorder.WriteScenarioDbChecks(new { request, instance, card = "Active", withdrawn, withdrawnInstance });
            recorder.WriteScenarioSteps(
                $"1. {Wf46CardMaker} creates and submits a card request: PendingApproval with an instance; the requester sees no approve form; {Wf46CardChecker} gets 403 on /ui/hr-core/Employees.\n" +
                $"2. {Wf46CardChecker} approves from the inbox; the request is Approved; {Wf46CardMaker} processes it: an Active card.\n" +
                "3. A second request is submitted and withdrawn: Draft, and its instance Cancelled.");
            recorder.WriteResult("Pass");
        }
    }

    [SkippableFact]
    public async Task AC_WF_46_vps_a_warning_through_compliance_and_the_authority_reaches_payroll_once()
    {
        var recorder = StartWf46("vps-discipline", "Demo server: a warning approved by the second compliance user, then the authority; payroll once");
        var password = DemoPassword();
        var employee = Sql("payroll_db", "select \"EmployeeId\" from \"EmployeePayProfiles\" where \"IsActive\" order by \"EmployeeId\" limit 1").SingleOrDefault()
            ?? Sql("hr_core_db", "select \"Id\" from \"Employees\" order by \"Id\" limit 1").Single();

        var (maker, _) = await SignInAsync(Wf46ComplianceMaker, password, "/ui/compliance/");
        var (checker, _) = await SignInAsync(Wf46ComplianceChecker, password, "/ui/compliance/");
        var (authority, _) = await SignInAsync(Wf46Authority, password);
        using (maker)
        using (checker)
        using (authority)
        {
            const string create = "/ui/compliance/DisciplinaryActions/Create";
            await PostFormAsync(maker, create, new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(maker, create),
                ["EmployeeId"] = employee,
                ["ActionType"] = "0",
                ["IssuedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ["Notes"] = $"{Wf46Marker} {Guid.NewGuid():N}"
            });
            var row = Sql("compliance_db",
                $"select \"Id\", \"ApprovalStatus\", \"WorkflowInstanceId\" from \"DisciplinaryActions\" where \"Notes\" like '{Wf46Marker}%' order by \"CreatedAt\" desc limit 1").Single().Split('|');
            Assert.Equal("1", row[1]);
            var (action, instance) = (row[0], row[2]);
            int Payroll() => int.Parse(Sql("payroll_db", $"select count(*) from \"PayrollAdjustments\" where \"AdjustmentReason\" like '%DisciplinaryAction-{action}%'").Single(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(0, Payroll());

            Assert.DoesNotContain("/ApproveWorkflow", await Wf45PageAsync(maker, $"/ui/hr-core/WorkflowApprovals/Details/{instance}"), StringComparison.Ordinal);
            Assert.Contains($"/ui/hr-core/WorkflowApprovals/Details/{instance}", await Wf45PageAsync(checker, "/ui/hr-core/WorkInbox"), StringComparison.OrdinalIgnoreCase);
            await Wf45PostRenderedAsync(checker, $"/ui/hr-core/WorkflowApprovals/Details/{instance}", "ApproveWorkflow", new Dictionary<string, string> { ["Comments"] = Wf46Marker });
            Assert.Equal(0, Payroll());
            await Wf45PostRenderedAsync(authority, $"/ui/hr-core/WorkflowApprovals/Details/{instance}", "ApproveWorkflow", new Dictionary<string, string> { ["Comments"] = Wf46Marker });

            var status = await Wf45PollAsync(() => Sql("compliance_db", $"select \"ApprovalStatus\" from \"DisciplinaryActions\" where \"Id\" = '{action}'").Single(), s => s == "2");
            Assert.Equal("2", status);
            var payroll = await Wf45PollAsync(Payroll, n => n == 1, 60);
            Assert.Equal(1, payroll);

            recorder.WriteScenarioDbChecks(new { action, instance, status = "Approved", payrollRecords = payroll });
            recorder.WriteScenarioSteps(
                $"1. {Wf46ComplianceMaker} creates a warning: PendingApproval with an instance, no payroll record; the creator sees no approve form.\n" +
                $"2. {Wf46ComplianceChecker} approves step 1 from the inbox: still no payroll record. {Wf46Authority} approves step 2.\n" +
                "3. The action is Approved, and Payroll's DisciplinaryActionTakenConsumer writes exactly one record within 60 s (psql).");
            recorder.WriteResult("Pass");
        }
    }

    [SkippableFact]
    public void AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist()
    {
        var recorder = StartWf46("vps-sweep", "Demo server: a cycle of each sweep in the log, and the metrics");
        var lines = new Dictionary<string, string>();
        var metrics = new Dictionary<string, string>();
        foreach (var (service, type) in new[] { ("idcard-web", "CardRequest"), ("compliance-web", "DisciplinaryAction") })
        {
            var container = ContainerOf(service);
            var (logCode, log) = Sh($"docker logs --tail 5000 {container} 2>&1 | grep -a 'Workflow approval sweep of {type}' | tail -1");
            Assert.True(logCode == 0 && log.Contains("tenant(s)", StringComparison.Ordinal), $"No cycle of the {type} sweep in the log of {service}: {log}");
            lines[service] = log.Trim();

            var (metricCode, metric) = Sh($"docker exec {container} sh -c {Q("curl -s http://localhost:8080/metrics | grep -E '^workflow_(outcome_ignored|approval_sweep_applied)_total' || true")}");
            Assert.True(metricCode == 0, metric);
            Assert.Contains($"entity_type=\"{type}\"", metric, StringComparison.Ordinal);
            Assert.Contains("workflow_outcome_ignored_total", metric, StringComparison.Ordinal);
            Assert.Contains("workflow_approval_sweep_applied_total", metric, StringComparison.Ordinal);
            metrics[service] = metric.Trim();
        }

        recorder.WriteScenarioDbChecks(new { lines, metrics });
        recorder.WriteScenarioSteps("docker logs of idcard-web and compliance-web each hold a sweep cycle; /metrics inside each container holds workflow_outcome_ignored_total and workflow_approval_sweep_applied_total for its type.");
        recorder.WriteResult("Pass");
    }
}
