using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.Contracts;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Messaging;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.IntegrationTests;

/// <summary>
/// Release-critical workflow chain: contract activation → leave event → payroll calculate → separation.
/// Full payroll post lifecycle is covered by PayrollRunLifecycleIntegrationTests (SQLite RowVersion limits multi-step post here).
/// Validates cross-service assumptions without requiring a live Docker stack.
/// </summary>
[Collection("IntegrationSerial")]
public class EndToEndReleaseWorkflowSmokeIntegrationTests
{
    [Fact]
    public async Task ReleaseWorkflow_Contract_Leave_PayrollPost_Separation_ChainSucceeds()
    {
        await using var scope = await CreateReleaseWorkflowScopeAsync();
        var contractsDb = scope.GetDb<ContractsDbContext>();
        var payrollDb = scope.GetDb<Payroll.Infrastructure.Data.PayrollDbContext>();
        var employeeId = Guid.NewGuid();

        var contractType = TestDataFactory.CreateContractType();
        var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
        contractsDb.ContractTypes.Add(contractType);
        contractsDb.Contracts.Add(contract);
        contractsDb.CommencementRecords.Add(TestDataFactory.CreateCommencement(employeeId, contract.Id));
        await contractsDb.SaveChangesAsync();

        using (var mediatorScope = scope.Provider.CreateScope())
        {
            var mediator = mediatorScope.ServiceProvider.GetRequiredService<IMediator>();
            var (activated, _) = await mediator.Send(new ActivateContractCommand(contract.Id));
            Assert.True(activated);
        }

        await IntegrationTestHelpers.AssertConsumedAsync<IContractActivated>(scope.Harness);
        payrollDb.ChangeTracker.Clear();
        Assert.NotNull(await payrollDb.EmployeePayProfiles
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.IsActive));

        await scope.Harness.Bus.Publish<ILeaveApproved>(new
        {
            TransactionId = Guid.NewGuid(),
            LeaveRequestId = Guid.NewGuid(),
            EmployeeId = employeeId,
            ApprovedBy = Guid.NewGuid(),
            LeaveType = "Unpaid",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(5),
            DurationDays = 5,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "ReleaseWorkflowSmoke"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ILeaveApproved>(scope.Harness);

        var payConfig = Payroll.Domain.Services.PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        payConfig.ContentHash = Payroll.Domain.Services.PayrollConfigurationHashService.ComputeContentHash(payConfig);
        payConfig.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        payrollDb.PayrollConfigurations.Add(payConfig);
        await payrollDb.SaveChangesAsync();

        using (var mediatorScope = scope.Provider.CreateScope())
        {
            var mediator = mediatorScope.ServiceProvider.GetRequiredService<IMediator>();
            var periodStart = new DateTime(2026, 5, 1);
            var periodEnd = new DateTime(2026, 5, 31);
            var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(periodStart, periodEnd, DateTime.UtcNow));
            Assert.True(created);
            var (calculated, lines, calcErr) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
            Assert.True(calculated, calcErr ?? "calculate failed");
            Assert.True(lines >= 1);
            payrollDb.ChangeTracker.Clear();
            var run = await payrollDb.PayrollRuns.Include(r => r.SalaryLines).FirstAsync(r => r.Id == runId);
            Assert.Equal(PayrollRunStatus.Calculated, run.Status);
            Assert.NotEmpty(run.SalaryLines);
        }

        contract.Status = ContractStatus.Active;
        var separation = TestDataFactory.CreateSeparationRecord(employeeId, contract.Id);
        contractsDb.SeparationRecords.Add(separation);
        await contractsDb.SaveChangesAsync();

        // DOM-001: publish requires all required checklist steps completed
        contractsDb.SeparationChecklists.AddRange(
            SeparationChecklistDefaults.CreateForRecord(separation.Id).Select(step =>
            {
                step.IsCompleted = true;
                step.CompletedAt = DateTime.UtcNow;
                step.CompletedBy = Guid.NewGuid();
                step.EvidenceRef = $"evidence:{step.Step}";
                step.SourceSystem = "ReleaseWorkflowSmoke";
                return step;
            }));
        await contractsDb.SaveChangesAsync();

        using (var mediatorScope = scope.Provider.CreateScope())
        {
            var mediator = mediatorScope.ServiceProvider.GetRequiredService<IMediator>();
            var (sepOk, sepError) = await mediator.Send(new PublishSeparationCommand(separation.Id));
            Assert.True(sepOk, sepError ?? "PublishSeparation failed without error message");
        }

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        payrollDb.ChangeTracker.Clear();
        var profile = await payrollDb.EmployeePayProfiles.FirstAsync(p => p.EmployeeId == employeeId);
        Assert.False(profile.IsActive);
    }

    private static async Task<ReleaseWorkflowScope> CreateReleaseWorkflowScopeAsync()
    {
        var inner = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var contractsConn = new SqliteConnection("Data Source=:memory:");
                contractsConn.Open();
                connections.Add(contractsConn);
                IntegrationTestHelpers.AddSqliteDbContext<ContractsDbContext>(services, contractsConn);
                services.AddScoped<IContractsRepository, ContractsRepository>();
                services.AddSingleton<IExecutionContextAccessor>(new StubExecutionContextAccessor());
                services.AddSingleton<IEmployeeLifecycleGate, HR.Contracts.Infrastructure.Services.PassThroughEmployeeLifecycleGate>();
                services.AddMediatR(cfg =>
                {
                    cfg.RegisterServicesFromAssemblyContaining<ActivateContractHandler>();
                    cfg.RegisterServicesFromAssemblyContaining<PublishSeparationHandler>();
                });
                services.AddScoped<IEventPublisher>(sp =>
                    new MassTransitEventPublisher(
                        sp.GetRequiredService<IPublishEndpoint>(),
                        sp.GetRequiredService<IExecutionContextAccessor>()));
                services.AddSingleton<SagaMetrics>();
                services.Configure<SeparationSagaOptions>(o =>
                {
                    o.MandatoryParties = [ProcessParties.Payroll];
                    o.PartyTimeoutMinutes = 30;
                });

                var payrollConn = new SqliteConnection("Data Source=:memory:");
                payrollConn.Open();
                connections.Add(payrollConn);
                IntegrationTestHelpers.AddSqliteDbContext<Payroll.Infrastructure.Data.PayrollDbContext>(services, payrollConn);
                PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
            },
            cfg =>
            {
                cfg.AddSagaStateMachine<SeparationStateMachine, SeparationSagaState>().InMemoryRepository();
                cfg.AddConsumer<ContractActivatedConsumer>();
                cfg.AddConsumer<LeaveApprovedConsumer>();
                cfg.AddConsumer<SeparationPartyConsumer>();
                cfg.AddConsumer<SeparationCompletedConsumer>();
                cfg.UsingInMemory((context, busCfg) =>
                {
                    busCfg.UseDelayedMessageScheduler();
                    busCfg.UseConsumeFilter(typeof(TenantIdConsumeFilter<>), context);
                    busCfg.ConfigureEndpoints(context);
                });
            },
            enableInboxFilter: false);

        await inner.GetDb<ContractsDbContext>().Database.EnsureCreatedAsync();
        await inner.GetDb<Payroll.Infrastructure.Data.PayrollDbContext>().Database.EnsureCreatedAsync();
        return new ReleaseWorkflowScope(inner);
    }

    private sealed class ReleaseWorkflowScope(MassTransitTestScope inner) : IAsyncDisposable
    {
        public ServiceProvider Provider => inner.Provider;
        public MassTransit.Testing.ITestHarness Harness => inner.Harness;
        public T GetDb<T>() where T : notnull => inner.GetDb<T>();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class StubExecutionContextAccessor : IExecutionContextAccessor
    {
        public string? CorrelationId => "release-smoke";
        public string SourceSystem => "ReleaseWorkflowSmoke";
    }
}
