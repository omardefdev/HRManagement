using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-47 §8 on PostgreSQL + RabbitMQ: a publication stages IPolicyPublished in HR.Core's outbox in its own save (PLAN-40), and
/// two HR.Core instances (two buses, each with the service's real MassTransit set-up) both clear their policy cache: each runs the
/// consumer on its own temporary queue, outside the consumer outbox and the inbox receipts (a shared receipt would make the second
/// instance skip the message).
/// </summary>
[Collection("PgRabbit")]
public sealed class Pol047PublishOutboxPostgresTests(PgRabbitFixture fixture)
{
    private sealed record Instance(ServiceProvider Provider, IPolicyPublishedSnapshotCache Cache, IReadOnlyList<IHostedService> Hosted);

    private Instance Start(string cs, string instanceId)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "true",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0",
            ["MassTransit:EndpointPrefix"] = "hr-core-web",
            ["MassTransit:InstanceId"] = instanceId
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(new Msg040FixedTenant(TenantDefaults.DefaultTenantId));
        services.AddDbContext<HRCoreDbContext>(o => o.UseNpgsql(cs, n => n.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name)));
        services.AddMemoryCache();
        services.AddSingleton<IPolicyPublishedSnapshotCache, PolicyPublishedSnapshotCache>();
        // HR.Core's own set-up (Program.cs): every consumer of the assembly, the bus outbox and the consumer outbox.
        services.AddServiceMassTransit(config,
            configureOutbox: x => x.AddPostgresOutbox<HRCoreDbContext>(consumerOutbox: true),
            consumerAssembly: typeof(PolicyPublishedCacheInvalidationConsumer).Assembly);

        var provider = services.BuildServiceProvider();
        return new Instance(provider, provider.GetRequiredService<IPolicyPublishedSnapshotCache>(),
            provider.GetServices<IHostedService>().Where(h => h.GetType().FullName!.StartsWith("MassTransit", StringComparison.Ordinal)).ToList());
    }

    private static PolicyVersionSnapshot Cached(string domain) => new() { Domain = domain, VersionId = Guid.NewGuid() };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_publication_leaves_the_outbox_and_both_instances_clear_their_cache()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var cs = await Pol047Pg.MigratedAsync(fixture.Postgres, "outbox");
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) seeded;
        await using (var db = Pol047Pg.AdminContext(cs))
            seeded = await Pol047Pg.SeedTemplateAsync(db, TenantDefaults.DefaultTenantId, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);

        var a = Start(cs, "pol047-a");
        var b = Start(cs, "pol047-b");
        try
        {
            foreach (var hosted in a.Hosted.Concat(b.Hosted))
                await hosted.StartAsync(CancellationToken.None);

            var key = IPolicyPublishedSnapshotCache.BuildCacheKey(PolicyDomains.AssetsCustody, null);
            a.Cache.Set(key, Cached(PolicyDomains.AssetsCustody), TimeSpan.FromMinutes(5));
            b.Cache.Set(key, Cached(PolicyDomains.AssetsCustody), TimeSpan.FromMinutes(5));
            var sequenceBefore = await Pol047Pg.ScalarAsync<long>(cs, """SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "OutboxMessage_SequenceNumber_seq" """);

            // Published from instance A's request scope, through its bus outbox. The publisher's own in-process clearing is given a
            // cache of its own here, so only the consumer can clear A's and B's caches.
            await using (var scope = a.Provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                var publisher = new PolicyVersionPublisher(db, new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())),
                    scope.ServiceProvider.GetRequiredService<IPublishEndpoint>());
                var result = await Pol047Pg.PublishService(db, publisher).PublishAsync(new(seeded.Draft, Pol047Pg.Approver(Guid.NewGuid()), "EV-OUTBOX"));
                Assert.True(result.Succeeded, result.MessageAr);
            }

            // The event was written by the publication's own save.
            var sequenceAfter = await Pol047Pg.ScalarAsync<long>(cs, """SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "OutboxMessage_SequenceNumber_seq" """);
            Assert.True(sequenceAfter > sequenceBefore, "the publication wrote no outbox row");

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && (a.Cache.TryGet(key, out _) || b.Cache.TryGet(key, out _)))
                await Task.Delay(250);

            Assert.False(a.Cache.TryGet(key, out _), "instance A kept its policy cache");
            Assert.False(b.Cache.TryGet(key, out _), "instance B kept its policy cache");

            // Delivered (no pending row for its deterministic id), and no inbox receipt was taken for the cache consumer.
            var messageId = DeterministicMessageId.For<IPolicyPublished>(seeded.Draft);
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(cs, $"""SELECT count(*) FROM "OutboxMessage" WHERE "MessageId" = '{messageId}'"""));
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(cs, """SELECT count(*) FROM "IntegrationMessageReceipts" WHERE "ConsumerType" LIKE '%policy-cache%'"""));
        }
        finally
        {
            foreach (var hosted in a.Hosted.Concat(b.Hosted).Reverse())
            {
                try { await hosted.StopAsync(CancellationToken.None); } catch (Exception) { /* best effort */ }
            }

            await a.Provider.DisposeAsync();
            await b.Provider.DisposeAsync();
        }
    }

    [Fact]
    public void The_cache_consumer_runs_per_instance_inside_the_service_permission()
    {
        Assert.True(MassTransitExtensions.IsPerInstanceConsumer(typeof(PolicyPublishedCacheInvalidationConsumer)));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MassTransit:EndpointPrefix"] = "HR.Core.Web",
            ["MassTransit:InstanceId"] = "7f3c2a1b9d0e"
        }).Build();

        var name = MassTransitExtensions.PerInstanceEndpointName(config, PolicyPublishedCacheInvalidationConsumer.Purpose);

        Assert.Equal("hr-core-web-policy-cache-instance-7f3c2a1b9d0e", name);
        Assert.True(MassTransitExtensions.IsPerInstanceEndpoint(name));
        // PLAN-38: hr-core-web's RabbitMQ user may declare queues named hr-core-web-.* only.
        Assert.Matches("^hr-core-web-.*$", name);
        Assert.False(MassTransitExtensions.IsPerInstanceEndpoint("hr-core-web-training-completed"));
    }
}
