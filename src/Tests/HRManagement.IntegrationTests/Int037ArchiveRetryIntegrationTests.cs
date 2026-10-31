using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.TestSupport.CodeGeneration;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Records.Archive.Infrastructure.Consumers;
using Records.Archive.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-37 (§5, AC-INT-37d at unit scale): with the production retry policy (MassTransitExtensions.ConfigureMessageRetry)
/// an archive event whose number HR.Core cannot issue is retried, then faulted to the endpoint's <c>_error</c> queue;
/// replayed after HR.Core recovers it creates exactly one archive item, also when replayed twice.
/// </summary>
public class Int037ArchiveRetryIntegrationTests
{
    private const int RetryCount = 2;

    [Fact]
    public async Task Failed_reference_is_retried_then_dead_lettered_and_the_replay_creates_one_item()
    {
        var available = false;
        var codes = new FakeCodeGenerationClient();
        codes.GenerateHandler = _ => new CodeGenerationResult { Code = available ? $"ARC-TEST-{Guid.NewGuid():N}" : "" };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MassTransit:RetryCount"] = RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MassTransit:RetryIntervalSeconds"] = "1",
            ["MassTransit:RetryUseJitter"] = "false"
        }).Build();

        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ArchiveDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ICodeGenerationClient>(codes);
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<LeaveApprovedConsumer>();
            cfg.UsingInMemory((context, bus) =>
            {
                bus.UseMessageRetry(r => MassTransitExtensions.ConfigureMessageRetry(r, configuration));
                bus.ConfigureEndpoints(context);
            });
        });

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var message = new LeaveApproved(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            // HR.Core unavailable: 1 attempt + RetryCount retries, then a fault (the message goes to _error).
            await harness.Bus.Publish<ILeaveApproved>(message);
            Assert.True(await harness.Published.Any<Fault<ILeaveApproved>>(), "the consumer did not fault");
            Assert.Equal(1 + RetryCount, codes.Requests.Count);
            var consumer = harness.GetConsumerHarness<LeaveApprovedConsumer>();
            Assert.True(await consumer.Consumed.Any<ILeaveApproved>(x => x.Exception is CodeGenerationUnavailableException));
            await using (var scope = provider.CreateAsyncScope())
                Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ArchiveDbContext>().ArchiveItems.CountAsync());

            // HR.Core back: replaying the message (ErrorQueueReplay) twice creates exactly one item.
            available = true;
            await harness.Bus.Publish<ILeaveApproved>(message);
            await harness.Bus.Publish<ILeaveApproved>(message);
            await harness.InactivityTask;
            Assert.True(await harness.Published.Any<ITransactionArchived>());
            await using (var scope = provider.CreateAsyncScope())
            {
                var items = await scope.ServiceProvider.GetRequiredService<ArchiveDbContext>().ArchiveItems.ToListAsync();
                var item = Assert.Single(items);
                Assert.StartsWith("ARC-TEST-", item.ReferenceNumber);
            }
        }
        finally
        {
            await harness.Stop();
        }
    }

    private sealed record LeaveApproved(Guid TransactionId, Guid LeaveRequestId, Guid EmployeeId) : ILeaveApproved
    {
        public string LeaveType { get; init; } = "Annual";
        public int DurationDays { get; init; } = 3;
        public Guid ApprovedBy { get; init; } = Guid.NewGuid();
        public string? CorrelationId { get; init; } = "int037";
        public string SourceSystem { get; init; } = "HR.Contracts";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public DateTime StartDate { get; init; } = DateTime.UtcNow.Date;
        public DateTime EndDate { get; init; } = DateTime.UtcNow.Date.AddDays(2);
    }
}
