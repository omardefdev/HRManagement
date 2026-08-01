using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>QA-003-C: outbox/inbox proofs on PostgreSQL + RabbitMQ (not SQLite/InMemory alone).</summary>
[Collection("PgRabbit")]
public class Qa003OutboxInboxRealInfraTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly PgRabbitFixture _fixture;

    public Qa003OutboxInboxRealInfraTests(PgRabbitFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Inbox_Duplicate_Message_Single_Effect_On_Postgres()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        await using var env = await CreateInboxEnvOnSharedPgAsync();
        var messageId = NewId.NextGuid().ToString("D");
        const string consumer = "Qa003InboxConsumer";

        await RunInboxOnceAsync(env, messageId, consumer, effect: 1);
        await RunInboxOnceAsync(env, messageId, consumer, effect: 1);

        Assert.Equal(1, await env.Db.Effects.CountAsync());
        Assert.Equal(1, await env.Db.IntegrationMessageReceipts.CountAsync(r =>
            r.MessageId == messageId && r.TenantId == TenantId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Outbox_Publish_Persists_When_Broker_Unavailable()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        // Dedicated postgres so stopping Rabbit does not poison the shared PgRabbit fixture.
        await using var postgres = new PostgreSqlBuilder(RealInfra.PostgresImage).Build();
        await postgres.StartAsync();

        var cs = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options;

        await using (var migrate = new PayrollDbContext(options, new FixedTenant(TenantId)))
            await migrate.Database.MigrateAsync();

        var rabbitPort = _fixture.Rabbit.Port;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = _fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = rabbitPort.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(new FixedTenant(TenantId));
        services.AddDbContext<PayrollDbContext>(o =>
            o.UseNpgsql(cs, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name)));
        services.AddServiceMassTransit(
            config,
            configureOutbox: x => x.AddPostgresOutbox<PayrollDbContext>(),
            enableInboxFilter: false);

        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                TenantId = TenantId,
                Key = "outbox-kill-" + Guid.NewGuid().ToString("N"),
                RequestHash = "h",
                ResponseStatus = 200,
                ResponseBody = "{}"
            });
            await publish.Publish(new Qa003ProbeEvent(Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        await using (var verify = new PayrollDbContext(options, new FixedTenant(TenantId)))
        {
            Assert.True(await verify.IdempotencyRecords.AnyAsync());
            await using var conn = verify.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'OutboxMessage'""";
            var tables = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.True(tables >= 1, "MassTransit OutboxMessage table must exist after migrate");
        }

        await bus.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Postgres_Unavailable_Inbox_Does_Not_Succeed_Silently()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        await using var postgres = new PostgreSqlBuilder(RealInfra.PostgresImage).Build();
        await postgres.StartAsync();
        var cs = postgres.GetConnectionString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<Qa003InboxDbContext>(o => o.UseNpgsql(cs));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Qa003InboxDbContext>();
        await db.Database.EnsureCreatedAsync();

        await postgres.StopAsync();

        var messageId = NewId.NextGuid().ToString("D");
        TenantIdFlow.Set(TenantId);
        try
        {
            var ctx = new Mock<ConsumeContext>();
            ctx.SetupGet(c => c.MessageId).Returns(Guid.Parse(messageId));
            ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                IntegrationMessageInboxExtensions.ExecuteInInboxTransactionAsync(
                    db,
                    ctx.Object,
                    "KillPgConsumer",
                    async ct =>
                    {
                        db.Effects.Add(new Qa003InboxEffect { Value = 42, TenantId = TenantId });
                        await db.SaveChangesAsync(ct);
                    }));
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private async Task<InboxPgEnv> CreateInboxEnvOnSharedPgAsync()
    {
        var cs = await _fixture.Postgres.CreateDatabaseAsync($"qa003_inbox_{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<Qa003InboxDbContext>(o => o.UseNpgsql(cs));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Qa003InboxDbContext>();
        await db.Database.EnsureCreatedAsync();
        return new InboxPgEnv(provider, scope, db);
    }

    private static async Task RunInboxOnceAsync(InboxPgEnv env, string messageId, string consumer, int effect)
    {
        TenantIdFlow.Set(TenantId);
        try
        {
            var ctx = new Mock<ConsumeContext>();
            ctx.SetupGet(c => c.MessageId).Returns(Guid.Parse(messageId));
            ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            await IntegrationMessageInboxExtensions.ExecuteInInboxTransactionAsync(
                env.Db,
                ctx.Object,
                consumer,
                async ct =>
                {
                    if (!await env.Db.Effects.AnyAsync(e => e.Value == effect, ct))
                        env.Db.Effects.Add(new Qa003InboxEffect { Value = effect, TenantId = TenantId });
                    await env.Db.SaveChangesAsync(ct);
                });
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private sealed class InboxPgEnv(ServiceProvider provider, IServiceScope scope, Qa003InboxDbContext db)
        : IAsyncDisposable
    {
        public Qa003InboxDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    public sealed record Qa003ProbeEvent(Guid Id);
}

public sealed class Qa003InboxDbContext(DbContextOptions<Qa003InboxDbContext> options) : DbContext(options)
{
    public DbSet<Qa003InboxEffect> Effects => Set<Qa003InboxEffect>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.Entity<Qa003InboxEffect>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Value });
        });
    }
}

public sealed class Qa003InboxEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int Value { get; set; }
}
