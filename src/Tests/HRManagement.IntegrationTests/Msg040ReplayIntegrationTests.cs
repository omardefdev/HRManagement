using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Infrastructure.Messaging;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-40 (§6) on a disposable PostgreSQL database with the real migrations: a "before the fix" promotion is listed by the
/// dry run, the apply writes its OutboxMessage with the deterministic id, and the second run lists zero.
/// </summary>
[Collection("PgRabbit")]
public sealed class Msg040ReplayIntegrationTests(PgRabbitFixture fixture)
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Dry_run_then_apply_then_zero_on_a_disposable_database()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var cs = await fixture.Postgres.CreateDatabaseAsync($"msg040_replay_{Guid.NewGuid():N}");
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

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(new Msg040FixedTenant(TenantId));
        services.AddDbContext<MovementsDbContext>(o => o.UseNpgsql(cs));
        services.AddServiceMassTransit(config, configureOutbox: x => x.AddPostgresOutbox<MovementsDbContext>(), enableInboxFilter: false);
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();

        var promotion = new PromotionRequest
        {
            TenantId = TenantId,
            EmployeeId = Guid.NewGuid(),
            FromGrade = 5,
            ToGrade = 4,
            FromStep = 3,
            ToStep = 1,
            TransitionDecisionRef = "DEC-40",
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PromotionRequestStatus.Executed
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MovementsDbContext>();
            await db.Database.MigrateAsync();
            db.PromotionRequests.Add(promotion);
            db.MovementExecutionRecords.Add(new MovementExecutionRecord
            {
                TenantId = TenantId,
                PromotionRequestId = promotion.Id,
                EmployeeId = promotion.EmployeeId,
                ExecutedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        async Task<T> InScope<T>(Func<MovementsDbContext, MovementsMessagingRepair, Task<T>> action)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MovementsDbContext>();
            return await action(db, new MovementsMessagingRepair(db, scope.ServiceProvider.GetRequiredService<IPublishEndpoint>()));
        }

        var dry = await InScope((_, repair) => repair.ReplayPromotionsAsync(dryRun: true, null));
        Assert.Equal([promotion.Id], dry.Ids);
        Assert.Equal(0, await InScope((db, _) => db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "OutboxMessage" """).SingleAsync()));

        var applied = await InScope((_, repair) => repair.ReplayPromotionsAsync(dryRun: false, null));
        Assert.Equal(1, applied.Count);

        // No hosted delivery service runs here, so the committed row is still in the table.
        var stored = await InScope((db, _) => db.Database.SqlQueryRaw<Guid>("""SELECT "MessageId" AS "Value" FROM "OutboxMessage" """).SingleAsync());
        Assert.Equal(MovementExecutedEvents.PromotionMessageId(promotion.Id), stored);

        Assert.Equal(0, (await InScope((_, repair) => repair.ReplayPromotionsAsync(dryRun: true, null))).Count);
        await bus.StopAsync();
    }
}
