using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-49 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password is
/// read from the server's .env into memory only. Evidence goes under <c>artifacts/plan49/vps</c>: it is never committed. The second check
/// leaves on the server one leave request (approved, then cancelled: the balance ends as it began) and one establishment change request
/// (a freeze of a vacant position, submitted, then cancelled with its approval), both marked «PLAN-49 server check», and the three ledger
/// rows they wrote. The third check reads what every server check of the plans before wrote since the deploy: run it after them.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pol49Marker = "PLAN-49 server check";
    private const string Pol49LeaveMaker = "e2e-maker-contracts";
    private const string Pol49Operator = "hradmin";
    private const string Pol49Reader = "e2e-policy-author";
    private const string Pol49SchemaRefusal = "حقل غير مسموح";

    /// <summary>The rules this plan (and PLAN-47) withdrew: none may be in an effective version.</summary>
    private static readonly string[] Pol49WithdrawnRules = ["R-ID-PROCESS", "R-LEAVE-MAX", "R-LEAVE-LONG", "R-REC-HIRE"];

    /// <summary>The eight services that no longer register the policy client (they never called it).</summary>
    private static readonly string[] Pol49WithoutClient =
        ["hr-health-web", "hr-movements-web", "hr-training-web", "payroll-web", "records-archive-web", "it-infra-web", "dev-analytics-web", "compliance-web"];

    /// <summary>The three of them whose only reader of HR.Core's address was that client: the key is gone from their environment.</summary>
    private static readonly string[] Pol49WithoutCoreAddress = ["hr-health-web", "hr-training-web", "dev-analytics-web"];

    /// <summary>The service that asks for each governed domain, and the compose service whose configuration says in which mode.</summary>
    private static readonly (string Service, string[] Domains)[] Pol49ModeHolders =
    [
        ("hr-core-web", ["Assets.Custody", "Contracts.Leave", "Core.Establishment", "Core.Recruitment", "IDCard.Lifecycle"]),
        ("hr-contracts-web", ["Contracts.Leave"]),
        ("admin-logistics-web", ["Assets.Custody"]),
        ("idcard-web", ["IDCard.Lifecycle"])
    ];

    private static EvidenceRecorder StartPol49(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pol49-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan49", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    /// <summary>One statement, written over several lines here, sent as one line.</summary>
    private static string[] Pol49Sql(string database, string sql) => Sql(database, Regex.Replace(sql, @"\s+", " ").Trim());

    private static readonly JsonSerializerOptions Pol49DecisionOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The ledger rows that match <paramref name="where"/> (alias <c>l</c>), read as PolicyScopeJourney reads them on the stack.</summary>
    private static List<PolicyLedgerRow> Pol49Ledger(string where)
    {
        var json = string.Join("", Pol49Sql("hr_core_db", $"""
            select coalesce(json_agg(json_build_object(
                'id', l."Id", 'domain', l."Domain", 'action', l."Action", 'mode', l."Mode", 'caller', l."CallerClientId", 'tenant', l."TenantId",
                'entity', l."EntityId", 'subject', l."SubjectType", 'factsHash', l."FactsHash", 'instance', l."WorkflowInstanceId",
                'actor', l."ActorUserId", 'decision', l."DecisionJson") order by l."OccurredAt", l."Id"), '[]'::json)::text
            from "PolicyEnforcementLedgers" l where {where}
            """));
        static Guid? Optional(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(row => new PolicyLedgerRow(
            row.GetProperty("id").GetGuid(),
            row.GetProperty("domain").GetString()!,
            row.GetProperty("action").GetString()!,
            row.GetProperty("mode").GetString()!,
            row.GetProperty("caller").GetString(),
            row.GetProperty("tenant").GetGuid(),
            Optional(row.GetProperty("entity")),
            row.GetProperty("subject").GetString(),
            row.GetProperty("factsHash").GetString()!,
            Optional(row.GetProperty("instance")),
            Optional(row.GetProperty("actor")),
            JsonSerializer.Deserialize<PolicyDecision>(row.GetProperty("decision").GetString()!, Pol49DecisionOptions)!)).ToList();
    }

    private static string Pol49Clock() =>
        Sql("hr_core_db", "select to_char(now() at time zone 'UTC', 'YYYY-MM-DD HH24:MI:SS.US')").Single();

    /// <summary>
    /// The modes a running service sees: its appsettings.json, then appsettings.Production.json, then its environment (the order
    /// the host reads them in). Keys are the names under <c>PolicyEngine</c>.
    /// </summary>
    private static Dictionary<string, string> Pol49Modes(string service)
    {
        var container = ContainerOf(service);
        Assert.False(string.IsNullOrEmpty(container), $"{service} is not running.");
        Assert.Equal("Production", Sh($"docker exec {container} printenv ASPNETCORE_ENVIRONMENT").Output.Trim());

        var modes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (var file in new[] { "appsettings.json", "appsettings.Production.json" })
        {
            var (code, text) = Sh($"docker exec {container} cat /app/{file}");
            Assert.True(code == 0, $"{service}: /app/{file} could not be read.");
            using var document = JsonDocument.Parse(text.TrimStart((char)0xFEFF), options);
            if (!document.RootElement.TryGetProperty("PolicyEngine", out var section) || section.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var property in section.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                modes[property.Name] = property.Value.GetString()!;
        }

        var (_, environment) = Sh($"docker exec {container} sh -c {Q("printenv | grep '^PolicyEngine__' || true")}");
        foreach (var line in environment.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = line.IndexOf('=', StringComparison.Ordinal);
            if (at > 0)
                modes[line["PolicyEngine__".Length..at].Replace("__", ":", StringComparison.Ordinal)] = line[(at + 1)..];
        }

        return modes;
    }

    private static List<(string Domain, string Action, string Mode, string Outcome, long Milliseconds)> Pol49ClientCalls(string service, string? sinceUtc = null)
    {
        var since = sinceUtc is null ? "" : $"--since {sinceUtc} ";
        var (_, lines) = Sh($"docker logs {since}{ContainerOf(service)} 2>&1 | grep -o {Q("policy_client_call domain=[A-Za-z.]* action=[A-Za-z]* mode=[A-Za-z]* outcome=[A-Za-z0-9-]* elapsed_ms=[0-9]*")} || true");
        return Regex.Matches(lines, @"policy_client_call domain=(\S+) action=(\S+) mode=(\S+) outcome=(\S+) elapsed_ms=(\d+)")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, long.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>
    /// Removes, by the pages' own actions, whatever this check wrote and is still open: a leave carrying the check's note that
    /// is Approved is cancelled (its days go back), one that is still Pending is deleted (the page cancels approved leaves
    /// only), and a freeze request carrying the note that is a draft or waits for approval is cancelled with its approval
    /// instance. Says what it did; a row that is still open afterwards fails, so a leftover cannot pass unseen.
    /// </summary>
    private static async Task<List<string>> Pol49CancelOpenAsync(HttpClient operatorClient)
    {
        var done = new List<string>();
        foreach (var row in Sql("hr_contracts_db",
                     $"select \"Id\", \"Status\" from \"LeaveRequests\" where \"Notes\" like '{Pol49Marker}%' and \"Status\" in ('Pending', 'Approved')"))
        {
            var leave = row.Split('|')[0];
            var action = row.Split('|')[1] == "Approved" ? "Cancel" : "Delete";
            await PostFormAsync(operatorClient, $"/ui/hr-contracts/LeaveRequests/{action}/{leave}",
                new Dictionary<string, string> { ["__RequestVerificationToken"] = await TokenAsync(operatorClient, "/ui/hr-contracts/LeaveRequests") });
            var after = Sql("hr_contracts_db",
                $"select \"Status\" from \"LeaveRequests\" where \"Id\" = '{leave}' and \"Status\" in ('Pending', 'Approved')");
            Assert.True(after.Length == 0, $"The leave {leave} of an earlier or failed run is still {string.Join(",", after)} after {action}.");
            done.Add($"leave {leave}: {action}");
        }

        foreach (var change in Sql("hr_core_db",
                     "select \"Id\" from \"EstablishmentChangeRequests\" where \"ChangeType\" = 'FreezeFundedPosition' and \"Status\" in ('Draft', 'PendingApproval') " +
                     $"and (\"PayloadJson\" like '%{Pol49Marker}%' or \"DecisionReference\" = '{Pol49Marker}')"))
        {
            await Wf45PostRenderedAsync(operatorClient, $"/ui/hr-core/EstablishmentChanges/Details/{change}", "Cancel");
            var after = Sql("hr_core_db", $"select \"Status\" from \"EstablishmentChangeRequests\" where \"Id\" = '{change}'").Single();
            Assert.True(after == "Cancelled", $"The freeze request {change} of an earlier or failed run is still {after} after Cancel.");
            done.Add($"change {change}: Cancel");
        }

        return done;
    }

    private static void Pol49AssertShadowRow(PolicyLedgerRow row, string domain, string action, string? caller, string subject)
    {
        Assert.Equal(domain, row.Domain);
        Assert.Equal(action, row.Action);
        Assert.Equal("Shadow", row.Mode);
        Assert.Equal(caller, row.Caller);
        Assert.Equal(TenantDefaults.DefaultTenantId, row.Tenant);
        Assert.Equal(subject, row.Subject);
        // Shadow records and starts nothing.
        Assert.Null(row.Instance);
        // The facts matched the schema: a request with a key the schema lacks is refused whole, with this message.
        Assert.DoesNotContain(row.Decision.Explanations, e => e.Contains(Pol49SchemaRefusal, StringComparison.Ordinal));
        Assert.DoesNotContain(Pol49SchemaRefusal, row.Decision.DenyReason ?? string.Empty, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void AC_POL_49_vps_the_migration_the_sets_the_retired_templates_and_the_modes()
    {
        var recorder = StartPol49("vps-schema", "Demo server: the migration and the four change sets, fifteen templates retired by a final version, the governed versions without the withdrawn rules, the modes, the services without the client");
        var outOfScope = PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        var governed = PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        Assert.Equal(15, outOfScope.Length);
        Assert.Equal(5, governed.Length);

        // 1. The migration, and the four sets: the retirement once for each of the fifteen, the three others each on its domain.
        Assert.Single(Sql("hr_core_db", "select \"MigrationId\" from \"__EFMigrationsHistory\" where \"MigrationId\" like '%POL049_PolicyDomainRetirement'"));
        var applications = Pol49Sql("hr_core_db", """
            select a."ChangeSetId" || '|' || t."Domain" || '|' || a."Outcome" from "PolicySeedApplications" a
            join "PolicyTemplates" t on t."Id" = a."PolicyTemplateId" where a."ChangeSetId" like 'POL-049-%' order by 1
            """).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        var expected = outOfScope.Select(domain => $"POL-049-SCOPE-RETIRE|{domain}|Published")
            .Concat(["POL-049-ASSETS-ACTION|Assets.Custody|Published", "POL-049-IDCARD-ORPHAN|IDCard.Lifecycle|Published", "POL-049-LEAVE|Contracts.Leave|Published"])
            .OrderBy(a => a, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, applications);

        // 2. The templates: the effective version of each national template, with its rules.
        var templates = Pol49Sql("hr_core_db", """
            select t."Domain" || '|' || (t."RetiredAt" is not null)::text || '|' || v."VersionNumber" || '|' || v."WithdrawsAllRules"::text || '|' ||
                   (select max(x."VersionNumber") from "PolicyTemplateVersions" x where x."PolicyTemplateId" = t."Id") || '|' ||
                   coalesce((select string_agg(r."RuleKey", ',' order by r."RuleKey") from "PolicyRules" r where r."PolicyTemplateVersionId" = v."Id"), '')
            from "PolicyTemplates" t
            join lateral (select * from "PolicyTemplateVersions" v where v."PolicyTemplateId" = t."Id" and v."Status" = 'Published'
                            and (v."EffectiveFrom" is null or v."EffectiveFrom" <= now()) and (v."EffectiveTo" is null or v."EffectiveTo" > now())
                          order by v."VersionNumber" desc limit 1) v on true
            where t."Scope" = 'National' order by 1
            """).Select(line => line.Split('|')).ToDictionary(
                cells => cells[0],
                cells => (Retired: cells[1] == "true", Version: cells[2], WithdrawsAll: cells[3] == "true", Latest: cells[4],
                    Rules: cells[5].Split(',', StringSplitOptions.RemoveEmptyEntries)),
                StringComparer.Ordinal);
        Assert.Equal(outOfScope.Concat(governed).OrderBy(d => d, StringComparer.Ordinal), templates.Keys.OrderBy(d => d, StringComparer.Ordinal));
        foreach (var domain in outOfScope)
        {
            var template = templates[domain];
            Assert.True(template.Retired, $"{domain} is not marked retired.");
            Assert.True(template.Rules.Length == 0, $"{domain}: the effective version still holds {string.Join(",", template.Rules)}.");
            Assert.True(template.WithdrawsAll, $"{domain}: the effective version is not a version that withdraws all rules.");
            // The final version is the last one: nothing was written after it.
            Assert.Equal(template.Latest, template.Version);
        }

        foreach (var domain in governed)
        {
            var template = templates[domain];
            Assert.False(template.Retired, $"{domain} is governed and marked retired.");
            Assert.DoesNotContain(template.Rules, rule => Pol49WithdrawnRules.Contains(rule));
        }

        Assert.Contains("R-LEAVE-SELF", templates["Contracts.Leave"].Rules);
        Assert.Empty(Pol49Sql("hr_core_db", """
            select t."Domain" from "PolicyTemplateVersions" v join "PolicyTemplates" t on t."Id" = v."PolicyTemplateId" where v."Status" = 'Draft'
            """));

        // The custody rules with an effect only the handover reads are bound to it, and the self-approval rule to the approval.
        const string effectiveOf = """
            (select v."Id" from "PolicyTemplateVersions" v join "PolicyTemplates" t on t."Id" = v."PolicyTemplateId"
             where t."Domain" = '{0}' and t."Scope" = 'National' and v."Status" = 'Published'
               and (v."EffectiveFrom" is null or v."EffectiveFrom" <= now()) and (v."EffectiveTo" is null or v."EffectiveTo" > now())
             order by v."VersionNumber" desc limit 1)
            """;
        var handoverRules = Pol49Sql("hr_core_db",
            "select r.\"RuleKey\" || '|' || r.\"WhenJson\" from \"PolicyRules\" r where r.\"PolicyTemplateVersionId\" = " +
            string.Format(CultureInfo.InvariantCulture, effectiveOf, "Assets.Custody") +
            " and (r.\"ThenJson\" like '%TriggerWorkflow%' or r.\"ThenJson\" like '%RequireEvidence%') order by 1");
        Assert.Equal(["R-HIGH-APPROVAL", "R-PRIV-EVIDENCE", "R-PRIV-WF"], handoverRules.Select(line => line.Split('|')[0]));
        Assert.All(handoverRules, line => Assert.Contains("""{"field":"action","operator":"Equals","value":"Assign"}""", line, StringComparison.Ordinal));
        var selfApproval = Pol49Sql("hr_core_db",
            "select r.\"WhenJson\" from \"PolicyRules\" r where r.\"RuleKey\" = 'R-LEAVE-SELF' and r.\"PolicyTemplateVersionId\" = " +
            string.Format(CultureInfo.InvariantCulture, effectiveOf, "Contracts.Leave")).Single();
        Assert.Contains("""{"field":"action","operator":"Equals","value":"Approve"}""", selfApproval, StringComparison.Ordinal);

        // 3. What HR.Core said about its data at its last start, both parts read by their place in the log (a part that could
        // not be taken fails here; the numbers of an earlier start never stand for it): no rule of an effective version is one
        // the validator refuses, of all twenty national templates, and no agency override is.
        var (_, logs) = Sh($"docker logs {ContainerOf("hr-core-web")} 2>&1 | grep -E 'PLAN-47 validator inventory|PLAN-49 agency override inventory|Unhandled exception' || true");
        Assert.DoesNotContain("Unhandled exception", logs);
        var inventory = PolicyScopeJourney.ParseStartUpInventory(logs);
        Assert.Equal(0, inventory.NationalInForce);
        Assert.Equal(outOfScope.Length + governed.Length, inventory.NationalTemplatesRead);
        Assert.Equal(0, inventory.OverridesInForce);
        Assert.Empty(inventory.OverrideRules);

        // 4. Modes, as the four running services see them: every governed domain in Shadow, nothing in Enforce, nothing set for a
        // domain out of scope (the start-up check would have stopped the service).
        var modes = Pol49ModeHolders.ToDictionary(holder => holder.Service, holder => Pol49Modes(holder.Service), StringComparer.Ordinal);
        foreach (var (service, domains) in Pol49ModeHolders)
        {
            foreach (var domain in domains)
                Assert.True(modes[service].TryGetValue(domain, out var mode) && mode == "Shadow", $"{service}: {domain} is not in Shadow.");
            Assert.DoesNotContain(modes[service], pair => string.Equals(pair.Value, "Enforce", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(modes[service], pair => outOfScope.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                                                          && !string.Equals(pair.Value, "Off", StringComparison.OrdinalIgnoreCase));
        }

        // 7. The eight services that lost the client they never called run and are healthy; three of them lost HR.Core's address
        // too. That the registration is gone is what the unit guard proves (Pol049PolicyDomainCatalogTests); here: they start
        // and stay healthy without it.
        const string healthFormat = "'{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}'";
        var withoutClient = Pol49WithoutClient.ToDictionary(service => service, service =>
        {
            var container = ContainerOf(service);
            Assert.False(string.IsNullOrEmpty(container), $"{service} is not running.");
            return Sh("docker inspect -f " + healthFormat + " " + container).Output.Trim();
        }, StringComparer.Ordinal);
        Assert.All(withoutClient, pair => Assert.True(pair.Value == "running|healthy", $"{pair.Key}: {pair.Value}"));
        foreach (var service in Pol49WithoutCoreAddress)
            Assert.Equal(string.Empty, Sh($"docker exec {ContainerOf(service)} printenv Integration__HrCoreBaseUrl").Output.Trim());

        recorder.WriteScenarioDbChecks(new
        {
            applications,
            templates = templates.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
            {
                domain = pair.Key,
                retired = pair.Value.Retired,
                effectiveVersion = pair.Value.Version,
                withdrawsAllRules = pair.Value.WithdrawsAll,
                rules = pair.Value.Rules
            }),
            handoverRules,
            selfApproval,
            startUpInventory = new
            {
                nationalRulesOfEffectiveVersionsRefused = inventory.NationalInForce,
                nationalTemplatesRead = inventory.NationalTemplatesRead,
                overridesInForceRefused = inventory.OverridesInForce,
                agencyTenantsRead = inventory.AgencyTenantsRead
            },
            modes,
            withoutClient,
            withoutCoreAddress = Pol49WithoutCoreAddress
        });
        recorder.WriteScenarioSteps(
            "psql (hr_core_db): POL049_PolicyDomainRetirement is recorded; PolicySeedApplications holds POL-049-SCOPE-RETIRE once for each of the fifteen domains out of scope, and POL-049-ASSETS-ACTION, POL-049-IDCARD-ORPHAN and POL-049-LEAVE, each Published;\n" +
            "the fifteen national templates out of scope are marked retired and their effective version is the last one, holds no rule and withdraws all; the five governed templates are not retired and hold none of R-ID-PROCESS, R-LEAVE-MAX, R-LEAVE-LONG, R-REC-HIRE; no draft is open;\n" +
            "the custody rules R-PRIV-EVIDENCE, R-PRIV-WF and R-HIGH-APPROVAL carry action=Assign, and R-LEAVE-SELF carries action=Approve.\n" +
            "docker logs (hr-core-web): the last start-up read the twenty national templates and lists 0 rules of effective versions the validator refuses and 0 agency override rules; neither part failed, and there is no unhandled exception.\n" +
            "docker exec (the four services): ASPNETCORE_ENVIRONMENT=Production; PolicyEngine of the files and the environment: every governed domain Shadow, none Enforce, none set for a domain out of scope.\n" +
            "docker inspect: the eight services of this release that no longer register the policy client (the unit guard proves the removal) are running and healthy; hr-health-web, hr-training-web and dev-analytics-web have no Integration__HrCoreBaseUrl.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_POL_49_vps_a_leave_and_an_establishment_change_each_leave_a_Shadow_ledger_row()
    {
        var recorder = StartPol49("vps-actions", "Demo server: a leave (created, approved, cancelled) and an establishment change (submitted, cancelled) each leave Shadow ledger rows, and nothing they do is changed by the engine");
        var password = DemoPassword();
        var makerId = Guid.Parse(KeycloakUserId(Pol49LeaveMaker));
        var operatorId = Guid.Parse(KeycloakUserId(Pol49Operator));
        // The server's own clocks bound what this check wrote: the host's for the container logs, the database's for the ledger.
        var startedUtc = Sh("date -u +%Y-%m-%dT%H:%M:%SZ").Output.Trim();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", startedUtc);
        var clock = Pol49Clock();

        // ---- The leave: a future working period (Sunday to Tuesday) of an employee with an annual balance and no leave in it.
        // The approval refuses a leave that starts sooner than the notice of its policy (seven days for an annual leave in the
        // demo seed), and the demo seed gave balances for the year it ran in. So the period is the first one past the notice,
        // read from the server with a day to spare: it stays inside the year until the last days of December, and after that
        // the balance query below says so before anything is written.
        var notice = int.Parse(
            Sql("hr_contracts_db", "select coalesce(max(\"MinNoticeDays\"), 7) from \"LeavePolicies\" where \"LeaveType\" = 'Annual'").Single(),
            CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddDays(Math.Max(notice, 7) + 1);
        while (start.DayOfWeek != DayOfWeek.Sunday)
            start = start.AddDays(1);
        var end = start.AddDays(2);
        var startText = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endText = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var candidate = Sql("hr_contracts_db",
            "select b.\"EmployeeId\", b.\"Id\", b.\"Used\" from \"LeaveBalances\" b " +
            $"where b.\"LeaveType\" = 'Annual' and b.\"Year\" = {start.Year} and b.\"TotalEntitlement\" - b.\"Used\" >= 10 " +
            "and not exists (select 1 from \"LeaveRequests\" l where l.\"EmployeeId\" = b.\"EmployeeId\" " +
            $"and l.\"Status\" in ('Pending', 'Approved') and l.\"EndDate\" >= '{startText}' and l.\"StartDate\" <= '{endText}') " +
            "order by b.\"EmployeeId\" limit 1");
        AcceptanceGate.RequireData(recorder, candidate.Length == 1, "لا رصيد إجازات سنوية مناسب",
            $"No employee with an Annual {start.Year} balance of 10 days or more and no leave in {startText}..{endText}.");
        var employeeId = Guid.Parse(candidate[0].Split('|')[0]);
        var balanceId = Guid.Parse(candidate[0].Split('|')[1]);
        var usedBefore = int.Parse(candidate[0].Split('|')[2], CultureInfo.InvariantCulture);

        string leaveId;
        int duration;
        string leaveAfterApproval;
        string leaveAfterCancel;
        string changeId;
        string positionId;
        string changeAfterSubmit;
        string changeAfterCancel;
        List<string> leftFromEarlierRuns;
        var (operatorClient, _) = await SignInAsync(Pol49Operator, password);
        using (operatorClient)
        {
            // What an earlier run of this check left open (it stopped between a write and its cancel) is cancelled first.
            leftFromEarlierRuns = await Pol49CancelOpenAsync(operatorClient);
            try
            {
                var (maker, _) = await SignInAsync(Pol49LeaveMaker, password, "/ui/hr-contracts/");
                using (maker)
                {
                    var (createStatus, createPath, _) = await PostFormAsync(maker, "/ui/hr-contracts/LeaveRequests/Create", new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = await TokenAsync(maker, "/ui/hr-contracts/LeaveRequests/Create"),
                        ["EmployeeId"] = employeeId.ToString(),
                        ["LeaveType"] = "Annual",
                        ["StartDate"] = startText,
                        ["EndDate"] = endText,
                        ["DurationDays"] = "3",
                        ["Notes"] = $"{Pol49Marker} (cancelled afterwards)"
                    });
                    Assert.True(createStatus == 200, $"leave create answered {createStatus} at {createPath}");
                    var created = Sql("hr_contracts_db",
                        $"select \"Id\", \"Status\", \"DurationDays\" from \"LeaveRequests\" where \"EmployeeId\" = '{employeeId}' and \"StartDate\" = '{startText}' " +
                        $"and \"Notes\" like '{Pol49Marker}%' order by \"CreatedAt\" desc limit 1");
                    Assert.True(created.Length == 1, "The leave was not created.");
                    leaveId = created[0].Split('|')[0];
                    Assert.Equal("Pending", created[0].Split('|')[1]);
                }

                duration = int.Parse(Sql("hr_contracts_db", $"select \"DurationDays\" from \"LeaveRequests\" where \"Id\" = '{leaveId}'").Single(), CultureInfo.InvariantCulture);

                // Another person approves it, then cancels it: the balance ends as it began.
                var (status, finalPath, _) = await PostFormAsync(operatorClient, $"/ui/hr-contracts/LeaveRequests/Approve/{leaveId}",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = await TokenAsync(operatorClient, "/ui/hr-contracts/LeaveRequests") });
                Assert.True(status == 200, $"approve answered {status} at {finalPath}");
                leaveAfterApproval = Sql("hr_contracts_db",
                    $"select l.\"Status\", b.\"Used\" from \"LeaveRequests\" l join \"LeaveBalances\" b on b.\"Id\" = '{balanceId}' where l.\"Id\" = '{leaveId}'").Single();
                Assert.Equal($"Approved|{usedBefore + duration}", leaveAfterApproval);

                (status, finalPath, _) = await PostFormAsync(operatorClient, $"/ui/hr-contracts/LeaveRequests/Cancel/{leaveId}",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = await TokenAsync(operatorClient, "/ui/hr-contracts/LeaveRequests") });
                Assert.True(status == 200, $"cancel answered {status} at {finalPath}");
                leaveAfterCancel = Sql("hr_contracts_db",
                    $"select l.\"Status\", b.\"Used\" from \"LeaveRequests\" l join \"LeaveBalances\" b on b.\"Id\" = '{balanceId}' where l.\"Id\" = '{leaveId}'").Single();
                Assert.Equal($"Cancelled|{usedBefore}", leaveAfterCancel);

                // ---- The establishment change: a freeze of a vacant position nobody is changing, submitted and cancelled again.
                var position = Pol49Sql("hr_core_db", """
                    select p."Id" from "FundedPositions" p where p."Status" = 'Vacant' and not exists
                      (select 1 from "EstablishmentChangeRequests" c where c."TargetEntityId" = p."Id" and c."Status" in ('Draft', 'PendingApproval'))
                    order by p."PositionCode" desc limit 1
                    """);
                AcceptanceGate.RequireData(recorder, position.Length == 1, "لا وظيفة شاغرة", "No vacant funded position without an open change request.");
                positionId = position[0];
                await Wf45PostRenderedAsync(operatorClient, $"/ui/hr-core/FundedPositions/Details/{positionId}", "Freeze",
                    new Dictionary<string, string> { ["FreezeReason"] = Pol49Marker });
                var draft = Sql("hr_core_db",
                    $"select \"Id\" from \"EstablishmentChangeRequests\" where \"TargetEntityId\" = '{positionId}' and \"Status\" = 'Draft' and \"ChangeType\" = 'FreezeFundedPosition' " +
                    "order by \"CreatedAt\" desc limit 1");
                Assert.True(draft.Length == 1, "The freeze request was not drafted.");
                changeId = draft[0];
                Assert.Empty(Pol49Ledger($"l.\"EntityId\" = '{changeId}'"));

                var details = $"/ui/hr-core/EstablishmentChanges/Details/{changeId}";
                await Wf45PostRenderedAsync(operatorClient, details, "Submit", new Dictionary<string, string>
                {
                    ["Submit.LegalSourceId"] = "SYR-LAW-50-2004",
                    ["Submit.DecisionReference"] = Pol49Marker
                });
                changeAfterSubmit = Pol49Sql("hr_core_db", $"""
                    select c."Status" || '|' || d."Code" || '|' || i."Status" from "EstablishmentChangeRequests" c
                    join "WorkflowInstances" i on i."Id" = c."WorkflowInstanceId" join "WorkflowDefinitions" d on d."Id" = i."DefinitionId" where c."Id" = '{changeId}'
                    """).SingleOrDefault() ?? "(no instance)";
                // Shadow changes nothing: the submission goes on by the workflow the service asked for.
                Assert.Equal("PendingApproval|ESTABLISHMENT-CHANGE-APPROVAL|InProgress", changeAfterSubmit);
                var instanceId = Sql("hr_core_db", $"select \"WorkflowInstanceId\" from \"EstablishmentChangeRequests\" where \"Id\" = '{changeId}'").Single();

                await Wf45PostRenderedAsync(operatorClient, details, "Cancel");
                changeAfterCancel = Pol49Sql("hr_core_db", $"""
                    select c."Status" || '|' || coalesce(c."WorkflowInstanceId"::text, '') || '|' || (select i."Status" from "WorkflowInstances" i where i."Id" = '{instanceId}')
                           || '|' || (select p."Status" from "FundedPositions" p where p."Id" = '{positionId}')
                    from "EstablishmentChangeRequests" c where c."Id" = '{changeId}'
                    """).Single();
                Assert.Equal("Cancelled||Cancelled|Vacant", changeAfterCancel);
            }
            finally
            {
                // A failure between a write and its cancel must not leave demo data: whatever this check wrote and is still
                // open is cancelled by the pages' own actions. A run that passed has nothing left to cancel. The failure that
                // brought us here is the one to report, so a clean-up that fails too only says where to look.
                try
                {
                    await Pol49CancelOpenAsync(operatorClient);
                }
                catch (Exception cleanUp) when (cleanUp is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"PLAN-49 server check: the clean-up failed ({cleanUp.GetType().Name}). Look for rows marked '{Pol49Marker}' that are still open.");
                }
            }
        }

        // ---- The ledger: the leave's two questions came from HR.Contracts, the submission was asked inside HR.Core.
        var leaveRows = Pol49Ledger($"l.\"EntityId\" = '{leaveId}'");
        Assert.Equal(2, leaveRows.Count);
        var create = Assert.Single(leaveRows, row => row.Action == "Create");
        var approve = Assert.Single(leaveRows, row => row.Action == "Approve");
        foreach (var row in leaveRows)
        {
            Pol49AssertShadowRow(row, PolicyDomains.ContractsLeave, row.Action, "svc-hr-contracts", "LeaveRequest");
            Assert.True(row.Decision.Allowed, $"{row.Action}: {row.Decision.DenyReason}");
        }

        Assert.Equal(PolicyScopeJourney.FactsHash(PolicyDomainFactsBuilders.ForLeaveRequest(Guid.Empty, null, duration, "Annual", "Create").Facts), create.FactsHash);
        Assert.Equal(PolicyScopeJourney.FactsHash(PolicyDomainFactsBuilders.ForLeaveRequest(Guid.Empty, null, duration, "Annual", "Approve", false).Facts), approve.FactsHash);
        Assert.DoesNotContain("R-LEAVE-SELF", approve.Decision.MatchedRuleIds);
        Assert.Equal(makerId, create.Actor);
        Assert.Equal(operatorId, approve.Actor);

        var changeRow = Assert.Single(Pol49Ledger($"l.\"EntityId\" = '{changeId}'"));
        Pol49AssertShadowRow(changeRow, PolicyDomains.CoreEstablishment, "SubmitForApproval", caller: null, "EstablishmentChangeRequest");
        Assert.True(changeRow.Decision.Allowed, changeRow.Decision.DenyReason);
        Assert.Equal(operatorId, changeRow.Actor);
        Assert.Contains("R-EST-SUBMIT", changeRow.Decision.MatchedRuleIds);
        Assert.DoesNotContain("R-EST-DENY-BRANCH", changeRow.Decision.MatchedRuleIds);
        Assert.Equal(
            PolicyScopeJourney.FactsHash(PolicyDomainFactsBuilders.ForEstablishmentChange(
                Guid.Parse(changeId), null, "SubmitForApproval", "FreezeFundedPosition", "Draft", actorIsBranchScoped: false).Facts),
            changeRow.FactsHash);

        // Nothing else was asked for these two people since the check began, and nothing in Enforce. Counted by who asked, not
        // by the entity: a question under another or an empty entity id would show here too.
        var since = Pol49Ledger($"l.\"OccurredAt\" >= '{clock}' and l.\"ActorUserId\" in ('{makerId}', '{operatorId}')");
        Assert.Equal(3, since.Count);
        Assert.All(since, row => Assert.Equal("Shadow", row.Mode));

        // What the two Shadow calls cost the leave's actions (the client's own clock, in HR.Contracts' log).
        var calls = Pol49ClientCalls("hr-contracts-web", startedUtc).Where(call => call.Domain == PolicyDomains.ContractsLeave).ToList();
        Assert.True(calls.Count >= 2, $"{calls.Count} timed policy calls in hr-contracts-web's log; two were made.");
        Assert.All(calls, call => Assert.True(call.Mode == "Shadow" && call.Outcome == "ok", $"{call.Action}: mode {call.Mode}, outcome {call.Outcome}"));

        recorder.WriteScenarioDbChecks(new
        {
            leave = new { id = leaveId, employeeId, from = startText, to = endText, workingDays = duration, afterApproval = leaveAfterApproval, afterCancel = leaveAfterCancel, balanceUsedBefore = usedBefore },
            change = new { id = changeId, positionId, type = "FreezeFundedPosition", afterSubmit = changeAfterSubmit, afterCancel = changeAfterCancel },
            ledger = leaveRows.Append(changeRow).Select(row => new
            {
                row.Id,
                row.Domain,
                row.Action,
                row.Mode,
                row.Caller,
                row.Tenant,
                row.Entity,
                row.Actor,
                row.FactsHash,
                allowed = row.Decision.Allowed,
                matched = row.Decision.MatchedRuleIds
            }),
            // What the assertions above established, and what these actions also leave that this check does not count.
            leftOnTheServer = new
            {
                leaveRequests = 1,
                leaveStatus = leaveAfterCancel.Split('|')[0],
                establishmentChangeRequests = 1,
                changeStatus = changeAfterCancel.Split('|')[0],
                workflowInstances = 1,
                instanceStatus = changeAfterCancel.Split('|')[2],
                ledgerRows = since.Count,
                alsoLeftAndNotCounted = "the leave's service transaction with its reference number, the change request's number, the archive item of the approval, audit rows"
            },
            cancelledFromEarlierRuns = leftFromEarlierRuns,
            shadowCallMilliseconds = PolicyScopeJourney.LatencySummary(calls.Select(call => call.Milliseconds).ToList())
        });
        recorder.WriteScenarioSteps(
            "1. e2e-maker-contracts records an annual leave of three working days (Sunday to Tuesday, five weeks ahead) on the HR page; hradmin approves it (the balance is debited) and cancels it (the balance is back).\n" +
            "2. psql (hr_core_db): two ledger rows for the leave, Create and Approve, Mode Shadow, CallerClientId svc-hr-contracts, the server's tenant, each an allow with no «حقل غير مسموح»; the facts hash of each equals the hash of what the builder sends (the approval with isSelfApproval=false); the actors are the maker and the approver.\n" +
            "3. hradmin drafts a freeze of a vacant funded position, submits it with a legal source: PendingApproval with an ESTABLISHMENT-CHANGE-APPROVAL instance in progress (Shadow changes nothing); then cancels it: the request and its instance are Cancelled, the position is still Vacant.\n" +
            "4. psql: one ledger row for the change, SubmitForApproval, Mode Shadow, no caller client (the domain runs inside HR.Core), the actor hradmin, R-EST-SUBMIT matched and R-EST-DENY-BRANCH not; the facts hash is the builder's with hasOrgScope=false.\n" +
            "5. docker logs (hr-contracts-web): the duration of each of the leave's Shadow calls to HR.Core (policy_client_call ... elapsed_ms=).");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_POL_49_vps_every_governed_domain_has_Shadow_rows_since_the_deploy_and_the_page_shows_the_scope()
    {
        var recorder = StartPol49("vps-ledger", "Demo server: since the deploy every governed domain has Shadow ledger rows from its owner, nothing in Enforce and nothing for a domain out of scope; the policy page shows five governed domains and fifteen out of scope");
        var governed = PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        var outOfScope = PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal).ToArray();

        // "Since the deploy": since this release first started on this database, which is when it applied its change sets. The
        // moment is in the database itself, so a later restart or recreate of HR.Core (AC-OPS-39f does one) does not move it.
        var startedAt = Sql("hr_core_db",
            "select to_char(min(\"AppliedAt\") at time zone 'UTC', 'YYYY-MM-DD HH24:MI:SS') from \"PolicySeedApplications\" where \"ChangeSetId\" like 'POL-049-%'").SingleOrDefault();
        Assert.True(startedAt is not null, "No POL-049 change set is recorded: this release has not started on this database.");
        var since = startedAt + "+00";
        var groups = Pol49Sql("hr_core_db", $"""
            select l."Domain" || '|' || l."Mode" || '|' || coalesce(l."CallerClientId", '') || '|' || count(*) from "PolicyEnforcementLedgers" l
            where l."OccurredAt" >= '{since}' group by l."Domain", l."Mode", l."CallerClientId" order by 1
            """).Select(line => line.Split('|')).Select(cells => (Domain: cells[0], Mode: cells[1], Caller: cells[2], Rows: int.Parse(cells[3], CultureInfo.InvariantCulture))).ToList();

        Assert.All(groups, group => Assert.Equal("Shadow", group.Mode));
        Assert.All(groups, group => Assert.Contains(group.Domain, governed));
        foreach (var domain in governed)
        {
            var owner = PolicyDomainCatalog.Find(domain)!.OwnerClientId ?? string.Empty;
            var ofDomain = groups.Where(group => group.Domain == domain).ToList();
            Assert.True(ofDomain.Count > 0, $"{domain}: no ledger row since the deploy ({since}). Run the PLAN-43..46 server checks and this plan's second check first.");
            // Only the owner asked: its service client for the three external domains, nobody's (HR.Core itself) for the two others.
            Assert.All(ofDomain, group => Assert.Equal(owner, group.Caller));
        }

        // The page: the catalog, once.
        Dictionary<string, (string Status, string Mode, string Html)> rows;
        var (reader, _) = await SignInAsync(Pol49Reader, DemoPassword(), "/ui/hr-core/PolicyTemplates");
        using (reader)
        {
            // Razor writes non-Latin text as character references: the catalog's reasons are compared with the decoded page.
            var page = System.Net.WebUtility.HtmlDecode(await Wf45PageAsync(reader, "/ui/hr-core/PolicyTemplates"));
            rows = Regex.Matches(page, @"<tr data-testid=""policy-domain-row"" data-domain=""([^""]+)"" data-status=""([^""]+)"" data-mode=""([^""]+)"">(.*?)</tr>", RegexOptions.Singleline)
                .ToDictionary(m => m.Groups[1].Value, m => (Status: m.Groups[2].Value, Mode: m.Groups[3].Value, Html: m.Groups[4].Value), StringComparer.Ordinal);
        }

        Assert.Equal(governed.Concat(outOfScope).OrderBy(d => d, StringComparer.Ordinal), rows.Keys.OrderBy(d => d, StringComparer.Ordinal));
        foreach (var domain in governed)
        {
            Assert.Equal("governed", rows[domain].Status);
            Assert.Equal("Shadow", rows[domain].Mode);
        }

        foreach (var domain in outOfScope)
        {
            Assert.Equal("out-of-scope", rows[domain].Status);
            Assert.Equal("Off", rows[domain].Mode);
            Assert.Contains(PolicyDomainCatalog.Find(domain)!.OutOfScopeReasonAr!, rows[domain].Html, StringComparison.Ordinal);
            Assert.DoesNotContain(@"data-testid=""domain-draft-link""", rows[domain].Html, StringComparison.Ordinal);
        }

        // The cost of Shadow to the three services that call HR.Core (the client's own clock), as far as their containers' logs
        // go back: a container recreated since the calls has none to show, so the numbers are recorded and only a call that did
        // not end well fails the check.
        var latency = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (service, domain) in new[] { ("hr-contracts-web", "Contracts.Leave"), ("admin-logistics-web", "Assets.Custody"), ("idcard-web", "IDCard.Lifecycle") })
        {
            var calls = Pol49ClientCalls(service).Where(call => call.Domain == domain).ToList();
            Assert.All(calls, call => Assert.True(call.Mode == "Shadow" && call.Outcome == "ok", $"{service} {call.Action}: mode {call.Mode}, outcome {call.Outcome}"));
            latency[$"{service} {domain}"] = new
            {
                summary = PolicyScopeJourney.LatencySummary(calls.Select(call => call.Milliseconds).ToList()),
                byAction = calls.GroupBy(call => call.Action, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(call => call.Milliseconds).ToList(), StringComparer.Ordinal)
            };
        }

        var before = Pol49Sql("hr_core_db", $"""
            select l."Domain" || '|' || l."Mode" || '|' || coalesce(l."CallerClientId", '') || '|' || count(*) from "PolicyEnforcementLedgers" l
            where l."OccurredAt" < '{since}' group by l."Domain", l."Mode", l."CallerClientId" order by 1
            """);
        recorder.WriteScenarioDbChecks(new
        {
            releaseFirstStartedAtUtc = startedAt,
            ledgerSinceTheDeploy = groups.Select(group => new { group.Domain, group.Mode, caller = group.Caller.Length == 0 ? "(HR.Core)" : group.Caller, rows = group.Rows }),
            ledgerBeforeTheDeploy = before,
            page = new { governed = governed.Select(domain => new { domain, mode = rows[domain].Mode }), outOfScope = outOfScope.Select(domain => new { domain, mode = rows[domain].Mode }) },
            shadowCallMilliseconds = latency
        });
        recorder.WriteScenarioSteps(
            "Run after the PLAN-43..46 server checks (recruitment, custody, cards) and this plan's second check (leave, establishment).\n" +
            "1. psql (hr_core_db): the ledger rows since this release applied its change sets (its first start on this database): every one in Mode Shadow, every one of a governed domain; each of the five governed domains has rows, asked by its owner only (svc-admin-logistics, svc-hr-contracts, svc-idcard; no caller client for the two Core domains).\n" +
            "2. e2e-policy-author opens ui/hr-core/PolicyTemplates: the catalog table lists all 20 domains once: five governed in Shadow, fifteen out of scope in Off with the catalog's reason and no draft link.\n" +
            "3. docker logs (hr-contracts-web, admin-logistics-web, idcard-web): every timed Shadow call to HR.Core ended ok; its durations are recorded.");
        recorder.WriteResult("Pass");
    }
}
