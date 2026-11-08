using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-25 (G-14, AC-HCM-25g): in Enforce mode a hire starts no RECRUITMENT-HIRE workflow — on a fresh database, on a
/// database seeded before PLAN-25 (published definition, R-REC-HIRE rule, policy binding), and on the static fallback —
/// and is still allowed (the offer approval governs it); the deny rules R-REC-ORG / R-REC-HIRE-OFFER still apply.
/// </summary>
public sealed class Hcm025RecruitmentHireRetiredTests
{
    private const string OldTrigger = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-HIRE"}}]""";

    private static PolicyEnforceRequest Hire(Guid applicationId, bool hasAcceptedOffer = true, bool hasOrgScope = true)
    {
        var request = PolicyDomainFactsBuilders.ForRecruitmentApplication(
            applicationId, TenantDefaults.DefaultTenantId, "Hire", hasAcceptedOffer, coiDeclared: true, rankAssigned: true,
            "Published", hasOrgScope);
        request.Mode = PolicyEnforcementMode.Enforce;
        return request;
    }

    [Fact]
    public async Task Fresh_database_hire_is_allowed_without_a_hire_workflow()
    {
        await using var db = CreateContext();
        await WorkflowPackSeeder.SeedAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);
        var applicationId = Guid.NewGuid();

        var result = await CreateOrchestrator(db).EnforceAsync(Hire(applicationId));

        Assert.True(result.Allowed);
        Assert.Null(result.WorkflowInstanceId);
        Assert.False(await db.WorkflowInstances.AnyAsync(i => i.EntityType == "RecruitmentApplication"));
        Assert.False(await db.PolicyRules.AnyAsync(r => r.RuleKey == "R-REC-HIRE"));
    }

    [Fact]
    public async Task Database_seeded_before_plan25_is_patched_and_hire_starts_no_workflow()
    {
        await using var db = CreateContext();
        await WorkflowPackSeeder.SeedAsync(db);

        // A database seeded before PLAN-25 and PLAN-47: v1 of every domain, no change set applied yet. What the older seed left
        // behind: the trigger rule in the published version (no interceptor on this context: it stands for the old data), a
        // published definition and a policy binding.
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db);
        var version = await db.PolicyTemplateVersions.Include(v => v.PolicyTemplate)
            .Where(v => v.PolicyTemplate!.Domain == PolicyDomains.CoreRecruitment && v.Status == PolicyVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();
        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersionId = version.Id,
            TenantId = version.TenantId,
            RuleKey = "R-REC-HIRE",
            Name = "توظيف → workflow",
            Priority = 10,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Hire"}]}""",
            ThenJson = OldTrigger
        });
        var hire = await db.WorkflowDefinitions.SingleAsync(d => d.Code == "RECRUITMENT-HIRE");
        hire.IsPublished = true;
        hire.ValidTo = null;
        db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = PolicyDomains.CoreRecruitment,
            WorkflowCode = "RECRUITMENT-HIRE",
            WorkflowDefinitionId = hire.Id
        });
        await db.SaveChangesAsync();

        // Start-up patches (run twice: idempotent).
        for (var i = 0; i < 2; i++)
        {
            await WorkflowPackSeeder.SeedAsync(db);
            await PolicyEngineSeeder.SeedAsync(db);
        }

        // PLAN-47: the retirement is a new version without the rule; the version that had it keeps it as it was.
        db.ChangeTracker.Clear();
        var versions = await db.PolicyTemplateVersions.Include(v => v.Rules).Include(v => v.PolicyTemplate)
            .Where(v => v.PolicyTemplate!.Domain == PolicyDomains.CoreRecruitment && v.Status == PolicyVersionStatus.Published)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        var effective = versions.Last();
        Assert.DoesNotContain(effective.Rules, r => r.RuleKey == "R-REC-HIRE");
        Assert.Contains(effective.Rules, r => r.RuleKey == "R-REC-HIRE-OFFER");
        var original = Assert.Single(versions.First().Rules, r => r.RuleKey == "R-REC-HIRE");
        Assert.Equal(OldTrigger, original.ThenJson);
        Assert.Equal("توظيف → workflow", original.Name);
        Assert.Equal(PolicySeedChangeSetApplier.OutcomePublished, (await db.PolicySeedApplications.SingleAsync(
            a => a.ChangeSetId == PolicySeedChangeSets.RetireRecruitmentHireId)).Outcome);
        Assert.Equal(PolicyPublicationKind.SystemSeed, (await db.PolicyPublications.SingleAsync(p => p.PolicyTemplateVersionId == effective.Id)).Kind);
        Assert.False((await db.WorkflowDefinitions.SingleAsync(d => d.Code == "RECRUITMENT-HIRE")).IsPublished);

        var result = await CreateOrchestrator(db).EnforceAsync(Hire(Guid.NewGuid()));

        Assert.True(result.Allowed);
        Assert.Null(result.WorkflowInstanceId);
        Assert.DoesNotContain(PolicyEffect.TriggerWorkflow.ToString(), result.Effects);
        Assert.False(await db.WorkflowInstances.AnyAsync(i => i.EntityType == "RecruitmentApplication"));
    }

    [Fact]
    public async Task The_kept_hire_deny_rules_still_refuse()
    {
        await using var db = CreateContext();
        await WorkflowPackSeeder.SeedAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);
        var orchestrator = CreateOrchestrator(db);

        Assert.False((await orchestrator.EnforceAsync(Hire(Guid.NewGuid(), hasAcceptedOffer: false))).Allowed);
        Assert.False((await orchestrator.EnforceAsync(Hire(Guid.NewGuid(), hasOrgScope: false))).Allowed);
    }

    [Fact]
    public async Task Static_fallback_hire_triggers_no_workflow()
    {
        await using var db = CreateContext();   // no policy seeded → static fallback
        await WorkflowPackSeeder.SeedAsync(db);

        var result = await CreateOrchestrator(db).EnforceAsync(Hire(Guid.NewGuid()));

        Assert.True(result.Allowed);
        Assert.Null(result.WorkflowInstanceId);
        Assert.False(await db.WorkflowInstances.AnyAsync());
    }
}
