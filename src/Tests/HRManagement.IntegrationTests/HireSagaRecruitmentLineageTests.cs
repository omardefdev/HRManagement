using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.TestSupport.CodeGeneration;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class HireSagaRecruitmentLineageTests
{
    [Fact]
    public async Task PublishHireCase_PassesRecruitmentApplicationId_ToStartHireSaga()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var recruitmentApplicationId = Guid.NewGuid();
            var txn = new ServiceTransaction
            {
                EmployeeId = employeeId,
                TransactionType = ServiceTransactionType.Appointment,
                Notes = $"فاطمة أحمد — مصدر توظيف: دائم | recruitmentApplicationId:{recruitmentApplicationId:D}"
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
                HireWorkflowRules.MarkStepCompleted(item, notes: "test", evidenceRef: "test:evidence");
                db.HireChecklists.Add(item);
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new PublishHireCaseCommand(txn.Id));

            Assert.True(success, error);
            Assert.Equal(recruitmentApplicationId, spy.LastRecruitmentApplicationId);
        }
    }

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
        services.ReplaceCodeGenerationClientWithFake();
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
}
