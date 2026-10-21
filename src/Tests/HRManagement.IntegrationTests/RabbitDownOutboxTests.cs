using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-05 AC-17: outbox survives Rabbit broker stop then drains once.</summary>
[Collection("PgRabbit")]
public class RabbitDownOutboxTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly PgRabbitFixture _fixture;

    public RabbitDownOutboxTests(PgRabbitFixture fixture) => _fixture = fixture;

    public static bool ChaosEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_CHAOS"), "true", StringComparison.OrdinalIgnoreCase)
        || RealInfra.IsEnabled;

    [Trait("Category", "Chaos")]
    [SkippableFact]
    public async Task RabbitDown_OutboxDrains_Once_After_Broker_Restart()
    {
        Skip.IfNot(ChaosEnabled, "Set RUN_CHAOS=true or RUN_TESTCONTAINERS=true");

        await using var postgres = new PostgreSqlBuilder(RealInfra.PostgresImage).Build();
        await postgres.StartAsync();
        var cs = postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options;

        await using (var migrate = new PayrollDbContext(options, new FixedTenant(TenantId)))
            await migrate.Database.MigrateAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = _fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = _fixture.Rabbit.Port.ToString(),
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

        await _fixture.Rabbit.Container.StopAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                TenantId = TenantId,
                Key = "chaos-outbox-" + Guid.NewGuid().ToString("N"),
                RequestHash = "h",
                ResponseStatus = 200,
                ResponseBody = "{}"
            });
            await publish.Publish(new RabbitDownProbeEvent(Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        long outboxDuring;
        await using (var verify = new PayrollDbContext(options, new FixedTenant(TenantId)))
        {
            await using var conn = verify.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT COUNT(*) FROM "OutboxMessage" """;
            outboxDuring = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        Assert.True(outboxDuring >= 1, "Expected outbox rows while Rabbit is down");

        await _fixture.Rabbit.Container.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));
        await bus.StartAsync();

        var drained = false;
        for (var i = 0; i < 30; i++)
        {
            await using var verify = new PayrollDbContext(options, new FixedTenant(TenantId));
            await using var conn = verify.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT COUNT(*) FROM "OutboxMessage" """;
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            if (count == 0)
            {
                drained = true;
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.True(drained, "Outbox did not drain after Rabbit restart");
        await bus.StopAsync();
    }

    public sealed record RabbitDownProbeEvent(Guid Id);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
