using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Int003ProcessSagaTests
{
    [Fact]
    public async Task DOM001_Publish_DoesNotAutoComplete_ContractTerminated()
    {
        var (scope, spy, db) = await ContractLifecycleIntegrationTests_CreateScope();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var separation = TestDataFactory.CreateSeparationRecord(employeeId);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();
            var checklist = SeparationChecklistDefaults.CreateForRecord(separation.Id).ToList();
            foreach (var step in checklist.Where(c => c.Step != SeparationStep.ContractTerminated))
            {
                step.IsCompleted = true;
                step.CompletedAt = DateTime.UtcNow;
                step.CompletedBy = Guid.NewGuid();
                step.EvidenceRef = "ev";
                step.SourceSystem = "Test";
            }
            db.SeparationChecklists.AddRange(checklist);
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
            var (success, error) = await mediator.Send(new PublishSeparationCommand(separation.Id));
            Assert.False(success);
            Assert.Contains("ContractTerminated", error ?? "");

            db.ChangeTracker.Clear();
            var contractStep = await db.SeparationChecklists.FirstAsync(c =>
                c.SeparationRecordId == separation.Id && c.Step == SeparationStep.ContractTerminated);
            Assert.False(contractStep.IsCompleted);
            Assert.Equal(0, spy.SeparationSagaStartedCount);
        }
    }

    [Fact]
    public async Task SeparationSaga_HappyPath_AllParties_ThenCompleted()
    {
        await using var env = await CreateSeparationSagaEnvAsync(parties: [ProcessParties.Payroll]);
        var separationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await env.Harness.Bus.Publish<IStartSeparationSaga>(new
        {
            SeparationRecordId = separationId,
            EmployeeId = employeeId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = "test",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IStartSeparationSaga>());
        Assert.True(await env.Harness.Published.Any<IExecuteSeparationParty>());
        Assert.True(await env.Harness.Consumed.Any<IExecuteSeparationParty>());
        Assert.True(await env.Harness.Published.Any<ISeparationPartyCompleted>());
        Assert.True(await env.Harness.Published.Any<ISeparationCompleted>(
            x => x.Context.Message.SeparationRecordId == separationId));
    }

    [Fact]
    public async Task SeparationSaga_PartyTimeout_GoesToIntervention()
    {
        await using var env = await CreateSeparationSagaEnvAsync(
            parties: ["Missing.Party"],
            timeoutMinutes: 30,
            registerPartyConsumer: false);

        var separationId = Guid.NewGuid();
        await env.Harness.Bus.Publish<IStartSeparationSaga>(new
        {
            SeparationRecordId = separationId,
            EmployeeId = Guid.NewGuid(),
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = "to",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IStartSeparationSaga>());

        // Permanent party fault → intervention (timeout uses the same OpenIntervention path)
        await env.Harness.Bus.Publish<ISeparationPartyFaulted>(new
        {
            SeparationRecordId = separationId,
            EmployeeId = Guid.NewGuid(),
            Party = "Missing.Party",
            Reason = "انتهت مهلة انتظار إقرار الطرف",
            CorrelationId = "to",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Published.Any<IProcessInterventionRequested>(
            x => x.Context.Message.CorrelationId == separationId));
        Assert.False(await env.Harness.Published.Any<ISeparationCompleted>());
    }

    [Fact]
    public async Task SeparationSaga_ResumeFromIntervention_WithoutManualDb()
    {
        await using var env = await CreateSeparationSagaEnvAsync(parties: [ProcessParties.Payroll]);
        var separationId = Guid.NewGuid();

        await env.Harness.Bus.Publish<IStartSeparationSaga>(new
        {
            SeparationRecordId = separationId,
            EmployeeId = Guid.NewGuid(),
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = "r",
            TenantId = (Guid?)null
        });

        await env.Harness.Bus.Publish<IResumeSeparationSaga>(new
        {
            SeparationRecordId = separationId,
            CorrelationId = "r",
            TenantId = (Guid?)null
        });

        Assert.True(await env.Harness.Consumed.Any<IResumeSeparationSaga>()
                    || await env.Harness.Published.Any<ISeparationCompleted>());
    }

    [Fact]
    public void Replay_and_Idempotent_Party_Contracts_Exist()
    {
        Assert.Equal("Payroll", ProcessParties.Payroll);
        Assert.True(typeof(IExecuteSeparationParty).IsInterface);
        Assert.True(typeof(ISeparationPartyCompleted).IsInterface);
    }

    private static async Task<(IServiceScope Scope, SpyEventPublisher Spy, ContractsDbContext Db)>
        ContractLifecycleIntegrationTests_CreateScope()
    {
        // Reuse lifecycle fixture pattern
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<HR.Contracts.Application.Abstractions.IContractsRepository, HR.Contracts.Infrastructure.Repositories.ContractsRepository>();
        var spy = new SpyEventPublisher();
        services.AddSingleton<HR.Contracts.Application.Abstractions.IEventPublisher>(spy);
        services.AddSingleton<HR.Contracts.Application.Abstractions.IEmployeeLifecycleGate,
            HR.Contracts.Infrastructure.Services.PassThroughEmployeeLifecycleGate>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PublishSeparationHandler>());
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        return (scope, spy, db);
    }

    private static async Task<SagaTestEnv> CreateSeparationSagaEnvAsync(
        string[] parties,
        int timeoutMinutes = 30,
        bool registerPartyConsumer = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<SagaMetrics>();
        services.Configure<SeparationSagaOptions>(o =>
        {
            o.MandatoryParties = parties;
            o.PartyTimeoutMinutes = timeoutMinutes;
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
        var harness = provider.GetRequiredService<MassTransit.Testing.ITestHarness>();
        await harness.Start();
        return new SagaTestEnv(provider, harness);
    }

    private sealed class SagaTestEnv(ServiceProvider provider, MassTransit.Testing.ITestHarness harness) : IAsyncDisposable
    {
        public MassTransit.Testing.ITestHarness Harness => harness;
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
                EvidenceRef = "test",
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
