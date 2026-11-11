using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-47 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password
/// is read from the server's .env into memory only. Evidence goes under <c>artifacts/plan47/vps</c>: it is never committed. The tour
/// leaves on the server two new versions of Contracts.Leave (a copy of the effective version, then a rollback to it): the rules end as
/// they began. PLAN-49: the tour runs on a domain the engine governs; Analytics.ETL, which it used, is retired and takes no draft. So
/// each run adds two versions to the national leave policy in force, and a tour that stops half-way discards the draft it left.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pol47Author = "e2e-policy-author";
    private const string Pol47Approver = "e2e-checker-mgmt";
    private const string Pol47Domain = "Contracts.Leave";

    private static EvidenceRecorder StartPol47(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pol47-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan47", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static string Pol47Fingerprint()
    {
        var sql = File.ReadAllText(Path.Combine(AcceptanceHarness.RepoRoot, "scripts", "pol047-policy-fingerprint.sql"));
        return string.Join("", Sql("hr_core_db", sql));
    }

    private static Dictionary<string, string> Pol47Hashes(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("VersionId").GetString()!, e => e.GetProperty("RulesSha256").GetString()!);

    private static string Pol47RulesPrint(string versionId) =>
        Sql("hr_core_db", $"select coalesce(string_agg(\"RuleKey\" || ':' || \"Priority\" || ':' || \"WhenJson\" || ':' || \"ThenJson\", '|' order by \"RuleKey\"), '') from \"PolicyRules\" where \"PolicyTemplateVersionId\" = '{versionId}'").Single();

    [SkippableFact]
    public void AC_POL_47_vps_the_migration_the_trigger_the_change_sets_and_unchanged_published_versions()
    {
        var recorder = StartPol47("vps-schema", "Demo server: the migration and its triggers, the roles, the change sets once, published versions unchanged, a direct UPDATE refused");

        Assert.Single(Sql("hr_core_db", "select \"MigrationId\" from \"__EFMigrationsHistory\" where \"MigrationId\" like '%POL047_PolicyRuleLifecycle'"));
        Assert.Equal(["policy_rules_guard", "policy_versions_guard"],
            Sql("hr_core_db", "select tgname from pg_trigger where tgname in ('policy_rules_guard','policy_versions_guard') order by 1"));
        Assert.Equal("t", Sql("hr_core_db", "select relforcerowsecurity from pg_class where oid = '\"PolicyVersionEdits\"'::regclass").Single());
        Assert.Equal(["policy-approver", "policy-author"],
            Sql("keycloak_db", "select k.name from keycloak_role k join realm r on r.id = k.realm_id where r.name = 'hrmanagement' and k.name like 'policy-%' and not k.client_role order by 1"));

        var applications = Sql("hr_core_db", "select \"ChangeSetId\" || ':' || \"Outcome\" from \"PolicySeedApplications\" order by 1");
        Assert.Contains(applications, a => a.StartsWith("POL-2026-10-30-BASELINE:", StringComparison.Ordinal));
        Assert.Contains(applications, a => a.StartsWith("POL-2026-10-30-RETIRE-R-REC-HIRE:", StringComparison.Ordinal));

        const string effectiveRecruitment = """
            (select v."Id" from "PolicyTemplateVersions" v join "PolicyTemplates" t on t."Id" = v."PolicyTemplateId"
             where t."Domain" = 'Core.Recruitment' and t."Scope" = 'National' and v."Status" = 'Published'
               and (v."EffectiveFrom" is null or v."EffectiveFrom" <= now()) and (v."EffectiveTo" is null or v."EffectiveTo" > now())
             order by v."VersionNumber" desc limit 1)
            """;
        Assert.Equal("0", Sql("hr_core_db", $"select count(*) from \"PolicyRules\" where \"RuleKey\" = 'R-REC-HIRE' and \"PolicyTemplateVersionId\" = {effectiveRecruitment}").Single());
        var hireAnywhere = Sql("hr_core_db", "select count(*) from \"PolicyRules\" where \"RuleKey\" = 'R-REC-HIRE'").Single();

        // Every version published before the deploy keeps its rules (the "before" was taken read-only before the deploy).
        var beforePath = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan47", "vps", "fingerprint-before.json");
        var before = Pol47Hashes(File.ReadAllText(beforePath));
        var now = Pol47Hashes(Pol47Fingerprint());
        var changed = before.Where(kv => !now.TryGetValue(kv.Key, out var hash) || hash != kv.Value).Select(kv => kv.Key).ToList();
        Assert.Empty(changed);
        var added = now.Keys.Except(before.Keys).ToList();

        // A direct UPDATE of a published rule, inside a transaction rolled back: the trigger refuses it, whoever runs it.
        var rule = Sql("hr_core_db", "select r.\"Id\" from \"PolicyRules\" r join \"PolicyTemplateVersions\" v on v.\"Id\" = r.\"PolicyTemplateVersionId\" where v.\"Status\" = 'Published' order by r.\"RuleKey\" limit 1").Single();
        var pg = ContainerOf("postgresql");
        var update = $"begin; update \"PolicyRules\" set \"WhenJson\" = '{{}}' where \"Id\" = '{rule}'; rollback;";
        var (code, output) = Sh($"docker exec {pg} sh -c {Q($"psql -U \"$POSTGRES_USER\" -d hr_core_db -v ON_ERROR_STOP=1 -tA -c {Q(update)}")}");
        Assert.NotEqual(0, code);
        Assert.Contains("PLAN-47 policy immutability", output);
        Assert.Equal(before.Count + added.Count, Pol47Hashes(Pol47Fingerprint()).Count);

        // The boot reported the published rules the validator refuses and did not fail on them.
        var (_, logs) = Sh($"docker logs {ContainerOf("hr-core-web")} 2>&1 | grep -E 'PLAN-47 validator inventory|Unhandled exception' || true");
        Assert.Contains("PLAN-47 validator inventory", logs);
        Assert.DoesNotContain("Unhandled exception", logs);
        var inventory = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains(" rule ", StringComparison.Ordinal)).Select(l => l[l.IndexOf("PLAN-47", StringComparison.Ordinal)..]).Distinct().ToList();

        recorder.WriteScenarioDbChecks(new { applications, hireRowsAnywhere = hireAnywhere, publishedBefore = before.Count, changed, addedSinceBefore = added.Count, refusedUpdate = true, inventory });
        recorder.WriteScenarioSteps(
            "psql: POL047_PolicyRuleLifecycle recorded, both triggers present, PolicyVersionEdits under forced row security; the realm holds policy-author and policy-approver;\n" +
            "PolicySeedApplications holds both change sets; R-REC-HIRE is not in the effective Core.Recruitment version; every version published before the deploy has the same hash;\n" +
            "a direct UPDATE of a published rule (in a rolled-back transaction) is refused by the trigger; the start-up log lists the rules the validator refuses and no unhandled exception.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_POL_47_vps_a_four_eyes_publication_and_a_rollback_that_leave_the_rules_as_they_were()
    {
        var recorder = StartPol47("vps-tour", "Demo server: the author copies Contracts.Leave, the approver publishes it, the author rolls back to the starting version, the approver publishes that");
        var password = DemoPassword();
        var template = Sql("hr_core_db", $"select \"Id\" from \"PolicyTemplates\" where \"Domain\" = '{Pol47Domain}' and \"Scope\" = 'National'").Single();
        string Effective() => Sql("hr_core_db",
            $"select \"Id\" from \"PolicyTemplateVersions\" where \"PolicyTemplateId\" = '{template}' and \"Status\" = 'Published' and (\"EffectiveFrom\" is null or \"EffectiveFrom\" <= now()) and (\"EffectiveTo\" is null or \"EffectiveTo\" > now()) order by \"VersionNumber\" desc limit 1").Single();
        string? Draft() => Sql("hr_core_db", $"select \"Id\" from \"PolicyTemplateVersions\" where \"PolicyTemplateId\" = '{template}' and \"Status\" = 'Draft'").SingleOrDefault();

        Assert.Null(Draft());
        var start = Effective();
        var startRules = Pol47RulesPrint(start);
        var authorId = KeycloakUserId(Pol47Author);
        var approverId = KeycloakUserId(Pol47Approver);

        var (author, _) = await SignInAsync(Pol47Author, password, "/ui/hr-core/PolicyTemplates");
        var (approver, _) = await SignInAsync(Pol47Approver, password, "/ui/hr-core/PolicyTemplates");
        using (author)
        using (approver)
        {
            try
            {
                var details = $"/ui/hr-core/PolicyTemplates/Details/{template}";

                // 1. The author copies the effective version; the author cannot publish it.
                await Wf45PostRenderedAsync(author, details, "CreateTemplateDraft");
                var copy = Draft();
                Assert.NotNull(copy);
                Assert.Equal(startRules, Pol47RulesPrint(copy!));
                var edit = $"/ui/hr-core/PolicyTemplates/EditRules?versionId={copy}";
                var (selfStatus, _, _) = await PostFormAsync(author, "/ui/hr-core/PolicyTemplates/PublishFromUi", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(author, edit),
                    ["versionId"] = copy!
                });
                Assert.Equal(403, selfStatus);
                Assert.Equal(copy, Draft());

                // 2. The approver reviews the draft and publishes what was reviewed (the page carries the revision).
                await Wf45PostRenderedAsync(approver, edit, "PublishFromUi", new Dictionary<string, string> { ["evidenceRef"] = "PLAN-47 server check: copy" });
                Assert.Null(Draft());
                Assert.Equal(copy, Effective());
                var first = Sql("hr_core_db", $"select \"Kind\" || '|' || \"MakerUserId\" || '|' || \"CheckerUserId\" from \"PolicyPublications\" where \"PolicyTemplateVersionId\" = '{copy}'").Single();
                Assert.Equal($"Manual|{authorId}|{approverId}", first);

                // 3. The author makes a rollback draft to the version the tour started from; the approver publishes it.
                await Wf45PostRenderedAsync(author, details, "CreateRollbackDraft", new Dictionary<string, string> { ["reason"] = "PLAN-47 server check: rollback" });
                var rollback = Draft();
                Assert.NotNull(rollback);
                await Wf45PostRenderedAsync(approver, $"/ui/hr-core/PolicyTemplates/EditRules?versionId={rollback}", "PublishFromUi",
                    new Dictionary<string, string> { ["evidenceRef"] = "PLAN-47 server check: rollback" });
                Assert.Null(Draft());
                Assert.Equal(rollback, Effective());
                var second = Sql("hr_core_db", $"select \"Kind\" || '|' || \"RolledBackFromVersionId\" || '|' || \"MakerUserId\" || '|' || \"CheckerUserId\" from \"PolicyPublications\" where \"PolicyTemplateVersionId\" = '{rollback}'").Single();
                Assert.Equal($"Rollback|{start}|{authorId}|{approverId}", second);
                Assert.Equal(startRules, Pol47RulesPrint(rollback!));

                // The history shows both publications.
                var history = await Wf45PageAsync(approver, details);
                Assert.Contains("Rollback", history);
                Assert.Contains("Manual", history);

                recorder.WriteScenarioDbChecks(new { template, start, copy, rollback, first, second, selfPublish = selfStatus, rulesUnchanged = true });
                recorder.WriteScenarioSteps(
                    "1. e2e-policy-author (policy-author only) creates a draft of Contracts.Leave from its page: the rules are the effective version's; its own PublishFromUi is refused (403).\n" +
                    "2. e2e-checker-mgmt opens the draft and publishes it from the rendered form (with the revision): Manual, maker the author, checker the approver.\n" +
                    "3. The author creates a rollback draft to the starting version with a reason; the approver publishes it: Rollback, RolledBackFromVersionId = the starting version.\n" +
                    "4. The effective rules equal the starting rules; the template page shows both publications.");
                recorder.WriteResult("Pass");
            }
            finally
            {
                // PLAN-49: the tour now runs on a template the engine governs, where an open draft postpones every later change set
                // of that template (the applier skips it at each start). No draft was open when the tour began, so one that is open
                // now is the tour's own, left by a failure between its creation and its publication: its author discards it. The
                // failure that brought us here is the one to report.
                var left = Draft();
                if (left is not null)
                {
                    try
                    {
                        await Wf45PostRenderedAsync(author, $"/ui/hr-core/PolicyTemplates/EditRules?versionId={left}", "DiscardDraft");
                    }
                    catch (Exception cleanUp) when (cleanUp is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"PLAN-47 server tour: the draft {left} of {Pol47Domain} could not be discarded ({cleanUp.GetType().Name}); discard it from the template's page.");
                    }
                }
            }
        }
    }
}
