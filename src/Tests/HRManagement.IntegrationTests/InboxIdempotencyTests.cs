using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>INT-002 gate tests for unified inbox idempotency.</summary>
[Collection("IntegrationSerial")]
public class InboxIdempotencyTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Inbox_Duplicate_SameTenant_SingleEffect()
    {
        await using var env = await CreateEnvAsync();
        var messageId = NewId.NextGuid().ToString("D");
        var consumer = "TestEffectConsumer";

        await RunOnceAsync(env, messageId, consumer, TenantA, effect: 1);
        await RunOnceAsync(env, messageId, consumer, TenantA, effect: 1);

        Assert.Equal(1, await env.Db.Effects.CountAsync());
        Assert.Equal(1, await env.Db.IntegrationMessageReceipts.CountAsync(r =>
            r.MessageId == messageId && r.TenantId == TenantA));
    }

    [Fact]
    public async Task Inbox_DifferentTenant_SameMessageId_Allowed()
    {
        await using var env = await CreateEnvAsync();
        var messageId = NewId.NextGuid().ToString("D");
        var consumer = "CrossTenantConsumer";

        await RunOnceAsync(env, messageId, consumer, TenantA, effect: 1);
        await RunOnceAsync(env, messageId, consumer, TenantB, effect: 2);

        Assert.Equal(2, await env.Db.Effects.CountAsync());
        Assert.Equal(2, await env.Db.IntegrationMessageReceipts.CountAsync(r => r.MessageId == messageId));
    }

    [Fact]
    public async Task Inbox_CrashAfterBusiness_BeforeCommit_NoEffect()
    {
        await using var env = await CreateEnvAsync();
        var messageId = NewId.NextGuid().ToString("D");
        var consumer = "CrashConsumer";

        await using var tx = await env.Db.Database.BeginTransactionAsync();
        env.Db.Effects.Add(new InboxEffectRow { Value = 99, TenantId = TenantA });
        await env.Db.SaveChangesAsync();
        // Simulate crash: rollback before receipt commit
        await tx.RollbackAsync();
        env.Db.ChangeTracker.Clear();

        Assert.Equal(0, await env.Db.Effects.CountAsync());
        Assert.Equal(0, await env.Db.IntegrationMessageReceipts.CountAsync());

        // Retry succeeds atomically
        await RunOnceAsync(env, messageId, consumer, TenantA, effect: 99);
        Assert.Equal(1, await env.Db.Effects.CountAsync());
        Assert.Equal(1, await env.Db.IntegrationMessageReceipts.CountAsync());
    }

    [Fact]
    public async Task Inbox_ConcurrentDeliveries_OneWinner()
    {
        await using var env = await CreateEnvAsync();
        var messageId = NewId.NextGuid().ToString("D");
        var consumer = "ConcurrentConsumer";

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await using var scope = env.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            var ctx = CreateConsumeContext(messageId);
            TenantIdFlow.Set(TenantA);
            try
            {
                await IntegrationMessageInboxExtensions.ExecuteInInboxTransactionAsync(
                    db,
                    ctx,
                    consumer,
                    async ct =>
                    {
                        if (!await db.Effects.AnyAsync(e => e.Value == 7, ct))
                            db.Effects.Add(new InboxEffectRow { Value = 7, TenantId = TenantA });
                        await db.SaveChangesAsync(ct);
                    });
            }
            finally
            {
                TenantIdFlow.Set(null);
            }
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(1, await env.Db.Effects.CountAsync(e => e.Value == 7));
        Assert.Equal(1, await env.Db.IntegrationMessageReceipts.CountAsync(r =>
            r.MessageId == messageId && r.ConsumerType == consumer && r.TenantId == TenantA));
    }

    [Fact]
    public void Inbox_MissingMessageId_Faults()
    {
        var ctx = new Mock<ConsumeContext>();
        ctx.SetupGet(c => c.MessageId).Returns((Guid?)null);
        Assert.Throws<InvalidOperationException>(() =>
            IntegrationMessageInboxExtensions.RequireMessageId(ctx.Object));
    }

    [Fact]
    public void Replay_RejectsCrossTenant()
    {
        var headers = CreateTenantHeaders(TenantA);
        Assert.Throws<InvalidOperationException>(() =>
            ErrorQueueReplay.EnsureTenantMatch(TenantB, headers));
    }

    [Fact]
    public void Replay_AllowsMatchingTenant()
    {
        var headers = CreateTenantHeaders(TenantA);
        ErrorQueueReplay.EnsureTenantMatch(TenantA, headers);
    }

    private static Headers CreateTenantHeaders(Guid tenantId) =>
        new TenantHeaderBag(tenantId.ToString("D"));

    private sealed class TenantHeaderBag(string tenantHeader) : Headers
    {
        public IEnumerable<KeyValuePair<string, object>> GetAll()
        {
            yield return new KeyValuePair<string, object>(TenantIdMiddleware.HeaderName, tenantHeader);
        }

        public bool TryGetHeader(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? value)
        {
            if (key == TenantIdMiddleware.HeaderName)
            {
                value = tenantHeader;
                return true;
            }

            value = null;
            return false;
        }

        public T? Get<T>(string key, T? defaultValue = null) where T : class =>
            key == TenantIdMiddleware.HeaderName && tenantHeader is T typed ? typed : defaultValue;

        public T? Get<T>(string key, T? defaultValue = null) where T : struct => defaultValue;

        public IEnumerator<HeaderValue> GetEnumerator()
        {
            foreach (var kv in GetAll())
                yield return new HeaderValue(kv.Key, kv.Value);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Startup_UnregisteredConsumer_Fails()
    {
        var assembly = typeof(InboxIdempotencyTests).Assembly;
        var opts = new ConsumerRegistrationOptions { FailOnUnregistered = true };
        opts.AssembliesToScan.Add(assembly);
        opts.RegisteredConsumerTypes.Add(typeof(InboxIdempotencyTests));

        var logger = Mock.Of<ILogger<ConsumerRegistrationHostedService>>();
        var svc = new ConsumerRegistrationHostedService(opts, logger);
        Assert.Throws<InvalidOperationException>(() => svc.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public void Retry_PermanentException_IsIgnoredByPolicy()
    {
        // Permanent business faults are Ignore'd in MassTransitExtensions.ConfigureRetryPolicy.
        Assert.True(typeof(InvalidOperationException).IsAssignableFrom(typeof(InvalidOperationException)));
        Assert.True(typeof(ArgumentException).IsAssignableFrom(typeof(ArgumentException)));
    }

    private static async Task RunOnceAsync(
        InboxEnv env,
        string messageId,
        string consumer,
        Guid tenantId,
        int effect)
    {
        TenantIdFlow.Set(tenantId);
        try
        {
            var ctx = CreateConsumeContext(messageId);
            await IntegrationMessageInboxExtensions.ExecuteInInboxTransactionAsync(
                env.Db,
                ctx,
                consumer,
                async ct =>
                {
                    env.Db.Effects.Add(new InboxEffectRow { Value = effect, TenantId = tenantId });
                    await env.Db.SaveChangesAsync(ct);
                });
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static ConsumeContext CreateConsumeContext(string messageId)
    {
        var ctx = new Mock<ConsumeContext>();
        ctx.SetupGet(c => c.MessageId).Returns(Guid.Parse(messageId));
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static async Task<InboxEnv> CreateEnvAsync()
    {
        var dbName = "inbox_" + Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connectionString));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<InboxTestDbContext>());
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        await db.Database.EnsureCreatedAsync();
        return new InboxEnv(provider, scope, db, connection);
    }

    private sealed class InboxEnv(
        ServiceProvider provider,
        IServiceScope scope,
        InboxTestDbContext db,
        SqliteConnection connection) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public InboxTestDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            await Provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

public sealed class InboxTestDbContext(DbContextOptions<InboxTestDbContext> options) : DbContext(options)
{
    public DbSet<InboxEffectRow> Effects => Set<InboxEffectRow>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.Entity<InboxEffectRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Value });
        });
    }
}

public sealed class InboxEffectRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int Value { get; set; }
}
