using System.Collections.Concurrent;
using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Infrastructure.Consumers;
using Compliance.Infrastructure.Data;
using Compliance.Infrastructure.Services;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport.Workflow;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Infrastructure.Data;
using IDCard.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-46 on PostgreSQL, the owners' side: the disciplinary consumer on the consumer outbox, <c>xmin</c> between a withdrawal and the
/// outcome, the Compliance migration as a non-superuser owner under forced row security, and both sweeps as the app role in two tenants.
/// </summary>
[Collection("PgRabbit")]
public sealed class Wf046DisciplinaryConsumerPostgresTests(PgRabbitFixture fixture)
{
    private static readonly Guid TenantA = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("46000000-0000-4000-8000-00000000f00d");
    private static readonly Guid Creator = Guid.Parse("46000000-0000-4000-8000-000000000c01");
    private static readonly Guid Checker = Guid.Parse("46000000-0000-4000-8000-000000000c02");
    private static readonly Guid Authority = Guid.Parse("46000000-0000-4000-8000-000000000c03");

    private const string PreviousComplianceMigration = "20261103235300_MSG040_InboxStateKeys";

    private static readonly WorkflowStepDecisionDto[] TwoSteps =
    [
        new(1, "compliance", Checker, "Approved", DateTime.UtcNow),
        new(2, "hr-management-approver", Authority, "Approved", DateTime.UtcNow)
    ];

    private static ComplianceDbContext ComplianceDb(string connection, Guid? tenant = null) =>
        new(new DbContextOptionsBuilder<ComplianceDbContext>().UseNpgsql(connection).Options,
            tenant is Guid t ? new Msg040FixedTenant(t) : null);

    private async Task<string> ComplianceDatabaseAsync(string name)
    {
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf046c_{name}_{Guid.NewGuid():N}");
        await using var db = ComplianceDb(connection);
        await db.Database.MigrateAsync();
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<DisciplinaryAction> PendingAsync(string connection, Guid tenant, DateTime? submittedAt = null)
    {
        await using var db = ComplianceDb(connection, tenant);
        var action = new DisciplinaryAction
        {
            TenantId = tenant,
            EmployeeId = Guid.NewGuid(),
            ActionType = ActionType.Warning,
            IssuedAt = DateTime.UtcNow,
            CreatedBy = Creator,
            ApprovalStatus = DisciplinaryApprovalStatus.PendingApproval,
            ApprovedBeforeEngine = false,
            WorkflowInstanceId = Guid.NewGuid(),
            SubmissionNo = 1,
            SubmittedBy = Creator,
            SubmittedAt = submittedAt ?? DateTime.UtcNow
        };
        db.DisciplinaryActions.Add(action);
        await db.SaveChangesAsync();
        return action;
    }

    // The consumer outbox.

    /// <summary>Counts what payroll would receive.</summary>
    public sealed class TakenCounter : IConsumer<IDisciplinaryActionTaken>
    {
        public static readonly ConcurrentBag<Guid> Received = [];

        public Task Consume(ConsumeContext<IDisciplinaryActionTaken> context)
        {
            Received.Add(context.Message.ActionId);
            return Task.CompletedTask;
        }
    }

    private ServiceProvider BusProvider(string connection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0",
            ["MassTransit:EndpointPrefix"] = $"wf046-{Guid.NewGuid():N}"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpContextAccessor();
        services.AddTenantContext();
        services.AddDbContext<ComplianceDbContext>(o => o.UseNpgsql(connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ComplianceDbContext>());
        services.AddSingleton<IWorkflowApprovalClient>(new FakeWorkflowApprovalClient());
        services.AddScoped<DisciplinaryApprovalService>();
        services.AddServiceMassTransit(
            configuration,
            configureConsumers: x =>
            {
                x.AddConsumer<DisciplinaryApprovalOutcomeConsumer>();
                x.AddConsumer<TakenCounter>();
            },
            configureOutbox: x => x.AddPostgresOutbox<ComplianceDbContext>(consumerOutbox: true));
        return services.BuildServiceProvider();
    }

    private static Task PublishOutcomeAsync(IBus bus, DisciplinaryAction action, Guid messageId) =>
        bus.Publish<IWorkflowOutcomeDecided>(
            FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", action.Id, action.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved,
                action.TenantId, Authority, decisions: TwoSteps),
            context =>
            {
                context.MessageId = messageId;
                context.Headers.Set(TenantIdMiddleware.HeaderName, action.TenantId.ToString("D"));
            });

    private static async Task WaitForAsync(Func<Task<bool>> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await done() && DateTime.UtcNow < deadline)
            await Task.Delay(200);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_approval_through_the_consumer_outbox_gives_one_event_and_a_redelivery_gives_none()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await ComplianceDatabaseAsync("consumer");
        var action = await PendingAsync(connection, TenantA);
        await using var provider = BusProvider(connection);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var messageId = Guid.NewGuid();
            await PublishOutcomeAsync(bus, action, messageId);
            await WaitForAsync(() => Task.FromResult(TakenCounter.Received.Contains(action.Id)), TimeSpan.FromSeconds(30));

            // The same message again, then the same outcome under another id: the inbox, then the guard, stop both.
            await PublishOutcomeAsync(bus, action, messageId);
            await PublishOutcomeAsync(bus, action, Guid.NewGuid());
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await bus.StopAsync();
        }

        Assert.Single(TakenCounter.Received, id => id == action.Id);
        Assert.Equal((int)DisciplinaryApprovalStatus.Approved, await ScalarAsync<int>(connection, $"""SELECT "ApprovalStatus" FROM "DisciplinaryActions" WHERE "Id" = '{action.Id:D}'"""));
        Assert.True(await ScalarAsync<bool>(connection, $"""SELECT "OutcomePublishedAt" IS NOT NULL FROM "DisciplinaryActions" WHERE "Id" = '{action.Id:D}'"""));
        Assert.Equal(0L, await ScalarAsync<long>(connection, """SELECT count(*) FROM "OutboxMessage" """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Xmin_stops_a_withdrawal_that_read_the_action_before_the_outcome_saved()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await ComplianceDatabaseAsync("xmin");
        var action = await PendingAsync(connection, TenantA);
        await using var withdrawing = ComplianceDb(connection, TenantA);
        var stale = await withdrawing.DisciplinaryActions.SingleAsync(a => a.Id == action.Id);

        await using (var consuming = ComplianceDb(connection, TenantA))
        {
            await new DisciplinaryApprovalService(consuming, new FakeWorkflowApprovalClient(), Mock.Of<IPublishEndpoint>(), NullLogger<DisciplinaryApprovalService>.Instance)
                .ApplyOutcomeAsync(FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", action.Id, action.WorkflowInstanceId!.Value,
                    WorkflowApprovalOutcomes.Approved, TenantA, Authority, decisions: TwoSteps));
        }

        var result = await new DisciplinaryApprovalService(withdrawing, new FakeWorkflowApprovalClient(), Mock.Of<IPublishEndpoint>(), NullLogger<DisciplinaryApprovalService>.Instance)
            .WithdrawAsync(stale, Creator, "سحب");

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalSubmission.ChangedMessage, result.Error);
        Assert.Equal((int)DisciplinaryApprovalStatus.Approved, await ScalarAsync<int>(connection, $"""SELECT "ApprovalStatus" FROM "DisciplinaryActions" WHERE "Id" = '{action.Id:D}'"""));
    }

    // The migration.

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_approves_every_existing_action_in_two_tenants_as_a_non_superuser_owner_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.Postgres.CreateDatabaseAsync($"wf046m_{Guid.NewGuid():N}");
        await using (var db = ComplianceDb(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousComplianceMigration);

        var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        foreach (var tenant in new[] { TenantA, TenantA, TenantB })
        {
            await ExecuteAsync(admin, $"""
                INSERT INTO "DisciplinaryActions" ("Id", "EmployeeId", "ActionType", "IssuedAt", "AppealStatus", "TenantId", "CreatedAt", "UpdatedAt")
                VALUES ('{Guid.NewGuid():D}', '{Guid.NewGuid():D}', 0, '{created:O}', 0, '{tenant:D}', '{created:O}', '{created:O}');
                """);
        }

        var role = $"wf046_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database!;
        await ExecuteAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'wf046_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            GRANT CONNECT ON DATABASE "{database}" TO {role};
            GRANT USAGE, CREATE ON SCHEMA public TO {role};
            DO $do$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {role}', t.tablename);
                END LOOP;
            END
            $do$;
            ALTER TABLE "DisciplinaryActions" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "DisciplinaryActions" FORCE ROW LEVEL SECURITY;
            """);
        var owner = new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "wf046_pwd" }.ConnectionString;

        // The control: this role sees no row, which is why a plain UPDATE in a migration would change nothing.
        Assert.Equal(0L, await ScalarAsync<long>(owner, """SELECT count(*) FROM "DisciplinaryActions" """));

        await using (var migrator = ComplianceDb(owner))
            await migrator.Database.MigrateAsync();

        Assert.Equal(3L, await ScalarAsync<long>(admin, """SELECT count(*) FROM "DisciplinaryActions" WHERE "ApprovalStatus" = 2 AND "ApprovedBeforeEngine" AND "OutcomePublishedAt" = "CreatedAt" """));
        Assert.Equal(2L, await ScalarAsync<long>(admin, $"""SELECT count(*) FROM "DisciplinaryActions" WHERE "TenantId" = '{TenantA:D}' AND "ApprovalStatus" = 2"""));
        Assert.True(await ScalarAsync<bool>(admin, """SELECT relforcerowsecurity FROM pg_class WHERE oid = '"DisciplinaryActions"'::regclass"""));

        // A row the previous tag writes after a rollback omits the columns: it published at once, so it is approved-before-engine too.
        await ExecuteAsync(admin, $"""
            INSERT INTO "DisciplinaryActions" ("Id", "EmployeeId", "ActionType", "IssuedAt", "AppealStatus", "TenantId", "CreatedAt", "UpdatedAt")
            VALUES ('{Guid.NewGuid():D}', '{Guid.NewGuid():D}', 0, now(), 0, '{TenantA:D}', now(), now());
            """);
        Assert.Equal(4L, await ScalarAsync<long>(admin, """SELECT count(*) FROM "DisciplinaryActions" WHERE "ApprovalStatus" = 2 AND "ApprovedBeforeEngine" """));

        // This code always writes both columns: a new action is a draft.
        await using (var db = ComplianceDb(admin, TenantA))
        {
            db.DisciplinaryActions.Add(new DisciplinaryAction { TenantId = TenantA, EmployeeId = Guid.NewGuid(), IssuedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1L, await ScalarAsync<long>(admin, """SELECT count(*) FROM "DisciplinaryActions" WHERE "ApprovalStatus" = 0 AND NOT "ApprovedBeforeEngine" """));
    }

    // The sweeps, as the app role under forced row security.

    private async Task<(string Admin, string App)> ForcedAsync(string admin)
    {
        var script = await global::System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", "ten002c-harden-tenant-rls.sql"));
        await ExecuteAsync(admin, script.Replace(@"\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal));
        return (admin, await fixture.Postgres.CreateAppConnectionAsync(new NpgsqlConnectionStringBuilder(admin).Database!));
    }

    private static ServiceProvider SweepProvider<TContext, TStore>(string app, FakeWorkflowApprovalClient engine, Action<IServiceCollection> owner)
        where TContext : DbContext
        where TStore : class, IWorkflowApprovalOwnerStore
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddTenantContext();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<TContext>((sp, o) => o.UseNpgsql(app).AddInterceptors(
            sp.GetRequiredService<AuditSaveChangesInterceptor>(),
            sp.GetRequiredService<TenantIdStampInterceptor>(),
            sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
        services.AddSingleton<IWorkflowApprovalClient>(engine);
        services.AddSingleton<IPublishEndpoint>(Mock.Of<IPublishEndpoint>());
        owner(services);
        services.AddScoped<TStore>();
        services.AddSingleton<IOptionsMonitor<WorkflowApprovalSweepOptions>>(new SweepOptions());
        services.AddSingleton<WorkflowApprovalSweepJob<TStore>>();
        return services.BuildServiceProvider();
    }

    /// <summary>As the controller saves it: as the app role, inside the tenant, with the real interceptors, so the audit row is real.</summary>
    private static async Task<T> SaveAsAppAsync<TContext, T>(ServiceProvider provider, Guid tenant, Func<TContext, T> add)
        where TContext : DbContext
    {
        TenantIdFlow.Set(tenant);
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var entity = add(db);
            await db.SaveChangesAsync();
            return entity;
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_discipline_sweep_applies_finished_outcomes_in_both_tenants_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app) = await ForcedAsync(await ComplianceDatabaseAsync("sweep"));
        Assert.False(await ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.True(await ScalarAsync<bool>(admin, """SELECT relforcerowsecurity FROM pg_class WHERE oid = '"DisciplinaryActions"'::regclass"""));

        var engine = new FakeWorkflowApprovalClient();
        await using var provider = SweepProvider<ComplianceDbContext, DisciplinaryApprovalStore>(app, engine, s => s.AddScoped<DisciplinaryApprovalService>());
        var actions = new List<DisciplinaryAction>();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            actions.Add(await SaveAsAppAsync<ComplianceDbContext, DisciplinaryAction>(provider, tenant, db =>
            {
                var action = new DisciplinaryAction
                {
                    TenantId = tenant, EmployeeId = Guid.NewGuid(), IssuedAt = DateTime.UtcNow, CreatedBy = Creator,
                    ApprovalStatus = DisciplinaryApprovalStatus.PendingApproval, WorkflowInstanceId = Guid.NewGuid(), SubmissionNo = 1,
                    SubmittedBy = Creator, SubmittedAt = DateTime.UtcNow.AddMinutes(-30)
                };
                db.Add(action);
                return action;
            }));
        }

        engine.SetStatus(actions[0].WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved, "DisciplinaryAction", actions[0].Id, decisions: TwoSteps);
        engine.SetStatus(actions[1].WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Rejected, "DisciplinaryAction", actions[1].Id, reason: "ناقص",
            decisions: [TwoSteps[0], new(2, "hr-management-approver", Authority, "Rejected", DateTime.UtcNow)]);

        // A session with no tenant sees no action at all.
        Assert.Equal(0L, await ScalarAsync<long>(app, """SELECT count(*) FROM "DisciplinaryActions" """));

        await provider.GetRequiredService<WorkflowApprovalSweepJob<DisciplinaryApprovalStore>>().RunOnceAsync();

        Assert.Equal((int)DisciplinaryApprovalStatus.Approved, await ScalarAsync<int>(admin, $"""SELECT "ApprovalStatus" FROM "DisciplinaryActions" WHERE "Id" = '{actions[0].Id:D}'"""));
        Assert.Equal((int)DisciplinaryApprovalStatus.Rejected, await ScalarAsync<int>(admin, $"""SELECT "ApprovalStatus" FROM "DisciplinaryActions" WHERE "Id" = '{actions[1].Id:D}'"""));
        Assert.Equal("ناقص", await ScalarAsync<string>(admin, $"""SELECT "RejectionReason" FROM "DisciplinaryActions" WHERE "Id" = '{actions[1].Id:D}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_card_sweep_applies_finished_outcomes_in_both_tenants_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var created = await fixture.Postgres.CreateDatabaseAsync($"wf046i_{Guid.NewGuid():N}");
        await using (var db = new IDCardDbContext(new DbContextOptionsBuilder<IDCardDbContext>().UseNpgsql(created).Options))
            await db.Database.MigrateAsync();
        var (admin, app) = await ForcedAsync(created);
        Assert.True(await ScalarAsync<bool>(admin, """SELECT relforcerowsecurity FROM pg_class WHERE oid = '"CardRequests"'::regclass"""));

        var engine = new FakeWorkflowApprovalClient();
        await using var provider = SweepProvider<IDCardDbContext, CardRequestApprovalStore>(app, engine, s => s.AddScoped<CardRequestApprovalService>());
        var requests = new List<CardRequest>();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            requests.Add(await SaveAsAppAsync<IDCardDbContext, CardRequest>(provider, tenant, db =>
            {
                var request = new CardRequest
                {
                    TenantId = tenant, EmployeeId = Guid.NewGuid(), RequestDate = DateOnly.FromDateTime(DateTime.UtcNow), RequestedBy = Creator,
                    Status = CardRequestStatus.PendingApproval, WorkflowInstanceId = Guid.NewGuid(), SubmissionNo = 1, SubmittedBy = Creator,
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-30)
                };
                db.Add(request);
                return request;
            }));
        }

        foreach (var request in requests)
            engine.SetStatus(request.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved, "CardRequest", request.Id,
                decisions: [new(1, "idcard", Checker, "Approved", DateTime.UtcNow)]);

        Assert.Equal(0L, await ScalarAsync<long>(app, """SELECT count(*) FROM "CardRequests" """));

        await provider.GetRequiredService<WorkflowApprovalSweepJob<CardRequestApprovalStore>>().RunOnceAsync();

        Assert.Equal(2L, await ScalarAsync<long>(admin, $"""SELECT count(*) FROM "CardRequests" WHERE "Status" = {(int)CardRequestStatus.Approved}"""));
        Assert.Equal(2, engine.StatusReads.Count);
    }

    private sealed class SweepOptions : IOptionsMonitor<WorkflowApprovalSweepOptions>
    {
        // One record per page: the keyset on (SubmittedAt, Id) runs on PostgreSQL for every tenant.
        public WorkflowApprovalSweepOptions CurrentValue { get; } = new() { StaleAfterMinutes = 15, BatchSize = 1 };

        public WorkflowApprovalSweepOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<WorkflowApprovalSweepOptions, string?> listener) => null;
    }
}
