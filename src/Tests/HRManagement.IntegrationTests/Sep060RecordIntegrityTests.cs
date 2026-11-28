using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Services;
using HRManagement.IntegrationTests.Support;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60 (§6, G-SEP-11): the record's integrity through the one creation path — the employee (PLAN-42's gate with his status), no
/// second open record (checked, then the partial unique index), the contract his own, the substance locked after the submission and
/// after an item, the transaction linked — and the three entries (the form, the wizard, the inbound requests) going through it.
/// </summary>
public sealed class Sep060RecordIntegrityTests
{
    private sealed class StubGate(Func<Guid, (bool, string?)> start) : IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult(start(employeeId));

        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));
    }

    private static async Task<(IServiceScope Scope, ContractsDbContext Db, IMediator Mediator)> ScopeAsync(
        Func<Guid, (bool, string?)>? start = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IEventPublisher>(new SpyEventPublisher());
        services.AddSingleton<IEmployeeLifecycleGate>(new StubGate(start ?? (_ => (true, null))));
        services.AddContractsApplicationForTests();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        return (scope, db, scope.ServiceProvider.GetRequiredService<IMediator>());
    }

    private static SeparationRecordDto Dto(Guid employee, Guid? contract = null, DateOnly? effective = null) =>
        new(null, employee, contract, SeparationType.Resignation, effective ?? new DateOnly(2026, 12, 31), "سبب", null);

    private static async Task<Contract> ContractAsync(ContractsDbContext db, Guid employee, ContractStatus status)
    {
        var type = await db.ContractTypes.FirstOrDefaultAsync() ?? TestDataFactory.CreateContractType();
        if (db.Entry(type).State == EntityState.Detached)
            db.ContractTypes.Add(type);
        var contract = TestDataFactory.CreatePendingContract(employee, type);
        contract.Status = status;
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract;
    }

    // ── The employee.

    [Fact]
    public async Task An_employee_HR_Core_does_not_know_is_refused()
    {
        var (scope, db, mediator) = await ScopeAsync(_ => (false, HttpEmployeeLifecycleGate.NotFoundMessage));
        using (scope)
        {
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid())));
            Assert.False(result.Success);
            Assert.Equal(HttpEmployeeLifecycleGate.NotFoundMessage, result.Error);
            Assert.Equal(0, await db.SeparationRecords.CountAsync());
            Assert.Equal(0, await db.ServiceTransactions.CountAsync());
        }
    }

    [Fact]
    public async Task An_employee_whose_service_ended_is_refused_by_the_status_name()
    {
        var message = HttpEmployeeLifecycleGate.TerminalStatusMessage(HRManagement.SharedKernel.Enums.EmploymentStatus.Retired);
        var (scope, db, mediator) = await ScopeAsync(_ => (false, message));
        using (scope)
        {
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid())));
            Assert.False(result.Success);
            Assert.Contains("متقاعد", result.Error);
            Assert.Equal(0, await db.SeparationRecords.CountAsync());
        }
    }

    [Fact]
    public async Task A_check_that_cannot_answer_refuses()
    {
        var (scope, db, mediator) = await ScopeAsync(_ => (false, HttpEmployeeLifecycleGate.UnverifiedMessage));
        using (scope)
        {
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid())));
            Assert.Equal((false, HttpEmployeeLifecycleGate.UnverifiedMessage), (result.Success, result.Error));
            Assert.Equal(0, await db.SeparationRecords.CountAsync());
        }
    }

    [Fact]
    public async Task No_employee_is_refused_before_the_gate()
    {
        var asked = 0;
        var (scope, _, mediator) = await ScopeAsync(_ => { asked++; return (true, null); });
        using (scope)
        {
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.Empty)));
            Assert.Equal((false, SeparationRecordFactory.EmployeeRequiredMessage), (result.Success, result.Error));
            Assert.Equal(0, asked);
        }
    }

    // ── One open record per employee.

    [Fact]
    public async Task A_second_open_record_is_refused_and_one_record_remains()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            Assert.True((await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)))).Success);

            var second = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee, effective: new DateOnly(2027, 1, 31))));
            Assert.Equal((false, SeparationRecordFactory.OpenRecordMessage), (second.Success, second.Error));
            Assert.Equal(1, await db.SeparationRecords.CountAsync(r => r.EmployeeId == employee));
            Assert.Equal(1, await db.ServiceTransactions.CountAsync(t => t.EmployeeId == employee));
        }
    }

    [Fact]
    public async Task After_a_cancellation_a_new_record_is_accepted()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            var first = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)));
            Assert.True((await mediator.Send(new CancelSeparationCommand(first.Record!.Id, "خطأ في الإدخال", Guid.NewGuid()))).Success);

            var second = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)));
            Assert.True(second.Success, second.Error);
            Assert.Equal(2, await db.SeparationRecords.CountAsync(r => r.EmployeeId == employee));
        }
    }

    [Fact]
    public async Task The_partial_unique_index_is_the_last_word()
    {
        var (scope, db, _) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            db.SeparationRecords.Add(new SeparationRecord { EmployeeId = employee, EffectiveDate = new DateOnly(2026, 12, 1) });
            db.SeparationRecords.Add(new SeparationRecord { EmployeeId = employee, EffectiveDate = new DateOnly(2026, 12, 2) });
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(SeparationRecordFactory.IsOpenRecordViolation(refused));
        }
    }

    // ── The contract.

    [Fact]
    public async Task Another_employees_contract_is_refused()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var other = await ContractAsync(db, Guid.NewGuid(), ContractStatus.Active);
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid(), other.Id)));
            Assert.Equal((false, SeparationRecordFactory.ContractNotEmployeesMessage), (result.Success, result.Error));
        }
    }

    [Theory]
    [InlineData(ContractStatus.Expired)]
    [InlineData(ContractStatus.Terminated)]
    public async Task A_contract_that_is_neither_active_nor_pending_is_refused(ContractStatus status)
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            var ended = await ContractAsync(db, employee, status);
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee, ended.Id)));
            Assert.Equal((false, SeparationRecordFactory.ContractNotEmployeesMessage), (result.Success, result.Error));
        }
    }

    [Fact]
    public async Task One_active_contract_is_chosen_for_the_employee()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            await ContractAsync(db, employee, ContractStatus.Expired);
            var active = await ContractAsync(db, employee, ContractStatus.Active);
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)));
            Assert.True(result.Success, result.Error);
            Assert.Equal(active.Id, result.Record!.ContractId);
        }
    }

    [Fact]
    public async Task Several_contracts_make_the_choice_required()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            await ContractAsync(db, employee, ContractStatus.Active);
            var pending = await ContractAsync(db, employee, ContractStatus.Pending);

            var unchosen = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)));
            Assert.Equal((false, SeparationRecordFactory.ContractChoiceRequiredMessage), (unchosen.Success, unchosen.Error));

            var chosen = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee, pending.Id)));
            Assert.True(chosen.Success, chosen.Error);
            Assert.Equal(pending.Id, chosen.Record!.ContractId);
        }
    }

    [Fact]
    public async Task With_no_contract_the_record_has_none()
    {
        var (scope, _, mediator) = await ScopeAsync();
        using (scope)
        {
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid())));
            Assert.True(result.Success, result.Error);
            Assert.Null(result.Record!.ContractId);
        }
    }

    [Fact]
    public async Task The_contract_list_is_the_employees_own_active_or_pending_contracts()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            var active = await ContractAsync(db, employee, ContractStatus.Active);
            var pending = await ContractAsync(db, employee, ContractStatus.Pending);
            await ContractAsync(db, employee, ContractStatus.Terminated);
            await ContractAsync(db, Guid.NewGuid(), ContractStatus.Active);

            var items = await mediator.Send(new HR.Contracts.Application.Features.Lookup.GetEmployeeContractsLookupQuery(employee));
            Assert.Equal(new[] { active.Id, pending.Id }.Order(), items.Select(i => i.Id).Order());
            Assert.Empty(await mediator.Send(new HR.Contracts.Application.Features.Lookup.GetEmployeeContractsLookupQuery(Guid.Empty)));
        }
    }

    // ── What the creation saves, in one save.

    [Fact]
    public async Task The_record_its_items_and_its_linked_transaction_are_saved_together()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var result = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid())));
            var id = result.Record!.Id;
            db.ChangeTracker.Clear();

            var record = await db.SeparationRecords.SingleAsync(r => r.Id == id);
            Assert.Equal((SeparationStatus.Draft, false, (Guid?)null), (record.Status, record.IsLegacyDraft, record.ApprovedBy));
            var items = await db.SeparationChecklists.Where(c => c.SeparationRecordId == id).ToListAsync();
            Assert.Equal(Enum.GetValues<SeparationStep>().Length, items.Count);
            Assert.All(items, i => Assert.Equal(SeparationChecklistDefaults.ModeFor(i.Step), i.CompletionMode));
            var transaction = await db.ServiceTransactions.SingleAsync(t => t.SeparationRecordId == id);
            Assert.Equal((ServiceTransactionType.Separation, ServiceTransactionState.Draft), (transaction.TransactionType, transaction.State));
            Assert.False(string.IsNullOrWhiteSpace(transaction.ReferenceNumber));
        }
    }

    // ── The substance is locked.

    [Fact]
    public async Task An_edit_after_the_submission_is_refused_and_changes_nothing()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            var created = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)));
            var record = await db.SeparationRecords.SingleAsync(r => r.Id == created.Record!.Id);
            await SeparationStatusSync.TransitionAsync(scope.ServiceProvider.GetRequiredService<IContractsRepository>(), record,
                SeparationStatus.PendingApproval, Guid.NewGuid(), null, CancellationToken.None,
                new SeparationSubmission(Guid.NewGuid(), "SEPARATION-APPROVAL", 1));
            await db.SaveChangesAsync();

            var edit = await mediator.Send(new UpdateSeparationRecordCommand(record.Id, Dto(Guid.NewGuid()) with { Id = record.Id }));
            Assert.Equal((false, UpdateSeparationRecordHandler.NotDraftMessage), edit);
            db.ChangeTracker.Clear();
            Assert.Equal(employee, (await db.SeparationRecords.SingleAsync(r => r.Id == record.Id)).EmployeeId);
        }
    }

    [Fact]
    public async Task Once_an_item_is_completed_the_employee_and_the_contract_no_longer_change()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            var created = await mediator.Send(new CreateSeparationRecordCommand(Dto(employee)));
            var item = await db.SeparationChecklists.SingleAsync(c =>
                c.SeparationRecordId == created.Record!.Id && c.Step == SeparationStep.EmployeeNotified);
            item.IsCompleted = true;
            await db.SaveChangesAsync();

            var edit = await mediator.Send(new UpdateSeparationRecordCommand(created.Record!.Id, Dto(Guid.NewGuid()) with { Id = created.Record.Id }));
            Assert.Equal((false, UpdateSeparationRecordHandler.SubjectLockedMessage), edit);

            // The other fields of a draft still change.
            var later = await mediator.Send(new UpdateSeparationRecordCommand(created.Record.Id,
                Dto(employee, effective: new DateOnly(2027, 2, 28)) with { Id = created.Record.Id }));
            Assert.True(later.Success, later.Error);
            db.ChangeTracker.Clear();
            var stored = await db.SeparationRecords.SingleAsync(r => r.Id == created.Record.Id);
            Assert.Equal((employee, new DateOnly(2027, 2, 28)), (stored.EmployeeId, stored.EffectiveDate));
        }
    }

    [Fact]
    public async Task A_changed_employee_in_a_draft_is_checked_as_a_creation_is()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var created = await mediator.Send(new CreateSeparationRecordCommand(Dto(Guid.NewGuid())));
            var busy = Guid.NewGuid();
            Assert.True((await mediator.Send(new CreateSeparationRecordCommand(Dto(busy)))).Success);

            var edit = await mediator.Send(new UpdateSeparationRecordCommand(created.Record!.Id, Dto(busy) with { Id = created.Record.Id }));
            Assert.Equal((false, SeparationRecordFactory.OpenRecordMessage), edit);

            var otherContract = await ContractAsync(db, Guid.NewGuid(), ContractStatus.Active);
            var wrongContract = await mediator.Send(new UpdateSeparationRecordCommand(created.Record.Id,
                Dto(created.Record.EmployeeId, otherContract.Id) with { Id = created.Record.Id }));
            Assert.Equal((false, SeparationRecordFactory.ContractNotEmployeesMessage), wrongContract);
        }
    }

    // ── The three entries.

    [Fact]
    public async Task The_inbound_request_goes_through_the_factory_and_a_refusal_throws_for_the_brokers_retry()
    {
        var (scope, db, mediator) = await ScopeAsync();
        using (scope)
        {
            var employee = Guid.NewGuid();
            await mediator.Send(new ProcessSeparationRequestCommand(employee, SeparationType.Judicial, new DateOnly(2026, 12, 15), "حكم", null));
            var record = await db.SeparationRecords.SingleAsync(r => r.EmployeeId == employee);
            Assert.Null(record.CreatedBy);
            Assert.True(await db.ServiceTransactions.AnyAsync(t => t.SeparationRecordId == record.Id));

            // The same request again is its idempotency: nothing new, no refusal.
            await mediator.Send(new ProcessSeparationRequestCommand(employee, SeparationType.Judicial, new DateOnly(2026, 12, 15), "حكم", null));
            Assert.Equal(1, await db.SeparationRecords.CountAsync(r => r.EmployeeId == employee));

            // Another request for the same employee meets the open record: it throws, neither lost nor acted on.
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(
                new ProcessSeparationRequestCommand(employee, SeparationType.Medical, new DateOnly(2026, 12, 20), "قرار طبي", null)));
            Assert.Equal(SeparationRecordFactory.OpenRecordMessage, refused.Message);
            Assert.Equal(1, await db.SeparationRecords.CountAsync(r => r.EmployeeId == employee));
        }
    }

    [Fact]
    public async Task The_inbound_request_is_refused_as_the_form_is_when_the_service_ended()
    {
        var message = HttpEmployeeLifecycleGate.TerminalStatusMessage(HRManagement.SharedKernel.Enums.EmploymentStatus.Resigned);
        var (scope, db, mediator) = await ScopeAsync(_ => (false, message));
        using (scope)
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(
                new ProcessSeparationRequestCommand(Guid.NewGuid(), SeparationType.Resignation, new DateOnly(2026, 12, 15), null, null)));
            Assert.Equal(message, refused.Message);
            Assert.Equal(0, await db.SeparationRecords.CountAsync());
        }
    }
}
