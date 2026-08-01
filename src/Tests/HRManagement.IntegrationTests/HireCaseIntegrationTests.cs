using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class HireCaseIntegrationTests
{
    private static async Task<(IServiceScope Scope, SpyEventPublisher Spy, ContractsDbContext Db)> CreateScopeAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var spy = new SpyEventPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IContractsRepository, ContractsRepository>();
        services.AddSingleton<IEventPublisher>(spy);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ProcessCandidateAcceptedHandler>();
            cfg.RegisterServicesFromAssemblyContaining<PublishHireCaseHandler>();
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (!await db.ContractTypes.AnyAsync(c => c.Code == "DEFAULT"))
        {
            db.ContractTypes.Add(TestDataFactory.CreateContractType("DEFAULT"));
            await db.SaveChangesAsync();
        }

        return (scope, spy, db);
    }

    [Fact]
    public async Task ProcessCandidateAccepted_CreatesAppointmentTransactionAndChecklist()
    {
        var (scope, _, db) = await CreateScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ProcessCandidateAcceptedCommand(
                employeeId, "دائم", DateOnly.FromDateTime(DateTime.UtcNow), "فاطمة أحمد"));

            var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
            Assert.Equal(ServiceTransactionType.Appointment, txn.TransactionType);
            Assert.Equal(ServiceTransactionState.InProcess, txn.State);
            Assert.Contains("فاطمة أحمد", txn.Notes);

            db.ChangeTracker.Clear();
            var steps = await db.HireChecklists.Where(c => c.ServiceTransactionId == txn.Id).ToListAsync();
            Assert.Equal(HireChecklistDefaults.DefaultSteps.Count, steps.Count);
            Assert.True(steps.Single(s => s.Step == HireStep.CandidateReceived).IsCompleted);
            Assert.True(steps.Single(s => s.Step == HireStep.PersonalFileOpened).IsCompleted);
        }
    }

    [Fact]
    public async Task ProcessCandidateAccepted_Duplicate_DoesNotCreateSecondAppointment()
    {
        var (scope, _, db) = await CreateScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ProcessCandidateAcceptedCommand(
                employeeId, "دائم", DateOnly.FromDateTime(DateTime.UtcNow), "فاطمة أحمد"));
            await mediator.Send(new ProcessCandidateAcceptedCommand(
                employeeId, "دائم", DateOnly.FromDateTime(DateTime.UtcNow), "فاطمة أحمد"));

            var count = await db.ServiceTransactions.CountAsync(t =>
                t.EmployeeId == employeeId && t.TransactionType == ServiceTransactionType.Appointment);
            Assert.Equal(1, count);

            var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
            var steps = await db.HireChecklists.CountAsync(c => c.ServiceTransactionId == txn.Id);
            Assert.Equal(HireChecklistDefaults.DefaultSteps.Count, steps);
            Assert.All(
                await db.HireChecklists.Where(c => c.ServiceTransactionId == txn.Id).ToListAsync(),
                c => Assert.False(string.IsNullOrWhiteSpace(c.OwnerRole)));
        }
    }

    [Fact]
    public async Task PublishHireCase_IncompleteChecklist_DoesNotPublish()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var txn = await SeedExecutingAppointmentAsync(db, employeeId, completeChecklist: false);
            db.ChangeTracker.Clear();
            var (success, error) = await mediator.Send(new PublishHireCaseCommand(txn.Id));

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Equal(0, spy.HireSagaStartedCount);
        }
    }

    [Fact]
    public async Task PublishHireCase_CompleteChecklist_PublishesOnce()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var txn = await SeedExecutingAppointmentAsync(db, employeeId, completeChecklist: true);
            db.ChangeTracker.Clear();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new PublishHireCaseCommand(txn.Id));

            Assert.True(success);
            Assert.Null(error);
            Assert.Equal(1, spy.HireSagaStartedCount);
            Assert.Equal(txn.Id, spy.LastHireServiceTransactionId);
        }
    }

    private static async Task<ServiceTransaction> SeedExecutingAppointmentAsync(
        ContractsDbContext db,
        Guid employeeId,
        bool completeChecklist)
    {
        var txn = new ServiceTransaction
        {
            EmployeeId = employeeId,
            TransactionType = ServiceTransactionType.Appointment
        };
        txn.MoveTo(ServiceTransactionState.Submitted, null, null);
        txn.MoveTo(ServiceTransactionState.UnderReview, null, null);
        txn.MoveTo(ServiceTransactionState.InProcess, null, null);
        txn.MoveTo(ServiceTransactionState.PendingApproval, null, null);
        txn.MoveTo(ServiceTransactionState.Approved, null, null);
        txn.MoveTo(ServiceTransactionState.Executing, null, null);

        db.ServiceTransactions.Add(txn);
        foreach (var item in HireChecklistDefaults.CreateForTransaction(txn.Id))
        {
            if (completeChecklist)
                HireWorkflowRules.MarkStepCompleted(item, notes: "seed", evidenceRef: "test:seed");
            db.HireChecklists.Add(item);
        }

        await db.SaveChangesAsync();
        return txn;
    }
}
