using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests.Messaging;

/// <summary>
/// PLAN-40 (D-40-6) with the real retry pipeline of the MassTransit test harness: a business failure publishes the fault
/// once and returns; a transient failure rethrows silently while retries remain and faults once on the last attempt.
/// The transaction side (savepoint, receipt) is proved on PostgreSQL in Msg040OutboxOrderingIntegrationTests.
/// </summary>
public sealed class PartyAckConsumerBaseTests
{
    private const int RetryCount = 2;
    private const string Party = "msg040-unit";

    private sealed class Script
    {
        public int Attempts;
        public Func<int, Exception?> Failure { get; init; } = _ => null;
    }

    private sealed class SeparationParty(Script script) : SeparationPartyAckConsumerBase(Party)
    {
        protected override Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context) =>
            script.Failure(++script.Attempts) is { } ex ? Task.FromException<string?>(ex) : Task.FromResult<string?>(null);
    }

    private sealed class HireParty(Script script) : HirePartyAckConsumerBase(Party)
    {
        protected override Task ExecuteAsync(ConsumeContext<IExecuteHireParty> context) =>
            script.Failure(++script.Attempts) is { } ex ? Task.FromException(ex) : Task.CompletedTask;
    }

    private sealed class TransferParty(Script script) : TransferPartyAckConsumerBase(Party)
    {
        protected override Task<string?> ExecuteAsync(ConsumeContext<IExecuteTransferParty> context) =>
            script.Failure(++script.Attempts) is { } ex ? Task.FromException<string?>(ex) : Task.FromResult<string?>(null);
    }

    private sealed class SecondmentParty(Script script) : SecondmentPartyAckConsumerBase(Party)
    {
        protected override Task<string?> ExecuteAsync(ConsumeContext<IExecuteSecondmentParty> context) =>
            script.Failure(++script.Attempts) is { } ex ? Task.FromException<string?>(ex) : Task.FromResult<string?>(null);
    }

    private static ServiceProvider Harness(Script script, Action<IBusRegistrationConfigurator> consumers)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MassTransit:RetryCount"] = RetryCount.ToString()
        }).Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(script)
            .AddMassTransitTestHarness(x =>
            {
                consumers(x);
                x.AddConfigureEndpointsCallback((_, _, cfg) => cfg.UseMessageRetry(r => r.Immediate(RetryCount)));
            })
            .BuildServiceProvider(true);
    }

    private static object SeparationMessage(string party = Party) => new
    {
        SeparationRecordId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        Party = party,
        Compensate = false,
        TenantId = Guid.NewGuid()
    };

    private static async Task<(int Completed, int Faulted, bool Rethrown)> RunSeparationAsync(Script script, string party = Party)
    {
        await using var provider = Harness(script, x => x.AddConsumer<SeparationParty>());
        var harness = provider.GetTestHarness();
        await harness.Start();
        await harness.Bus.Publish<IExecuteSeparationParty>(SeparationMessage(party));
        await harness.InactivityTask;

        var completed = harness.Published.Select<ISeparationPartyCompleted>().Count();
        var faulted = harness.Published.Select<ISeparationPartyFaulted>().Count();
        var rethrown = harness.Consumed.Select<IExecuteSeparationParty>().Any(m => m.Exception is not null);
        return (completed, faulted, rethrown);
    }

    [Fact]
    public async Task Success_publishes_completed_once()
    {
        var script = new Script();
        var (completed, faulted, rethrown) = await RunSeparationAsync(script);
        Assert.Equal((1, 0, false), (completed, faulted, rethrown));
        Assert.Equal(1, script.Attempts);
    }

    [Fact]
    public async Task Another_party_is_ignored()
    {
        var script = new Script();
        var (completed, faulted, _) = await RunSeparationAsync(script, "someone-else");
        Assert.Equal((0, 0), (completed, faulted));
        Assert.Equal(0, script.Attempts);
    }

    [Fact]
    public async Task Business_failure_publishes_one_fault_and_returns_without_rethrowing()
    {
        var script = new Script { Failure = _ => new ArgumentException("refused") };
        var (completed, faulted, rethrown) = await RunSeparationAsync(script);
        Assert.Equal((0, 1, false), (completed, faulted, rethrown));
        Assert.Equal(1, script.Attempts);
    }

    [Fact]
    public async Task Transient_failure_with_retries_left_rethrows_without_publishing_and_a_later_attempt_completes()
    {
        var script = new Script { Failure = attempt => attempt == 1 ? new TimeoutException("party down") : null };
        var (completed, faulted, _) = await RunSeparationAsync(script);
        Assert.Equal((1, 0), (completed, faulted));
        Assert.Equal(2, script.Attempts);
    }

    [Fact]
    public async Task Transient_failure_on_the_last_attempt_publishes_one_fault_and_returns()
    {
        var script = new Script { Failure = _ => new TimeoutException("party down") };
        var (completed, faulted, rethrown) = await RunSeparationAsync(script);
        Assert.Equal((0, 1, false), (completed, faulted, rethrown));
        Assert.Equal(RetryCount + 1, script.Attempts);
    }

    [Fact]
    public async Task A_transient_failure_wrapped_by_ef_is_retried_by_the_bus_policy_and_a_plain_one_is_not()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MassTransit:RetryCount"] = "1",
            ["MassTransit:RetryIntervalSeconds"] = "1",
            ["MassTransit:RetryUseJitter"] = "false"
        }).Build();

        async Task<int> AttemptsAsync(Exception failure)
        {
            var script = new Script { Failure = attempt => attempt == 1 ? failure : null };
            await using var provider = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(script)
                .AddMassTransitTestHarness(x =>
                {
                    x.AddConsumer<ThrowingConsumer>();
                    x.AddConfigureEndpointsCallback((_, _, cfg) => cfg.UseMessageRetry(r => MassTransitExtensions.ConfigureMessageRetry(r, configuration)));
                })
                .BuildServiceProvider(true);
            var harness = provider.GetTestHarness();
            await harness.Start();
            await harness.Bus.Publish<IExecuteSeparationParty>(SeparationMessage());
            await harness.InactivityTask;
            return script.Attempts;
        }

        Assert.Equal(2, await AttemptsAsync(new InvalidOperationException("transient failure", new TimeoutException())));
        Assert.Equal(1, await AttemptsAsync(new InvalidOperationException("business rule")));
    }

    private sealed class ThrowingConsumer(Script script) : IConsumer<IExecuteSeparationParty>
    {
        public Task Consume(ConsumeContext<IExecuteSeparationParty> context) =>
            script.Failure(++script.Attempts) is { } ex ? Task.FromException(ex) : Task.CompletedTask;
    }

    [Fact]
    public async Task Hire_transfer_and_secondment_bases_follow_the_same_rule()
    {
        var script = new Script { Failure = _ => new TimeoutException("party down") };
        await using var provider = Harness(script, x =>
        {
            x.AddConsumer<HireParty>();
            x.AddConsumer<TransferParty>();
            x.AddConsumer<SecondmentParty>();
        });
        var harness = provider.GetTestHarness();
        await harness.Start();
        var common = new { EmployeeId = Guid.NewGuid(), Party, Compensate = false, TenantId = Guid.NewGuid() };
        await harness.Bus.Publish<IExecuteHireParty>(new { common.EmployeeId, common.Party, common.Compensate, common.TenantId, ServiceTransactionId = Guid.NewGuid() });
        await harness.Bus.Publish<IExecuteTransferParty>(new { common.EmployeeId, common.Party, common.Compensate, common.TenantId, TransferRequestId = Guid.NewGuid() });
        await harness.Bus.Publish<IExecuteSecondmentParty>(new { common.EmployeeId, common.Party, common.Compensate, common.TenantId, SecondmentRequestId = Guid.NewGuid() });
        await harness.InactivityTask;

        Assert.Single(harness.Published.Select<IHirePartyFaulted>());
        Assert.Single(harness.Published.Select<ITransferPartyFaulted>());
        Assert.Single(harness.Published.Select<ISecondmentPartyFaulted>());
        Assert.Empty(harness.Published.Select<IHirePartyCompleted>());
        Assert.Equal(3 * (RetryCount + 1), script.Attempts);
    }

    [Fact]
    public void Transient_means_concurrency_timeouts_and_network_failures()
    {
        Assert.True(MessagingFaults.IsTransient(new TimeoutException()));
        Assert.True(MessagingFaults.IsTransient(new HttpRequestException()));
        Assert.True(MessagingFaults.IsTransient(new DbUpdateConcurrencyException()));
        Assert.True(MessagingFaults.IsTransient(new TaskCanceledException()));
        Assert.True(MessagingFaults.IsTransient(new DbUpdateException("wrapped", new TimeoutException())));
        Assert.False(MessagingFaults.IsTransient(new InvalidOperationException("business")));
        Assert.False(MessagingFaults.IsTransient(new DbUpdateException("unique violation")));
    }

    [Fact]
    public void A_cancelled_caller_is_not_a_transient_failure()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False(MessagingFaults.IsTransient(new TaskCanceledException(), cancelled.Token));
    }
}
