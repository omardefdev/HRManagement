using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.Contracts;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Messaging;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using MassTransit;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Repositories;
using HRManagement.IntegrationTests.Support;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class ContractSeparationChainIntegrationTests
{
    [Fact]
    public async Task ActivateContract_PublishesEvent_CreatesPayProfile()
    {
        await using var scope = await CreateActivationChainScopeAsync();

        var contractsDb = scope.GetDb<ContractsDbContext>();
        var payrollDb = scope.GetDb<Payroll.Infrastructure.Data.PayrollDbContext>();

        var contractType = TestDataFactory.CreateContractType();
        var employeeId = Guid.NewGuid();
        var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
        contractsDb.ContractTypes.Add(contractType);
        contractsDb.Contracts.Add(contract);
        contractsDb.CommencementRecords.Add(TestDataFactory.CreateCommencement(employeeId, contract.Id));
        await contractsDb.SaveChangesAsync();

        using var mediatorScope = scope.Provider.CreateScope();
        var mediator = mediatorScope.ServiceProvider.GetRequiredService<IMediator>();
        var (success, _) = await mediator.Send(new ActivateContractCommand(contract.Id));
        Assert.True(success);

        await IntegrationTestHelpers.AssertConsumedAsync<IContractActivated>(scope.Harness);

        payrollDb.ChangeTracker.Clear();
        var profile = await payrollDb.EmployeePayProfiles
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.IsActive);
        Assert.NotNull(profile);
    }

    [Fact]
    public async Task SeparationCompleted_DeactivatesPayProfile()
    {
        await using var scope = await CreateSeparationChainScopeAsync();
        var payrollDb = scope.GetDb<Payroll.Infrastructure.Data.PayrollDbContext>();
        var empId = Guid.NewGuid();
        payrollDb.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(empId, 8000m));
        await payrollDb.SaveChangesAsync();

        var sepId = Guid.NewGuid();
        await scope.Harness.Bus.Publish<IExecuteSeparationParty>(new
        {
            SeparationRecordId = sepId,
            EmployeeId = empId,
            Party = ProcessParties.Payroll,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = (string?)null,
            TenantId = (Guid?)null,
            Compensate = false,
            RemainingLeaveDays = 0
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IExecuteSeparationParty>(scope.Harness);
        payrollDb.ChangeTracker.Clear();
        var profile = await payrollDb.EmployeePayProfiles.FirstAsync(p => p.EmployeeId == empId);
        Assert.False(profile.IsActive);
    }

    private static async Task<ChainScope> CreateActivationChainScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                IntegrationTestHelpers.AddDeterministicServiceClients(services);
                var contractsConn = new SqliteConnection("Data Source=:memory:");
                contractsConn.Open();
                connections.Add(contractsConn);
                IntegrationTestHelpers.AddSqliteDbContext<ContractsDbContext>(services, contractsConn);
                services.AddSingleton<IExecutionContextAccessor>(new StubExecutionContextAccessor());
                services.AddSingleton(Moq.Mock.Of<IEmployeeLifecycleGate>());
                services.AddContractsApplicationForTests();
                services.AddScoped<IEventPublisher>(sp =>
                    new MassTransitEventPublisher(
                        sp.GetRequiredService<IPublishEndpoint>(),
                        sp.GetRequiredService<IExecutionContextAccessor>()));

                var payrollConn = new SqliteConnection("Data Source=:memory:");
                payrollConn.Open();
                connections.Add(payrollConn);
                IntegrationTestHelpers.AddSqliteDbContext<Payroll.Infrastructure.Data.PayrollDbContext>(services, payrollConn);
                PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
            },
            cfg => cfg.AddConsumer<ContractActivatedConsumer>());

        await scope.GetDb<ContractsDbContext>().Database.EnsureCreatedAsync();
        await scope.GetDb<Payroll.Infrastructure.Data.PayrollDbContext>().Database.EnsureCreatedAsync();
        return new ChainScope(scope);
    }

    private static async Task<ChainScope> CreateSeparationChainScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var payrollConn = new SqliteConnection("Data Source=:memory:");
                payrollConn.Open();
                connections.Add(payrollConn);
                IntegrationTestHelpers.AddSqliteDbContext<Payroll.Infrastructure.Data.PayrollDbContext>(services, payrollConn);
                PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
            },
            cfg => cfg.AddConsumer<SeparationPartyConsumer>());

        await scope.GetDb<Payroll.Infrastructure.Data.PayrollDbContext>().Database.EnsureCreatedAsync();
        return new ChainScope(scope);
    }

    private sealed class ChainScope(MassTransitTestScope inner) : IAsyncDisposable
    {
        public ServiceProvider Provider => inner.Provider;
        public MassTransit.Testing.ITestHarness Harness => inner.Harness;
        public T GetDb<T>() where T : notnull => inner.GetDb<T>();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class StubExecutionContextAccessor : IExecutionContextAccessor
    {
        public string? CorrelationId => "test-correlation";
        public string SourceSystem => "IntegrationTest";
    }
}
