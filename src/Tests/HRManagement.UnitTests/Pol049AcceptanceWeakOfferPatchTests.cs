using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (P-10): the weak offer rule of AC-POL-49f is acceptance seeding. It is applied only where its own switch is on (the
/// pol49 overlay of the acceptance stack), through the change-set applier, once; it is never one of the product's change sets;
/// and with it the national policy does fire the ordinary approval for an exception offer, which is the swap the approval start
/// must refuse (Pol049CoordinatorWorkflowCodeTests).
/// </summary>
public sealed class Pol049AcceptanceWeakOfferPatchTests
{
    private static ServiceProvider Services(string? switchValue)
    {
        var settings = new Dictionary<string, string?>();
        if (switchValue is not null)
            settings[AcceptancePol49WeakOfferPatch.SwitchKey] = switchValue;
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();
    }

    private static async Task<HRCoreDbContext> SeededAsync()
    {
        var db = CreateContext();
        await SeedReferenceDataAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);
        return db;
    }

    private static async Task<PolicyTemplateVersion> EffectiveRecruitmentAsync(HRCoreDbContext db)
    {
        db.ChangeTracker.Clear();
        var template = await db.PolicyTemplates.AsNoTracking()
            .Include(t => t.Versions).ThenInclude(v => v.Rules)
            .SingleAsync(t => t.Domain == PolicyDomains.CoreRecruitment && t.Scope == PolicyTemplateScope.National);
        return HR.Core.Infrastructure.Services.Policy.PolicyDraftService.EffectiveVersion(template, DateTime.UtcNow)!;
    }

    private static Task<PolicyDecision> EvaluateOfferAsync(HRCoreDbContext db, bool exception) =>
        CreateOrchestrator(db).EvaluateAsync(
            PolicyDomainFactsBuilders.ForOfferLetter(Guid.NewGuid(), null, "Issue", Guid.NewGuid(), isExceptionOffer: exception));

    [Fact]
    public void The_weak_offer_rule_is_not_one_of_the_products_change_sets()
    {
        Assert.DoesNotContain(PolicySeedChangeSets.All, set => set.Id == AcceptancePol49WeakOfferPatch.ChangeSetId);
        Assert.DoesNotContain(PolicySeedChangeSets.All.SelectMany(set => set.Operations), op => op.RuleKey == AcceptancePol49WeakOfferPatch.RuleKey);

        // What it is: one rule on the national recruitment policy, ahead of every seeded offer rule.
        var operation = Assert.Single(AcceptancePol49WeakOfferPatch.ChangeSet.Operations);
        Assert.Equal(PolicySeedOperationKind.AddRule, operation.Kind);
        Assert.Equal(PolicyDomains.CoreRecruitment, operation.Domain);
        Assert.Equal(1, operation.Priority);
        Assert.Contains(RecruitmentOfferService.OfferApprovalWorkflowCode, operation.ThenJson, StringComparison.Ordinal);
        var seededOfferPriorities = PolicySeedChangeSets.All.SelectMany(set => set.Operations)
            .Where(op => op.Domain == PolicyDomains.CoreRecruitment && op.Kind == PolicySeedOperationKind.AddRule
                         && (op.WhenJson ?? string.Empty).Contains("\"Issue\"", StringComparison.Ordinal))
            .Select(op => op.Priority)
            .ToList();
        Assert.NotEmpty(seededOfferPriorities);
        Assert.All(seededOfferPriorities, priority => Assert.True(priority > operation.Priority));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Without_its_switch_nothing_is_applied(string? switchValue)
    {
        await using var db = await SeededAsync();
        var before = await EffectiveRecruitmentAsync(db);
        await using var services = Services(switchValue);

        await AcceptancePol49WeakOfferPatch.ApplyAsync(db, services);

        var after = await EffectiveRecruitmentAsync(db);
        Assert.Equal(before.Id, after.Id);
        Assert.DoesNotContain(after.Rules, r => r.RuleKey == AcceptancePol49WeakOfferPatch.RuleKey);
        Assert.False(await db.PolicySeedApplications.AnyAsync(a => a.ChangeSetId == AcceptancePol49WeakOfferPatch.ChangeSetId));
        // The seeded policy keeps the stricter approval for an exception offer.
        Assert.Equal(OfferOverrideApprovalRulesCode, (await EvaluateOfferAsync(db, exception: true)).GetWorkflowCode());
    }

    [Fact]
    public async Task With_its_switch_the_rule_is_published_once_as_a_new_version_and_wins_over_the_exception_rules()
    {
        await using var db = await SeededAsync();
        var before = await EffectiveRecruitmentAsync(db);
        await using var services = Services("true");

        await AcceptancePol49WeakOfferPatch.ApplyAsync(db, services);
        // A restart of the container applies nothing more.
        await AcceptancePol49WeakOfferPatch.ApplyAsync(db, services);

        var after = await EffectiveRecruitmentAsync(db);
        Assert.NotEqual(before.Id, after.Id);
        Assert.Equal(before.VersionNumber + 1, after.VersionNumber);
        var weak = Assert.Single(after.Rules, r => r.RuleKey == AcceptancePol49WeakOfferPatch.RuleKey);
        Assert.Equal(1, weak.Priority);
        // Everything the version before held is carried over as it was.
        Assert.Equal(
            before.Rules.Select(r => $"{r.RuleKey}|{r.Priority}|{r.WhenJson}|{r.ThenJson}").OrderBy(x => x, StringComparer.Ordinal),
            after.Rules.Where(r => r.RuleKey != AcceptancePol49WeakOfferPatch.RuleKey)
                .Select(r => $"{r.RuleKey}|{r.Priority}|{r.WhenJson}|{r.ThenJson}").OrderBy(x => x, StringComparer.Ordinal));
        var application = await db.PolicySeedApplications.AsNoTracking().SingleAsync(a => a.ChangeSetId == AcceptancePol49WeakOfferPatch.ChangeSetId);
        Assert.Equal(PolicySeedChangeSetApplier.OutcomePublished, application.Outcome);
        Assert.Equal(after.Id, application.ResultVersionId);

        // The swap AC-POL-49f is about: the policy now names the ordinary approval for both kinds of offer.
        Assert.Equal(RecruitmentOfferService.OfferApprovalWorkflowCode, (await EvaluateOfferAsync(db, exception: true)).GetWorkflowCode());
        Assert.Equal(RecruitmentOfferService.OfferApprovalWorkflowCode, (await EvaluateOfferAsync(db, exception: false)).GetWorkflowCode());
    }

    [Fact]
    public void Only_the_pol49_overlay_of_the_acceptance_stack_turns_the_switch_on()
    {
        var root = RepoPaths.Root;
        var variable = AcceptancePol49WeakOfferPatch.SwitchKey.Replace(":", "__", StringComparison.Ordinal);
        var files = Directory.EnumerateFiles(root, "docker-compose*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "deploy"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            .Where(f => global::System.IO.File.ReadAllText(f).Contains("Pol49WeakOfferRule", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["docker-compose.acceptance-pol49.yml"], files);
        Assert.Contains($"{variable}: \"true\"", global::System.IO.File.ReadAllText(Path.Combine(root, "docker-compose.acceptance-pol49.yml")), StringComparison.Ordinal);
    }

    private const string OfferOverrideApprovalRulesCode = HR.Core.Domain.Services.OfferOverrideApprovalRules.WorkflowCode;
}
