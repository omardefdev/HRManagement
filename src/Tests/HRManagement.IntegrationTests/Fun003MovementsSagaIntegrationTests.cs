using HR.Movements.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>FUN-003-C/E: transfer/secondment saga cancel/compensate.</summary>
[Collection("IntegrationSerial")]
public class Fun003MovementsSagaIntegrationTests
{
    [Fact]
    public async Task TransferSaga_Cancel_DispatchesCompensateTrue()
    {
        await using var env = await CreateTransferEnvAsync(
            parties: [ProcessParties.Payroll, "Slow.Party"],
            registerPartyConsumer: true);

        var id = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartTransferSaga>(new
        {
            TransferRequestId = id,
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow,
            SalaryChange = (decimal?)null,
            AllowanceChange = (decimal?)null,
            CorrelationId = "fun003-trf-cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<ITransferPartyCompleted>(
            x => x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.TransferRequestId == id));

        await env.Harness.Bus.Publish<ICancelTransferSaga>(new
        {
            TransferRequestId = id,
            Reason = "abort",
            CorrelationId = "fun003-trf-cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IExecuteTransferParty>(
            x => x.Context.Message.Compensate
                 && x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.TransferRequestId == id));
        Assert.False(await env.Harness.Published.Any<ITransferExecuted>(
            x => x.Context.Message.TransferRequestId == id));
    }

    [Fact]
    public async Task TransferSaga_PublishesRegimeFields_OnComplete()
    {
        await using var env = await CreateTransferEnvAsync(
            parties: [ProcessParties.Payroll],
            registerPartyConsumer: true);

        var id = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartTransferSaga>(new
        {
            TransferRequestId = id,
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow,
            SalaryChange = (decimal?)null,
            AllowanceChange = (decimal?)null,
            SourceLegalRegimeId = "law50",
            TargetLegalRegimeId = "law53",
            TransitionDecisionRef = "DEC-TRANSFER-2026",
            ConditionCode = "agency_reclassification",
            ConflictNotes = "Article 159 note",
            CorrelationId = "fun003-trf-regime",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<ITransferExecuted>(
            x => x.Context.Message.TransferRequestId == id
                 && x.Context.Message.FromLegalRegimeId == "law50"
                 && x.Context.Message.ToLegalRegimeId == "law53"
                 && x.Context.Message.PayrollRegimeCode == ServiceRegimeToPayrollMapper.PayrollLaw53
                 && x.Context.Message.TransitionDecisionRef == "DEC-TRANSFER-2026"
                 && x.Context.Message.ConflictNotes == "Article 159 note"));
    }

    [Fact]
    public async Task SecondmentSaga_UsesHostOrgAndEndDate_FromStart()
    {
        await using var env = await CreateSecondmentEnvAsync(
            parties: [ProcessParties.Payroll],
            registerPartyConsumer: true);

        var id = Guid.NewGuid();
        var end = DateTime.UtcNow.Date.AddMonths(4);
        await env.Harness.Bus.Publish<IStartSecondmentSaga>(new
        {
            SecondmentRequestId = id,
            EmployeeId = Guid.NewGuid(),
            ActivatedAt = DateTime.UtcNow,
            HostOrg = "جهة-مضيفة-اختبار",
            EndDate = end,
            CorrelationId = "fun003-sec",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<ISecondmentExecuted>(
            x => x.Context.Message.SecondmentRequestId == id
                 && x.Context.Message.HostOrg == "جهة-مضيفة-اختبار"
                 && x.Context.Message.EndDate == end));
    }

    [Fact]
    public async Task SecondmentSaga_Cancel_DispatchesCompensateTrue()
    {
        await using var env = await CreateSecondmentEnvAsync(
            parties: [ProcessParties.Payroll, "Slow.Party"],
            registerPartyConsumer: true);

        var id = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartSecondmentSaga>(new
        {
            SecondmentRequestId = id,
            EmployeeId = Guid.NewGuid(),
            ActivatedAt = DateTime.UtcNow,
            HostOrg = "H",
            EndDate = DateTime.UtcNow.AddMonths(3),
            CorrelationId = "fun003-sec-cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<ISecondmentPartyCompleted>(
            x => x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.SecondmentRequestId == id));

        await env.Harness.Bus.Publish<ICancelSecondmentSaga>(new
        {
            SecondmentRequestId = id,
            Reason = "abort",
            CorrelationId = "fun003-sec-cancel",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IExecuteSecondmentParty>(
            x => x.Context.Message.Compensate
                 && x.Context.Message.Party == ProcessParties.Payroll
                 && x.Context.Message.SecondmentRequestId == id));
    }

    private static async Task<SagaTestEnv> CreateTransferEnvAsync(string[] parties, bool registerPartyConsumer)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<SagaMetrics>();
        services.Configure<TransferSagaOptions>(o =>
        {
            o.MandatoryParties = parties;
            o.PartyTimeoutMinutes = 30;
        });

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<TransferStateMachine, TransferSagaState>().InMemoryRepository();
            if (registerPartyConsumer)
                cfg.AddConsumer<TestPayrollTransferPartyConsumer>();
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

    private static async Task<SagaTestEnv> CreateSecondmentEnvAsync(string[] parties, bool registerPartyConsumer)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<SagaMetrics>();
        services.Configure<SecondmentSagaOptions>(o =>
        {
            o.MandatoryParties = parties;
            o.PartyTimeoutMinutes = 30;
        });

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<SecondmentStateMachine, SecondmentSagaState>().InMemoryRepository();
            if (registerPartyConsumer)
                cfg.AddConsumer<TestPayrollSecondmentPartyConsumer>();
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

    private sealed class TestPayrollTransferPartyConsumer : IConsumer<IExecuteTransferParty>
    {
        public async Task Consume(ConsumeContext<IExecuteTransferParty> context)
        {
            var m = context.Message;
            if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
                return;
            await context.Publish<ITransferPartyCompleted>(new
            {
                m.TransferRequestId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? "test:compensate" : "payroll:test",
                m.CorrelationId,
                m.TenantId
            });
        }
    }

    private sealed class TestPayrollSecondmentPartyConsumer : IConsumer<IExecuteSecondmentParty>
    {
        public async Task Consume(ConsumeContext<IExecuteSecondmentParty> context)
        {
            var m = context.Message;
            if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
                return;
            await context.Publish<ISecondmentPartyCompleted>(new
            {
                m.SecondmentRequestId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? "test:compensate" : "payroll:test",
                m.CorrelationId,
                m.TenantId
            });
        }
    }
}
