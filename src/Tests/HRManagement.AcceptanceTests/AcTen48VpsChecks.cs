using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-48 (P7) on the demo server, read-only apart from a sign-in: national_shared_read on exactly the ten national tables;
/// ten048-code-policy-upgrade-v1 recorded once, with v1 archived and v2 published for FundedPosition and RecruitmentRequisition;
/// no counter below the highest number issued in its scope; no number without its prefix; every requisition number issued since
/// the upgrade shows its budget year; /CodePolicies shows the national templates (the demo has no agency, so they carry the
/// «وطني» badge; the inherited badge is AC-TEN-48a's, on the prod-like stack).
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Ten48Reader = "e2e-checker-mgmt";

    private static EvidenceRecorder StartTen48(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"ten48-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan48", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo user on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    [SkippableFact]
    public async Task AC_TEN_48_vps_national_read_upgrade_counters_and_code_policies()
    {
        var recorder = StartTen48("vps-national", "Demo server: the national read policy, the code policy upgrade, the counters, the prefixes and /CodePolicies");

        var tables = Sql("hr_core_db",
            "SELECT tablename FROM pg_policies WHERE policyname = 'national_shared_read' AND cmd = 'SELECT' AND permissive = 'PERMISSIVE' ORDER BY 1");
        Assert.Equal(NationalTenant.TableNames.OrderBy(t => t, StringComparer.Ordinal), tables);

        Assert.Equal(["1"], Sql("hr_core_db", "SELECT count(*) FROM \"SeedPackApplications\" WHERE \"PackId\" = 'ten048-code-policy-upgrade-v1'"));
        var versions = Sql("hr_core_db",
            "SELECT t.\"EntityKind\" || ':' || v.\"VersionNumber\" || ':' || v.\"Status\" FROM \"CodePolicyTemplates\" t " +
            "JOIN \"CodePolicyVersions\" v ON v.\"CodePolicyTemplateId\" = t.\"Id\" " +
            "WHERE t.\"EntityKind\" IN ('FundedPosition', 'RecruitmentRequisition') AND t.\"Scope\" = 'National' ORDER BY 1");
        Assert.Equal(["FundedPosition:1:Archived", "FundedPosition:2:Published", "RecruitmentRequisition:1:Archived", "RecruitmentRequisition:2:Published"], versions);

        // No counter below the highest number issued in its scope (a derived kind has no counter).
        var below = Sql("hr_core_db",
            "SELECT i.\"EntityKind\" || ' ' || i.\"SequenceScope\" || ' ' || i.m || ' > ' || coalesce(c.v::text, 'none') FROM " +
            "(SELECT \"TenantId\", \"EntityKind\", \"SequenceScope\", max(substring(\"Code\" from '([0-9]+)$')::bigint) AS m FROM \"CodeIssuances\" " +
            " WHERE \"EntityKind\" <> 'CostCenter' GROUP BY 1, 2, 3) i " +
            "LEFT JOIN (SELECT \"TenantId\", \"EntityKind\", \"ScopeKey\", max(\"LastValue\") AS v FROM \"CodeSequenceCounters\" GROUP BY 1, 2, 3) c " +
            "ON c.\"TenantId\" = i.\"TenantId\" AND c.\"EntityKind\" = i.\"EntityKind\" AND c.\"ScopeKey\" = i.\"SequenceScope\" " +
            "WHERE c.v IS NULL OR c.v < i.m");
        Assert.Empty(below);
        Assert.Equal(["0"], Sql("hr_core_db", "SELECT count(*) FROM \"CodeIssuances\" WHERE \"Code\" LIKE '-%'"));

        // Every requisition number issued since the upgrade shows its budget year (REQ-<budget year>-#####).
        var requisitions = Sql("hr_core_db",
            "SELECT i.\"Code\" FROM \"CodeIssuances\" i, \"SeedPackApplications\" p WHERE p.\"PackId\" = 'ten048-code-policy-upgrade-v1' " +
            "AND i.\"EntityKind\" = 'RecruitmentRequisition' AND i.\"CreatedAt\" > p.\"AppliedAt\" ORDER BY i.\"CreatedAt\"");
        Assert.All(requisitions, code => Assert.Matches(@"^REQ-\d{4}-\d{5}$", code));

        var (reader, _) = await SignInAsync(Ten48Reader, DemoPassword(), "/ui/hr-core/CodePolicies");
        using (reader)
        {
            var page = await reader.GetAsync($"https://{AppDomain}/ui/hr-core/CodePolicies");
            var html = await page.Content.ReadAsStringAsync();
            Assert.True(page.IsSuccessStatusCode, $"GET /ui/hr-core/CodePolicies answered {(int)page.StatusCode}");
            Assert.Contains("data-testid=\"badge-national\"", html, StringComparison.Ordinal);
            Assert.Contains("data-entity-kind=\"RecruitmentRequisition\"", html, StringComparison.Ordinal);
        }

        recorder.WriteScenarioDbChecks(new { tables, versions, belowHighestIssued = below, requisitionsSinceUpgrade = requisitions, codePoliciesPage = "national templates, badge-national" });
        recorder.WriteScenarioSteps(
            "1. psql: national_shared_read (SELECT, permissive) on exactly NationalTenant.TableNames.\n" +
            "2. psql: ten048-code-policy-upgrade-v1 recorded once; FundedPosition and RecruitmentRequisition v1 Archived, v2 Published.\n" +
            "3. psql: no counter below the highest number issued in its scope; no issuance starting with '-'; every requisition number issued since the upgrade is REQ-<budget year>-#####.\n" +
            "4. e2e-checker-mgmt (hr-core) opens /ui/hr-core/CodePolicies: the national templates with the national badge (the demo has no agency).");
        recorder.WriteResult("Pass");
    }
}
