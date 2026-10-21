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

/// <summary>FUN-001-C/E: hire saga timeout/fault/resume/cancel + Directory gate.</summary>
[Collection("IntegrationSerial")]
public class Fun001HireSagaIntegrationTests
{
    [Fact]
    public async Task HireSaga_StartWithRecruitmentApplicationId_PropagatesToCompletedEvent()
    {
        await using var env = await CreateHireSagaEnvAsync(
            parties: [ProcessParties.Payroll]);
        var txnId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await env.Harness.Bus.Publish<IStartHireSaga>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = employeeId,
            ContractId = (Guid?)null,
            RecruitmentApplicationId = appId,
            CorrelationId = "fun001-lineage",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IHireCaseCompleted>(
            x => x.Context.Message.RecruitmentApplicationId == appId
                 && x.Context.Message.ServiceTransactionId == txnId));
    }

    [Fact]
    public async Task HireSaga_HappyPath_IncludesAuthDirectory_ThenCompleted()
    {
        await using var env = await CreateHireSagaEnvAsync(
            parties: [ProcessParties.Payroll, ProcessParties.AuthDirectory]);
        var txnId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await env.Harness.Bus.Publish<IStartHireSaga>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = employeeId,
            ContractId = (Guid?)null,
            RecruitmentApplicationId = (Guid?)null,
            CorrelationId = "fun001-happy",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IStartHireSaga>());
        Assert.True(await env.Harness.Published.Any<IExecuteHireParty>(
            x => x.Context.Message.Party == ProcessParties.AuthDirectory));
        Assert.True(await env.Harness.Published.Any<IHireCaseCompleted>(
            x => x.Context.Message.ServiceTransactionId == txnId));
    }

    [Fact]
    public async Task HireSaga_PartyFault_DoesNotComplete_OpensIntervention()
    {
        await using var env = await CreateHireSagaEnvAsync(
            parties: ["Missing.Party"],
            registerPartyConsumer: false);

        var txnId = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartHireSaga>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = Guid.NewGuid(),
            ContractId = (Guid?)null,
            RecruitmentApplicationId = (Guid?)null,
            CorrelationId = "fault",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IStartHireSaga>());

        await env.Harness.Bus.Publish<IHirePartyFaulted>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = Guid.NewGuid(),
            Party = "Missing.Party",
            Reason = "طرف معطل",
            CorrelationId = "fault",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IProcessInterventionRequested>(
            x => x.Context.Message.CorrelationId == txnId));
        Assert.False(await env.Harness.Published.Any<IHireCaseCompleted>());
    }

    [Fact]
    public async Task HireSaga_Cancel_DispatchesCompensateTrue()
    {
        await using var env = await CreateHireSagaEnvAsync(
            parties: [ProcessParties.Payroll, "Slow.Party"],
            registerPartyConsumer: true);

        var txnId = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartHireSaga>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = Guid.NewGuid(),
            ContractId = (Guid?)null,
            RecruitmentApplicationId = (Guid?)null,
            CorrelationId = "cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IHirePartyCompleted>(
            x => x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.ServiceTransactionId == txnId));

        await env.Harness.Bus.Publish<ICancelHireSaga>(new
        {
            ServiceTransactionId = txnId,
            Reason = "abort",
            CorrelationId = "cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IExecuteHireParty>(
            x => x.Context.Message.Compensate
                 && x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.ServiceTransactionId == txnId));
        Assert.False(await env.Harness.Published.Any<IHireCaseCompleted>(
            x => x.Context.Message.ServiceTransactionId == txnId));
    }

    [Fact]
    public async Task HireSaga_ResumeAfterFault_CanComplete()
    {
        var employeeId = Guid.NewGuid();
        await using var env = await CreateHireSagaEnvAsync(
            parties: [ProcessParties.Payroll],
            registerPartyConsumer: false);
        var txnId = Guid.NewGuid();

        await env.Harness.Bus.Publish<IStartHireSaga>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = employeeId,
            ContractId = (Guid?)null,
            RecruitmentApplicationId = (Guid?)null,
            CorrelationId = "resume",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IStartHireSaga>());

        await env.Harness.Bus.Publish<IHirePartyFaulted>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = employeeId,
            Party = ProcessParties.Payroll,
            Reason = "transient",
            CorrelationId = "resume",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IProcessInterventionRequested>());

        await env.Harness.Bus.Publish<IResumeHireSaga>(new
        {
            ServiceTransactionId = txnId,
            CorrelationId = "resume",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IResumeHireSaga>());

        await env.Harness.Bus.Publish<IHirePartyCompleted>(new
        {
            ServiceTransactionId = txnId,
            EmployeeId = employeeId,
            Party = ProcessParties.Payroll,
            EvidenceRef = "test:resumed-ack",
            CorrelationId = "resume",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IHireCaseCompleted>(
            x => x.Context.Message.ServiceTransactionId == txnId));
    }

    private static async Task<SagaTestEnv> CreateHireSagaEnvAsync(
        string[] parties,
        int timeoutMinutes = 30,
        bool registerPartyConsumer = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<SagaMetrics>();
        services.Configure<HireSagaOptions>(o =>
        {
            o.MandatoryParties = parties;
            o.PartyTimeoutMinutes = timeoutMinutes;
        });

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<HireCaseStateMachine, HireCaseSagaState>()
                .InMemoryRepository();
            if (registerPartyConsumer)
                cfg.AddConsumer<TestHirePartyConsumer>();
            cfg.AddConsumer<TestHireInterventionConsumer>();
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

    private sealed class TestHirePartyConsumer : IConsumer<IExecuteHireParty>
    {
        public async Task Consume(ConsumeContext<IExecuteHireParty> context)
        {
            var m = context.Message;
            if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(m.Party, ProcessParties.AuthDirectory, StringComparison.OrdinalIgnoreCase))
                return;

            await context.Publish<IHirePartyCompleted>(new
            {
                m.ServiceTransactionId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? "test:compensate" : "test:ack",
                m.CorrelationId,
                m.TenantId
            });
        }
    }

    private sealed class TestHireInterventionConsumer : IConsumer<IProcessInterventionRequested>
    {
        public Task Consume(ConsumeContext<IProcessInterventionRequested> context) => Task.CompletedTask;
    }
}
