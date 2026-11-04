using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-42 §2: the S2S042_PolicyLedgerCaller migration and what the ledger records, on PostgreSQL (RUN_TESTCONTAINERS=true).</summary>
public class S2s042PolicyLedgerCallerPostgresTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private PostgreSqlContainer? _postgres;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private async Task<HRCoreDbContext> MigrateAsync()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), n => n.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
            .Options;
        var db = new HRCoreDbContext(options, new FixedTenantContext(TenantId));
        await db.Database.MigrateAsync();
        return db;
    }

    private static PolicyOrchestrator Orchestrator(HRCoreDbContext db, IWorkflowStarter starter) =>
        new(db, starter, new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);

    private static PolicyEnforceRequest Privileged(string subjectType, Guid? entityId, PolicyEnforcementMode mode = PolicyEnforcementMode.Enforce) => new()
    {
        Domain = PolicyDomains.AssetsCustody,
        SubjectType = subjectType,
        Action = "Assign",
        EntityId = entityId,
        TenantId = TenantId,
        Mode = mode,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["isPrivileged"] = "true", ["value"] = "20000", ["action"] = "Assign" }
    };

    private static Mock<IWorkflowStarter> Starter(Guid instance)
    {
        var starter = new Mock<IWorkflowStarter>();
        starter.Setup(s => s.StartByWorkflowCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        return starter;
    }

    private static void VerifyNoStart(Mock<IWorkflowStarter> starter) =>
        starter.Verify(s => s.StartByWorkflowCodeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_adds_the_columns_and_the_index()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();

        var columns = await db.Database
            .SqlQueryRaw<string>("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'PolicyEnforcementLedgers'""")
            .ToListAsync();
        var indexes = await db.Database
            .SqlQueryRaw<string>("""SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename = 'PolicyEnforcementLedgers'""")
            .ToListAsync();

        Assert.Contains("CallerClientId", columns);
        Assert.Contains("SubjectType", columns);
        Assert.Contains("EntityId", columns);
        Assert.Contains(indexes, i => i.Contains("\"EntityId\"") && i.Contains("\"OccurredAt\""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_ledger_records_the_caller_the_subject_the_entity_and_the_derived_mode()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();
        var instance = Guid.NewGuid();
        var entity = Guid.NewGuid();

        var response = await Orchestrator(db, Starter(instance).Object)
            .EnforceForServiceAsync(Privileged("AssetAssignment", entity), "svc-admin-logistics");

        var row = await db.PolicyEnforcementLedgers.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == response.LedgerId);
        Assert.Equal("svc-admin-logistics", row.CallerClientId);
        Assert.Equal("AssetAssignment", row.SubjectType);
        Assert.Equal(entity, row.EntityId);
        Assert.Equal(instance, row.WorkflowInstanceId);
        Assert.Equal("Enforce", row.Mode.ToString());
        Assert.Equal(PolicyEnforcementMode.Enforce, response.EffectiveMode);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_service_starts_no_workflow_outside_the_allowlist_and_is_told_why()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();
        var starter = Starter(Guid.NewGuid());

        var response = await Orchestrator(db, starter.Object)
            .EnforceForServiceAsync(Privileged("Asset", Guid.NewGuid()), "svc-admin-logistics");

        VerifyNoStart(starter);
        Assert.Null(response.WorkflowInstanceId);
        Assert.Contains(PolicyWorkflowTriggerAllowlist.NotAllowedExplanation, response.Explanations);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_service_starts_no_workflow_without_an_entity()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();
        var starter = Starter(Guid.NewGuid());

        var missing = await Orchestrator(db, starter.Object).EnforceForServiceAsync(Privileged("AssetAssignment", null), "svc-admin-logistics");
        var empty = await Orchestrator(db, starter.Object).EnforceForServiceAsync(Privileged("AssetAssignment", Guid.Empty), "svc-admin-logistics");

        VerifyNoStart(starter);
        Assert.Contains(PolicyWorkflowTriggerAllowlist.NotAllowedExplanation, missing.Explanations);
        Assert.Contains(PolicyWorkflowTriggerAllowlist.NotAllowedExplanation, empty.Explanations);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_decision_inside_HR_Core_has_no_caller_and_is_not_limited_by_the_allowlist()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();
        var instance = Guid.NewGuid();

        var response = await Orchestrator(db, Starter(instance).Object).EnforceAsync(Privileged("Asset", Guid.NewGuid()));

        var row = await db.PolicyEnforcementLedgers.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == response.LedgerId);
        Assert.Null(row.CallerClientId);
        Assert.Equal(instance, response.WorkflowInstanceId);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Off_writes_no_ledger_row_and_starts_no_workflow()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();
        var starter = Starter(Guid.NewGuid());

        var response = await Orchestrator(db, starter.Object)
            .EnforceForServiceAsync(Privileged("AssetAssignment", Guid.NewGuid(), PolicyEnforcementMode.Off), "svc-admin-logistics");

        Assert.True(response.Allowed);
        Assert.Equal(PolicyEnforcementMode.Off, response.EffectiveMode);
        Assert.Null(response.LedgerId);
        Assert.Equal(0, await db.PolicyEnforcementLedgers.IgnoreQueryFilters().CountAsync());
        VerifyNoStart(starter);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Shadow_writes_its_ledger_row_and_starts_no_workflow()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigrateAsync();
        var starter = Starter(Guid.NewGuid());

        var response = await Orchestrator(db, starter.Object)
            .EnforceForServiceAsync(Privileged("AssetAssignment", Guid.NewGuid(), PolicyEnforcementMode.Shadow), "svc-admin-logistics");

        var row = await db.PolicyEnforcementLedgers.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == response.LedgerId);
        Assert.Equal("Shadow", row.Mode.ToString());
        Assert.Equal(PolicyEnforcementMode.Shadow, response.EffectiveMode);
        VerifyNoStart(starter);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
