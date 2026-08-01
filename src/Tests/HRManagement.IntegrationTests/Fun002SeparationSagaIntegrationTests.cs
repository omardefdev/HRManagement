using HR.Contracts.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>FUN-002-C/E: separation saga cancel/compensate/fault.</summary>
[Collection("IntegrationSerial")]
public class Fun002SeparationSagaIntegrationTests
{
    [Fact]
    public async Task SeparationSaga_Cancel_DispatchesCompensateTrue()
    {
        await using var env = await CreateEnvAsync(
            parties: [ProcessParties.Payroll, "Slow.Party"],
            registerPartyConsumer: true);

        var id = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartSeparationSaga>(new
        {
            SeparationRecordId = id,
            EmployeeId = Guid.NewGuid(),
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = "fun002-cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<ISeparationPartyCompleted>(
            x => x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.SeparationRecordId == id));

        await env.Harness.Bus.Publish<ICancelSeparationSaga>(new
        {
            SeparationRecordId = id,
            Reason = "abort",
            CorrelationId = "fun002-cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IExecuteSeparationParty>(
            x => x.Context.Message.Compensate
                 && x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.SeparationRecordId == id));
        Assert.False(await env.Harness.Published.Any<ISeparationCompleted>(
            x => x.Context.Message.SeparationRecordId == id));
    }

    [Fact]
    public async Task SeparationSaga_PartyFault_DoesNotComplete()
    {
        await using var env = await CreateEnvAsync(
            parties: ["Missing.Party"],
            registerPartyConsumer: false);

        var id = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartSeparationSaga>(new
        {
            SeparationRecordId = id,
            EmployeeId = Guid.NewGuid(),
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = "fault",
            TenantId = (Guid?)null
        });

        await env.Harness.Bus.Publish<ISeparationPartyFaulted>(new
        {
            SeparationRecordId = id,
            EmployeeId = Guid.NewGuid(),
            Party = "Missing.Party",
            Reason = "outage",
            CorrelationId = "fault",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IProcessInterventionRequested>(
            x => x.Context.Message.CorrelationId == id));
        Assert.False(await env.Harness.Published.Any<ISeparationCompleted>());
    }

    private static async Task<SagaTestEnv> CreateEnvAsync(
        string[] parties,
        bool registerPartyConsumer = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<SagaMetrics>();
        services.Configure<SeparationSagaOptions>(o =>
        {
            o.MandatoryParties = parties;
            o.PartyTimeoutMinutes = 30;
        });

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<SeparationStateMachine, SeparationSagaState>()
                .InMemoryRepository();
            if (registerPartyConsumer)
                cfg.AddConsumer<TestPayrollSeparationPartyConsumer>();
            cfg.AddConsumer<TestInterventionConsumer>();
            cfg.UsingInMemory((context, busCfg) =>
            {
                busCfg.UseDelayedMessageScheduler();
                busCfg.ConfigureEndpoints(context);
            });
        });

        var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return new SagaTestEnv(provider, harness);
    }

    private sealed class SagaTestEnv(ServiceProvider provider, ITestHarness harness) : IAsyncDisposable
    {
        public ITestHarness Harness => harness;
        public async ValueTask DisposeAsync()
        {
            await harness.Stop();
            await provider.DisposeAsync();
        }
    }

    private sealed class TestPayrollSeparationPartyConsumer : IConsumer<IExecuteSeparationParty>
    {
        public async Task Consume(ConsumeContext<IExecuteSeparationParty> context)
        {
            var m = context.Message;
            if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
                return;
            await context.Publish<ISeparationPartyCompleted>(new
            {
                m.SeparationRecordId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? "test:compensate" : "eos:test",
                m.CorrelationId,
                m.TenantId
            });
        }
    }

    private sealed class TestInterventionConsumer : IConsumer<IProcessInterventionRequested>
    {
        public Task Consume(ConsumeContext<IProcessInterventionRequested> context) => Task.CompletedTask;
    }
}
