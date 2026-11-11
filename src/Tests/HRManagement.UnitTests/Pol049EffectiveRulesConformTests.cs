using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (plan section 2, rule 1): after the change sets no effective version holds an effect outside its domain's
/// AllowedEffects, a SetField outside its SetFields, or a TriggerWorkflow outside its WorkflowTriggers; and a domain out of the
/// engine's scope holds no effective rule at all. Proved on a seeded and applied database of both kinds: one from before the
/// plan (the demo server's shape) and a new one. The first check reads the catalog directly; the second asks the validator,
/// which is what the start-up inventory logs.
/// </summary>
public sealed class Pol049EffectiveRulesConformTests
{
    private static async Task<Pol047Harness> AppliedAsync(bool legacy)
    {
        var h = new Pol047Harness();
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(h.Db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(h.Db);
        if (legacy)
            await Pol049LegacyPolicyData.SeedAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db);
        // The code policies: the validator checks a SelectCodePolicy rule against their keys (the national seeding writes them
        // after the policy seed, and takes the start-up inventory after both).
        await CodeGenerationSeeder.SeedAsync(h.Db);
        h.Db.ChangeTracker.Clear();
        return h;
    }

    private static async Task<List<(PolicyTemplate Template, PolicyTemplateVersion? Effective)>> EffectiveAsync(Pol047Harness h)
    {
        var templates = await h.Db.PolicyTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Rules).ToListAsync();
        return templates.Select(t => (t, PolicyDraftService.EffectiveVersion(t, DateTime.UtcNow))).ToList();
    }

    private static List<PolicyEffectDefinition> Effects(PolicyRule rule)
    {
        var issues = new List<PolicyRuleIssue>();
        var effects = PolicyRuleJson.ParseThenStrict(rule.ThenJson, issues);
        Assert.True(effects is not null && issues.Count == 0, $"{rule.RuleKey}: {string.Join("; ", issues.Select(i => i.MessageAr))}");
        return effects!;
    }

    private static PolicyConditionGroup When(PolicyRule rule)
    {
        var issues = new List<PolicyRuleIssue>();
        var when = PolicyRuleJson.ParseWhenStrict(rule.WhenJson, issues);
        Assert.True(when is not null && issues.Count == 0, $"{rule.RuleKey}: {string.Join("; ", issues.Select(i => i.MessageAr))}");
        return when!;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_effective_rule_lies_outside_its_domain_descriptor(bool legacy)
    {
        await using var h = await AppliedAsync(legacy);

        var checkedRules = 0;
        foreach (var (template, effective) in await EffectiveAsync(h))
        {
            var descriptor = PolicyDomainCatalog.Find(template.Domain);
            Assert.NotNull(descriptor);
            Assert.NotNull(effective);

            if (!descriptor!.IsGoverned)
            {
                Assert.Empty(effective!.Rules);
                continue;
            }

            foreach (var rule in effective!.Rules)
            {
                checkedRules++;
                var when = When(rule);
                // The actions the rule can match, and the entity types those actions belong to.
                var actions = PolicyRuleActionScope.PossibleActions(when, descriptor.Actions);
                Assert.NotEmpty(actions);
                Assert.All(PolicyRuleJson.Leaves(when).Where(l => l.Leaf.Field == PolicyRuleActionScope.ActionField),
                    l => Assert.Contains(l.Leaf.Value, descriptor.Actions));

                foreach (var effect in Effects(rule))
                {
                    Assert.True(descriptor.AllowedEffects.Contains(effect.Effect),
                        $"{template.Domain} {rule.RuleKey}: effect {effect.Effect} has no consumer in the domain.");
                    if (effect.Effect == PolicyEffect.SetField)
                        Assert.Contains(effect.Params!["field"], descriptor.SetFields);
                    if (effect.Effect == PolicyEffect.TriggerWorkflow)
                    {
                        var code = effect.Params!["workflowCode"];
                        foreach (var action in actions)
                            Assert.All(descriptor.SubjectTypesOf(action), type =>
                                Assert.True(descriptor.AllowsWorkflowStart(type, action, code),
                                    $"{template.Domain} {rule.RuleKey}: {code} is not a workflow {type} starts at {action}."));
                    }
                }
            }
        }

        Assert.True(checkedRules >= 19, $"only {checkedRules} effective rules were checked");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_start_up_inventory_lists_no_rule_of_an_effective_version(bool legacy)
    {
        await using var h = await AppliedAsync(legacy);

        var found = await PolicyPublishedRuleInventory.FindAsync(h.Db, h.Validation);

        var inForce = found.Where(r => r.InForce).ToList();
        Assert.True(inForce.Count == 0,
            "Refused rules of effective versions: " + string.Join("; ", inForce.Select(r => $"{r.Domain} v{r.VersionNumber} {r.RuleKey}: {r.Codes}")));

        var historic = found.Where(r => !r.InForce).ToList();
        if (legacy)
        {
            // History is kept as it was written, and the inventory still knows it would be refused today.
            foreach (var key in new[] { "R-ID-PROCESS", "R-LEAVE-LONG", "R-HIRE-START", "R-SEP-START", "R-TRF", "R-TKT", "R-DISC", "R-PRIV-WF", "R-HIGH-APPROVAL" })
                Assert.Contains(historic, r => r.RuleKey == key && r.VersionNumber == 1);
            Assert.All(historic.Where(r => !PolicyDomainCatalog.IsGoverned(r.Domain)), r => Assert.Equal(PolicyRuleValidator.OutOfScopeCode, r.Codes));
            Assert.Contains(historic, r => r.RuleKey == "R-LEAVE-LONG" && r.Codes.Contains("THEN_EFFECT_NOT_ALLOWED", StringComparison.Ordinal));
            Assert.Contains(historic, r => r.RuleKey == "R-ID-PROCESS" && r.Codes.Contains("THEN_EFFECT_NOT_ALLOWED", StringComparison.Ordinal));
            Assert.Contains(historic, r => r.RuleKey == "R-PRIV-WF" && r.Codes.Contains("THEN_WORKFLOW_NOT_ALLOWED", StringComparison.Ordinal));
        }
        else
        {
            Assert.Empty(historic);
        }
    }

    /// <summary>
    /// The inventory is taken once at HR.Core's start, after every seed of the profile. Inside the national seed (where PLAN-47
    /// put it) a first start read no agency at all, because the profile's seed creates them afterwards: the acceptance run of
    /// PLAN-49 showed it. It also ran again at every agency provisioning, which calls the national seed.
    /// </summary>
    [Fact]
    public void The_start_up_inventory_is_taken_once_after_the_seeds_of_the_profile()
    {
        const string call = "PolicyPublishedRuleInventory.LogAsync(";
        var hrCore = Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "src", "Services", "HRCore");
        var callers = Directory.EnumerateFiles(hrCore, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => global::System.IO.File.ReadAllText(f).Contains(call, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(hrCore, f).Replace('\\', '/'))
            .ToList();
        Assert.Equal(["HR.Core.Web/Program.cs"], callers);

        var program = global::System.IO.File.ReadAllText(Path.Combine(hrCore, "HR.Core.Web", "Program.cs"));
        var inventory = program.IndexOf(call, StringComparison.Ordinal);
        Assert.Equal(inventory, program.LastIndexOf(call, StringComparison.Ordinal));
        foreach (var seed in new[] { "NationalSeedOrchestrator.ApplyMandatoryAsync(", "DemoSeedOrchestrator.ApplyAsync(", "AcceptanceSeedOrchestrator.ApplyAsync(" })
        {
            var at = program.IndexOf(seed, StringComparison.Ordinal);
            Assert.True(at > 0 && at < inventory, $"{seed}...) must run before the inventory in HR.Core's Program.cs.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_rules_the_plan_names_are_as_it_says(bool legacy)
    {
        await using var h = await AppliedAsync(legacy);
        var effective = (await EffectiveAsync(h)).ToDictionary(e => e.Template.Domain, e => e.Effective!, StringComparer.Ordinal);

        var allKeys = effective.Values.SelectMany(v => v.Rules).Select(r => r.RuleKey).ToList();
        foreach (var gone in new[] { "R-ID-PROCESS", "R-LEAVE-MAX", "R-LEAVE-LONG", "R-REC-HIRE", "R-HIRE-START", "R-SEP-START", "R-TRF", "R-TKT", "R-DISC", "R-ADV-HIGH", "R-CON-RENEW", "R-INS-MASK", "R-PAY-CLOSE", "R-TRN", "R-ETL", "R-ARC", "R-ARC-HOLD", "R-RCV-FULFIL", "R-RCV-DENY" })
            Assert.DoesNotContain(gone, allKeys);

        // Assets.Custody: six rules, every one bound to an action; the three of the handover are bound to Assign.
        var custody = effective[PolicyDomains.AssetsCustody].Rules;
        Assert.Equal(["R-CLASSIFY-CAPITAL", "R-CLASSIFY-HIGH-VALUE", "R-HIGH-APPROVAL", "R-PRIV-CODE", "R-PRIV-EVIDENCE", "R-PRIV-WF"],
            custody.Select(r => r.RuleKey).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(custody, r => Assert.Contains(PolicyRuleJson.Leaves(When(r)), l => l.Leaf.Field == "action"));
        foreach (var rule in PolicySeedChangeSets.CustodyAssignRules)
            Assert.Equal(rule.When, custody.Single(r => r.RuleKey == rule.Key).WhenJson);

        Assert.Empty(effective[PolicyDomains.IDCardLifecycle].Rules);

        var leave = Assert.Single(effective[PolicyDomains.ContractsLeave].Rules);
        Assert.Equal("R-LEAVE-SELF", leave.RuleKey);
        Assert.Equal(PolicySeedChangeSets.LeaveSelfApprovalRule.When, leave.WhenJson);
        Assert.Equal(PolicySeedChangeSets.LeaveSelfApprovalRule.Then, leave.ThenJson);

        Assert.Equal(10, effective[PolicyDomains.CoreRecruitment].Rules.Count);
        Assert.Equal(["R-EST-DENY-BRANCH", "R-EST-SUBMIT"],
            effective[PolicyDomains.CoreEstablishment].Rules.Select(r => r.RuleKey).OrderBy(k => k, StringComparer.Ordinal));

        // No effective rule has an effect that needs an approval, a mask, a receipt or a checklist nobody implements.
        var unconsumed = new[] { PolicyEffect.RequireApproval, PolicyEffect.RequireDualApproval, PolicyEffect.RequirePrivacyMask, PolicyEffect.BlockUntilChecklist, PolicyEffect.RequireOriginReceipt };
        Assert.DoesNotContain(effective.Values.SelectMany(v => v.Rules).SelectMany(Effects), e => unconsumed.Contains(e.Effect));
    }

    [Fact]
    public async Task The_guard_itself_finds_the_old_rules_before_the_sets_apply()
    {
        // The negative of the two checks above: the database of before the plan, with no change set applied.
        await using var h = new Pol047Harness();
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(h.Db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(h.Db);
        await Pol049LegacyPolicyData.SeedAsync(h.Db);
        await CodeGenerationSeeder.SeedAsync(h.Db);
        h.Db.ChangeTracker.Clear();

        var found = await PolicyPublishedRuleInventory.FindAsync(h.Db, h.Validation);

        var inForce = found.Where(r => r.InForce).Select(r => r.RuleKey).ToList();
        foreach (var key in new[] { "R-ID-PROCESS", "R-LEAVE-LONG", "R-PRIV-WF", "R-HIGH-APPROVAL", "R-EST-SUBMIT", "R-HIRE-START", "R-ETL", "R-ARC-HOLD" })
            Assert.Contains(key, inForce);
        // R-EST-SUBMIT is refused only for want of its binding, which the seeder adds before it applies the sets.
        Assert.Equal("THEN_WORKFLOW_UNBOUND", found.Single(r => r.RuleKey == "R-EST-SUBMIT").Codes);
        Assert.DoesNotContain(found, r => !r.InForce);
    }
}
