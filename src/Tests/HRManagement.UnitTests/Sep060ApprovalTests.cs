using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Services;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Infrastructure.Workflow;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Entities;
using HRManagement.TestSupport.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-60 (P-7): what the separation's approval is built from, on an in-memory context and PLAN-46's fake engine.</summary>
internal sealed class Sep060ApprovalHarness : IAsyncDisposable
{
    public static readonly Guid Tenant = Guid.Parse("a0000060-0000-4000-8000-0000000000e1");

    public ServiceProvider Provider { get; }

    public FakeWorkflowApprovalClient Engine { get; } = new();

    public Func<Guid, (bool, string?)> Gate { get; set; } = _ => (true, null);

    public string DatabaseName { get; } = Guid.NewGuid().ToString();

    /// <summary>The numbers COD-001 handed out, in order (a stand-in that counts).</summary>
    public CountingCodes Codes { get; } = new();

    /// <summary>Now, for the clock: 2026-11-28 10:00 UTC unless a test moves it.</summary>
    public FixedTime Time { get; } = new(new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero));

    public Sep060ApprovalHarness(SeparationOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseInMemoryDatabase(DatabaseName));
        services.AddScoped<IContractsRepository, ContractsRepository>();
        services.AddSingleton<IWorkflowApprovalClient>(Engine);
        services.AddSingleton<IEmployeeLifecycleGate>(new DelegateGate(id => Gate(id)));
        services.AddSingleton<ICodeGenerationClient>(Codes);
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<HRManagement.ServiceDefaults.Localization.ICalendarService>(new HRManagement.ServiceDefaults.Localization.DamascusCalendarService());
        services.AddSingleton<SeparationClock>();
        services.AddSingleton<IEventPublisher>(Mock.Of<IEventPublisher>());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SeparationApprovalService>());
        services.AddSingleton<IHrCoreLookupClient>(new NamedLookup());
        services.AddSingleton(Options.Create(options ?? new SeparationOptions()));
        services.AddScoped<SeparationRecordFactory>();
        services.AddScoped<SeparationApprovalService>();
        services.AddScoped<SeparationApprovalOwnerStore>();
        Provider = services.BuildServiceProvider();
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> work) => InScopeAsync(async sp => { await work(sp); return 0; });

    /// <summary>A draft as the factory leaves it: its items, its linked transaction, its creator (null for an inbound request).</summary>
    public Task<SeparationRecord> DraftAsync(Guid? creator, SeparationType type = SeparationType.Resignation, Guid? employee = null) =>
        InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ContractsDbContext>();
            var record = new SeparationRecord
            {
                TenantId = Tenant,
                EmployeeId = employee ?? Guid.NewGuid(),
                SeparationType = type,
                EffectiveDate = new DateOnly(2026, 12, 31),
                CreatedBy = creator
            };
            db.SeparationRecords.Add(record);
            db.SeparationChecklists.AddRange(SeparationChecklistDefaults.CreateForRecord(record.Id));
            db.ServiceTransactions.Add(new ServiceTransaction
            {
                TenantId = Tenant,
                EmployeeId = record.EmployeeId,
                TransactionType = ServiceTransactionType.Separation,
                SeparationRecordId = record.Id
            });
            db.AuditLogs.Add(new EntityAuditLog { TenantId = Tenant, EntityType = nameof(SeparationRecord), EntityId = record.Id, Action = "Created" });
            await db.SaveChangesAsync();
            return record;
        });

    public Task<SeparationRecord> ReadAsync(Guid id) =>
        InScopeAsync(sp => sp.GetRequiredService<ContractsDbContext>().SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == id));

    public Task<ServiceTransaction> TransactionAsync(Guid id) =>
        InScopeAsync(sp => sp.GetRequiredService<ContractsDbContext>().ServiceTransactions.AsNoTracking()
            .Include(t => t.StateHistory).SingleAsync(t => t.SeparationRecordId == id));

    public Task<WorkflowApprovalSubmitResult> SubmitAsync(Guid id, Guid? submitter) =>
        InScopeAsync(sp => sp.GetRequiredService<SeparationApprovalService>().SubmitAsync(id, submitter));

    public Task ApplyAsync(IWorkflowOutcomeDecided outcome) =>
        InScopeAsync(sp => sp.GetRequiredService<SeparationApprovalService>().ApplyOutcomeAsync(outcome));

    public async ValueTask DisposeAsync() => await Provider.DisposeAsync();

    public sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public sealed class CountingCodes : ICodeGenerationClient
    {
        public List<HRManagement.SharedKernel.CodeGeneration.CodeGenerationRequest> Requests { get; } = [];

        public Task<HRManagement.SharedKernel.CodeGeneration.CodeGenerationResult?> GenerateAsync(
            HRManagement.SharedKernel.CodeGeneration.CodeGenerationRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult<HRManagement.SharedKernel.CodeGeneration.CodeGenerationResult?>(
                new() { Code = $"DEC-SEP-2026-{Requests.Count:00000}" });
        }

        public Task<HRManagement.SharedKernel.CodeGeneration.CodeGenerationResult?> PreviewAsync(
            HRManagement.SharedKernel.CodeGeneration.CodeGenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult<HRManagement.SharedKernel.CodeGeneration.CodeGenerationResult?>(null);
    }

    private sealed class DelegateGate(Func<Guid, (bool, string?)> start) : IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult(start(employeeId));

        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class NamedLookup : IHrCoreLookupClient
    {
        public Task<IReadOnlyList<HrCoreLookupItem>> GetEmployeesByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HrCoreLookupItem>>(ids.Select(id => new HrCoreLookupItem(id, "سلمى الحسن", "مديرية الموارد")).ToList());

        public Task<IReadOnlyList<HrCoreLookupItem>> GetOrgUnitsByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HrCoreLookupItem>>([]);
    }
}

public sealed class Sep060ApprovalRoutingTests
{
    [Theory]
    [InlineData(SeparationType.Disciplinary, SeparationApprovalRouting.SensitiveCode)]
    [InlineData(SeparationType.Judicial, SeparationApprovalRouting.SensitiveCode)]
    [InlineData(SeparationType.Resignation, SeparationApprovalRouting.StandardCode)]
    [InlineData(SeparationType.Termination, SeparationApprovalRouting.StandardCode)]
    [InlineData(SeparationType.EndOfContract, SeparationApprovalRouting.StandardCode)]
    [InlineData(SeparationType.Retirement, SeparationApprovalRouting.StandardCode)]
    [InlineData(SeparationType.Medical, SeparationApprovalRouting.StandardCode)]
    public void By_default_the_disciplinary_and_judicial_types_take_the_sensitive_chain(SeparationType type, string code) =>
        Assert.Equal(code, SeparationApprovalRouting.CodeFor(type, new SeparationApprovalOptions()));

    [Fact]
    public void The_configured_list_replaces_the_default_and_an_unknown_name_is_ignored()
    {
        var options = new SeparationApprovalOptions { SensitiveTypes = ["medical", "NoSuchType"] };
        Assert.Equal(SeparationApprovalRouting.SensitiveCode, SeparationApprovalRouting.CodeFor(SeparationType.Medical, options));
        Assert.Equal(SeparationApprovalRouting.StandardCode, SeparationApprovalRouting.CodeFor(SeparationType.Disciplinary, options));
        Assert.Equal(SeparationApprovalRouting.StandardCode,
            SeparationApprovalRouting.CodeFor(SeparationType.Judicial, new SeparationApprovalOptions { SensitiveTypes = [] }));
    }
}

public sealed class Sep060SubmitTests
{
    [Fact]
    public async Task The_submission_asks_the_engine_with_the_creator_and_the_submitter_excluded_and_saves_PendingApproval_with_the_instance()
    {
        await using var h = new Sep060ApprovalHarness();
        var creator = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var record = await h.DraftAsync(creator, SeparationType.Disciplinary);
        await h.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ContractsDbContext>();
            db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot { EmployeeId = record.EmployeeId, TenantId = Sep060ApprovalHarness.Tenant, PrimaryOrgUnitId = unit });
            await db.SaveChangesAsync();
        });

        var result = await h.SubmitAsync(record.Id, submitter);

        Assert.True(result.Success, result.Error);
        var start = Assert.Single(h.Engine.Starts);
        Assert.Equal(("SeparationRecord", record.Id, SeparationApprovalRouting.SensitiveCode, $"SeparationRecord:{record.Id:D}:1"),
            (start.EntityType, start.EntityId, start.WorkflowCode, start.IdempotencyKey));
        Assert.Equal(new[] { creator, submitter }.Order(), start.ExcludedApproverUserIds.Order());
        Assert.Equal(record.EmployeeId, start.SubjectEmployeeId);
        Assert.Equal(unit, start.OrgUnitId);
        Assert.Equal("إنهاء خدمة — سلمى الحسن — تأديبية", start.DisplayTitle);
        Assert.Equal($"/ui/hr-contracts/SeparationRecords/Details/{record.Id:D}", start.DetailsUrl);

        var stored = await h.ReadAsync(record.Id);
        Assert.Equal((SeparationStatus.PendingApproval, result.InstanceId, submitter, 1, SeparationApprovalRouting.SensitiveCode),
            (stored.Status, stored.WorkflowInstanceId, stored.SubmittedBy!.Value, stored.SubmissionNo, stored.WorkflowCode));
        var transaction = await h.TransactionAsync(record.Id);
        Assert.Equal(ServiceTransactionState.PendingApproval, transaction.State);
        Assert.All(transaction.StateHistory, row => Assert.Equal(submitter, row.ChangedBy));
    }

    [Fact]
    public async Task A_record_no_person_created_excludes_the_submitter_alone_and_names_the_subject()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(creator: null);
        var submitter = Guid.NewGuid();

        Assert.True((await h.SubmitAsync(record.Id, submitter)).Success);
        var start = Assert.Single(h.Engine.Starts);
        Assert.Equal([submitter], start.ExcludedApproverUserIds);
        Assert.Equal(record.EmployeeId, start.SubjectEmployeeId);
    }

    [Fact]
    public async Task A_submission_with_no_user_is_refused_and_asks_nothing()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        foreach (var submitter in new Guid?[] { null, Guid.Empty })
            Assert.Equal(SeparationApprovalService.NoSubmitterMessage, (await h.SubmitAsync(record.Id, submitter)).Error);
        Assert.Empty(h.Engine.Starts);
        Assert.Equal(SeparationStatus.Draft, (await h.ReadAsync(record.Id)).Status);
    }

    [Theory]
    [InlineData(WorkflowApprovalErrorCodes.DefinitionMissing)]
    [InlineData(WorkflowApprovalErrorCodes.EngineUnavailable)]
    [InlineData(WorkflowApprovalErrorCodes.Forbidden)]
    [InlineData(WorkflowApprovalErrorCodes.Conflict)]
    [InlineData(WorkflowApprovalErrorCodes.Invalid)]
    public async Task A_failed_start_keeps_the_draft_with_the_message_of_its_code(string code)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        h.Engine.StartFailure = new WorkflowApprovalStartResult(false, null, false, code, null);

        var result = await h.SubmitAsync(record.Id, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalErrorCodes.MessageFor(code), result.Error);
        var stored = await h.ReadAsync(record.Id);
        Assert.Equal((SeparationStatus.Draft, (Guid?)null), (stored.Status, stored.WorkflowInstanceId));
        Assert.Equal(ServiceTransactionState.Draft, (await h.TransactionAsync(record.Id)).State);
    }

    [Fact]
    public async Task Only_a_draft_is_submitted_and_the_subject_is_checked_again_first()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        h.Gate = _ => (false, "انتهت خدمة الموظف (الحالة: مستقيل) — لا يُفتح له سجل فصل.");
        Assert.Contains("مستقيل", (await h.SubmitAsync(record.Id, Guid.NewGuid())).Error);
        Assert.Empty(h.Engine.Starts);

        h.Gate = _ => (true, null);
        Assert.True((await h.SubmitAsync(record.Id, Guid.NewGuid())).Success);
        Assert.Equal(SeparationApprovalService.NotDraftMessage, (await h.SubmitAsync(record.Id, Guid.NewGuid())).Error);
        Assert.Single(h.Engine.Starts);
    }

    [Fact]
    public async Task A_reopening_after_a_rejection_takes_the_next_number()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        var first = await h.SubmitAsync(record.Id, Guid.NewGuid());
        await h.ApplyAsync(FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, first.InstanceId!.Value,
            WorkflowApprovalOutcomes.Rejected, Sep060ApprovalHarness.Tenant, Guid.NewGuid(), "ناقص"));
        Assert.True((await h.InScopeAsync(sp => sp.GetRequiredService<SeparationApprovalService>().ReopenAsync(record.Id, Guid.NewGuid()))).Success);

        Assert.True((await h.SubmitAsync(record.Id, Guid.NewGuid())).Success);
        Assert.Equal($"SeparationRecord:{record.Id:D}:2", h.Engine.Starts[^1].IdempotencyKey);
        Assert.Equal(2, (await h.ReadAsync(record.Id)).SubmissionNo);
    }
}

public sealed class Sep060OutcomeConsumerTests
{
    private static ConsumeContext<IWorkflowOutcomeDecided> Context(IWorkflowOutcomeDecided message) =>
        Mock.Of<ConsumeContext<IWorkflowOutcomeDecided>>(c => c.Message == message && c.CancellationToken == CancellationToken.None);

    private static async Task<(Sep060ApprovalHarness H, SeparationRecord Record, Guid Instance, Guid Creator, Guid Submitter)> PendingAsync()
    {
        var h = new Sep060ApprovalHarness();
        var creator = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var record = await h.DraftAsync(creator);
        var submitted = await h.SubmitAsync(record.Id, submitter);
        return (h, record, submitted.InstanceId!.Value, creator, submitter);
    }

    private static Task ConsumeAsync(Sep060ApprovalHarness h, IWorkflowOutcomeDecided message) =>
        h.InScopeAsync(sp => new SeparationApprovalOutcomeConsumer(sp.GetRequiredService<SeparationApprovalService>()).Consume(Context(message)));

    [Fact]
    public async Task An_approval_of_the_linked_instance_approves_once_and_a_repeat_changes_nothing()
    {
        var (h, record, instance, _, _) = await PendingAsync();
        await using var _h = h;
        var decider = Guid.NewGuid();
        var approval = FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, instance, WorkflowApprovalOutcomes.Approved,
            Sep060ApprovalHarness.Tenant, decider, decisions: [new WorkflowStepDecisionDto(2, "hr-management-approver", decider, "Approved", DateTime.UtcNow)]);

        await ConsumeAsync(h, approval);
        var rows = (await h.TransactionAsync(record.Id)).StateHistory.Count;
        await ConsumeAsync(h, approval);

        var stored = await h.ReadAsync(record.Id);
        Assert.Equal((SeparationStatus.Approved, decider), (stored.Status, stored.DecidedBy!.Value));
        var transaction = await h.TransactionAsync(record.Id);
        Assert.Equal(ServiceTransactionState.Approved, transaction.State);
        Assert.Equal(rows, transaction.StateHistory.Count);
    }

    [Fact]
    public async Task An_outcome_naming_another_instance_is_ignored()
    {
        var (h, record, _, _, _) = await PendingAsync();
        await using var _h = h;
        await ConsumeAsync(h, FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, Guid.NewGuid(), WorkflowApprovalOutcomes.Approved,
            Sep060ApprovalHarness.Tenant, Guid.NewGuid()));
        Assert.Equal(SeparationStatus.PendingApproval, (await h.ReadAsync(record.Id)).Status);
    }

    [Fact]
    public async Task Another_owners_outcome_is_not_this_consumers()
    {
        var (h, record, instance, _, _) = await PendingAsync();
        await using var _h = h;
        await ConsumeAsync(h, FakeWorkflowApprovalClient.Outcome("CardRequest", record.Id, instance, WorkflowApprovalOutcomes.Approved,
            Sep060ApprovalHarness.Tenant, Guid.NewGuid()));
        Assert.Equal(SeparationStatus.PendingApproval, (await h.ReadAsync(record.Id)).Status);
    }

    [Fact]
    public async Task A_withdrawal_in_the_engine_returns_the_record_to_draft()
    {
        var (h, record, instance, _, submitter) = await PendingAsync();
        await using var _h = h;
        await ConsumeAsync(h, FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, instance, WorkflowApprovalOutcomes.Cancelled,
            Sep060ApprovalHarness.Tenant, submitter, "سحب"));
        Assert.Equal(SeparationStatus.Draft, (await h.ReadAsync(record.Id)).Status);
        Assert.Equal(ServiceTransactionState.InProcess, (await h.TransactionAsync(record.Id)).State);
    }

    [Fact]
    public async Task A_rejection_keeps_its_reason()
    {
        var (h, record, instance, _, _) = await PendingAsync();
        await using var _h = h;
        await ConsumeAsync(h, FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, instance, WorkflowApprovalOutcomes.Rejected,
            Sep060ApprovalHarness.Tenant, Guid.NewGuid(), "المستندات ناقصة"));
        var stored = await h.ReadAsync(record.Id);
        Assert.Equal((SeparationStatus.Rejected, "المستندات ناقصة"), (stored.Status, stored.RejectionReason!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_approval_naming_the_creator_or_the_submitter_is_refused(bool byCreator)
    {
        var (h, record, instance, creator, submitter) = await PendingAsync();
        await using var _h = h;
        var barred = byCreator ? creator : submitter;
        await ConsumeAsync(h, FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, instance, WorkflowApprovalOutcomes.Approved,
            Sep060ApprovalHarness.Tenant, barred, decisions: [new WorkflowStepDecisionDto(1, "hr-core", barred, "Approved", DateTime.UtcNow)]));
        var stored = await h.ReadAsync(record.Id);
        Assert.Equal((SeparationStatus.Rejected, SeparationApprovalService.RefusedApprovalReason), (stored.Status, stored.RejectionReason!));
    }
}

public sealed class Sep060SweepAndWithdrawTests
{
    private sealed class Monitor(WorkflowApprovalSweepOptions value) : IOptionsMonitor<WorkflowApprovalSweepOptions>
    {
        public WorkflowApprovalSweepOptions CurrentValue => value;
        public WorkflowApprovalSweepOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<WorkflowApprovalSweepOptions, string?> listener) => null;
    }

    private static WorkflowApprovalSweepJob<SeparationApprovalOwnerStore> Job(Sep060ApprovalHarness h) =>
        new(h.Provider.GetRequiredService<IServiceScopeFactory>(), new Monitor(new WorkflowApprovalSweepOptions { StaleAfterMinutes = 0 }),
            NullLogger<WorkflowApprovalSweepJob<SeparationApprovalOwnerStore>>.Instance);

    [Fact]
    public async Task The_sweep_applies_an_ended_outcome_once()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        var instance = (await h.SubmitAsync(record.Id, Guid.NewGuid())).InstanceId!.Value;
        var authority = Guid.NewGuid();
        h.Engine.SetStatus(instance, WorkflowApprovalOutcomes.Approved, "SeparationRecord", record.Id,
            decisions: [new WorkflowStepDecisionDto(2, "hr-management-approver", authority, "Approved", DateTime.UtcNow)]);

        await Job(h).RunOnceAsync();
        Assert.Equal((SeparationStatus.Approved, authority), ((await h.ReadAsync(record.Id)).Status, (await h.ReadAsync(record.Id)).DecidedBy!.Value));
        var reads = h.Engine.StatusReads.Count;

        // The next cycle finds nothing pending: no read, no second application.
        await Job(h).RunOnceAsync();
        Assert.Equal(reads, h.Engine.StatusReads.Count);
        Assert.Equal(SeparationStatus.Approved, (await h.ReadAsync(record.Id)).Status);
    }

    [Fact]
    public async Task The_sweep_leaves_a_record_whose_instance_is_still_running()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        var instance = (await h.SubmitAsync(record.Id, Guid.NewGuid())).InstanceId!.Value;
        h.Engine.SetStatus(instance, WorkflowApprovalOutcomes.InProgress, "SeparationRecord", record.Id);
        await Job(h).RunOnceAsync();
        Assert.Equal(SeparationStatus.PendingApproval, (await h.ReadAsync(record.Id)).Status);
    }

    [Fact]
    public async Task A_withdrawal_in_time_returns_the_record_to_draft()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        await h.SubmitAsync(record.Id, Guid.NewGuid());

        var result = await h.InScopeAsync(sp => sp.GetRequiredService<SeparationApprovalService>().WithdrawAsync(record.Id, Guid.NewGuid(), null));

        Assert.True(result.Success, result.Error);
        Assert.Equal(SeparationApprovalService.DefaultWithdrawReason, h.Engine.Cancels.Single().Reason);
        Assert.Equal(SeparationStatus.Draft, (await h.ReadAsync(record.Id)).Status);
    }

    [Theory]
    [InlineData(WorkflowApprovalOutcomes.Approved, SeparationStatus.Approved)]
    [InlineData(WorkflowApprovalOutcomes.Rejected, SeparationStatus.Rejected)]
    public async Task A_withdrawal_that_meets_the_end_applies_the_final_outcome_never_a_draft(string outcome, SeparationStatus expected)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        var instance = (await h.SubmitAsync(record.Id, Guid.NewGuid())).InstanceId!.Value;
        h.Engine.CancelResult = new WorkflowApprovalCancelResult(false, true, outcome, null, null);
        h.Engine.SetStatus(instance, outcome, "SeparationRecord", record.Id, "سبب",
            [new WorkflowStepDecisionDto(2, "hr-management-approver", Guid.NewGuid(), outcome, DateTime.UtcNow)]);

        var result = await h.InScopeAsync(sp => sp.GetRequiredService<SeparationApprovalService>().WithdrawAsync(record.Id, Guid.NewGuid(), "سحب"));

        Assert.False(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(outcome, result.FinalOutcome);
        Assert.Equal(expected, (await h.ReadAsync(record.Id)).Status);
    }

    [Fact]
    public async Task Only_a_pending_record_is_withdrawn()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        var result = await h.InScopeAsync(sp => sp.GetRequiredService<SeparationApprovalService>().WithdrawAsync(record.Id, Guid.NewGuid(), null));
        Assert.Equal(SeparationApprovalService.NotPendingMessage, result.Error);
        Assert.Empty(h.Engine.Cancels);
    }
}
