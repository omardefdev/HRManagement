using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.TestSupport.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>PLAN-46: the sweep walks the tenants, applies only what finished, and one bad record or tenant does not stop the others.</summary>
[Collection("TenantFlow")]
public sealed class Wf046ApprovalSweepJobTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Theory]
    [InlineData(WorkflowApprovalOutcomes.Approved)]
    [InlineData(WorkflowApprovalOutcomes.Rejected)]
    [InlineData(WorkflowApprovalOutcomes.Cancelled)]
    public async Task A_finished_outcome_is_applied_under_the_tenant_of_the_record(string outcome)
    {
        var state = new SweepState();
        var record = state.Pending(TenantA);
        state.Engine.SetStatus(record.InstanceId, outcome, "Test", record.EntityId, reason: "السبب");

        await Job(state).RunOnceAsync();

        var applied = Assert.Single(state.Applied);
        Assert.Equal(TenantA, applied.Flow);
        Assert.Equal(TenantA, applied.Outcome.TenantId);
        Assert.Equal(record.InstanceId, applied.Outcome.InstanceId);
        Assert.Equal(record.EntityId, applied.Outcome.EntityId);
        Assert.Equal(outcome, applied.Outcome.Outcome);
        Assert.Equal("السبب", applied.Outcome.Reason);
        Assert.Null(TenantIdFlow.Get());
    }

    [Fact]
    public async Task A_running_instance_and_a_status_the_engine_did_not_give_change_nothing()
    {
        var state = new SweepState();
        var running = state.Pending(TenantA);
        var unknown = state.Pending(TenantA);
        state.Engine.SetStatus(running.InstanceId, WorkflowApprovalOutcomes.InProgress);

        await Job(state).RunOnceAsync();

        Assert.Empty(state.Applied);
        Assert.Equal([running.InstanceId, unknown.InstanceId], state.Engine.StatusReads);
    }

    [Fact]
    public async Task Each_tenant_lists_its_own_records()
    {
        var state = new SweepState();
        var a = state.Pending(TenantA);
        var b = state.Pending(TenantB);
        state.Engine.SetStatus(a.InstanceId, WorkflowApprovalOutcomes.Approved);
        state.Engine.SetStatus(b.InstanceId, WorkflowApprovalOutcomes.Approved);

        await Job(state).RunOnceAsync();

        Assert.Equal([TenantA, TenantB], state.Applied.Select(x => x.Flow).OrderBy(t => t == TenantB));
    }

    [Fact]
    public async Task The_cutoff_and_the_batch_come_from_the_options()
    {
        var state = new SweepState();
        var before = DateTime.UtcNow.AddMinutes(-15);
        state.Pending(TenantA);

        await Job(state, new WorkflowApprovalSweepOptions { StaleAfterMinutes = 15, BatchSize = 7 }).RunOnceAsync();

        Assert.Equal(7, state.LastTake);
        Assert.InRange(state.LastCutoff, before.AddSeconds(-1), DateTime.UtcNow.AddMinutes(-15).AddSeconds(1));
    }

    [Fact]
    public async Task A_record_that_fails_does_not_stop_the_next()
    {
        var state = new SweepState();
        var bad = state.Pending(TenantA);
        var good = state.Pending(TenantA);
        state.Engine.SetStatus(bad.InstanceId, WorkflowApprovalOutcomes.Approved);
        state.Engine.SetStatus(good.InstanceId, WorkflowApprovalOutcomes.Approved);
        state.FailOn = bad.InstanceId;

        await Job(state).RunOnceAsync();

        Assert.Equal(good.InstanceId, Assert.Single(state.Applied).Outcome.InstanceId);
    }

    [Fact]
    public async Task A_tenant_that_fails_does_not_stop_the_next()
    {
        var state = new SweepState { FailTenant = TenantA };
        state.Pending(TenantA);
        var b = state.Pending(TenantB);
        state.Engine.SetStatus(b.InstanceId, WorkflowApprovalOutcomes.Approved);

        await Job(state).RunOnceAsync();

        Assert.Equal(TenantB, Assert.Single(state.Applied).Flow);
        Assert.Null(TenantIdFlow.Get());
    }

    [Fact]
    public void A_finished_status_reads_as_the_event()
    {
        var at = DateTime.UtcNow;
        var approver = Guid.NewGuid();
        var status = new WorkflowApprovalStatusDto(
            Guid.NewGuid(), "CardRequest", Guid.NewGuid(), "ID-CARD-ISSUANCE", 2, WorkflowApprovalOutcomes.Approved, null, null, null,
            [new WorkflowStepDecisionDto(1, "idcard", approver, "Approved", at)], at.AddHours(-1), at, "/ui", null, "CardRequest:1:1");

        var outcome = WorkflowOutcomeDecision.From(status, TenantA);

        Assert.Equal(approver, outcome.DecidedByUserId);
        Assert.Equal(at, outcome.DecidedAt);
        Assert.Equal("CardRequest:1:1", outcome.CorrelationId);
        Assert.Equal(1, ((IWorkflowOutcomeDecided)outcome).SchemaVersion);
    }

    private static WorkflowApprovalSweepJob<StubStore> Job(SweepState state, WorkflowApprovalSweepOptions? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddScoped<StubStore>();
        services.AddSingleton<IWorkflowApprovalClient>(state.Engine);
        services.AddDbContext<LeaderDb>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();

        return new WorkflowApprovalSweepJob<StubStore>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptions(settings ?? new WorkflowApprovalSweepOptions()),
            NullLogger<WorkflowApprovalSweepJob<StubStore>>.Instance);
    }

    private sealed class LeaderDb(DbContextOptions<LeaderDb> options) : DbContext(options);

    private sealed class SweepState
    {
        public FakeWorkflowApprovalClient Engine { get; } = new();

        public Dictionary<Guid, List<WorkflowApprovalPendingRecord>> Records { get; } = [];

        public List<(Guid? Flow, IWorkflowOutcomeDecided Outcome)> Applied { get; } = [];

        public Guid? FailOn { get; set; }

        public Guid? FailTenant { get; set; }

        public DateTime LastCutoff { get; set; }

        public int LastTake { get; set; }

        public WorkflowApprovalPendingRecord Pending(Guid tenant)
        {
            var record = new WorkflowApprovalPendingRecord(Guid.NewGuid(), Guid.NewGuid());
            if (!Records.TryGetValue(tenant, out var list))
                Records[tenant] = list = [];
            list.Add(record);
            return record;
        }
    }

    private sealed class StubStore(SweepState state, LeaderDb db) : IWorkflowApprovalOwnerStore
    {
        public string EntityType => "Test";

        public DbContext LeaderDatabase => db;

        public long LeaderLockKey => 1;

        public Task<IReadOnlyList<Guid>> ListTenantsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(state.Records.Keys.ToList());

        public Task<IReadOnlyList<WorkflowApprovalPendingRecord>> ListPendingAsync(DateTime submittedBefore, int take, CancellationToken ct)
        {
            state.LastCutoff = submittedBefore;
            state.LastTake = take;
            var tenant = TenantIdFlow.Get()!.Value;
            if (tenant == state.FailTenant)
                throw new InvalidOperationException("this tenant fails");
            return Task.FromResult<IReadOnlyList<WorkflowApprovalPendingRecord>>(state.Records[tenant]);
        }

        public Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct)
        {
            if (outcome.InstanceId == state.FailOn)
                throw new InvalidOperationException("this record fails");
            state.Applied.Add((TenantIdFlow.Get(), outcome));
            return Task.CompletedTask;
        }
    }

    private sealed class StaticOptions(WorkflowApprovalSweepOptions value) : IOptionsMonitor<WorkflowApprovalSweepOptions>
    {
        public WorkflowApprovalSweepOptions CurrentValue => value;

        public WorkflowApprovalSweepOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<WorkflowApprovalSweepOptions, string?> listener) => null;
    }
}
