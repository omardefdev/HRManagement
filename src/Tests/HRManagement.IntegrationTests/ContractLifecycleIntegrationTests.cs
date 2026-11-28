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

    // PLAN-60: the four separation tests that AC-QA-34e counts by name keep their names and their meaning; the record reaches its status
    // the product's way (SeparationJourneyDriver: the factory, the engine's outcome, the decision, the notification), and the publication
    // they name is now the execution.
    [Fact]
    public async Task PublishSeparation_AlreadyPublished_ReturnsError()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, new DateOnly(2026, 6, 30));
            Assert.True((await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id)).Success);
            var executed = spy.SeparationCompletedCount;

            var (success, error) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);

            Assert.False(success);
            Assert.Contains("مسبقاً", error ?? string.Empty);
            Assert.Equal(executed, spy.SeparationCompletedCount);
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
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, employeeId, contract.Id, new DateOnly(2026, 6, 30));

            var (firstSuccess, firstError) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);
            Assert.True(firstSuccess, firstError);
            Assert.Null(firstError);
            Assert.Equal(1, spy.SeparationCompletedCount);

            var (secondSuccess, _) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);
            Assert.False(secondSuccess);
            Assert.Equal(1, spy.SeparationCompletedCount);

            db.ChangeTracker.Clear();
            var stored = await db.SeparationRecords.FindAsync(id);
            Assert.Equal(SeparationStatus.Executing, stored!.Status);
            Assert.True(stored.SeparationEventPublished);
            var ended = await db.Contracts.FindAsync(contract.Id);
            Assert.Equal((ContractStatus.Terminated, (DateOnly?)new DateOnly(2026, 6, 30)), (ended!.Status, ended.EndDate));
        }
    }

    [Fact]
    public async Task PublishSeparation_IncompleteChecklist_ReturnsError_DoesNotAutoComplete()
    {
        var (scope, spy, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            // PLAN-60: the employee's notification is a pre-execution item; the parties' items only follow the execution.
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(
                scope.ServiceProvider, Guid.NewGuid(), null, new DateOnly(2026, 6, 30), notify: false);

            var (success, error) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);

            Assert.False(success);
            Assert.Contains("تبليغ الموظف", error ?? string.Empty);
            Assert.Equal(0, spy.SeparationCompletedCount);

            db.ChangeTracker.Clear();
            var storedChecklist = await db.SeparationChecklists
                .Where(c => c.SeparationRecordId == id)
                .ToListAsync();
            Assert.Contains(storedChecklist, c => c.Step == SeparationStep.EmployeeNotified && !c.IsCompleted);
            Assert.Equal(SeparationStatus.Approved, (await db.SeparationRecords.SingleAsync(r => r.Id == id)).Status);
        }
    }

    [Fact]
    public async Task CompleteSeparationChecklistStep_RecordsActorAndEvidence()
    {
        var (scope, _, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var id = await SeparationJourneyDriver.CreateAsync(scope.ServiceProvider, Guid.NewGuid(), null, new DateOnly(2026, 6, 30));
            var actorId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            // PLAN-60 (§4): a manual item, by the signed-in user, with evidence.
            var (success, error) = await mediator.Send(new CompleteManualStepCommand(
                id, SeparationStep.EmployeeNotified, "notice:123", null, "notice handed over", actorId, null));

            Assert.True(success);
            Assert.Null(error);

            db.ChangeTracker.Clear();
            var step = await db.SeparationChecklists.FirstAsync(c => c.SeparationRecordId == id && c.Step == SeparationStep.EmployeeNotified);
            Assert.True(step.IsCompleted);
            Assert.Equal(actorId, step.CompletedBy);
            Assert.Equal("notice:123", step.EvidenceRef);
            Assert.Equal("HRContracts", step.SourceSystem);
            Assert.Equal("notice handed over", step.Notes);
        }
    }

    // PLAN-60 (§4): the old override took its second approver as a GUID from the form. An exception is now two requests from two
    // sessions: the requester never approves his own.
    [Fact]
    public async Task StepException_RequiresASecondPerson()
    {
        var (scope, _, _) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var id = await SeparationJourneyDriver.CreateAsync(scope.ServiceProvider, Guid.NewGuid(), null, new DateOnly(2026, 6, 30));
            var actorId = Guid.NewGuid();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            Assert.True((await mediator.Send(new RequestStepExceptionCommand(
                id, SeparationStep.EmployeeNotified, "الموظف خارج البلاد ولا يمكن تبليغه حالياً", actorId))).Success);
            var (success, error) = await mediator.Send(new ApproveStepExceptionCommand(id, SeparationStep.EmployeeNotified, actorId));

            Assert.False(success);
            Assert.Equal(SeparationChecklistRules.SecondPersonMessage, error);
        }
    }

    [Fact]
    public async Task StepException_ApprovedByASecondPerson_IsExceptedNotCompleted()
    {
        var (scope, _, db) = await CreateContractsHandlerScopeAsync();
        using (scope)
        {
            var id = await SeparationJourneyDriver.CreateAsync(scope.ServiceProvider, Guid.NewGuid(), null, new DateOnly(2026, 6, 30));
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var requester = Guid.NewGuid();
            var approver = Guid.NewGuid();
            Assert.True((await mediator.Send(new RequestStepExceptionCommand(
                id, SeparationStep.EmployeeNotified, "الموظف خارج البلاد ولا يمكن تبليغه حالياً", requester))).Success);
            var (success, error) = await mediator.Send(new ApproveStepExceptionCommand(id, SeparationStep.EmployeeNotified, approver));

            Assert.True(success, error);
            db.ChangeTracker.Clear();
            var step = await db.SeparationChecklists.FirstAsync(c => c.SeparationRecordId == id && c.Step == SeparationStep.EmployeeNotified);
            Assert.False(step.IsCompleted);
            Assert.True(step.IsExcepted);
            Assert.Equal((requester, approver), (step.ExceptionRequestedBy!.Value, step.ExceptionApprovedBy!.Value));
        }
    }

    // PLAN-60: «Delete» is gone (a record is never deleted); its successor, the cancellation, is refused after the execution as the
    // edit is — the name is AC-QA-34e's.
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
            db.ContractTypes.Add(contractType);
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, employeeId, contract.Id, new DateOnly(2026, 6, 30));
            Assert.True((await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id)).Success);
            Assert.Equal(1, spy.SeparationCompletedCount);

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var update = await mediator.Send(new UpdateSeparationRecordCommand(id, new HR.Contracts.Application.Models.SeparationRecordDto(
                id, employeeId, contract.Id, SeparationType.Resignation, new DateOnly(2026, 6, 30), "changed", null)));
            var cancel = await mediator.Send(new CancelSeparationCommand(id, "إلغاء بعد التنفيذ", Guid.NewGuid()));

            Assert.False(update.Success);
            Assert.Equal(UpdateSeparationRecordHandler.NotDraftMessage, update.Error);
            Assert.False(cancel.Success);
            Assert.Equal(SeparationWorkflowRules.ExecutingCancelMessage, cancel.Error);
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
