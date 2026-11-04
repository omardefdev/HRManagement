using System.Diagnostics.Metrics;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static HRManagement.IntegrationTests.Msg040OutboxOrderingIntegrationTests;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-40 (§5, D-40-5) with the real bus outbox on PostgreSQL + RabbitMQ: an endpoint that publishes and does not save
/// fails the request where the switch is on, and in Production it counts and logs while the request goes through.
/// </summary>
[Collection("PgRabbit")]
public sealed class Msg040UnsavedOutboxGuardIntegrationTests(PgRabbitFixture fixture)
{
    public sealed record Msg040Leak(Guid Id);

    private async Task<WebApplication> StartAsync(string environment, string? failOnUnsaved)
    {
        var cs = await fixture.Postgres.CreateDatabaseAsync($"msg040_guard_{Guid.NewGuid():N}");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0",
            [UnsavedOutboxGuardMiddleware.FailOnUnsavedKey] = failOnUnsaved
        });
        builder.Services.AddDbContext<Msg040OrderingDbContext>(o => o.UseNpgsql(cs));
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<Msg040OrderingDbContext>());
        builder.Services.AddServiceMassTransit(
            builder.Configuration,
            configureOutbox: x => x.AddPostgresOutbox<Msg040OrderingDbContext>(),
            enableInboxFilter: false);

        var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Msg040OrderingDbContext>().Database.EnsureCreatedAsync();

        app.UseRouting();
        app.UseHrOutboxFlushGuard();
        app.MapPost("/leak", async (IPublishEndpoint publish) =>
        {
            await publish.Publish(new Msg040Leak(Guid.NewGuid()));
            return Results.Ok();
        });
        app.MapPost("/saved", async (IPublishEndpoint publish, Msg040OrderingDbContext db) =>
        {
            await publish.Publish(new Msg040Leak(Guid.NewGuid()));
            await db.SaveChangesAsync();
            return Results.Ok();
        });
        await app.StartAsync();
        return app;
    }

    private static MeterListener CountUnsaved(Action<long, string?> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Name == "outbox_unsaved_messages")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? route = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "route")
                    route = tag.Value?.ToString();
            }

            onMeasurement(value, route);
        });
        listener.Start();
        return listener;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData("Testing", null)]
    [InlineData("Acceptance", null)]
    [InlineData("Production", "true")]
    public async Task Publish_without_a_save_fails_the_request_where_the_guard_throws(string environment, string? failOnUnsaved)
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var app = await StartAsync(environment, failOnUnsaved);
        using var client = app.GetTestClient();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostAsync("/leak", null));

        Assert.Contains("never saved", failure.Message, StringComparison.Ordinal);
        await app.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Production_counts_the_route_and_lets_the_request_through()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var app = await StartAsync("Production", null);
        using var client = app.GetTestClient();
        var measured = new List<(long Value, string? Route)>();
        using var listener = CountUnsaved((value, route) =>
        {
            lock (measured)
                measured.Add((value, route));
        });

        var response = await client.PostAsync("/leak", null);

        Assert.True(response.IsSuccessStatusCode);
        lock (measured)
            Assert.Contains((1L, (string?)"/leak"), measured);
        await app.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Publish_then_save_passes_the_guard()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var app = await StartAsync("Testing", null);
        using var client = app.GetTestClient();

        var response = await client.PostAsync("/saved", null);

        Assert.True(response.IsSuccessStatusCode);
        await app.StopAsync();
    }
}
