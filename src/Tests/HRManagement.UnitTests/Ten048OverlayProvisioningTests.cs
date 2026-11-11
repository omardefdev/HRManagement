using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Onboarding;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static HRManagement.UnitTests.Ten048PolicyHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§4, P-6, D-TEN-8 = D-POL-4): an agency inherits the national policy and only adds stricter rules. An overlay pack goes
/// through the validator, the ceiling and the publish service (every rule of a domain in one version, the pack refused whole when
/// one rule is refused, a renamed key not added twice); an agency with no override receives the national decision; an old
/// overlay written before the ceiling cannot weaken it at evaluation, and the decision says so.
/// </summary>
public sealed class Ten048OverlayProvisioningTests
{
    private const string LeaveWhen = """{"all":[{"field":"leaveType","operator":"Equals","value":"Annual"}]}""";

    /// <summary>PLAN-49: the stricter effect a call site of Contracts.Leave consumes (RequireEvidence there has no consumer).</summary>
    private const string LeaveDeny = """[{"effect":"Deny","params":{"reason":"agency"}}]""";

    private static string Pack(string packId, string? prefix, params (string Domain, string Key, string When, string Then, string? RenamedFrom)[] rules) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            packId,
            ruleKeyPrefix = prefix,
            rules = rules.Select((r, i) => new
            {
                domain = r.Domain,
                ruleKey = r.Key,
                renamedFrom = r.RenamedFrom,
                name = r.Key,
                priority = 30 + i,
                whenJson = r.When,
                thenJson = r.Then
            })
        });

    [Fact]
    public async Task The_finance_manifest_publishes_its_overlay_through_the_publish_service_under_the_renamed_key()
    {
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);
        var publish = Mock.Of<IPublishEndpoint>();
        var orgProvisioner = new AgencyOrgStructureProvisioner(db, new HR.Core.Infrastructure.OrgUnitPathService(db), new OrgUnitIntegrationService(db, publish));
        var provisioning = new AgencyProvisioningService(db, new ServiceCollection().BuildServiceProvider(), orgProvisioner, publish,
            NullLogger<AgencyProvisioningService>.Instance);

        var manifest = AgencyManifestLoader.LoadFromFile(RepoPaths.Resolve("seed-packs", "examples", "agency-ministry-finance.json"));
        Assert.Contains("policy-ministry-finance-v1", manifest.Policies.PolicyPackIds);
        var result = await provisioning.ApplyManifestAsync(manifest, dryRun: false);
        var tenant = result.TenantId!.Value;

        var version = await db.PolicyTemplateVersions.Include(v => v.PolicyTemplate).Include(v => v.Rules).Include(v => v.Publications)
            .SingleAsync(v => v.TenantId == tenant);
        Assert.Equal(PolicyTemplateScope.TenantOverride, version.PolicyTemplate.Scope);
        // PLAN-49: the pack's old rule (RequireEvidence on leave, an effect nobody consumed) left it. The rule in its place is
        // one the domain catalog accepts, and it restates the national R-PRIV-EVIDENCE word for word: an example pack decides
        // nothing the national policy has not decided (asking for more is an owner's decision).
        Assert.Equal(PolicyDomains.AssetsCustody, version.PolicyTemplate.Domain);
        Assert.Equal(PolicyVersionStatus.Published, version.Status);
        var packRule = Assert.Single(version.Rules);
        Assert.Equal("MOF:R-FIN-HANDOVER-PROOF", packRule.RuleKey);
        var national = PolicySeedChangeSets.CustodyAssignRules.Single(r => r.Key == "R-PRIV-EVIDENCE");
        Assert.Equal(national.When, packRule.WhenJson);
        Assert.Equal(national.Then, packRule.ThenJson);
        var publication = Assert.Single(version.Publications);
        Assert.Equal(PolicyPublicationKind.SystemSeed, publication.Kind);
        Assert.Equal("overlay-policy-ministry-finance-v1", publication.EvidenceRef);
        Assert.Equal(new[] { PolicyVersionEditAction.Create, PolicyVersionEditAction.AddRule },
            await db.PolicyVersionEdits.Where(e => e.PolicyTemplateVersionId == version.Id).OrderBy(e => e.Action).Select(e => e.Action).ToListAsync());

        // Applied again: every rule is in force, nothing is added.
        var again = await AgencyPolicySeeder.ApplyPackAsync(db, null, tenant, "policy-ministry-finance-v1");
        Assert.Empty(again.Published);
        Assert.Single(again.Skipped);
        Assert.Equal(1, await db.PolicyTemplateVersions.CountAsync(v => v.TenantId == tenant));
    }

    [Fact]
    public async Task Every_rule_of_a_domain_goes_into_one_version_and_a_second_pack_copies_it_forward()
    {
        await using var h = await CreateAsync();
        await using (var db = h.DesignTime())
        {
            var first = await AgencyPolicySeeder.ApplyPackContentAsync(db, null, AgencyA, "p48-one", Pack("p48-one", "P48A",
                (Domain, "R-DENY", PrivilegedWhen, """[{"effect":"Deny","params":{"reason":"agency"}}]""", null),
                (Domain, "R-EVIDENCE", PrivilegedWhen, """[{"effect":"RequireEvidence"}]""", null)));
            Assert.Single(first.Published);
        }

        await using (var db = h.DesignTime())
        {
            var second = await AgencyPolicySeeder.ApplyPackContentAsync(db, null, AgencyA, "p48-two", Pack("p48-two", "P48B",
                (Domain, "R-LEGACY", """{"all":[{"field":"isLegacyEntry","operator":"Equals","value":"true"}]}""", """[{"effect":"Deny","params":{"reason":"agency: no legacy entry"}}]""", null)));
            Assert.Single(second.Published);
        }

        await using var check = h.DesignTime();
        var versions = await check.PolicyTemplateVersions.Include(v => v.Rules)
            .Where(v => v.TenantId == AgencyA).OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(new[] { "P48A:R-DENY", "P48A:R-EVIDENCE" }, versions[0].Rules.Select(r => r.RuleKey).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "P48A:R-DENY", "P48A:R-EVIDENCE", "P48B:R-LEGACY" }, versions[1].Rules.Select(r => r.RuleKey).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(versions, v => Assert.Equal(PolicyVersionStatus.Published, v.Status));
        // The publish service closed v1 when v2 took effect (its own tenant, though the seeding session is not the agency's).
        Assert.NotNull(versions[0].EffectiveTo);
        Assert.Null(versions[1].EffectiveTo);
    }

    [Theory]
    [InlineData("""[{"effect":"SetField","params":{"field":"isPrivileged","value":"false"}}]""", PolicyCeiling.SetFieldCode)]
    [InlineData("""[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]""", PolicyCeiling.TriggerWorkflowCode)]
    [InlineData("""[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]""", PolicyCeiling.SelectCodePolicyCode)]
    public async Task A_pack_the_ceiling_refuses_is_refused_whole_and_nothing_is_saved(string thenJson, string code)
    {
        await using var h = await CreateAsync();
        await using var db = h.DesignTime();

        var refused = await Assert.ThrowsAsync<AgencyPolicyPackException>(() => AgencyPolicySeeder.ApplyPackContentAsync(db, null, AgencyA, "p48-bad",
            Pack("p48-bad", "P48X",
                // A rule the catalog and the ceiling both accept: it is not saved either, because the pack is refused whole.
                (PolicyDomains.ContractsLeave, "R-NOTE", LeaveWhen, LeaveDeny, null),
                (Domain, "R-WEAKER", PrivilegedWhen, thenJson, null))));

        Assert.Contains(refused.Issues, i => i.Contains(code, StringComparison.Ordinal));
        Assert.DoesNotContain(refused.Issues, i => i.Contains(PolicyDomains.ContractsLeave, StringComparison.Ordinal));
        await using var check = h.DesignTime();
        Assert.False(await check.PolicyTemplates.AnyAsync(t => t.TenantId == AgencyA));
        Assert.False(await check.PolicyRules.AnyAsync(r => r.TenantId == AgencyA));
    }

    [Fact]
    public async Task A_key_that_fails_the_pattern_refuses_the_pack()
    {
        await using var h = await CreateAsync();
        await using var db = h.DesignTime();

        // No prefix: the stored key is "{packId}:{ruleKey}", and a lower-case pack id fails PLAN-47's key pattern.
        var refused = await Assert.ThrowsAsync<AgencyPolicyPackException>(() => AgencyPolicySeeder.ApplyPackContentAsync(db, null, AgencyA, "p48-lower",
            Pack("p48-lower", null, (PolicyDomains.ContractsLeave, "R-NOTE", LeaveWhen, LeaveDeny, null))));

        Assert.Contains(refused.Issues, i => i.Contains("RULE_KEY", StringComparison.Ordinal));
    }

    /// <summary>
    /// PLAN-49 (P-4): a pack goes through the domain catalog as any rule does. An effect no call site of the domain consumes
    /// (the pack's old RequireEvidence on leave), a domain out of the engine's scope, and a workflow not bound to the action
    /// that owns it are each refused, whole, with nothing saved.
    /// </summary>
    [Theory]
    [InlineData(PolicyDomains.ContractsLeave, LeaveWhen, """[{"effect":"RequireEvidence"}]""", "THEN_EFFECT_NOT_ALLOWED")]
    [InlineData(PolicyDomains.AssetsCustody, PrivilegedWhen, """[{"effect":"RequireDualApproval"}]""", "THEN_EFFECT_NOT_ALLOWED")]
    [InlineData(PolicyDomains.ArchiveRetention, """{"all":[{"field":"action","operator":"Equals","value":"Delete"}]}""", """[{"effect":"Deny","params":{"reason":"agency"}}]""", "DOMAIN_OUT_OF_SCOPE")]
    [InlineData(PolicyDomains.AnalyticsEtl, """{"all":[{"field":"environment","operator":"Equals","value":"Production"}]}""", """[{"effect":"Deny","params":{"reason":"agency"}}]""", "DOMAIN_OUT_OF_SCOPE")]
    public async Task A_pack_the_domain_catalog_refuses_is_refused_whole_and_nothing_is_saved(string domain, string when, string then, string code)
    {
        await using var h = await CreateAsync();
        await using var db = h.DesignTime();

        var refused = await Assert.ThrowsAsync<AgencyPolicyPackException>(() => AgencyPolicySeeder.ApplyPackContentAsync(db, null, AgencyA, "p49-bad",
            Pack("p49-bad", "P49X",
                (PolicyDomains.AssetsCustody, "R-FINE", PrivilegedWhen, """[{"effect":"RequireEvidence"}]""", null),
                (domain, "R-REFUSED", when, then, null))));

        Assert.Contains(refused.Issues, i => i.Contains(code, StringComparison.Ordinal));
        await using var check = h.DesignTime();
        Assert.False(await check.PolicyTemplates.AnyAsync(t => t.TenantId == AgencyA));
        Assert.False(await check.PolicyRules.AnyAsync(r => r.TenantId == AgencyA));
    }

    [Fact]
    public async Task A_rule_renamed_in_its_pack_is_not_added_again_under_its_new_key()
    {
        await using var h = await CreateAsync();
        await h.SeedOldOverrideAsync(AgencyA, Rule("old-pack:R-NOTE", 30, """[{"effect":"RequireEvidence"}]"""));

        await using var db = h.DesignTime();
        var result = await AgencyPolicySeeder.ApplyPackContentAsync(db, null, AgencyA, "p48-renamed", Pack("p48-renamed", "P48R",
            (Domain, "R-NOTE", PrivilegedWhen, """[{"effect":"RequireEvidence"}]""", "old-pack:R-NOTE")));

        Assert.Empty(result.Published);
        await using var check = h.DesignTime();
        Assert.Equal(1, await check.PolicyTemplateVersions.CountAsync(v => v.TenantId == AgencyA));
        Assert.False(await check.PolicyRules.AnyAsync(r => r.RuleKey == "P48R:R-NOTE"));
    }

    [Fact]
    public async Task An_agency_with_no_override_receives_the_national_decision()
    {
        await using var h = await CreateAsync();
        await using var platform = h.Session(National);
        var expected = await Orchestrator(platform).EvaluateAsync(new PolicyEnforceRequest { Domain = Domain, TenantId = National, Facts = PrivilegedFacts() });

        await using var db = h.Session(AgencyB);
        var snapshot = await Orchestrator(db).GetPublishedSnapshotAsync(Domain, AgencyB);
        var decision = await Orchestrator(db).EvaluateAsync(new PolicyEnforceRequest { Domain = Domain, TenantId = AgencyB, Facts = PrivilegedFacts() });

        // G-POL-05: the agency's EF filter used to hide the national template, and the agency fell back to PolicyStaticFallback.
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.Rules, r => r.Id == "R-PRIV-WF" && r.Origin == PolicyRuleOrigin.National);
        Assert.Equal("ASSET-ASSIGN-APPROVAL", decision.GetWorkflowCode());
        Assert.Equal(expected.Effects, decision.Effects);
        Assert.Equal(expected.MatchedRuleIds, decision.MatchedRuleIds);
        Assert.Equal(expected.PolicyVersionId, decision.PolicyVersionId);
    }

    [Fact]
    public async Task An_old_overlay_cannot_weaken_the_national_decision_and_the_decision_says_so()
    {
        await using var h = await CreateAsync();
        await h.SeedOldOverrideAsync(AgencyA,
            Rule("A-OLD-WF", 1, """[{"effect":"TriggerWorkflow","params":{"workflowCode":"AGENCY-LIGHT"}}]"""),
            Rule("A-OLD-UNPRIV", 2, """[{"effect":"SetField","params":{"field":"isPrivileged","value":"false"}}]""",
                """{"all":[{"field":"action","operator":"Equals","value":"Classify"}]}"""),
            Rule("A-OLD-DENY", 3, """[{"effect":"Deny","params":{"reason":"agency rule"}}]""",
                """{"all":[{"field":"category","operator":"Equals","value":"محظور"}]}"""));

        await using var db = h.Session(AgencyA);
        var orchestrator = Orchestrator(db);
        var snapshot = await orchestrator.GetPublishedSnapshotAsync(Domain, AgencyA);
        Assert.All(snapshot!.Rules.Where(r => r.Id!.StartsWith("A-OLD", StringComparison.Ordinal)), r => Assert.Equal(PolicyRuleOrigin.Agency, r.Origin));

        // The agency's lighter workflow is dropped: the national one stands, with the explanation naming the agency rule.
        var assign = await orchestrator.EvaluateAsync(new PolicyEnforceRequest { Domain = Domain, TenantId = AgencyA, Facts = PrivilegedFacts() });
        Assert.Equal("ASSET-ASSIGN-APPROVAL", assign.GetWorkflowCode());
        Assert.Contains(assign.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr, StringComparison.Ordinal) && e.Contains("A-OLD-WF", StringComparison.Ordinal));

        // The national classification of a high-value asset stands against the agency's SetField.
        var classify = PrivilegedFacts("Classify");
        classify["value"] = "20000";
        var classified = await orchestrator.EvaluateAsync(new PolicyEnforceRequest { Domain = Domain, TenantId = AgencyA, Facts = classify });
        Assert.Equal("true", classified.GetSetFieldValue("isPrivileged"));
        Assert.Contains(classified.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr, StringComparison.Ordinal) && e.Contains("A-OLD-UNPRIV", StringComparison.Ordinal));

        // A stricter agency rule (Deny) passes.
        var banned = PrivilegedFacts();
        banned["category"] = "محظور";
        var denied = await orchestrator.EvaluateAsync(new PolicyEnforceRequest { Domain = Domain, TenantId = AgencyA, Facts = banned });
        Assert.False(denied.Allowed);
        Assert.Equal("agency rule", denied.DenyReason);
    }
}
