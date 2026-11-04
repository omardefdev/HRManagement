using System.Collections.Concurrent;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-40 (P-1, §4 rules 1-3) on PostgreSQL + RabbitMQ: the inbox in join mode and the consumer outbox share one
/// transaction. No "already an active transaction", a message leaves only after its commit, a failed commit leaves nothing
/// on the broker, a duplicate gives one effect, and a failed attempt leaves no receipt behind that would skip the retry.
/// Party acknowledgements (D-40-6) run through the same path.
/// </summary>
[Collection("PgRabbit")]
public sealed class Msg040OutboxOrderingIntegrationTests(PgRabbitFixture fixture)
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(4);

    public sealed record Msg040Work(Guid RunId);

    public sealed record Msg040Done(Guid RunId);

    public sealed class Msg040Effect
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RunId { get; set; }
    }

    public sealed class Msg040OrderingDbContext(DbContextOptions<Msg040OrderingDbContext> options) : DbContext(options)
    {
        public DbSet<Msg040Effect> Effects => Set<Msg040Effect>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMassTransitOutbox();
            modelBuilder.ConfigureIntegrationMessageReceipts();
            modelBuilder.Entity<Msg040Effect>().HasIndex(e => e.RunId);
        }
    }

    /// <summary>What the consumers do and what they saw, shared with the test.</summary>
    public sealed class Script
    {
        public int FailAttempts { get; set; }
        public bool AlwaysTransient { get; set; }
        public bool BusinessFailure { get; set; }
        public bool DatabaseFailure { get; set; }
        public bool FailCommit { get; set; }
        public int Attempts;
        public readonly ConcurrentBag<Guid> Done = [];
        public readonly ConcurrentBag<string> PartyOutcomes = [];
        public Func<Task<long>>? ReadCommittedEffects { get; set; }
        public readonly ConcurrentBag<long> CommittedEffectsWhenDone = [];
    }

    private sealed class FailingCommit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            Armed = false;
            throw new NotSupportedException("injected commit failure");
        }
    }

    public sealed class WorkConsumer(Msg040OrderingDbContext db, Script script, IServiceProvider services) : IConsumer<Msg040Work>
    {
        public async Task Consume(ConsumeContext<Msg040Work> context)
        {
            var attempt = Interlocked.Increment(ref script.Attempts);
            db.Effects.Add(new Msg040Effect { RunId = context.Message.RunId });
            await db.SaveChangesAsync(context.CancellationToken);
            await context.Publish(new Msg040Done(context.Message.RunId));

            if (attempt <= script.FailAttempts)
                throw new TimeoutException("transient failure after the publish");
            if (script.FailCommit)
                services.GetRequiredService<FailingCommitHolder>().Interceptor.Armed = true;
        }
    }

    private sealed class FailingCommitHolder(FailingCommit interceptor)
    {
        public FailingCommit Interceptor { get; } = interceptor;
    }

    public sealed class DoneConsumer(Script script) : IConsumer<Msg040Done>
    {
        public async Task Consume(ConsumeContext<Msg040Done> context)
        {
            if (script.ReadCommittedEffects is not null)
                script.CommittedEffectsWhenDone.Add(await script.ReadCommittedEffects());
            script.Done.Add(context.Message.RunId);
        }
    }

    public sealed class PartyConsumer(Msg040OrderingDbContext db, Script script) : SeparationPartyAckConsumerBase("msg040")
    {
        protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)
        {
            Interlocked.Increment(ref script.Attempts);
            db.Effects.Add(new Msg040Effect { RunId = context.Message.SeparationRecordId });
            await db.SaveChangesAsync(context.CancellationToken);

            if (script.DatabaseFailure)
                await db.Database.ExecuteSqlRawAsync("""SELECT 1/0""", context.CancellationToken);
            if (script.AlwaysTransient)
                throw new TimeoutException("party unreachable");
            if (script.BusinessFailure)
                throw new InvalidOperationException("party refused");
            return "msg040:ok";
        }
    }

    public sealed class PartyListener(Script script) : IConsumer<ISeparationPartyCompleted>, IConsumer<ISeparationPartyFaulted>
    {
        public Task Consume(ConsumeContext<ISeparationPartyCompleted> context)
        {
            script.PartyOutcomes.Add("completed");
            return Task.CompletedTask;
        }

        public Task Consume(ConsumeContext<ISeparationPartyFaulted> context)
        {
            script.PartyOutcomes.Add("faulted");
            return Task.CompletedTask;
        }
    }

    private sealed class Env(ServiceProvider provider, IBusControl bus, Script script, string connectionString) : IAsyncDisposable
    {
        public Script Script { get; } = script;
        public IBusControl Bus { get; } = bus;
        public string ConnectionString { get; } = connectionString;

        public async Task PublishAsync<T>(object message, Guid messageId) where T : class
        {
            void Stamp(PublishContext<T> ctx)
            {
                ctx.MessageId = messageId;
                ctx.Headers.Set(TenantIdMiddleware.HeaderName, Tenant.ToString("D"));
            }

            if (message is T typed)
                await Bus.Publish(typed, Stamp);
            else
                await Bus.Publish<T>(message, Stamp);
        }

        public async Task<long> CountAsync(string sql)
        {
            await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        public Task<long> EffectsAsync() => CountAsync("""SELECT COUNT(*) FROM "Effects" """);

        public Task<long> ReceiptsAsync(Guid messageId) =>
            CountAsync($"""SELECT COUNT(*) FROM "IntegrationMessageReceipts" WHERE "MessageId" = '{messageId:D}' """);

        public async ValueTask DisposeAsync()
        {
            await Bus.StopAsync();
            await provider.DisposeAsync();
        }
    }

    private async Task<Env> StartAsync(int retryCount, Script? script = null)
    {
        var cs = await fixture.Postgres.CreateDatabaseAsync($"msg040_ord_{Guid.NewGuid():N}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = retryCount.ToString(),
            ["MassTransit:RetryIntervalSeconds"] = "1",
            ["MassTransit:RetryUseJitter"] = "false",
            ["MassTransit:EndpointPrefix"] = $"msg040-{Guid.NewGuid():N}"
        }).Build();

        script ??= new Script();
        var failing = new FailingCommit();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(script);
        services.AddSingleton(new FailingCommitHolder(failing));
        services.AddDbContext<Msg040OrderingDbContext>(o => o.UseNpgsql(cs).AddInterceptors(failing));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<Msg040OrderingDbContext>());
        services.AddServiceMassTransit(
            config,
            configureConsumers: x =>
            {
                x.AddConsumer<WorkConsumer>();
                x.AddConsumer<DoneConsumer>();
                x.AddConsumer<PartyConsumer>();
                x.AddConsumer<PartyListener>();
            },
            configureOutbox: x => x.AddPostgresOutbox<Msg040OrderingDbContext>(consumerOutbox: true));

        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Msg040OrderingDbContext>().Database.EnsureCreatedAsync();

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        return new Env(provider, bus, script, cs);
    }

    private static async Task WaitForAsync(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
            await Task.Delay(100);
    }

    private static object PartyMessage(Guid recordId) => new
    {
        SeparationRecordId = recordId,
        EmployeeId = Guid.NewGuid(),
        Party = "msg040",
        SeparationType = "Resignation",
        EffectiveDate = DateTime.UtcNow,
        Compensate = false,
        CorrelationId = Guid.NewGuid().ToString("D"),
        TenantId = Tenant
    };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Consumer_publish_is_sent_after_the_commit_with_the_receipt_in_the_same_transaction()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 0);
        env.Script.ReadCommittedEffects = env.EffectsAsync;
        var run = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await env.PublishAsync<Msg040Work>(new Msg040Work(run), messageId);
        await WaitForAsync(() => !env.Script.Done.IsEmpty, TimeSpan.FromSeconds(30));

        Assert.Equal([run], env.Script.Done);
        // The reply reached its consumer only once the work was committed and visible to another connection.
        Assert.Equal([1L], env.Script.CommittedEffectsWhenDone);
        Assert.Equal(1, await env.EffectsAsync());
        Assert.Equal(1, await env.ReceiptsAsync(messageId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Duplicate_message_gives_one_effect_and_one_reply_even_after_the_inbox_state_is_gone()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 0);
        var run = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await env.PublishAsync<Msg040Work>(new Msg040Work(run), messageId);
        await env.PublishAsync<Msg040Work>(new Msg040Work(run), messageId);
        await WaitForAsync(() => !env.Script.Done.IsEmpty, TimeSpan.FromSeconds(30));
        await Task.Delay(Settle);

        // MassTransit forgets InboxState after its window; the receipt still refuses the message.
        await env.CountAsync("""WITH d AS (DELETE FROM "InboxState" RETURNING 1) SELECT COUNT(*) FROM d""");
        await env.PublishAsync<Msg040Work>(new Msg040Work(run), messageId);
        await Task.Delay(Settle);

        Assert.Single(env.Script.Done);
        Assert.Equal(1, await env.EffectsAsync());
        Assert.Equal(1, await env.ReceiptsAsync(messageId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Failed_commit_leaves_no_message_on_the_broker_no_state_and_no_receipt()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 0, new Script { FailCommit = true });
        var messageId = Guid.NewGuid();

        await env.PublishAsync<Msg040Work>(new Msg040Work(Guid.NewGuid()), messageId);
        await WaitForAsync(() => env.Script.Attempts > 0, TimeSpan.FromSeconds(30));
        await Task.Delay(Settle);

        Assert.Empty(env.Script.Done);
        Assert.Equal(0, await env.EffectsAsync());
        Assert.Equal(0, await env.ReceiptsAsync(messageId));
        Assert.Equal(0, await env.CountAsync("""SELECT COUNT(*) FROM "OutboxMessage" """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Failed_attempt_leaves_no_receipt_so_the_retry_runs_and_one_reply_leaves()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 2, new Script { FailAttempts = 1 });
        var messageId = Guid.NewGuid();

        await env.PublishAsync<Msg040Work>(new Msg040Work(Guid.NewGuid()), messageId);
        await WaitForAsync(() => !env.Script.Done.IsEmpty, TimeSpan.FromSeconds(30));
        await Task.Delay(Settle);

        Assert.Equal(2, env.Script.Attempts);
        Assert.Single(env.Script.Done);
        Assert.Equal(1, await env.EffectsAsync());
        Assert.Equal(1, await env.ReceiptsAsync(messageId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Party_business_failure_publishes_one_fault_undoes_the_work_and_commits_the_receipt(bool databaseFailure)
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 2, new Script { BusinessFailure = !databaseFailure, DatabaseFailure = databaseFailure });
        var messageId = Guid.NewGuid();

        await env.PublishAsync<IExecuteSeparationParty>(PartyMessage(Guid.NewGuid()), messageId);
        await WaitForAsync(() => !env.Script.PartyOutcomes.IsEmpty, TimeSpan.FromSeconds(30));
        await Task.Delay(Settle);

        Assert.Equal(1, env.Script.Attempts);
        Assert.Equal(["faulted"], env.Script.PartyOutcomes);
        Assert.Equal(0, await env.EffectsAsync());
        Assert.Equal(1, await env.ReceiptsAsync(messageId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Party_transient_failure_retries_silently_and_faults_once_on_the_last_attempt()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 2, new Script { AlwaysTransient = true });
        var messageId = Guid.NewGuid();

        await env.PublishAsync<IExecuteSeparationParty>(PartyMessage(Guid.NewGuid()), messageId);
        await WaitForAsync(() => !env.Script.PartyOutcomes.IsEmpty, TimeSpan.FromSeconds(30));
        await Task.Delay(Settle);

        Assert.Equal(3, env.Script.Attempts);
        Assert.Equal(["faulted"], env.Script.PartyOutcomes);
        Assert.Equal(0, await env.EffectsAsync());
        Assert.Equal(1, await env.ReceiptsAsync(messageId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Party_success_publishes_completed_with_the_work_committed()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var env = await StartAsync(retryCount: 2);
        var messageId = Guid.NewGuid();

        await env.PublishAsync<IExecuteSeparationParty>(PartyMessage(Guid.NewGuid()), messageId);
        await WaitForAsync(() => !env.Script.PartyOutcomes.IsEmpty, TimeSpan.FromSeconds(30));

        Assert.Equal(["completed"], env.Script.PartyOutcomes);
        Assert.Equal(1, await env.EffectsAsync());
        Assert.Equal(1, await env.ReceiptsAsync(messageId));
    }
}
