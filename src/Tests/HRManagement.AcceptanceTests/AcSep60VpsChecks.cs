using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-60 (P9, P-17, P-18) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo's
/// own users act (the password read into memory only); evidence goes under <c>artifacts/plan60/vps</c>, never committed, and holds ids,
/// statuses and counts only — never a name, a national number or an amount.
/// <para>
/// They check the release (its tag, every container healthy, <c>SEP060</c> the last contracts migration), the backfill against the
/// picture taken before the deploy, the two chains, and one Resignation of a demo employee taken <b>only as far as Approved</b> through
/// HR.Core's inbox, then cancelled with a reason marked «PLAN-60 server check». No decision is issued (it would spend an official
/// number), nothing is executed (it would end a demo employee's service), no user is added, and nothing is left in progress.
/// </para>
/// The expectations come from the picture (<c>artifacts/plan60/vps/picture.sh</c>), in the environment: <c>SEP060_VPS_TAG</c>,
/// <c>SEP060_VPS_STATUSES</c> («Status|legacy|count» joined by «;»), <c>SEP060_VPS_LINKED</c>, <c>SEP060_VPS_CONTRACTS</c> and
/// <c>SEP060_VPS_EMPLOYEES</c> («key|count» joined by «;»), and Payroll's <c>SEP060_VPS_RUNS</c>, <c>_LINES</c>, <c>_BATCHES</c>,
/// <c>_ORDERS</c>, <c>_ARREARS</c>, <c>_PROFILES</c> («count|md5»).
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Sep60Maker = "e2e-maker-contracts";
    private const string Sep60CoreStep = "e2e-delegate-hr";
    private const string Sep60Authority = "e2e-checker-mgmt";
    private const string Sep60Marker = "PLAN-60 server check";
    private const string Sep60Contracts = "hr_contracts_db";
    private const string Sep60Core = "hr_core_db";
    private const string Sep60OpenStatuses = "'Draft','PendingApproval','Approved','Executing','AwaitingIntervention'";

    private static readonly string Sep60Out = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan60", "vps");

    private static EvidenceRecorder StartSep60(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"sep60-{key}-{Guid.NewGuid():N}", Sep60Out) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "the demo's users (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static string Sep60One(string database, string sql) => Sql(database, sql).SingleOrDefault() ?? "";

    private static string Sep60Env(string name) => Environment.GetEnvironmentVariable($"SEP060_VPS_{name}") ?? "";

    /// <summary>«a|b;c|d» as a sorted list of rows, for a comparison with psql's rows.</summary>
    private static List<string> Sep60Rows(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------------------------------------ the release

    [SkippableFact]
    public void AC_SEP_60_vps_the_release_runs_every_container_is_healthy_and_SEP060_is_the_last_contracts_migration()
    {
        var recorder = StartSep60("release", "Demo server: PLAN-60's tag runs, every container healthy, SEP060 the last contracts migration, none pending");
        var expectedTag = Sep60Env("TAG");
        var tags = Sh("docker ps --format '{{.Image}}' | grep -o ':[0-9a-f]\\{12\\}$' | sort | uniq -c").Output.Trim();
        var unhealthy = Sh("docker ps --format '{{.Names}} {{.Status}}' | grep -v '(healthy)' | grep -ci 'starting\\|unhealthy' || true").Output.Trim();
        var latest = Directory.GetFiles(Path.Combine(AcceptanceHarness.RepoRoot, "src", "Services", "HRContracts", "HR.Contracts.Infrastructure", "Migrations"), "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !n.EndsWith(".Designer", StringComparison.Ordinal) && !n.EndsWith("ModelSnapshot", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Last();
        var applied = Sep60One(Sep60Contracts, "select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\" desc limit 1");
        recorder.WriteScenarioDbChecks(new { expectedTag, tags, unhealthy, latestMigrationInTheTree = latest, lastAppliedOnTheDemo = applied });

        Assert.False(string.IsNullOrWhiteSpace(expectedTag), "Set SEP060_VPS_TAG to the deployed tag.");
        Assert.Contains(":" + expectedTag, tags, StringComparison.Ordinal);
        Assert.DoesNotMatch(@":(?!" + expectedTag + @")[0-9a-f]{12}", tags);
        Assert.Equal("0", unhealthy);
        Assert.EndsWith("_SEP060_SeparationStatusGovernance", latest, StringComparison.Ordinal);
        Assert.Equal(latest, applied);
        recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ the backfill

    [SkippableFact]
    public void AC_SEP_60_vps_the_backfill_gave_the_statuses_the_picture_predicts_and_left_its_index_items_and_links()
    {
        var recorder = StartSep60("backfill", "Demo server: SEP060's statuses as the picture predicts, rule by rule; the partial index; every open record's ten items; the links");
        var notTheChecks = $"coalesce(\"Reason\", '') not like '{Sep60Marker}%'";
        var statuses = Sql(Sep60Contracts,
            $"select \"Status\", \"IsLegacyDraft\", count(*) from \"SeparationRecords\" where {notTheChecks} group by 1, 2 order by 1, 2").ToList();
        var plan60Cancelled = Sql(Sep60Contracts,
            "select \"CancellationReason\", count(*) from \"SeparationRecords\" where \"CancellationReason\" like 'PLAN-60:%' group by 1 order by 1").ToList();
        var index = Sep60One(Sep60Contracts,
            "select indexdef from pg_indexes where indexname = 'IX_SeparationRecords_Tenant_Employee_Open'");
        var itemsShort = Sep60One(Sep60Contracts,
            $"select count(*) from \"SeparationRecords\" r where r.\"Status\" in ({Sep60OpenStatuses}) and " +
            "(select count(*) from \"SeparationChecklists\" c where c.\"SeparationRecordId\" = r.\"Id\") <> 10");
        var modes = Sql(Sep60Contracts,
            "select \"CompletionMode\", count(*) from \"SeparationChecklists\" group by 1 order by 1").ToList();
        var linked = Sep60One(Sep60Contracts,
            "select count(*) from \"ServiceTransactions\" t join \"SeparationRecords\" r on r.\"Id\" = t.\"SeparationRecordId\" " +
            $"where coalesce(r.\"Reason\", '') not like '{Sep60Marker}%'");
        var force = Sql(Sep60Contracts,
            "select relname, relforcerowsecurity from pg_class where relname in ('SeparationRecords', 'SeparationChecklists', 'SeparationSagaStates', 'ServiceTransactions') order by 1").ToList();
        var expected = Sep60Rows(Sep60Env("STATUSES"));
        recorder.WriteScenarioDbChecks(new { statuses, expected, plan60Cancelled, index, itemsShort, modes, linked, expectedLinked = Sep60Env("LINKED"), force });

        Assert.NotEmpty(expected);
        Assert.Equal(expected, statuses.Order(StringComparer.Ordinal).ToList());
        Assert.Empty(plan60Cancelled);
        Assert.Contains("(\"TenantId\", \"EmployeeId\")", index, StringComparison.Ordinal);
        Assert.Contains("WHERE", index, StringComparison.Ordinal);
        Assert.Equal("0", itemsShort);
        Assert.Equal(Sep60Env("LINKED"), linked);
        recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ the definitions

    private static IReadOnlyList<string> Sep60Roles(string stepsJson)
    {
        using var doc = JsonDocument.Parse(stepsJson);
        static JsonElement Find(JsonElement step, string name) =>
            step.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
        return doc.RootElement.EnumerateArray()
            .OrderBy(s => Find(s, "Level") is { ValueKind: JsonValueKind.Number } level ? level.GetInt32() : 0)
            .Select(s => Find(s, "RoleRequired") is { ValueKind: JsonValueKind.String } role ? role.GetString() ?? "" : "")
            .ToList();
    }

    [SkippableFact]
    public void AC_SEP_60_vps_the_two_chains_are_published_with_their_steps_and_distinct_approvers()
    {
        var recorder = StartSep60("definitions", "Demo server: SEPARATION-APPROVAL and -SENSITIVE published with D-SEP-4's steps and distinct approvers");
        var rows = Sql(Sep60Core,
            "select \"Code\", \"RequireDistinctApprovers\", \"StepsJson\" from \"WorkflowDefinitions\" " +
            "where \"Code\" in ('SEPARATION-APPROVAL', 'SEPARATION-APPROVAL-SENSITIVE') and \"Status\" = 'Published' and \"IsPublished\" order by 1");
        var chains = rows.Select(r => r.Split('|', 3)).ToDictionary(p => p[0], p => (Distinct: p[1], Roles: Sep60Roles(p[2])), StringComparer.Ordinal);
        recorder.WriteScenarioDbChecks(new { chains = chains.ToDictionary(c => c.Key, c => new { c.Value.Distinct, c.Value.Roles }) });

        Assert.Equal(2, chains.Count);
        Assert.Equal(["hr-core", "hr-management-approver"], chains["SEPARATION-APPROVAL"].Roles);
        Assert.Equal(["hr-core", "compliance", "hr-management-approver"], chains["SEPARATION-APPROVAL-SENSITIVE"].Roles);
        Assert.All(chains.Values, c => Assert.Equal("t", c.Distinct));
        recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ as far as Approved

    private static string Sep60EmployeeIdOf(string username) =>
        Sql("keycloak_db",
            "select ua.value from user_attribute ua join user_entity u on u.id = ua.user_id join realm r on r.id = u.realm_id " +
            $"where r.name = 'hrmanagement' and u.username = '{username}' and ua.name = 'employee_id'").FirstOrDefault() ?? "";

    /// <summary>
    /// A demo employee for the check: one contract, active, no open separation record; active in HR.Core, no legal hold; none of the three
    /// users' own employee (an approver is never the subject).
    /// </summary>
    private static (string Employee, string Contract) Sep60Subject()
    {
        var people = new[] { Sep60Maker, Sep60CoreStep, Sep60Authority }.Select(Sep60EmployeeIdOf).Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = Sql(Sep60Contracts,
            "select c.\"EmployeeId\", c.\"Id\" from \"Contracts\" c where c.\"Status\" = 'Active' " +
            "and (select count(*) from \"Contracts\" o where o.\"EmployeeId\" = c.\"EmployeeId\" and o.\"Status\" in ('Active', 'Pending')) = 1 " +
            $"and not exists (select 1 from \"SeparationRecords\" s where s.\"EmployeeId\" = c.\"EmployeeId\" and s.\"Status\" in ({Sep60OpenStatuses})) " +
            "order by c.\"EmployeeId\" limit 200")
            .Select(r => r.Split('|'))
            .Where(p => !people.Contains(p[0]))
            .ToList();
        Assert.NotEmpty(candidates);
        var ids = string.Join(",", candidates.Select(p => $"'{p[0]}'"));
        var active = Sql(Sep60Core,
            $"select \"Id\" from \"Employees\" where \"Id\" in ({ids}) and \"CurrentStatus\" = 0 and not \"LegalHold\" order by \"Id\" limit 1").SingleOrDefault();
        Assert.False(string.IsNullOrEmpty(active), "No active demo employee with one active contract and no open separation record.");
        return (active!, candidates.First(p => string.Equals(p[0], active, StringComparison.OrdinalIgnoreCase))[1]);
    }

    private static (string Status, string Instance) Sep60State(string record)
    {
        var row = Sep60One(Sep60Contracts, $"select \"Status\" || '|' || coalesce(\"WorkflowInstanceId\"::text, '') from \"SeparationRecords\" where \"Id\" = '{record}'");
        var parts = row.Split('|');
        return (parts[0], parts.ElementAtOrDefault(1) ?? "");
    }

    private static async Task<int> Sep60PostAsync(HttpClient client, string tokenPage, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenAsync(client, tokenPage);
        var (status, _, _) = await PostFormAsync(client, path, fields);
        return status;
    }

    private static async Task Sep60ApproveAsync(string user, string password, string instance, EvidenceRecorder recorder)
    {
        var (client, _) = await SignInAsync(user, password, "/ui/hr-core/");
        using var _client = client;
        var status = await Sep60PostAsync(client, $"/ui/hr-core/WorkflowApprovals/Details/{instance}", $"/ui/hr-core/WorkflowApprovals/ApproveWorkflow/{instance}",
            new Dictionary<string, string> { ["Comments"] = Sep60Marker });
        recorder.CaptureHttp($"approve-{user}", "POST", $"/ui/hr-core/WorkflowApprovals/ApproveWorkflow/{instance}", status);
        Assert.True(status < 400, $"{user}'s approval answered {status}");
    }

    [SkippableFact]
    public async Task AC_SEP_60_vps_a_resignation_reaches_Approved_through_HR_Cores_inbox_and_is_cancelled_with_its_reason()
    {
        var recorder = StartSep60("approved",
            "Demo server: a Resignation created and submitted by e2e-maker-contracts, approved by e2e-delegate-hr and e2e-checker-mgmt, then cancelled with «PLAN-60 server check»; no decision, no execution");
        var password = DemoPassword();
        var (employee, contract) = Sep60Subject();
        var effective = DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string? record = null;
        var checks = new Dictionary<string, object?> { ["employee"] = employee, ["contract"] = contract, ["effectiveDate"] = effective };
        var (maker, _) = await SignInAsync(Sep60Maker, password, "/ui/hr-contracts/");
        using var _maker = maker;
        try
        {
            var created = await Sep60PostAsync(maker, "/ui/hr-contracts/SeparationRecords/Create", "/ui/hr-contracts/SeparationRecords/Create",
                new Dictionary<string, string>
                {
                    ["EmployeeId"] = employee,
                    ["ContractId"] = contract,
                    ["SeparationType"] = "Resignation",
                    ["EffectiveDate"] = effective,
                    ["Reason"] = $"{Sep60Marker}: a resignation taken to Approved, then cancelled"
                });
            record = Sep60One(Sep60Contracts,
                $"select \"Id\" from \"SeparationRecords\" where \"EmployeeId\" = '{employee}' and \"Reason\" like '{Sep60Marker}%' and \"Status\" <> 'Cancelled' order by \"CreatedAt\" desc limit 1");
            checks["create"] = created;
            checks["record"] = record;
            Assert.False(string.IsNullOrEmpty(record), $"The creation answered {created} and made no record.");
            Assert.Equal("Draft", Sep60State(record).Status);

            checks["submit"] = await Sep60PostAsync(maker, $"/ui/hr-contracts/SeparationRecords/Details/{record}", $"/ui/hr-contracts/SeparationRecords/Submit/{record}", []);
            var (pending, instance) = Sep60State(record);
            checks["instance"] = instance;
            checks["afterTheSubmission"] = pending;
            Assert.Equal("PendingApproval", pending);
            Assert.Equal("SEPARATION-APPROVAL", Sep60One(Sep60Core,
                $"select d.\"Code\" from \"WorkflowInstances\" i join \"WorkflowDefinitions\" d on d.\"Id\" = i.\"DefinitionId\" where i.\"Id\" = '{instance}'"));

            await Sep60ApproveAsync(Sep60CoreStep, password, instance, recorder);
            checks["stepAfterTheFirst"] = Sep60One(Sep60Core, $"select \"CurrentStepLevel\" from \"WorkflowInstances\" where \"Id\" = '{instance}'");
            await Sep60ApproveAsync(Sep60Authority, password, instance, recorder);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            var status = Sep60State(record).Status;
            while (status != "Approved" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                status = Sep60State(record).Status;
            }

            checks["afterTheApprovals"] = status;
            checks["instanceStatus"] = Sep60One(Sep60Core, $"select \"Status\" from \"WorkflowInstances\" where \"Id\" = '{instance}'");
            checks["decision"] = Sep60One(Sep60Contracts, $"select coalesce(\"DecisionId\"::text, 'none') from \"SeparationRecords\" where \"Id\" = '{record}'");
            Assert.Equal("2", checks["stepAfterTheFirst"]);
            Assert.Equal("Approved", status);
            Assert.Equal("Completed", checks["instanceStatus"]);
            Assert.Equal("none", checks["decision"]);
        }
        finally
        {
            if (!string.IsNullOrEmpty(record) && Sep60State(record).Status is not ("Cancelled" or "Executing" or "Completed"))
            {
                checks["cancel"] = await Sep60PostAsync(maker, $"/ui/hr-contracts/SeparationRecords/Details/{record}", $"/ui/hr-contracts/SeparationRecords/Cancel/{record}",
                    new Dictionary<string, string> { ["reason"] = $"{Sep60Marker}: cancelled after its approval (no decision, no execution)" });
                checks["afterTheCancellation"] = Sep60State(record).Status;
            }

            checks["inProgressSeparationInstances"] = Sep60One(Sep60Core,
                "select count(*) from \"WorkflowInstances\" where \"EntityType\" = 'SeparationRecord' and \"Status\" in ('NotStarted', 'InProgress')");
            recorder.WriteScenarioDbChecks(checks);
        }

        Assert.Equal("Cancelled", checks["afterTheCancellation"]);
        Assert.Equal("0", checks["inProgressSeparationInstances"]);
        recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ nothing else moved

    [SkippableFact]
    public void AC_SEP_60_vps_nothing_else_moved_payroll_the_contracts_and_the_employees_are_as_in_the_picture()
    {
        var recorder = StartSep60("picture", "Demo server: Payroll's checksums, the contracts by status and HR.Core's employees by status as in the picture before the deploy");
        var payroll = Pay58Picture();
        payroll["PROFILES"] = Pay58One("select count(*) || '|' || md5(string_agg(\"Id\"::text||\"IsActive\"::text, ',' order by \"Id\")) from \"EmployeePayProfiles\"");
        var expectedPayroll = new[] { "RUNS", "LINES", "BATCHES", "ORDERS", "ARREARS", "PROFILES" }
            .ToDictionary(k => k, Sep60Env, StringComparer.Ordinal);
        var contracts = Sql(Sep60Contracts, "select \"Status\", count(*) from \"Contracts\" group by 1 order by 1").ToList();
        var employees = Sql(Sep60Core, "select \"CurrentStatus\", count(*) from \"Employees\" group by 1 order by 1").ToList();
        recorder.WriteScenarioDbChecks(new
        {
            payroll,
            expectedPayroll,
            contracts,
            expectedContracts = Sep60Env("CONTRACTS"),
            employees,
            expectedEmployees = Sep60Env("EMPLOYEES"),
            pictureFormat = "count|md5 and key|count (picture.sh's expressions; no amount)"
        });

        Assert.All(expectedPayroll, e => Assert.False(string.IsNullOrEmpty(e.Value), $"Set SEP060_VPS_{e.Key} from the picture."));
        foreach (var (key, value) in expectedPayroll)
            Assert.True(payroll[key] == value, $"{key}: {payroll[key]} on the demo, {value} in the picture before the deploy");
        Assert.Equal(Sep60Rows(Sep60Env("CONTRACTS")), contracts.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(Sep60Rows(Sep60Env("EMPLOYEES")), employees.Order(StringComparer.Ordinal).ToList());
        recorder.WriteResult("Pass");
    }
}
