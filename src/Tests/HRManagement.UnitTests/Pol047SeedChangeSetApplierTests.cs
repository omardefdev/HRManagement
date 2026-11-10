using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §4: seed changes are new versions through recorded change sets, on a context with the immutability interceptor: no
/// repeat on a second boot, NoOp where the rules are already there, the retirement of R-REC-HIRE as a version, a changed digest
/// fails the boot, a set the validator refuses fails the boot, and carried-over rules of retired workflows never do.
/// </summary>
public sealed class Pol047SeedChangeSetApplierTests
{
    private static async Task<Pol047Harness> SeededAsync(Dictionary<string, string?>? settings = null)
    {
        var h = new Pol047Harness(settings);
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db);
        h.Db.ChangeTracker.Clear();
        return h;
    }

    private static Task<List<PolicyTemplateVersion>> VersionsAsync(Pol047Harness h, string domain) =>
        h.Db.PolicyTemplateVersions.AsNoTracking().Include(v => v.Rules).Include(v => v.PolicyTemplate)
            .Where(v => v.PolicyTemplate.Domain == domain && v.PolicyTemplate.Scope == PolicyTemplateScope.National)
            .OrderBy(v => v.VersionNumber).ToListAsync();

    [Fact]
    public async Task A_new_database_gets_v2_where_the_baseline_adds_rules_and_a_second_boot_repeats_nothing()
    {
        await using var h = await SeededAsync();

        var recruitment = await VersionsAsync(h, PolicyDomains.CoreRecruitment);
        Assert.Equal([1, 2], recruitment.Select(v => v.VersionNumber));
        Assert.Empty(recruitment[0].Rules);
        Assert.Contains(recruitment[1].Rules, r => r.RuleKey == "R-OFF-ISSUE");
        Assert.DoesNotContain(recruitment[1].Rules, r => r.RuleKey == "R-REC-HIRE");
        Assert.Equal(PolicyVersionStatus.Published, recruitment[1].Status);
        Assert.NotNull(recruitment[0].EffectiveTo);

        var publication = await h.Db.PolicyPublications.SingleAsync(p => p.PolicyTemplateVersionId == recruitment[1].Id);
        Assert.Equal(PolicyPublicationKind.SystemSeed, publication.Kind);
        Assert.Equal(PolicySeedChangeSets.BaselineId, publication.EvidenceRef);

        // Custody already had every baseline rule in v1 (and R-PRIV-CODE): NoOp, no new version.
        Assert.Single(await VersionsAsync(h, PolicyDomains.AssetsCustody));
        var applications = await h.Db.PolicySeedApplications.AsNoTracking().ToListAsync();
        Assert.Contains(applications, a => a.ChangeSetId == PolicySeedChangeSets.BaselineId && a.Outcome == "NoOp" && a.ResultVersionId == null);
        Assert.Contains(applications, a => a.ChangeSetId == PolicySeedChangeSets.RetireRecruitmentHireId && a.Outcome == "NoOp");
        var versionCount = await h.Db.PolicyTemplateVersions.CountAsync();

        for (var boot = 0; boot < 2; boot++)
            await PolicyEngineSeeder.SeedAsync(h.Db);

        Assert.Equal(versionCount, await h.Db.PolicyTemplateVersions.CountAsync());
        Assert.Equal(applications.Count, await h.Db.PolicySeedApplications.CountAsync());
        var facts = await h.Db.PolicyFactsSchemas.SingleAsync(s => s.Domain == PolicyDomains.CoreRecruitment);
        Assert.Contains("isExceptionOffer", facts.FieldsJson);
    }

    [Fact]
    public async Task A_database_whose_rules_were_added_in_place_records_noop_everywhere()
    {
        await using var h = new Pol047Harness(interceptor: false);
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(h.Db);
        // Before PLAN-47 the incremental seed wrote straight into v1; stand for that with the baseline's own rules.
        var v1 = await h.Db.PolicyTemplateVersions.Include(v => v.PolicyTemplate)
            .SingleAsync(v => v.PolicyTemplate.Domain == PolicyDomains.CoreRecruitment);
        foreach (var op in PolicySeedChangeSets.Baseline.For(PolicyDomains.CoreRecruitment).Where(o => o.Kind == PolicySeedOperationKind.AddRule))
            h.Db.PolicyRules.Add(new PolicyRule { PolicyTemplateVersionId = v1.Id, TenantId = v1.TenantId, RuleKey = op.RuleKey, Name = op.Name, Priority = op.Priority, WhenJson = op.WhenJson!, ThenJson = op.ThenJson! });
        await h.Db.SaveChangesAsync();

        await PolicyEngineSeeder.SeedAsync(h.Db);

        Assert.Single(await VersionsAsync(h, PolicyDomains.CoreRecruitment));
        var recruitment = await h.Db.PolicySeedApplications.AsNoTracking()
            .Where(a => a.PolicyTemplateId == v1.PolicyTemplateId).ToListAsync();
        Assert.All(recruitment, a => Assert.Equal("NoOp", a.Outcome));
        Assert.Equal(2, recruitment.Count);
    }

    [Fact]
    public async Task The_retirement_of_R_REC_HIRE_is_a_new_version_and_the_old_one_keeps_it()
    {
        await using var h = new Pol047Harness(interceptor: false);
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(h.Db);
        var v1 = await h.Db.PolicyTemplateVersions.Include(v => v.PolicyTemplate)
            .SingleAsync(v => v.PolicyTemplate.Domain == PolicyDomains.CoreRecruitment);
        // The PLAN-25 in-place rewrite left this row; PLAN-47 never touches it.
        h.Db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersionId = v1.Id,
            TenantId = v1.TenantId,
            RuleKey = "R-REC-HIRE",
            Name = "[معطّلة] توظيف → workflow",
            Priority = 10,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"__deactivated__"}]}""",
            ThenJson = "[]"
        });
        await h.Db.SaveChangesAsync();

        await PolicyEngineSeeder.SeedAsync(h.Db);

        var versions = await VersionsAsync(h, PolicyDomains.CoreRecruitment);
        Assert.Equal([1, 2, 3], versions.Select(v => v.VersionNumber));
        Assert.Contains(versions[1].Rules, r => r.RuleKey == "R-REC-HIRE");      // the baseline carried it over as it was
        Assert.DoesNotContain(versions[2].Rules, r => r.RuleKey == "R-REC-HIRE"); // the retirement
        Assert.Equal(versions[1].Rules.Count - 1, versions[2].Rules.Count);
        var old = Assert.Single(versions[0].Rules, r => r.RuleKey == "R-REC-HIRE");
        Assert.StartsWith("[معطّلة]", old.Name);
        var edit = await h.Db.PolicyVersionEdits.SingleAsync(e => e.PolicyTemplateVersionId == versions[2].Id && e.Action == PolicyVersionEditAction.RemoveRule);
        Assert.Null(edit.UserId);
        Assert.Equal("Published", (await h.Db.PolicySeedApplications.SingleAsync(a => a.ChangeSetId == PolicySeedChangeSets.RetireRecruitmentHireId)).Outcome);
    }

    [Fact]
    public async Task A_changed_digest_of_an_applied_set_fails_the_boot()
    {
        await using var h = await SeededAsync();
        var tampered = PolicySeedChangeSets.RetireRecruitmentHire with
        {
            Operations = [new(PolicySeedOperationKind.RetireRule, PolicyDomains.CoreRecruitment, RuleKey: "R-OFF-ISSUE", ReasonAr: "تلاعب")]
        };

        var applier = PolicySeedChangeSetApplier.Create(h.Db, null);
        var ex = await Assert.ThrowsAsync<PolicySeedChangeSetException>(() => applier.ApplyAsync([tampered]));

        Assert.Contains(PolicySeedChangeSets.RetireRecruitmentHireId, ex.Message);
        Assert.Contains("digest", ex.Message);
        Assert.Contains((await VersionsAsync(h, PolicyDomains.CoreRecruitment)).Last().Rules, r => r.RuleKey == "R-OFF-ISSUE");
    }

    // PLAN-49: the four tests below worked in Movements.Transfer and Archive.Retention, which the engine no longer governs (a set
    // adds nothing to an out-of-scope domain). They now use Core.Recruitment, whose seeded rules all pass the catalog.
    private const string NoteWhen = """{"all":[{"field":"action","operator":"Equals","value":"PublishInternal"}]}""";
    private const string NoteThen = """[{"effect":"Allow"}]""";

    [Fact]
    public async Task A_set_the_validator_refuses_fails_the_boot_and_publishes_nothing()
    {
        await using var h = await SeededAsync();
        var before = await h.Db.PolicyTemplateVersions.CountAsync();
        // The G-14 pattern: a hire rule that fires RECRUITMENT-HIRE, the workflow PLAN-25 retired.
        var bad = new PolicySeedChangeSet("POL-TEST-BAD",
        [
            new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, "R-REC-HIRE-AGAIN", "توظيف", 5,
                """{"all":[{"field":"action","operator":"Equals","value":"Hire"}]}""",
                """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-HIRE"}}]""")
        ]);

        var ex = await Assert.ThrowsAsync<PolicySeedChangeSetException>(() => PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([bad]));

        Assert.Contains("$.then[0].params.workflowCode", ex.Message);
        Assert.Contains("RECRUITMENT-HIRE", ex.Message);
        // The draft the set opened went with its transaction.
        h.Db.ChangeTracker.Clear();
        Assert.Equal(before, await h.Db.PolicyTemplateVersions.CountAsync());
        Assert.False(await h.Db.PolicySeedApplications.AnyAsync(a => a.ChangeSetId == "POL-TEST-BAD"));
    }

    [Fact]
    public async Task Rules_of_retired_workflows_in_the_effective_version_never_fail_the_boot()
    {
        await using var h = new Pol047Harness(interceptor: false);
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db);
        // A rule from before the validator, in the effective version: it fires RECRUITMENT-HIRE, which PLAN-25 retired.
        var effective = (await VersionsAsync(h, PolicyDomains.CoreRecruitment)).Last();
        h.Db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersionId = effective.Id,
            TenantId = effective.TenantId,
            RuleKey = "R-OLD-HIRE",
            Name = "قديمة",
            Priority = 10,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Hire"}]}""",
            ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-HIRE"}}]"""
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var set = new PolicySeedChangeSet("POL-TEST-CARRY",
        [
            new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, "R-REQ-NOTE", "ملاحظة", 20, NoteWhen, NoteThen)
        ]);

        await PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([set]);

        var versions = await VersionsAsync(h, PolicyDomains.CoreRecruitment);
        Assert.Equal(effective.VersionNumber + 1, versions.Last().VersionNumber);
        Assert.Contains(versions.Last().Rules, r => r.RuleKey == "R-OLD-HIRE");     // RECRUITMENT-HIRE is retired: carried over, never validated
        Assert.Contains(versions.Last().Rules, r => r.RuleKey == "R-REQ-NOTE");
    }

    [Fact]
    public async Task With_require_approval_a_set_leaves_a_draft_for_an_approver()
    {
        await using var h = await SeededAsync(new() { [PolicySeedChangeSetApplier.RequireApprovalKey] = "true" });
        var set = new PolicySeedChangeSet("POL-TEST-APPROVAL",
        [
            new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, "R-REQ-NOTE", "ملاحظة", 20, NoteWhen, NoteThen)
        ]);

        await PolicySeedChangeSetApplier.Create(h.Db, Services(h.Configuration)).ApplyAsync([set]);

        var application = await h.Db.PolicySeedApplications.SingleAsync(a => a.ChangeSetId == "POL-TEST-APPROVAL");
        Assert.Equal("Draft", application.Outcome);
        var draft = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == application.ResultVersionId);
        Assert.Equal(PolicyVersionStatus.Draft, draft.Status);

        var approver = Guid.NewGuid();
        var published = await h.PublishService.PublishAsync(new(draft.Id, Pol047Harness.Actor(approver, HRManagement.ServiceDefaults.HrManagementRoles.PolicyApprover), "EV"));
        Assert.True(published.Succeeded, published.MessageAr);
    }

    [Fact]
    public async Task An_open_draft_postpones_the_set_to_a_later_boot()
    {
        await using var h = await SeededAsync();
        var template = await h.Db.PolicyTemplates.SingleAsync(t => t.Domain == PolicyDomains.CoreRecruitment && t.Scope == PolicyTemplateScope.National);
        var draft = await h.Drafts.CreateDraftAsync(template.Id, null, Guid.NewGuid());
        Assert.True(draft.Succeeded, draft.MessageAr);
        var set = new PolicySeedChangeSet("POL-TEST-LATER",
        [
            new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, "R-REQ-LATER", "لاحقاً", 20, NoteWhen, NoteThen)
        ]);

        await PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([set]);
        Assert.False(await h.Db.PolicySeedApplications.AnyAsync(a => a.ChangeSetId == "POL-TEST-LATER"));

        await h.Drafts.DiscardDraftAsync(draft.Id!.Value, Guid.NewGuid());
        await PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([set]);
        Assert.Equal("Published", (await h.Db.PolicySeedApplications.SingleAsync(a => a.ChangeSetId == "POL-TEST-LATER")).Outcome);
    }

    [Fact]
    public async Task A_set_adds_nothing_to_a_domain_out_of_the_engine_scope_and_records_it_once()
    {
        // PLAN-49: a set written before a domain left the engine's scope (the baseline's Archive.Retention part, on a database
        // that never applied it). The template is an old one; the set opens no draft and is recorded as NoOp, once.
        await using var h = await SeededAsync();
        var template = await h.Db.PolicyTemplates.SingleOrDefaultAsync(t => t.Domain == PolicyDomains.ArchiveRetention && t.Scope == PolicyTemplateScope.National);
        if (template is null)
        {
            template = new PolicyTemplate
            {
                TenantId = Pol047Harness.Tenant,
                Domain = PolicyDomains.ArchiveRetention,
                Name = "أرشفة",
                SubjectType = "ArchiveRecord",
                Scope = PolicyTemplateScope.National
            };
            h.Db.PolicyTemplates.Add(template);
            await PolicySeedVersionWriter.AddAsync(h.Db, new PolicyTemplateVersion { PolicyTemplate = template, TenantId = template.TenantId, VersionNumber = 1, EffectiveFrom = DateTime.UtcNow.AddDays(-5) },
                [new PolicyRule { RuleKey = "R-ARC", Name = "احتفاظ", Priority = 10, WhenJson = """{"all":[{"field":"requiresRetention","operator":"Equals","value":"true"}]}""", ThenJson = """[{"effect":"RequireEvidence"}]""" }],
                PolicyVersionStatus.Published, new PolicyPublication { PublishedAt = DateTime.UtcNow.AddDays(-5), EvidenceRef = "seed-before-plan-49" });
        }

        h.Db.ChangeTracker.Clear();
        var versionsBefore = (await VersionsAsync(h, PolicyDomains.ArchiveRetention)).Count;
        var set = new PolicySeedChangeSet("POL-TEST-OUT-OF-SCOPE",
        [
            new(PolicySeedOperationKind.AddRule, PolicyDomains.ArchiveRetention, "R-ARC-NOTE", "ملاحظة", 20,
                """{"all":[{"field":"action","operator":"Equals","value":"Delete"}]}""", """[{"effect":"Deny","params":{"reason":"سبب"}}]""")
        ]);

        for (var boot = 0; boot < 2; boot++)
            await PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([set]);

        var application = await h.Db.PolicySeedApplications.SingleAsync(a => a.ChangeSetId == "POL-TEST-OUT-OF-SCOPE");
        Assert.Equal("NoOp", application.Outcome);
        Assert.Null(application.ResultVersionId);
        Assert.Equal(versionsBefore, (await VersionsAsync(h, PolicyDomains.ArchiveRetention)).Count);
    }

    [Fact]
    public void Digests_are_stable_and_per_domain()
    {
        var baseline = PolicySeedChangeSets.Baseline;
        Assert.Equal(baseline.Digest(PolicyDomains.ContractsLeave), PolicySeedChangeSets.Baseline.Digest(PolicyDomains.ContractsLeave));
        Assert.NotEqual(baseline.Digest(PolicyDomains.ContractsLeave), baseline.Digest(PolicyDomains.CoreRecruitment));
        Assert.Matches("^[0-9a-f]{64}$", baseline.Digest(PolicyDomains.ArchiveRetention));
        Assert.Equal(["Contracts.Leave", "Core.Recruitment", "Archive.Retention", "Assets.Custody"], baseline.Domains);
        // No baseline rule fires a workflow PLAN-46 retired.
        foreach (var code in WorkflowPackSeeder.RetiredCodes)
            Assert.DoesNotContain(baseline.Operations, o => o.ThenJson?.Contains(code, StringComparison.Ordinal) == true);
    }

    private static IServiceProvider Services(IConfiguration configuration)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, configuration);
        return Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
    }
}
