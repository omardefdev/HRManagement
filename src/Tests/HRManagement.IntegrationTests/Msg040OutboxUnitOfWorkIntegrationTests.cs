using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-40 (§3 rule 2) on PostgreSQL + RabbitMQ: a failure on the last save leaves neither the state nor the message,
/// for "change, publish, one save" and for <see cref="OutboxUnitOfWork"/> (intermediate save and ExecuteUpdate included).
/// The state row is an <see cref="IdempotencyRecord"/> of the migrated Payroll database, which has the real outbox tables.
/// Rows are read through the OutboxMessage sequence as well, because delivered rows are deleted.
/// </summary>
[Collection("PgRabbit")]
public sealed class Msg040OutboxUnitOfWorkIntegrationTests(PgRabbitFixture fixture)
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public sealed record Msg040UowProbe(Guid Id);

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed ? throw new InvalidOperationException("injected failure on the last save") : ValueTask.FromResult(result);
    }

    private static IdempotencyRecord State(string key) => new()
    {
        TenantId = TenantId, Key = key, RequestHash = "h", ResponseStatus = 200, ResponseBody = "{}"
    };

    private async Task<(ServiceProvider Provider, IBusControl Bus, FailingSave Failure, DbContextOptions<PayrollDbContext> Options)> StartAsync()
    {
        var cs = await fixture.Postgres.CreateDatabaseAsync($"msg040_uow_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options;
        await using (var migrate = new PayrollDbContext(options, new Msg040FixedTenant(TenantId)))
            await migrate.Database.MigrateAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0"
        }).Build();

        var failure = new FailingSave();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(new Msg040FixedTenant(TenantId));
        services.AddDbContext<PayrollDbContext>(o => o
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .AddInterceptors(failure));
        services.AddServiceMassTransit(config, configureOutbox: x => x.AddPostgresOutbox<PayrollDbContext>(), enableInboxFilter: false);

        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        return (provider, bus, failure, options);
    }

    private static async Task<long> OutboxRowsEverWrittenAsync(DbContextOptions<PayrollDbContext> options)
    {
        await using var db = new PayrollDbContext(options, new Msg040FixedTenant(TenantId));
        await using var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "OutboxMessage_SequenceNumber_seq" """;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> StoredStatusAsync(DbContextOptions<PayrollDbContext> options, string key)
    {
        await using var db = new PayrollDbContext(options, new Msg040FixedTenant(TenantId));
        return await db.IdempotencyRecords.IgnoreQueryFilters().Where(r => r.Key == key).Select(r => r.ResponseStatus).FirstOrDefaultAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Single_save_failure_leaves_neither_state_nor_message()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (provider, bus, failure, options) = await StartAsync();
        await using var _ = provider;
        var key = "single-" + Guid.NewGuid().ToString("N");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            db.IdempotencyRecords.Add(State(key));
            await publish.Publish(new Msg040UowProbe(Guid.NewGuid()));
            failure.Armed = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        failure.Armed = false;
        Assert.Equal(0, await StoredStatusAsync(options, key));
        Assert.Equal(0, await OutboxRowsEverWrittenAsync(options));
        await bus.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Unit_of_work_failure_on_the_last_save_rolls_back_the_intermediate_save_and_ExecuteUpdate()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (provider, bus, failure, options) = await StartAsync();
        await using var _ = provider;
        var existing = "existing-" + Guid.NewGuid().ToString("N");
        var added = "added-" + Guid.NewGuid().ToString("N");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            db.IdempotencyRecords.Add(State(existing));
            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => OutboxUnitOfWork.ExecuteAsync(db, async ct =>
            {
                await db.IdempotencyRecords.IgnoreQueryFilters().Where(r => r.Key == existing)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResponseStatus, 500), ct);
                db.IdempotencyRecords.Add(State(added));
                await db.SaveChangesAsync(ct);
                await publish.Publish(new Msg040UowProbe(Guid.NewGuid()), ct);
                failure.Armed = true;
            }));
        }

        failure.Armed = false;
        Assert.Equal(200, await StoredStatusAsync(options, existing));
        Assert.Equal(0, await StoredStatusAsync(options, added));
        Assert.Equal(0, await OutboxRowsEverWrittenAsync(options));
        await bus.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Unit_of_work_commits_state_and_outbox_row_together()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (provider, bus, _, options) = await StartAsync();
        await using var disposeProvider = provider;
        var key = "ok-" + Guid.NewGuid().ToString("N");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var result = await OutboxUnitOfWork.ExecuteAsync(db, async ct =>
            {
                db.IdempotencyRecords.Add(State(key));
                await db.SaveChangesAsync(ct);
                await publish.Publish(new Msg040UowProbe(Guid.NewGuid()), ct);
                return 7;
            });
            Assert.Equal(7, result);
        }

        Assert.Equal(200, await StoredStatusAsync(options, key));
        Assert.Equal(1, await OutboxRowsEverWrittenAsync(options));
        await bus.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Unit_of_work_joins_an_ambient_transaction_and_leaves_the_commit_to_its_owner()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (provider, bus, _, options) = await StartAsync();
        await using var disposeProvider = provider;
        var key = "ambient-" + Guid.NewGuid().ToString("N");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            await using var ambient = await db.Database.BeginTransactionAsync();
            await OutboxUnitOfWork.ExecuteAsync(db, async ct =>
            {
                db.IdempotencyRecords.Add(State(key));
                await publish.Publish(new Msg040UowProbe(Guid.NewGuid()), ct);
            });
            Assert.Same(ambient, db.Database.CurrentTransaction);
            await ambient.RollbackAsync();
        }

        Assert.Equal(0, await StoredStatusAsync(options, key));
        // A sequence does not roll back, so here the rows themselves are counted: nothing committed, nothing delivered.
        await using var verify = new PayrollDbContext(options, new Msg040FixedTenant(TenantId));
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "OutboxMessage" """).SingleAsync());
        await bus.StopAsync();
    }
}

internal sealed class Msg040FixedTenant(Guid tenantId) : ITenantContext
{
    public Guid? TenantId => tenantId;
    public bool IsPlatformOperator => false;
    public Guid RequireTenantId() => tenantId;
}
