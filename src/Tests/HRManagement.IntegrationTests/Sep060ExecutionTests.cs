using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Sagas;
using HR.Contracts.Web.Controllers;
using HRManagement.IntegrationTests.Support;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport.Integration;
using HRManagement.TestSupport.Workflow;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60 (§5, P-10): the execution — each refusal in the plan's order, Damascus's date around 21:00 UTC, the contract's end, nothing saved
/// when the save fails — the cancellation by status, the interventions, and the saga's end.
/// </summary>
public sealed class Sep060ExecutionTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class HoldGate(bool hold) : IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>(hold ? (false, "الموظف تحت حجز قانوني (Legal Hold) — لا يمكن تنفيذ الفصل.") : (true, null));
    }

    /// <summary>Fails the save that would execute a record, to show that nothing of the execution is kept.</summary>
    private sealed class FailExecutionSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<SeparationRecord>().Any(e => e.Entity.Status == SeparationStatus.Executing))
                throw new DbUpdateException("PLAN-60 test: the database refused the save.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed record Host(IServiceScope Scope, ContractsDbContext Db, SpyEventPublisher Spy, FixedTime Time,
        FakeWorkflowApprovalClient Engine, FailExecutionSave Failure) : IDisposable
    {
        public IServiceProvider Services => Scope.ServiceProvider;
        public void Dispose() => Scope.Dispose();
    }

    private static async Task<Host> HostAsync(
        DateTimeOffset? now = null, bool hold = false, FakeRegimeMode regime = FakeRegimeMode.Law50)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var spy = new SpyEventPublisher();
        var time = new FixedTime(now ?? new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero));
        var engine = new FakeWorkflowApprovalClient();
        var failure = new FailExecutionSave();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection).AddInterceptors(failure));
        services.AddSingleton<IEventPublisher>(spy);
        services.AddSingleton<IEmployeeLifecycleGate>(new HoldGate(hold));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IWorkflowApprovalClient>(engine);
        services.AddContractsApplicationForTests(new FakeHrCoreActiveRegimeClient(regime));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        return new Host(scope, db, spy, time, engine, failure);
    }

    private static async Task<Contract> ActiveContractAsync(Host h, Guid employee, DateOnly start)
    {
        var type = TestDataFactory.CreateContractType();
        var contract = TestDataFactory.CreatePendingContract(employee, type);
        contract.Status = ContractStatus.Active;
        contract.StartDate = start;
        h.Db.ContractTypes.Add(type);
        h.Db.Contracts.Add(contract);
        await h.Db.SaveChangesAsync();
        return contract;
    }

    private static Task<SeparationRecord> ReadAsync(Host h, Guid id) =>
        h.Db.SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == id);

    // ── The order of the checks.

    [Fact]
    public async Task A_record_that_is_not_approved_is_refused_first_whatever_else_is_missing()
    {
        using var h = await HostAsync(hold: true, regime: FakeRegimeMode.NoRegime);
        var id = await SeparationJourneyDriver.CreateAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2027, 6, 1));
        Assert.Equal((false, ExecuteSeparationHandler.NotApprovedMessage), await SeparationJourneyDriver.ExecuteAsync(h.Services, id));
        Assert.Equal(0, h.Spy.SeparationSagaStartedCount);
    }

    [Fact]
    public async Task Then_the_decision_then_the_items_then_the_date_then_the_hold_then_the_regime()
    {
        using var h = await HostAsync(hold: true, regime: FakeRegimeMode.NoRegime);
        var future = new DateOnly(2026, 12, 15);
        var id = await SeparationJourneyDriver.CreateAsync(h.Services, Guid.NewGuid(), null, future);
        await SeparationJourneyDriver.ApproveAsync(h.Services, id);
        Assert.Equal((false, ExecuteSeparationHandler.NoDecisionMessage), await SeparationJourneyDriver.ExecuteAsync(h.Services, id));

        await SeparationJourneyDriver.IssueDecisionAsync(h.Services, id);
        var items = await SeparationJourneyDriver.ExecuteAsync(h.Services, id);
        Assert.Contains("تبليغ الموظف", items.Error);

        await SeparationJourneyDriver.NotifyAsync(h.Services, id);
        Assert.Equal((false, ExecuteSeparationHandler.NotYetMessage(future, new DateOnly(2026, 11, 28))),
            await SeparationJourneyDriver.ExecuteAsync(h.Services, id));

        h.Time.Now = new DateTimeOffset(2026, 12, 15, 9, 0, 0, TimeSpan.Zero);
        Assert.Contains("Legal Hold", (await SeparationJourneyDriver.ExecuteAsync(h.Services, id)).Error);
        Assert.Equal(SeparationStatus.Approved, (await ReadAsync(h, id)).Status);
        Assert.Equal(0, h.Spy.SeparationSagaStartedCount);
    }

    [Fact]
    public async Task With_no_hold_the_regime_is_the_last_check()
    {
        using var h = await HostAsync(regime: FakeRegimeMode.NoRegime);
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 11, 1));
        Assert.Equal((false, ServiceRegimeSeparationRules.RegimeRulesNotFoundMessage), await SeparationJourneyDriver.ExecuteAsync(h.Services, id));
        Assert.Equal(SeparationStatus.Approved, (await ReadAsync(h, id)).Status);
    }

    [Fact]
    public async Task An_execution_needs_the_signed_in_user()
    {
        using var h = await HostAsync();
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 11, 1));
        var refused = await h.Services.GetRequiredService<IMediator>().Send(new ExecuteSeparationCommand(id, null));
        Assert.Equal((false, ExecuteSeparationHandler.NoActorMessage), refused);
    }

    // ── Damascus's date (UTC+3): between 21:00 and 24:00 UTC it is already tomorrow there.

    [Theory]
    [InlineData(22, true)]
    [InlineData(20, false)]
    public async Task The_effective_date_is_judged_in_Damascus(int utcHour, bool executes)
    {
        using var h = await HostAsync(now: new DateTimeOffset(2026, 11, 27, utcHour, 0, 0, TimeSpan.Zero));
        var effective = new DateOnly(2026, 11, 28);
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, Guid.NewGuid(), null, effective);

        var (success, error) = await SeparationJourneyDriver.ExecuteAsync(h.Services, id);

        Assert.Equal(executes, success);
        if (!executes)
            Assert.Equal(ExecuteSeparationHandler.NotYetMessage(effective, new DateOnly(2026, 11, 27)), error);
    }

    // ── What the execution saves, together.

    [Fact]
    public async Task The_contract_ends_at_the_effective_date_with_its_item_the_status_and_the_saga_in_one_save()
    {
        using var h = await HostAsync();
        var employee = Guid.NewGuid();
        var contract = await ActiveContractAsync(h, employee, new DateOnly(2024, 1, 1));
        var effective = new DateOnly(2026, 11, 20);
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, employee, contract.Id, effective);

        Assert.True((await SeparationJourneyDriver.ExecuteAsync(h.Services, id)).Success);

        h.Db.ChangeTracker.Clear();
        var record = await ReadAsync(h, id);
        Assert.Equal((SeparationStatus.Executing, SeparationJourneyDriver.Checker, true),
            (record.Status, record.ExecutedBy!.Value, record.SeparationEventPublished));
        var ended = await h.Db.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal((ContractStatus.Terminated, (DateOnly?)effective), (ended.Status, ended.EndDate));
        var item = await h.Db.SeparationChecklists.AsNoTracking().SingleAsync(c => c.SeparationRecordId == id && c.Step == SeparationStep.ContractTerminated);
        Assert.True(item.IsCompleted);
        var transaction = await h.Db.ServiceTransactions.AsNoTracking().Include(t => t.StateHistory).SingleAsync(t => t.SeparationRecordId == id);
        Assert.Equal(ServiceTransactionState.Executing, transaction.State);
        Assert.Equal(SeparationJourneyDriver.Checker, transaction.StateHistory.OrderBy(r => r.CreatedAt).Last().ChangedBy);
        Assert.Equal(1, h.Spy.SeparationSagaStartedCount);
    }

    [Fact]
    public async Task A_failed_save_keeps_nothing_of_the_execution()
    {
        using var h = await HostAsync();
        var employee = Guid.NewGuid();
        var contract = await ActiveContractAsync(h, employee, new DateOnly(2024, 1, 1));
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, employee, contract.Id, new DateOnly(2026, 11, 20));

        h.Failure.Armed = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => SeparationJourneyDriver.ExecuteAsync(h.Services, id));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(SeparationStatus.Approved, (await ReadAsync(h, id)).Status);
        Assert.Equal(ContractStatus.Active, (await h.Db.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id)).Status);
        Assert.False((await h.Db.SeparationChecklists.AsNoTracking()
            .SingleAsync(c => c.SeparationRecordId == id && c.Step == SeparationStep.ContractTerminated)).IsCompleted);
    }

    [Fact]
    public async Task An_effective_date_before_the_contracts_start_is_refused()
    {
        using var h = await HostAsync();
        var employee = Guid.NewGuid();
        var contract = await ActiveContractAsync(h, employee, new DateOnly(2026, 11, 25));
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, employee, contract.Id, new DateOnly(2026, 11, 20));
        var (success, error) = await SeparationJourneyDriver.ExecuteAsync(h.Services, id);
        Assert.False(success);
        Assert.Contains("يسبق بداية العقد", error);
    }

    // ── The cancellation by status.

    [Fact]
    public async Task A_cancellation_needs_a_reason()
    {
        using var h = await HostAsync();
        var id = await SeparationJourneyDriver.CreateAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 12, 1));
        Assert.Equal((false, CancelSeparationHandler.ReasonRequiredMessage),
            await h.Services.GetRequiredService<IMediator>().Send(new CancelSeparationCommand(id, "  ", Guid.NewGuid())));
    }

    [Fact]
    public async Task A_pending_record_is_withdrawn_in_the_engine_first_then_cancelled()
    {
        using var h = await HostAsync();
        var id = await SeparationJourneyDriver.CreateAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 12, 1));
        var submitted = await h.Services.GetRequiredService<IMediator>().Send(new SubmitSeparationCommand(id, SeparationJourneyDriver.Maker));

        var cancelled = await h.Services.GetRequiredService<IMediator>().Send(new CancelSeparationCommand(id, "تراجع الموظف عن الاستقالة", Guid.NewGuid()));

        Assert.True(cancelled.Success, cancelled.Error);
        Assert.Equal(submitted.InstanceId, h.Engine.Cancels.Single().InstanceId);
        h.Db.ChangeTracker.Clear();
        var record = await ReadAsync(h, id);
        Assert.Equal((SeparationStatus.Cancelled, "تراجع الموظف عن الاستقالة"), (record.Status, record.CancellationReason!));
        Assert.Equal(ServiceTransactionState.Cancelled, (await h.Db.ServiceTransactions.AsNoTracking().SingleAsync(t => t.SeparationRecordId == id)).State);
    }

    [Fact]
    public async Task A_pending_record_whose_approval_had_ended_is_judged_again_on_the_status_that_results()
    {
        using var h = await HostAsync();
        var id = await SeparationJourneyDriver.CreateAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 12, 1));
        var submitted = await h.Services.GetRequiredService<IMediator>().Send(new SubmitSeparationCommand(id, SeparationJourneyDriver.Maker));
        h.Engine.CancelResult = new WorkflowApprovalCancelResult(false, true, WorkflowApprovalOutcomes.Approved, null, null);
        h.Engine.SetStatus(submitted.InstanceId!.Value, WorkflowApprovalOutcomes.Approved, "SeparationRecord", id,
            decisions: [new HRManagement.MessageContracts.Events.WorkflowStepDecisionDto(2, "hr-management-approver", SeparationJourneyDriver.Authority, "Approved", DateTime.UtcNow)]);

        var cancelled = await h.Services.GetRequiredService<IMediator>().Send(new CancelSeparationCommand(id, "تراجع بعد الاعتماد", Guid.NewGuid()));

        // Approved first (the engine's outcome, never a draft), then cancelled: an approved record may be cancelled.
        Assert.True(cancelled.Success, cancelled.Error);
        h.Db.ChangeTracker.Clear();
        var record = await ReadAsync(h, id);
        Assert.Equal((SeparationStatus.Cancelled, SeparationJourneyDriver.Authority), (record.Status, record.DecidedBy!.Value));
    }

    [Fact]
    public async Task An_approved_record_is_cancelled_and_its_decision_stays_linked_as_history()
    {
        using var h = await HostAsync();
        var id = await SeparationJourneyDriver.CreateAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 12, 1));
        await SeparationJourneyDriver.ApproveAsync(h.Services, id);
        await SeparationJourneyDriver.IssueDecisionAsync(h.Services, id);

        Assert.True((await h.Services.GetRequiredService<IMediator>().Send(new CancelSeparationCommand(id, "عدول الإدارة عن القرار", Guid.NewGuid()))).Success);

        h.Db.ChangeTracker.Clear();
        var record = await ReadAsync(h, id);
        Assert.Equal(SeparationStatus.Cancelled, record.Status);
        Assert.NotNull(record.DecisionId);
        Assert.True(await h.Db.AdministrativeDecisions.AnyAsync(d => d.Id == record.DecisionId));
        Assert.Equal(0, h.Spy.SeparationSagaCancelledCount);
    }

    [Theory]
    [InlineData(SeparationStatus.Executing)]
    [InlineData(SeparationStatus.AwaitingIntervention)]
    [InlineData(SeparationStatus.Completed)]
    public async Task An_executed_record_is_refused_with_PLAN63s_message(SeparationStatus status)
    {
        using var h = await HostAsync();
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 11, 1));
        Assert.True((await SeparationJourneyDriver.ExecuteAsync(h.Services, id)).Success);
        if (status != SeparationStatus.Executing)
        {
            var tracked = await h.Db.SeparationRecords.SingleAsync(r => r.Id == id);
            SeparationWorkflowRules.Transition(tracked, status, null, "اختبار (النظام)");
            await h.Db.SaveChangesAsync();
        }

        Assert.Equal((false, SeparationWorkflowRules.ExecutingCancelMessage),
            await h.Services.GetRequiredService<IMediator>().Send(new CancelSeparationCommand(id, "إلغاء بعد التنفيذ", Guid.NewGuid())));
    }

    // ── The interventions and the saga's end.

    private static async Task<Guid> ExecutedAsync(Host h)
    {
        var id = await SeparationJourneyDriver.ReadyToExecuteAsync(h.Services, Guid.NewGuid(), null, new DateOnly(2026, 11, 1));
        Assert.True((await SeparationJourneyDriver.ExecuteAsync(h.Services, id)).Success);
        h.Db.ChangeTracker.Clear();
        return id;
    }

    [Fact]
    public async Task A_partys_trouble_puts_the_record_awaiting_an_intervention_and_the_resume_takes_it_back()
    {
        using var h = await HostAsync();
        var id = await ExecutedAsync(h);
        var message = Mock.Of<IProcessInterventionRequested>(m =>
            m.SagaType == SagaTypeNames.Separation && m.CorrelationId == id && m.Reason == "انتهت المهلة" && m.FailedParty == ProcessParties.ItInfra);
        await new SeparationInterventionStatusConsumer(h.Db, h.Services.GetRequiredService<IContractsRepository>())
            .Consume(Mock.Of<ConsumeContext<IProcessInterventionRequested>>(c => c.Message == message && c.CancellationToken == CancellationToken.None));
        Assert.Equal(SeparationStatus.AwaitingIntervention, (await ReadAsync(h, id)).Status);

        var intervention = new ProcessIntervention { SagaType = SagaTypeNames.Separation, CorrelationId = id, Reason = "انتهت المهلة", Status = ProcessInterventionStatuses.Open };
        h.Db.ProcessInterventions.Add(intervention);
        await h.Db.SaveChangesAsync();
        var publish = new Mock<IPublishEndpoint>();
        var controller = new ProcessInterventionsController(h.Db, publish.Object, h.Services.GetRequiredService<IContractsRepository>())
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        Assert.IsType<OkObjectResult>(await controller.Resume(intervention.Id, CancellationToken.None));
        h.Db.ChangeTracker.Clear();
        Assert.Equal(SeparationStatus.Executing, (await ReadAsync(h, id)).Status);
    }

    [Fact]
    public async Task The_interventions_cancel_refuses_a_separation_until_PLAN63()
    {
        using var h = await HostAsync();
        var id = await ExecutedAsync(h);
        var intervention = new ProcessIntervention { SagaType = SagaTypeNames.Separation, CorrelationId = id, Reason = "تعثر", Status = ProcessInterventionStatuses.Open };
        h.Db.ProcessInterventions.Add(intervention);
        await h.Db.SaveChangesAsync();
        var publish = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        var controller = new ProcessInterventionsController(h.Db, publish.Object, h.Services.GetRequiredService<IContractsRepository>())
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var refused = Assert.IsType<ConflictObjectResult>(await controller.Cancel(intervention.Id, new ProcessInterventionsController.CancelRequest { Reason = "إلغاء" }, CancellationToken.None));
        Assert.Equal(SeparationWorkflowRules.ExecutingCancelMessage, refused.Value);
        Assert.Equal(ProcessInterventionStatuses.Open, (await h.Db.ProcessInterventions.AsNoTracking().SingleAsync(i => i.Id == intervention.Id)).Status);
        Assert.Equal(SeparationStatus.Executing, (await ReadAsync(h, id)).Status);
    }

    [Fact]
    public async Task A_successful_saga_completes_the_record_with_every_partys_item_acknowledged()
    {
        using var h = await HostAsync();
        var id = await ExecutedAsync(h);
        h.Db.SeparationSagaStates.Add(new SeparationSagaState
        {
            CorrelationId = id,
            SeparationRecordId = id,
            CurrentState = "Completed",
            EmployeeId = Guid.NewGuid(),
            SeparationType = "Resignation",
            PendingParties = "",
            CompletedParties = string.Join(',', new SeparationSagaOptions().MandatoryParties)
        });
        await h.Db.SaveChangesAsync();

        await new SeparationSagaFinalizedConsumer(h.Db, h.Services.GetRequiredService<IContractsRepository>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SeparationSagaFinalizedConsumer>.Instance)
            .Consume(Mock.Of<ConsumeContext<ISeparationSagaFinalized>>(c =>
                c.Message == Mock.Of<ISeparationSagaFinalized>(m => m.SeparationRecordId == id && m.Success)
                && c.CancellationToken == CancellationToken.None));

        h.Db.ChangeTracker.Clear();
        var record = await ReadAsync(h, id);
        Assert.Equal(SeparationStatus.Completed, record.Status);
        Assert.NotNull(record.CompletedAt);
        var items = await h.Db.SeparationChecklists.AsNoTracking().Where(c => c.SeparationRecordId == id).ToListAsync();
        Assert.True(SeparationWorkflowRules.AreAllPartiesAcknowledged(items));
        var transaction = await h.Db.ServiceTransactions.AsNoTracking().SingleAsync(t => t.SeparationRecordId == id);
        Assert.Equal(ServiceTransactionState.Archived, transaction.State);
    }
}
