using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>The message of <see cref="Pol049EngineOutageRetryTests"/>: how its consumer fails ("outage" or anything else).</summary>
public sealed record Pol049OutageProbe(Guid Id, string Failure);

/// <summary>Counts the deliveries, and lets the consumer fail until <see cref="FailuresLeft"/> runs out.</summary>
internal sealed class Pol049OutageProbeState
{
    internal int Deliveries;
    internal int FailuresLeft;
}

internal sealed class Pol049OutageProbeConsumer(Pol049OutageProbeState state) : IConsumer<Pol049OutageProbe>
{
    public Task Consume(ConsumeContext<Pol049OutageProbe> context)
    {
        Interlocked.Increment(ref state.Deliveries);
        if (Interlocked.Decrement(ref state.FailuresLeft) < 0)
            return Task.CompletedTask;

        throw context.Message.Failure == "outage"
            ? new PolicyEngineUnavailableException(PolicyDomains.ContractsLeave, "Create")
            : new InvalidOperationException("a business failure");
    }
}

/// <summary>
/// PLAN-49 (the second review of P5) with the retry policy of every service bus (MassTransitExtensions.ConfigureMessageRetry): a
/// consumer that could not ask the policy engine in Enforce throws <see cref="PolicyEngineUnavailableException"/>, and the bus
/// retries it; when the engine stays away the message is faulted to <c>_error</c> after the last retry. The
/// InvalidOperationException the self-service leave handler threw first is one the policy ignores: it reached <c>_error</c> at
/// the first failed call, with no retry at all. The handler's own behaviour is in Pol049LeaveChannelsTests.
/// </summary>
public sealed class Pol049EngineOutageRetryTests
{
    private const int RetryCount = 2;

    private static async Task<(int Deliveries, bool Faulted)> PublishAsync(string failure, int failures)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MassTransit:RetryCount"] = RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MassTransit:RetryIntervalSeconds"] = "1",
            ["MassTransit:RetryUseJitter"] = "false"
        }).Build();

        var state = new Pol049OutageProbeState { FailuresLeft = failures };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<Pol049OutageProbeConsumer>();
            cfg.UsingInMemory((context, bus) =>
            {
                bus.UseMessageRetry(r => MassTransitExtensions.ConfigureMessageRetry(r, configuration));
                bus.ConfigureEndpoints(context);
            });
        });

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        // The harness calls the bus idle after its inactivity window, and its default leaves a fraction of a second over each
        // one-second retry gap: on a loaded runner a retry's timer could come later than that, and the count below would be
        // read too early (the cause of an earlier flaky test, Hcm029CandidateAcceptedV4Tests). The assertions are unchanged.
        harness.TestInactivityTimeout = TimeSpan.FromSeconds(6);
        harness.TestTimeout = TimeSpan.FromSeconds(60);
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new Pol049OutageProbe(Guid.NewGuid(), failure));
            Assert.True(await harness.GetConsumerHarness<Pol049OutageProbeConsumer>().Consumed.Any<Pol049OutageProbe>(), "the message was not consumed");
            await harness.InactivityTask;
            return (state.Deliveries, await harness.Published.Any<Fault<Pol049OutageProbe>>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task An_engine_outage_is_retried_and_the_message_is_consumed_when_the_engine_is_back()
    {
        // Two failed deliveries, the third finds the engine: no fault, nothing in _error.
        var (deliveries, faulted) = await PublishAsync("outage", failures: 2);

        Assert.Equal(3, deliveries);
        Assert.False(faulted);
    }

    [Fact]
    public async Task An_engine_that_stays_away_faults_the_message_only_after_the_last_retry()
    {
        var (deliveries, faulted) = await PublishAsync("outage", failures: int.MaxValue);

        Assert.Equal(1 + RetryCount, deliveries);
        Assert.True(faulted);
    }

    [Fact]
    public async Task The_exception_the_handler_threw_first_was_never_retried()
    {
        // What the review found: one delivery, and the fault at once.
        var (deliveries, faulted) = await PublishAsync("business", failures: int.MaxValue);

        Assert.Equal(1, deliveries);
        Assert.True(faulted);
    }
}
