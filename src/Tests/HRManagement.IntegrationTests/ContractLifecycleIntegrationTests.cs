using FluentValidation;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.AdvanceRequests;
using HR.Contracts.Application.Features.Contracts;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.SharedKernel.Enums;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HRManagement.IntegrationTests.Support;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class ContractLifecycleIntegrationTests
{
    private static async Task<(IServiceScope Scope, SpyEventPublisher Spy, ContractsDbContext Db)> CreateContractsHandlerScopeAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var spy = new SpyEventPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        IntegrationTestHelpers.AddDeterministicServiceClients(services);
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IEventPublisher>(spy);
        // QA-034: repository, gate, MediatR handlers and the HR.Core active-regime fake (law50) in one place.
        services.AddContractsApplicationForTests();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        return (scope, spy, db);
    }

    [Fact]
    public async Task ActivateContract_MissingCommencement_ReturnsError_NoPublish()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var contractType = TestDataFactory.CreateContractType();
            var employeeId = Guid.NewGuid();
            var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new ActivateContractCommand(contract.Id));

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Equal(0, spy.ContractActivatedCount);
            db.ChangeTracker.Clear();
            var stored = await db.Contracts.FindAsync(contract.Id);
            Assert.Equal(ContractStatus.Pending, stored!.Status);
        }
    }

    [Fact]
    public async Task ActivateContract_ValidPendingContract_ActivatesAndPublishesOnce()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var contractType = TestDataFactory.CreateContractType();
            var employeeId = Guid.NewGuid();
            var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            db.CommencementRecords.Add(TestDataFactory.CreateCommencement(employeeId, contract.Id));
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new ActivateContractCommand(contract.Id));

            Assert.True(success);
            Assert.Null(error);
            Assert.Equal(1, spy.ContractActivatedCount);
            Assert.NotNull(spy.LastActivatedContract);
            Assert.Equal(contract.Id, spy.LastActivatedContract!.Id);

            db.ChangeTracker.Clear();
            var stored = await db.Contracts.FindAsync(contract.Id);
            Assert.Equal(ContractStatus.Active, stored!.Status);
        }
    }

    [Fact]
    public async Task PublishSeparation_AlreadyPublished_ReturnsError()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var separation = TestDataFactory.CreateSeparationRecord(employeeId);
            separation.SeparationEventPublished = true;
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new PublishSeparationCommand(separation.Id));

            Assert.False(success);
            Assert.Contains("مسبقاً", error ?? string.Empty);
            Assert.Equal(0, spy.SeparationCompletedCount);
        }
    }

    [Fact]
    public async Task PublishSeparation_Complete_PublishesOnce_IdempotentOnRetry()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var contractType = TestDataFactory.CreateContractType();
            var employeeId = Guid.NewGuid();
            var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
            contract.Status = ContractStatus.Active;
            var separation = TestDataFactory.CreateSeparationRecord(employeeId, contract.Id);
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();
            db.SeparationChecklists.AddRange(
                SeparationChecklistDefaults.CreateForRecord(separation.Id).Select(step =>
                {
                    step.IsCompleted = true;
                    step.CompletedAt = DateTime.UtcNow;
                    step.CompletedBy = Guid.NewGuid();
                    step.EvidenceRef = $"evidence:{step.Step}";
                    step.SourceSystem = "Test";
                    return step;
                }));
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (firstSuccess, firstError) = await mediator.Send(new PublishSeparationCommand(separation.Id));
            Assert.True(firstSuccess);
            Assert.Null(firstError);
            Assert.Equal(1, spy.SeparationCompletedCount);

            var (secondSuccess, _) = await mediator.Send(new PublishSeparationCommand(separation.Id));
            Assert.False(secondSuccess);
            Assert.Equal(1, spy.SeparationCompletedCount);

            db.ChangeTracker.Clear();
            var stored = await db.SeparationRecords.FindAsync(separation.Id);
            Assert.True(stored!.SeparationEventPublished);
        }
    }

    [Fact]
    public async Task PublishSeparation_IncompleteChecklist_ReturnsError_DoesNotAutoComplete()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var contractType = TestDataFactory.CreateContractType();
            var employeeId = Guid.NewGuid();
            var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
            contract.Status = ContractStatus.Active;
            var separation = TestDataFactory.CreateSeparationRecord(employeeId, contract.Id);
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();

            var checklist = SeparationChecklistDefaults.CreateForRecord(separation.Id).ToList();
            foreach (var step in checklist.Where(c => c.Step != SeparationStep.AccountDisabled))
            {
                step.IsCompleted = true;
                step.CompletedAt = DateTime.UtcNow;
                step.CompletedBy = Guid.NewGuid();
                step.EvidenceRef = $"evidence:{step.Step}";
                step.SourceSystem = "Test";
            }
            db.SeparationChecklists.AddRange(checklist);
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new PublishSeparationCommand(separation.Id));

            Assert.False(success);
            Assert.Contains("AccountDisabled", error ?? string.Empty);
            Assert.Equal(0, spy.SeparationCompletedCount);

            db.ChangeTracker.Clear();
            var storedChecklist = await db.SeparationChecklists
                .Where(c => c.SeparationRecordId == separation.Id)
                .ToListAsync();
            Assert.Contains(storedChecklist, c => c.Step == SeparationStep.AccountDisabled && !c.IsCompleted);
        }
    }

    [Fact]
    public async Task CompleteSeparationChecklistStep_RecordsActorAndEvidence()
    {
        var (scope, _, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var separation = TestDataFactory.CreateSeparationRecord(employeeId);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();
            db.SeparationChecklists.AddRange(SeparationChecklistDefaults.CreateForRecord(separation.Id));
            await db.SaveChangesAsync();

            var actorId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new CompleteSeparationChecklistStepCommand(
                separation.Id,
                SeparationStep.CardRevoked,
                "badge returned",
                actorId,
                "badge:123",
                "HRContracts"));

            Assert.True(success);
            Assert.Null(error);

            db.ChangeTracker.Clear();
            var step = await db.SeparationChecklists.FirstAsync(c =>
                c.SeparationRecordId == separation.Id && c.Step == SeparationStep.CardRevoked);
            Assert.True(step.IsCompleted);
            Assert.Equal(actorId, step.CompletedBy);
            Assert.Equal("badge:123", step.EvidenceRef);
            Assert.Equal("HRContracts", step.SourceSystem);
            Assert.Equal("badge returned", step.Notes);
        }
    }

    [Fact]
    public async Task OverrideSeparationChecklistStep_RequiresDualApproval()
    {
        var (scope, _, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var separation = TestDataFactory.CreateSeparationRecord(employeeId);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();
            db.SeparationChecklists.AddRange(SeparationChecklistDefaults.CreateForRecord(separation.Id));
            await db.SaveChangesAsync();

            var actorId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new OverrideSeparationChecklistStepCommand(
                separation.Id,
                SeparationStep.AssetsReturned,
                "manual override",
                actorId,
                actorId));

            Assert.False(success);
            Assert.Contains("موافقة مزدوجة", error ?? string.Empty);
        }
    }

    [Fact]
    public async Task OverrideSeparationChecklistStep_StoresOverrideEvidence()
    {
        var (scope, _, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var separation = TestDataFactory.CreateSeparationRecord(employeeId);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();
            db.SeparationChecklists.AddRange(SeparationChecklistDefaults.CreateForRecord(separation.Id));
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var actorId = Guid.NewGuid();
            var approverId = Guid.NewGuid();
            var (success, error) = await mediator.Send(new OverrideSeparationChecklistStepCommand(
                separation.Id,
                SeparationStep.AssetsReturned,
                "missing document",
                actorId,
                approverId));

            Assert.True(success);
            Assert.Null(error);

            db.ChangeTracker.Clear();
            var step = await db.SeparationChecklists.FirstAsync(c =>
                c.SeparationRecordId == separation.Id && c.Step == SeparationStep.AssetsReturned);
            Assert.True(step.IsCompleted);
            Assert.Equal(actorId, step.CompletedBy);
            Assert.Equal("OVERRIDE:missing document", step.EvidenceRef);
            Assert.Equal("Override", step.SourceSystem);
            Assert.Contains(approverId.ToString(), step.Notes ?? string.Empty);
        }
    }

    [Fact]
    public async Task UpdateAndDeleteSeparation_AfterPublish_AreBlocked()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var contractType = TestDataFactory.CreateContractType();
            var employeeId = Guid.NewGuid();
            var contract = TestDataFactory.CreatePendingContract(employeeId, contractType);
            contract.Status = ContractStatus.Active;
            var separation = TestDataFactory.CreateSeparationRecord(employeeId, contract.Id);
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            db.SeparationRecords.Add(separation);
            await db.SaveChangesAsync();
            db.SeparationChecklists.AddRange(
                SeparationChecklistDefaults.CreateForRecord(separation.Id).Select(step =>
                {
                    step.IsCompleted = true;
                    step.CompletedAt = DateTime.UtcNow;
                    step.CompletedBy = Guid.NewGuid();
                    step.EvidenceRef = $"evidence:{step.Step}";
                    step.SourceSystem = "Test";
                    return step;
                }));
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var publish = await mediator.Send(new PublishSeparationCommand(separation.Id));
            Assert.True(publish.Success);
            Assert.Equal(1, spy.SeparationCompletedCount);

            var update = await mediator.Send(new UpdateSeparationRecordCommand(separation.Id, new HR.Contracts.Application.Models.SeparationRecordDto(
                separation.Id,
                separation.EmployeeId,
                separation.ContractId,
                separation.SeparationType,
                separation.EffectiveDate,
                "changed",
                separation.ApprovedBy,
                separation.Documents,
                true)));
            var delete = await mediator.Send(new DeleteSeparationRecordCommand(separation.Id));

            Assert.False(update);
            Assert.False(delete);
        }
    }

    [Fact]
    public async Task ApproveAdvanceRequest_OpenAdvance_Blocked()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var existingAdvanceId = Guid.NewGuid();
            db.AdvanceRequests.Add(new AdvanceRequest
            {
                Id = existingAdvanceId,
                EmployeeId = employeeId,
                Amount = 5000m,
                Status = ApprovalStatus.Approved
            });
            db.AdvanceInstallmentPlans.Add(new AdvanceInstallmentPlan
            {
                AdvanceRequestId = existingAdvanceId,
                TotalAmount = 5000m,
                MonthlyInstallment = 500m,
                NumberOfInstallments = 10,
                RemainingAmount = 3000m,
                StartMonth = DateTime.UtcNow
            });

            var newTx = TestDataFactory.CreateLeaveTransaction(employeeId);
            var pendingAdvance = TestDataFactory.CreatePendingAdvance(employeeId, 2000m, newTx.Id);
            db.ServiceTransactions.Add(newTx);
            db.AdvanceRequests.Add(pendingAdvance);
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (success, error) = await mediator.Send(new ApproveAdvanceRequestCommand(pendingAdvance.Id, Guid.NewGuid()));

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Equal(0, spy.AdvanceApprovedCount);
        }
    }

    [Fact]
    public async Task CreateAdvanceRequest_ExceedsLimit_ThrowsValidation()
    {
        var (scope, _, _) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var employeeId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<ValidationException>(() =>
                mediator.Send(new CreateAdvanceRequestCommand(new HR.Contracts.Application.Models.AdvanceRequestDto(
                    Id: null,
                    EmployeeId: employeeId,
                    Amount: LeaveAndAdvanceRules.DefaultMaxSingleAdvance + 1m,
                    Reason: null,
                    Status: ApprovalStatus.Pending,
                    ApprovedBy: null))));
        }
    }
}
