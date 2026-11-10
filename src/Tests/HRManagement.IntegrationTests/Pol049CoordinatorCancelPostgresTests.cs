using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-49 (G-POL-09, P-7) on PostgreSQL, as the app role under forced row security in an agency's session: when the policy
/// starts another workflow than the one the service asked for, the cancel of that instance, the audit row and the policy ledger
/// row commit in the transaction of the start, together (one transaction id), and the caller's change is never applied. When
/// the same transaction is rolled back, none of the three is left: nothing can announce an instance that does not exist.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol049CoordinatorCancelPostgresTests(PostgreSqlFixture fixture)
{
    private static readonly Guid Tenant = Guid.Parse("a0000049-0000-4000-8000-0000000000c1");
    private static readonly Guid Actor = Guid.Parse("00000049-0000-4000-8000-0000000000c2");
    private const string OfferCode = RecruitmentOfferService.OfferApprovalWorkflowCode;
    private const string OverrideCode = "RECRUITMENT-OFFER-OVERRIDE-APPROVAL";

    /// <summary>A rule the catalog accepts (an offer may trigger either offer workflow), which wins by its priority.</summary>
    private static readonly PolicySeedChangeSet WeakOfferRule = new("POL-TEST-PG-WEAK-OFFER",
    [
        new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, "ACC-R-OFF-WEAK", "كل عرض ← اعتماد العرض العادي", 1,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-APPROVAL"}}]""")
    ]);

    private static readonly IConfiguration Enforce = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:Core.Recruitment"] = "Enforce" })
        .Build();

    private static ApprovalStartCoordinator Coordinator(HRCoreDbContext db)
    {
        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
        var orchestrator = new PolicyOrchestrator(db, starter, new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);
        return new ApprovalStartCoordinator(db, orchestrator, starter, Enforce);
    }

    /// <summary>What RecruitmentOfferService asks for an exception offer: the override workflow.</summary>
    private static ApprovalStartRequest ExceptionOffer(Guid offerId, string requestedCode = OverrideCode) =>
        new("OfferLetter", offerId, Tenant, requestedCode, PolicyDomains.CoreRecruitment, Actor, "actor", "{}",
            () => PolicyDomainFactsBuilders.ForOfferLetter(offerId, Tenant, "Issue", Guid.NewGuid(), isExceptionOffer: true));

    private async Task<(string Admin, ServiceProvider Provider)> ArrangeAsync(string name)
    {
        var admin = await Pol047Pg.MigratedAsync(fixture, name);
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        await Pol047Pg.ApplyNationalSharedReadAsync(admin);
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        var provider = Pol047Pg.AppProvider(app, Enforce);
        await Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            await WorkflowPackSeeder.SeedAsync(db);
            await PolicyEngineSeeder.SeedAsync(db);
            await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync([WeakOfferRule]);
            return true;
        });
        return (admin, provider);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_cancel_the_audit_and_the_ledger_row_commit_in_one_transaction_and_nothing_is_applied()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, provider) = await ArrangeAsync("p49cancel");
        await using var _ = provider;
        var offerId = Guid.NewGuid();
        var applied = false;

        var result = await Pol047Pg.AsTenantAsync(provider, Tenant, db =>
            Coordinator(db).StartAndApplyAsync(ExceptionOffer(offerId), _ => { applied = true; return Task.CompletedTask; }));

        Assert.False(result.Started);
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, result.Error);
        Assert.False(applied);

        // One instance of the entity: the policy's, of the weaker workflow, cancelled; none in progress.
        var instance = await Pol047Pg.ScalarAsync<string>(admin, $"""
            SELECT string_agg(i."Status" || '|' || d."Code" || '|' || (i."CompletedAt" IS NOT NULL)::text || '|' || (i."TenantId" = '{Tenant}')::text, ';')
            FROM "WorkflowInstances" i JOIN "WorkflowDefinitions" d ON d."Id" = i."DefinitionId" WHERE i."EntityId" = '{offerId}'
            """);
        Assert.Equal($"Cancelled|{OfferCode}|true|true", instance);
        // Its history says why (the Arabic text is escaped in the stored JSON; the plan mark and the two codes are not).
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""
            SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{offerId}'
              AND "StepHistoryJson" LIKE '%(PLAN-49)%' AND "StepHistoryJson" LIKE '%{OfferCode}%' AND "StepHistoryJson" LIKE '%{OverrideCode}%'
            """));

        // The ledger row and the audit row, in the agency's tenant.
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""
            SELECT count(*) FROM "PolicyEnforcementLedgers" l JOIN "WorkflowInstances" i ON i."Id" = l."WorkflowInstanceId"
            WHERE l."EntityId" = '{offerId}' AND l."Mode" = 'Enforce' AND l."TenantId" = '{Tenant}' AND i."Status" = 'Cancelled'
            """));
        Assert.Equal($"{PolicyWorkflowCodeGuard.CancelAuditAction}|OfferLetter|{Tenant}|{Actor}", await Pol047Pg.ScalarAsync<string>(admin, $"""
            SELECT string_agg("Action" || '|' || "EntityType" || '|' || "TenantId"::text || '|' || coalesce("UserId"::text, '-'), ';')
            FROM "AuditLogs" WHERE "EntityId" = '{offerId}' AND "Action" = '{PolicyWorkflowCodeGuard.CancelAuditAction}'
            """));

        // The cancelled instance is not picked up again: the same request is refused the same way, with a second cancelled one.
        var again = await Pol047Pg.AsTenantAsync(provider, Tenant, db =>
            Coordinator(db).StartAndApplyAsync(ExceptionOffer(offerId), _ => { applied = true; return Task.CompletedTask; }));
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, again.Error);
        Assert.False(applied);
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{offerId}' AND "Status" = 'Cancelled'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{offerId}' AND "Status" = 'InProgress'"""));

        // And an offer that asks for the workflow the policy starts gets it, and is applied.
        var ordinary = Guid.NewGuid();
        Guid? linked = null;
        var taken = await Pol047Pg.AsTenantAsync(provider, Tenant, db =>
            Coordinator(db).StartAndApplyAsync(ExceptionOffer(ordinary, OfferCode), id => { linked = id; return Task.CompletedTask; }));
        Assert.True(taken.Started, taken.Error);
        Assert.Equal(taken.InstanceId, linked);
        Assert.Equal("InProgress", await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "Status" FROM "WorkflowInstances" WHERE "Id" = '{taken.InstanceId}'"""));
    }

    /// <summary>
    /// The three rows are one unit of work. Seen from another connection: none of them exists while the transaction of the start
    /// is open; all three appear when it commits, and none when it is rolled back. (The transaction here is the caller's, which
    /// the coordinator joins; without one it opens its own around the same statements, the test above.)
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_instance_its_cancel_the_ledger_row_and_the_audit_appear_together_or_not_at_all(bool commit)
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, provider) = await ArrangeAsync(commit ? "p49commit" : "p49rollback");
        await using var _ = provider;
        var offerId = Guid.NewGuid();

        async Task<(long Instances, long Ledger, long Audit)> SeenFromOutsideAsync() =>
            (await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{offerId}'"""),
                await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyEnforcementLedgers" WHERE "EntityId" = '{offerId}'"""),
                await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "AuditLogs" WHERE "EntityId" = '{offerId}'"""));

        var error = await Pol047Pg.AsTenantAsync(provider, Tenant, async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await Coordinator(db).StartAndApplyAsync(ExceptionOffer(offerId), _ => Task.CompletedTask);

            // Inside the transaction the refusal is whole: the cancelled instance, its ledger row, its audit row.
            Assert.Equal(1, await db.WorkflowInstances.CountAsync(i => i.EntityId == offerId && i.Status == HRManagement.SharedKernel.Workflow.WorkflowStatus.Cancelled));
            Assert.Equal(1, await db.PolicyEnforcementLedgers.CountAsync(l => l.EntityId == offerId));
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == offerId && a.Action == PolicyWorkflowCodeGuard.CancelAuditAction));
            // Outside it, nothing yet: no moment at which the instance is visible in progress, or cancelled without its record.
            Assert.Equal((0L, 0L, 0L), await SeenFromOutsideAsync());

            if (commit)
                await transaction.CommitAsync();
            else
                await transaction.RollbackAsync();
            return result.Error;
        });

        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, error);
        Assert.Equal(commit ? (1L, 1L, 1L) : (0L, 0L, 0L), await SeenFromOutsideAsync());
    }
}
