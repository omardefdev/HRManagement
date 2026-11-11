using System.Reflection;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-25 (G-14): a published workflow definition must have an effect when it completes. PLAN-46 (G-WF-17): that effect is either a
/// handler in HR.Core's action service, or, for a type another service owns, a consumer of <see cref="IWorkflowOutcomeDecided"/> that this
/// test finds by reflection in that service's infrastructure assembly. No sentence stands in for a consumer.
/// </summary>
public sealed class WorkflowDefinitionsHaveHandlersTests
{
    /// <summary>The owner of a consumer that a later plan writes. Its type may publish no definition until the consumer exists.</summary>
    public sealed record OutcomeConsumer(string ServiceName, string ConsumerTypeName, string? PendingPlan = null);

    /// <summary>Entity type of the registry → the service that applies its outcome, and the consumer that does it.</summary>
    public static readonly IReadOnlyDictionary<string, OutcomeConsumer> OutcomeConsumers = new Dictionary<string, OutcomeConsumer>(StringComparer.OrdinalIgnoreCase)
    {
        ["CardRequest"] = new("IDCard", "CardRequestApprovalOutcomeConsumer"),
        ["DisciplinaryAction"] = new("Compliance", "DisciplinaryApprovalOutcomeConsumer"),
        // PLAN-60 writes SeparationApprovalOutcomeConsumer in HR.Contracts, seeds the two codes, and removes PendingPlan.
        ["SeparationRecord"] = new("HR.Contracts", "SeparationApprovalOutcomeConsumer", PendingPlan: "PLAN-60")
    };

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static async Task<HRCoreDbContext> SeededAsync()
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);
        return db;
    }

    [Fact]
    public async Task Every_published_definition_has_an_outcome_handler_or_a_consumer()
    {
        await using var db = await SeededAsync();

        var published = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.IsPublished)
            .Select(d => new { d.Code, d.DomainEntityType })
            .ToListAsync();
        var offenders = published
            .Where(d => !WorkflowInstanceActionService.HandledEntityTypes.Contains(d.DomainEntityType)
                || (OutcomeConsumers.TryGetValue(d.DomainEntityType, out var consumer) && consumer.PendingPlan is not null))
            .Select(d => $"{d.Code} ({d.DomainEntityType})")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Published workflow definitions without an outcome handler or an existing consumer: " + string.Join(", ", offenders));
    }

    [Theory]
    [MemberData(nameof(Consumers))]
    public void Each_consumer_exists_and_consumes_the_outcome_event(string entityType)
    {
        var entry = OutcomeConsumers[entityType];
        var consumer = FindConsumer(entry);

        if (entry.PendingPlan is not null)
        {
            Assert.True(consumer is null,
                $"{entry.ConsumerTypeName} exists now: remove PendingPlan {entry.PendingPlan} from the entry of {entityType}.");
            return;
        }

        Assert.NotNull(consumer);
        Assert.True(typeof(IConsumer<IWorkflowOutcomeDecided>).IsAssignableFrom(consumer),
            $"{consumer.FullName} does not consume IWorkflowOutcomeDecided.");
        Assert.False(consumer.IsAbstract);
    }

    public static TheoryData<string> Consumers() => [.. OutcomeConsumers.Keys];

    [Fact]
    public void The_guard_finds_nothing_that_is_not_a_consumer()
    {
        // A type of that name that does not consume the event would pass a search by name; it fails here.
        var notAConsumer = new OutcomeConsumer("IDCard", "CardIssuanceWorkflow");

        Assert.Null(FindConsumer(new OutcomeConsumer("IDCard", "NoSuchConsumer")));
        var found = FindConsumer(notAConsumer);
        Assert.True(found is null || !typeof(IConsumer<IWorkflowOutcomeDecided>).IsAssignableFrom(found));
    }

    [Fact]
    public async Task The_four_orphans_are_retired_and_existing_databases_are_patched()
    {
        string[] orphans = ["HIRE-WIZARD", "TRANSFER-APPROVAL", "IT-TICKET", "SEPARATION-WIZARD", "RECRUITMENT-HIRE"];
        await using var db = await SeededAsync();
        Assert.All(await db.WorkflowDefinitions.Where(d => orphans.Contains(d.Code)).ToListAsync(), d =>
        {
            Assert.False(d.IsPublished, d.Code);
            Assert.NotNull(d.ValidTo);
        });

        // A database seeded before PLAN-46 has them published: the start-up patch unpublishes them, idempotently.
        foreach (var definition in await db.WorkflowDefinitions.Where(d => orphans.Contains(d.Code)).ToListAsync())
        {
            definition.IsPublished = true;
            definition.ValidTo = null;
        }

        await db.SaveChangesAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.SeedAsync(db);
        db.ChangeTracker.Clear();
        Assert.All(await db.WorkflowDefinitions.Where(d => orphans.Contains(d.Code)).ToListAsync(), d =>
        {
            Assert.False(d.IsPublished, d.Code);
            Assert.NotNull(d.ValidTo);
        });
    }

    [Fact]
    public async Task The_card_and_the_discipline_are_raised_to_their_chains_once()
    {
        await using var db = await SeededAsync();
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);

        var card = await db.WorkflowDefinitions.Where(d => d.Code == "ID-CARD-ISSUANCE").OrderBy(d => d.Version).ToListAsync();
        var discipline = await db.WorkflowDefinitions.Where(d => d.Code == "DISCIPLINARY-APPROVAL").OrderBy(d => d.Version).ToListAsync();

        Assert.Equal([false, true], card.Select(d => d.IsPublished));
        Assert.Equal([false, true], discipline.Select(d => d.IsPublished));
        Assert.Contains("\"idcard\"", card[1].StepsJson, StringComparison.Ordinal);
        Assert.True(discipline[1].RequireDistinctApprovers);
        Assert.Equal("seed:PLAN-46", discipline[1].ChangeReason);
    }

    [Fact]
    public void Every_handled_entity_type_is_dispatched_by_the_outcome_method()
    {
        var source = global::System.IO.File.ReadAllText(Path.Combine(RepoRoot,
            "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs"));
        var start = source.IndexOf("private async Task PublishWorkflowOutcomeAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "PublishWorkflowOutcomeAsync not found.");
        var body = source[start..];
        var competitionHandler = global::System.IO.File.ReadAllText(Path.Combine(RepoRoot,
            "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Competitions/CompetitionWorkflowHandler.cs"));

        foreach (var type in WorkflowInstanceActionService.HandledEntityTypes)
        {
            var dispatched = body.Contains($"\"{type}\"", StringComparison.Ordinal)
                || (ExternalApprovalTypes.IsGeneric(type) && body.Contains("ExternalApprovalTypes.IsGeneric(instance.EntityType)", StringComparison.Ordinal))
                || (body.Contains("competitionHandler.HandleAsync", StringComparison.Ordinal)
                    && type is "RecruitmentCompetition" or "CompetitionResultVersion"
                    && competitionHandler.Contains("EntityType", StringComparison.Ordinal));
            Assert.True(dispatched, $"{type} is listed as handled but PublishWorkflowOutcomeAsync does not dispatch it.");
        }
    }

    /// <summary>
    /// PLAN-49 (plan section 2, rule 3): the rules of the effective policy versions that fire a workflow whose entity type has
    /// neither a handler in HR.Core nor an existing cross-service consumer (PLAN-46). Such a rule starts an instance whose
    /// outcome nobody applies.
    /// </summary>
    private static async Task<List<string>> OrphanTriggersAsync(HRCoreDbContext db)
    {
        var definitions = (await db.WorkflowDefinitions.AsNoTracking().Select(d => new { d.Code, d.DomainEntityType }).ToListAsync())
            .GroupBy(d => d.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(d => d.DomainEntityType).Distinct().ToList(), StringComparer.Ordinal);
        var templates = await db.PolicyTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Rules).ToListAsync();

        var offenders = new List<string>();
        foreach (var template in templates)
        {
            var effective = HR.Core.Infrastructure.Services.Policy.PolicyDraftService.EffectiveVersion(template, DateTime.UtcNow);
            foreach (var rule in effective?.Rules ?? [])
            {
                var issues = new List<HRManagement.SharedKernel.Policy.PolicyRuleIssue>();
                var effects = HRManagement.SharedKernel.Policy.PolicyRuleJson.ParseThenStrict(rule.ThenJson, issues) ?? [];
                foreach (var effect in effects.Where(e => e.Effect == HRManagement.SharedKernel.Policy.PolicyEffect.TriggerWorkflow))
                {
                    var code = effect.Params!["workflowCode"];
                    var types = definitions.TryGetValue(code, out var found) ? found : [];
                    var applied = types.Count > 0 && types.All(type =>
                        WorkflowInstanceActionService.HandledEntityTypes.Contains(type)
                        && !(OutcomeConsumers.TryGetValue(type, out var consumer) && consumer.PendingPlan is not null));
                    if (!applied)
                        offenders.Add($"{template.Domain} {rule.RuleKey} -> {code} ({(types.Count == 0 ? "no definition" : string.Join("/", types))})");
                }
            }
        }

        return offenders;
    }

    [Fact]
    public async Task No_rule_of_an_effective_policy_version_fires_a_workflow_nobody_applies()
    {
        // A new database: the seed of the five governed domains, with the change sets applied.
        await using var db = await SeededAsync();
        await PolicyEngineSeeder.SeedAsync(db);

        var offenders = await OrphanTriggersAsync(db);

        Assert.True(offenders.Count == 0, "Effective policy rules that fire a workflow with no outcome handler or consumer: " + string.Join(", ", offenders));
        // And the check has something to look at: the governed rules do fire workflows.
        Assert.True(await db.PolicyRules.CountAsync(r => r.ThenJson.Contains("TriggerWorkflow")) >= 9);
    }

    [Fact]
    public async Task The_rule_check_finds_the_triggers_of_before_PLAN_49_and_none_after_its_change_sets()
    {
        // A database of before the plan: R-HIRE-START, R-TRF and R-TKT fire codes whose types nobody handles, and R-SEP-START a
        // type whose consumer a later plan writes. The change sets leave none of them in an effective version.
        await using var db = await SeededAsync();
        await HRManagement.UnitTests.Pol049LegacyPolicyData.SeedAsync(db);

        var before = await OrphanTriggersAsync(db);
        foreach (var key in new[] { "R-HIRE-START", "R-TRF", "R-TKT", "R-SEP-START" })
            Assert.Contains(before, o => o.Contains($" {key} ", StringComparison.Ordinal));

        await PolicyEngineSeeder.SeedAsync(db);
        db.ChangeTracker.Clear();

        Assert.Empty(await OrphanTriggersAsync(db));
    }

    /// <summary>The consumer by its name, among the exported types of <c>{ServiceName}.Infrastructure</c>.</summary>
    private static Type? FindConsumer(OutcomeConsumer entry)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load($"{entry.ServiceName}.Infrastructure");
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        return assembly.GetExportedTypes().SingleOrDefault(t => t.Name == entry.ConsumerTypeName);
    }
}
