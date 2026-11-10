using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (plan section 3, P-5): a new database is seeded with the five governed domains and nothing else: five templates, five
/// facts schemas, the bindings of the codes a governed rule may name, and no rule the validator would refuse with the catalog's
/// capabilities, in any version.
/// </summary>
public sealed class Pol049FreshSeedTests
{
    private static async Task<Pol047Harness> FreshAsync()
    {
        var h = new Pol047Harness();
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(h.Db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db);
        await CodeGenerationSeeder.SeedAsync(h.Db);
        h.Db.ChangeTracker.Clear();
        return h;
    }

    private static readonly string[] Governed = [.. PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal)];

    [Fact]
    public async Task A_new_database_holds_the_five_governed_templates_and_no_other()
    {
        await using var h = await FreshAsync();

        var templates = await h.Db.PolicyTemplates.AsNoTracking().ToListAsync();
        Assert.Equal(5, templates.Count);
        Assert.Equal(Governed, templates.Select(t => t.Domain).OrderBy(d => d, StringComparer.Ordinal));
        Assert.All(templates, t =>
        {
            Assert.Equal(PolicyTemplateScope.National, t.Scope);
            Assert.Equal(NationalTenant.Id, t.TenantId);
            Assert.Null(t.RetiredAt);
            Assert.Null(t.RetiredReason);
            // The template's subject type is one the catalog gives the domain.
            Assert.True(PolicyDomainCatalog.Find(t.Domain)!.AllowsSubjectType(t.SubjectType), $"{t.Domain}: {t.SubjectType}");
        });

        var schemas = await h.Db.PolicyFactsSchemas.AsNoTracking().Select(s => s.Domain).ToListAsync();
        Assert.Equal(Governed, schemas.OrderBy(d => d, StringComparer.Ordinal));

        // No set recorded anything for a domain out of the engine's scope, and nothing waits as a draft.
        var applied = await h.Db.PolicySeedApplications.AsNoTracking().ToListAsync();
        Assert.All(applied, a => Assert.Contains(a.PolicyTemplateId, templates.Select(t => t.Id)));
        Assert.DoesNotContain(applied, a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId);
        Assert.False(await h.Db.PolicyTemplateVersions.AnyAsync(v => v.Status == PolicyVersionStatus.Draft));
    }

    [Fact]
    public async Task Every_seeded_rule_of_every_version_passes_the_validator_with_the_catalog()
    {
        await using var h = await FreshAsync();
        var templates = await h.Db.PolicyTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Rules).ToListAsync();

        var rules = 0;
        foreach (var template in templates)
        {
            var context = await h.Validation.LoadContextAsync(template.Domain, template.Scope);
            Assert.Same(CatalogPolicyDomainCapabilities.Instance, context.Capabilities);
            foreach (var version in template.Versions)
            {
                Assert.Equal(PolicyVersionStatus.Published, version.Status);
                foreach (var rule in version.Rules)
                {
                    rules++;
                    var issues = PolicyRuleValidator.ValidateRule(PolicyRuleValidationService.ToInput(rule), context);
                    Assert.True(issues.Count == 0,
                        $"{template.Domain} v{version.VersionNumber} {rule.RuleKey}: {string.Join("; ", issues.Select(i => $"{i.Code} {i.MessageAr}"))}");
                }
            }
        }

        Assert.True(rules >= 19, $"only {rules} rules were validated");
        Assert.Empty(await PolicyPublishedRuleInventory.FindAsync(h.Db, h.Validation));
    }

    [Fact]
    public async Task The_versions_of_a_new_database_are_the_seed_and_the_sets_that_still_have_something_to_add()
    {
        await using var h = await FreshAsync();

        async Task<List<PolicyTemplateVersion>> Versions(string domain) =>
            await h.Db.PolicyTemplateVersions.AsNoTracking().Include(v => v.Rules).Include(v => v.PolicyTemplate)
                .Where(v => v.PolicyTemplate.Domain == domain).OrderBy(v => v.VersionNumber).ToListAsync();

        // Custody is seeded with its rules already bound to their actions: POL-049-ASSETS-ACTION replaces nothing here.
        var custody = await Versions(PolicyDomains.AssetsCustody);
        Assert.Single(custody);
        Assert.Equal(6, custody[0].Rules.Count);
        var actions = await h.Db.PolicySeedApplications.AsNoTracking().SingleAsync(a => a.ChangeSetId == PolicySeedChangeSets.AssetsActionId);
        Assert.Equal(PolicySeedChangeSetApplier.OutcomeNoOp, actions.Outcome);

        // The card has no rule from the start, so there is no orphan to retire.
        var card = await Versions(PolicyDomains.IDCardLifecycle);
        Assert.Empty(Assert.Single(card).Rules);
        Assert.Equal(PolicySeedChangeSetApplier.OutcomeNoOp, (await h.Db.PolicySeedApplications.AsNoTracking().SingleAsync(a => a.ChangeSetId == PolicySeedChangeSets.IdCardOrphanId)).Outcome);

        // Leave: an empty seed, the baseline's two rules, then POL-049-LEAVE: the placeholder limit goes, the self-approval rule
        // is bound to Approve. R-LEAVE-LONG was never seeded.
        var leave = await Versions(PolicyDomains.ContractsLeave);
        Assert.Equal([1, 2, 3], leave.Select(v => v.VersionNumber));
        Assert.Empty(leave[0].Rules);
        Assert.Equal(["R-LEAVE-MAX", "R-LEAVE-SELF"], leave[1].Rules.Select(r => r.RuleKey).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(PolicySeedChangeSets.LeaveSelfApprovalRule.When, Assert.Single(leave[2].Rules).WhenJson);
        Assert.DoesNotContain(leave.SelectMany(v => v.Rules), r => r.RuleKey == "R-LEAVE-LONG");

        Assert.Equal([1, 2], (await Versions(PolicyDomains.CoreRecruitment)).Select(v => v.VersionNumber));
        Assert.Equal(2, Assert.Single(await Versions(PolicyDomains.CoreEstablishment)).Rules.Count);
    }

    [Fact]
    public async Task The_bindings_of_a_new_database_are_the_workflow_codes_of_the_catalog()
    {
        await using var h = await FreshAsync();

        var fromCatalog = PolicyDomainCatalog.Governed
            .SelectMany(d => d.WorkflowTriggers.Values.SelectMany(t => t).Select(t => (d.Domain, t.WorkflowCode)))
            .Distinct()
            .OrderBy(b => b.Domain, StringComparer.Ordinal).ThenBy(b => b.WorkflowCode, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(7, fromCatalog.Count);

        // The seeder's own list is the catalog's, and the database holds exactly it.
        Assert.Equal(fromCatalog, PolicyEngineSeeder.SeededBindings.OrderBy(b => b.Domain, StringComparer.Ordinal).ThenBy(b => b.WorkflowCode, StringComparer.Ordinal));
        var bindings = await h.Db.PolicyWorkflowBindings.AsNoTracking().ToListAsync();
        Assert.Equal(fromCatalog, bindings.Select(b => (b.Domain, b.WorkflowCode)).OrderBy(b => b.Domain, StringComparer.Ordinal).ThenBy(b => b.WorkflowCode, StringComparer.Ordinal));
        Assert.All(bindings, b => Assert.Equal(NationalTenant.Id, b.TenantId));

        // A pinned binding points at a definition of its code; every bound code has a published definition.
        foreach (var binding in bindings)
        {
            Assert.True(await h.Db.WorkflowDefinitions.AnyAsync(d => d.Code == binding.WorkflowCode && d.IsPublished), binding.WorkflowCode);
            if (binding.WorkflowDefinitionId is Guid pinned)
                Assert.Equal(binding.WorkflowCode, (await h.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == pinned)).Code);
        }

        // The code R-EST-SUBMIT fires is bound without a pin: a new version of its definition is the one that starts.
        Assert.Null(bindings.Single(b => b.WorkflowCode == "ESTABLISHMENT-CHANGE-APPROVAL").WorkflowDefinitionId);

        // A second start adds none.
        await PolicyEngineSeeder.SeedAsync(h.Db);
        Assert.Equal(7, await h.Db.PolicyWorkflowBindings.CountAsync());
    }

    [Fact]
    public void The_seeders_name_no_domain_out_of_the_engine_scope()
    {
        // The seeds are the governed domains' and nobody else's: the source of the two seeders names none of the fifteen.
        foreach (var file in new[]
                 {
                     "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs",
                     "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs"
                 })
        {
            var text = global::System.IO.File.ReadAllText(HRManagement.TestSupport.RepoPaths.Resolve(file.Split('/')));
            foreach (var domain in PolicyDomainCatalog.OutOfScopeDomains)
            {
                Assert.DoesNotContain($"\"{domain.Domain}\"", text, StringComparison.Ordinal);
                var constant = typeof(PolicyDomains).GetFields().Single(f => f.IsLiteral && (string)f.GetRawConstantValue()! == domain.Domain).Name;
                Assert.DoesNotContain($"PolicyDomains.{constant}", text, StringComparison.Ordinal);
            }
        }
    }
}
