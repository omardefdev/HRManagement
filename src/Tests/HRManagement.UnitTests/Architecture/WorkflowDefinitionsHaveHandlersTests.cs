using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-25 (G-14): a published workflow definition must have an effect when it completes — its entity type is handled by
/// <c>WorkflowInstanceActionService.PublishWorkflowOutcomeAsync</c>, or it is allow-listed below because another service
/// owns the outcome. RECRUITMENT-HIRE (RecruitmentApplication) had neither and is retired.
/// </summary>
public sealed class WorkflowDefinitionsHaveHandlersTests
{
    /// <summary>Entity type → why its outcome is not handled in HR.Core's action service.</summary>
    private static readonly IReadOnlyDictionary<string, string> OutcomeOwnedElsewhere = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ServiceTransaction"] = "HIRE-WIZARD — HR.Contracts drives the hire transaction state itself",
        ["SeparationRecord"] = "SEPARATION-WIZARD — HR.Contracts publishes the separation",
        ["CardRequest"] = "ID-CARD-ISSUANCE — IDCard processes the request locally",
        ["TransferRequest"] = "TRANSFER-APPROVAL — HR.Movements approves transfers locally (HrManagementApprove)",
        ["DisciplinaryAction"] = "DISCIPLINARY-APPROVAL — the compliance service owns the action",
        ["Ticket"] = "IT-TICKET — IT infrastructure owns ticket escalation"
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

    [Fact]
    public async Task Every_published_definition_has_an_outcome_handler_or_a_reason()
    {
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);

        var orphans = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.IsPublished)
            .Select(d => new { d.Code, d.DomainEntityType })
            .ToListAsync();
        var offenders = orphans
            .Where(d => !WorkflowInstanceActionService.HandledEntityTypes.Contains(d.DomainEntityType)
                && !OutcomeOwnedElsewhere.ContainsKey(d.DomainEntityType))
            .Select(d => $"{d.Code} ({d.DomainEntityType})")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Published workflow definitions without an outcome handler: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task Recruitment_hire_is_seeded_unpublished_and_existing_databases_are_patched()
    {
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);
        var hire = await db.WorkflowDefinitions.SingleAsync(d => d.Code == "RECRUITMENT-HIRE");
        Assert.False(hire.IsPublished);
        Assert.NotNull(hire.ValidTo);

        // A database seeded before PLAN-25 has it published: the start-up patch unpublishes it (idempotently).
        hire.IsPublished = true;
        hire.ValidTo = null;
        await db.SaveChangesAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.SeedAsync(db);
        db.ChangeTracker.Clear();
        var patched = await db.WorkflowDefinitions.SingleAsync(d => d.Code == "RECRUITMENT-HIRE");
        Assert.False(patched.IsPublished);
        Assert.NotNull(patched.ValidTo);
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
                || (body.Contains("competitionHandler.HandleAsync", StringComparison.Ordinal)
                    && type is "RecruitmentCompetition" or "CompetitionResultVersion"
                    && competitionHandler.Contains("EntityType", StringComparison.Ordinal));
            Assert.True(dispatched, $"{type} is listed as handled but PublishWorkflowOutcomeAsync does not dispatch it.");
        }
    }
}
