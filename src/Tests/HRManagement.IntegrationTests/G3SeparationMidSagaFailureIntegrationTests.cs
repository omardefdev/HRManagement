using HR.Contracts.Domain.Entities;

using HR.Contracts.Domain.Enums;

using HR.Contracts.Infrastructure.Consumers;

using HR.Contracts.Infrastructure.Data;

using HR.Contracts.Infrastructure.Sagas;

using HRManagement.MessageContracts.Commands;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using HRManagement.SharedKernel.Entities;

using HRManagement.SharedKernel.Tenancy;

using MassTransit;

using MassTransit.Testing;

using Microsoft.Data.Sqlite;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options;

using Records.Archive.Infrastructure.Consumers;

using Records.Archive.Infrastructure.Data;



namespace HRManagement.IntegrationTests;



/// <summary>G3: mid-saga failure leaves visible Executing state and no premature archive.</summary>

[Collection("IntegrationSerial")]

public class G3SeparationMidSagaFailureIntegrationTests

{

    [Fact]

    public async Task MidSagaCancel_KeepsExecutingState_NoArchiveItem()

    {

        await using var env = await CreateEnvAsync(

            parties: [ProcessParties.Archive, ProcessParties.Payroll, "Slow.Party"],

            registerSlowParty: false);



        var tenantId = env.TenantId;
        var employeeId = Guid.NewGuid();
        var separationId = Guid.NewGuid();

        await SeedSeparationAsync(env.ContractsDb, tenantId, employeeId, separationId);



        await env.Harness.Bus.Publish<IStartSeparationSaga>(new

        {

            SeparationRecordId = separationId,

            EmployeeId = employeeId,

            SeparationType = "Resignation",

            EffectiveDate = DateTime.UtcNow,

            ContractId = (Guid?)null,

            CorrelationId = separationId.ToString(),

            TenantId = tenantId,

            RemainingLeaveDays = 0

        });



        Assert.True(await env.Harness.Published.Any<ISeparationPartyCompleted>(

            x => x.Context.Message.Party == ProcessParties.Archive

                 && x.Context.Message.SeparationRecordId == separationId));



        await env.Harness.Bus.Publish<ICancelSeparationSaga>(new

        {

            SeparationRecordId = separationId,

            Reason = "mid-saga abort",

            CorrelationId = separationId.ToString(),

            TenantId = tenantId

        });



        Assert.True(await env.Harness.Published.Any<IExecuteSeparationParty>(

            x => x.Context.Message.Compensate

                 && x.Context.Message.Party == ProcessParties.Archive));



        await env.Harness.Bus.Publish<ISeparationSagaFinalized>(new

        {

            SeparationRecordId = separationId,

            EmployeeId = employeeId,

            Success = false,

            CorrelationId = separationId.ToString(),

            TenantId = tenantId

        });



        env.ContractsDb.ChangeTracker.Clear();

        env.ArchiveDb.ChangeTracker.Clear();



        var txn = await env.ContractsDb.ServiceTransactions.SingleAsync();

        Assert.Equal(ServiceTransactionState.Executing, txn.State);



        var record = await env.ContractsDb.SeparationRecords.SingleAsync();
        Assert.True(record.SeparationEventPublished);



        Assert.False(await env.ArchiveDb.ArchiveItems.AnyAsync());

        var staged = await env.ArchiveDb.ArchiveChecklists

            .Where(c => c.CorrelationId == separationId && c.IsCompleted)

            .ToListAsync();

        Assert.Empty(staged);



        Assert.False(await env.Harness.Published.Any<ISeparationCompleted>(

            x => x.Context.Message.SeparationRecordId == separationId));

    }



    [Fact]

    public async Task PartyFault_OpensIntervention_NoArchiveItem()

    {

        await using var env = await CreateEnvAsync(

            parties: ["Missing.Party"],

            registerSlowParty: false);



        var separationId = Guid.NewGuid();

        var employeeId = Guid.NewGuid();

        await SeedSeparationAsync(env.ContractsDb, env.TenantId, employeeId, separationId);



        await env.Harness.Bus.Publish<IStartSeparationSaga>(new

        {

            SeparationRecordId = separationId,

            EmployeeId = employeeId,

            SeparationType = "Resignation",

            EffectiveDate = DateTime.UtcNow,

            ContractId = (Guid?)null,

            CorrelationId = "fault",

            TenantId = (Guid?)null,

            RemainingLeaveDays = 0

        });



        await env.Harness.Bus.Publish<ISeparationPartyFaulted>(new

        {

            SeparationRecordId = separationId,

            EmployeeId = employeeId,

            Party = "Missing.Party",

            Reason = "outage",

            CorrelationId = "fault",

            TenantId = (Guid?)null

        });



        Assert.True(await env.Harness.Published.Any<IProcessInterventionRequested>(

            x => x.Context.Message.CorrelationId == separationId));



        env.ContractsDb.ChangeTracker.Clear();

        Assert.Equal(ServiceTransactionState.Executing,

            await env.ContractsDb.ServiceTransactions.Select(t => t.State).SingleAsync());

        Assert.False(await env.ArchiveDb.ArchiveItems.AnyAsync());

    }



    private static async Task SeedSeparationAsync(

        ContractsDbContext db, Guid tenantId, Guid employeeId, Guid separationId)

    {

        db.SeparationRecords.Add(new SeparationRecord

        {

            Id = separationId,

            TenantId = tenantId,

            EmployeeId = employeeId,

            SeparationType = SeparationType.Resignation,

            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),

            SeparationEventPublished = true

        });



        var txn = new ServiceTransaction
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            TransactionType = ServiceTransactionType.Separation
        };
        txn.MoveTo(ServiceTransactionState.Submitted, null, null);
        txn.MoveTo(ServiceTransactionState.UnderReview, null, null);
        txn.MoveTo(ServiceTransactionState.InProcess, null, null);
        txn.MoveTo(ServiceTransactionState.PendingApproval, null, null);
        txn.MoveTo(ServiceTransactionState.Approved, null, null);
        txn.MoveTo(ServiceTransactionState.Executing, null, "Saga started");

        db.ServiceTransactions.Add(txn);

        await db.SaveChangesAsync();

    }



    private static async Task<SeparationEnv> CreateEnvAsync(string[] parties, bool registerSlowParty)

    {

        var contractsConn = new SqliteConnection("Data Source=:memory:");

        await contractsConn.OpenAsync();

        var archiveConn = new SqliteConnection("Data Source=:memory:");

        await archiveConn.OpenAsync();



        var tenantId = Guid.NewGuid();

        var tenant = new FixedTenantContext(tenantId);



        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton<SagaMetrics>();

        services.Configure<SeparationSagaOptions>(o =>

        {

            o.MandatoryParties = parties;

            o.PartyTimeoutMinutes = 30;

        });



        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(contractsConn));

        services.AddSingleton<ITenantContext>(tenant);

        services.AddDbContext<ArchiveDbContext>(o => o.UseSqlite(archiveConn));



        services.AddMassTransitTestHarness(cfg =>

        {

            cfg.AddSagaStateMachine<SeparationStateMachine, SeparationSagaState>()

                .InMemoryRepository();

            cfg.AddConsumer<SeparationPartyAckConsumer>();

            cfg.AddConsumer<SeparationSagaFinalizedConsumer>();

            cfg.AddConsumer<ProcessInterventionRequestedConsumer>();

            cfg.AddConsumer<TestPayrollSeparationPartyConsumer>();

            if (registerSlowParty)

                cfg.AddConsumer<TestSlowSeparationPartyConsumer>();

            cfg.UsingInMemory((context, busCfg) =>

            {

                busCfg.UseDelayedMessageScheduler();

                busCfg.ConfigureEndpoints(context);

            });

        });



        var provider = services.BuildServiceProvider(true);
        var scope = provider.CreateAsyncScope();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var contractsDb = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await contractsDb.Database.EnsureCreatedAsync();
        var archiveDb = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        await archiveDb.Database.EnsureCreatedAsync();

        return new SeparationEnv(provider, scope, harness, contractsDb, archiveDb, tenantId);

    }



    private sealed class SeparationEnv(
        ServiceProvider provider,
        AsyncServiceScope scope,
        ITestHarness harness,
        ContractsDbContext contractsDb,
        ArchiveDbContext archiveDb,
        Guid tenantId) : IAsyncDisposable
    {
        public ITestHarness Harness => harness;
        public ContractsDbContext ContractsDb => contractsDb;
        public ArchiveDbContext ArchiveDb => archiveDb;
        public Guid TenantId => tenantId;

        public async ValueTask DisposeAsync()
        {
            await harness.Stop();
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }



    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext

    {

        public Guid? TenantId { get; } = tenantId;

        public bool IsPlatformOperator => false;

        public Guid RequireTenantId() => tenantId;

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

                EvidenceRef = m.Compensate ? "test:payroll-compensate" : "test:payroll-ack",

                m.CorrelationId,

                m.TenantId

            });

        }

    }



    private sealed class TestSlowSeparationPartyConsumer : IConsumer<IExecuteSeparationParty>

    {

        public Task Consume(ConsumeContext<IExecuteSeparationParty> context) =>

            Task.FromException(new InvalidOperationException("slow party down"));

    }

}


