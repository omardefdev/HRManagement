using System.Net;
using System.Net.Sockets;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-05 AC-17: outbox survives Rabbit broker stop then drains once.</summary>
public class RabbitDownOutboxTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static bool ChaosEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_CHAOS"), "true", StringComparison.OrdinalIgnoreCase)
        || RealInfra.IsEnabled;

    [Trait("Category", "Chaos")]
    [SkippableFact]
    public async Task RabbitDown_OutboxDrains_Once_After_Broker_Restart()
    {
        Skip.IfNot(ChaosEnabled, "Set RUN_CHAOS=true or RUN_TESTCONTAINERS=true");

        // A broker of its own on a fixed host port: restarting a container gives it a new random port otherwise, and the
        // bus (which keeps the old one) could never reconnect, and the shared fixture would be left with a stale port.
        var brokerPort = FreeTcpPort();
        await using var rabbit = new RabbitMqBuilder(RealInfra.RabbitImage)
            .WithUsername(RabbitMqFixture.Username)
            .WithPassword(RabbitMqFixture.Password)
            .WithPortBinding(brokerPort, RabbitMqBuilder.RabbitMqPort)
            .Build();
        await rabbit.StartAsync();

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
            ["RabbitMQ:Host"] = rabbit.Hostname,
            ["RabbitMQ:Port"] = brokerPort.ToString(),
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

        await rabbit.StopAsync();

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

        await rabbit.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));
        await bus.StartAsync();

        // The bus outbox is drained by a hosted service, which nothing starts in this host-less test.
        var delivery = provider.GetServices<IHostedService>()
            .Single(h => h.GetType().Name.StartsWith("BusOutboxDeliveryService", StringComparison.Ordinal));
        await delivery.StartAsync(CancellationToken.None);

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
        await delivery.StopAsync(CancellationToken.None);
        await bus.StopAsync();
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public sealed record RabbitDownProbeEvent(Guid Id);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
