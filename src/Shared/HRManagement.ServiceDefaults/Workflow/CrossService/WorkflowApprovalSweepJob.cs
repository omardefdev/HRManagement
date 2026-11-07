using HRManagement.MessageContracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Workflow.CrossService;

public sealed class WorkflowApprovalSweepOptions
{
    public const string SectionName = "WorkflowApproval:Sweep";

    public int IntervalMinutes { get; set; } = 10;

    public int StaleAfterMinutes { get; set; } = 15;

    public int BatchSize { get; set; } = 100;
}

/// <summary>A record waiting for its approval, and the instance it is linked to.</summary>
public sealed record WorkflowApprovalPendingRecord(Guid EntityId, Guid InstanceId);

/// <summary>
/// PLAN-46 (section 1, rule 7): what an owner gives the sweep. Each owner registers one for its type, with
/// <see cref="WorkflowApprovalClientExtensions.AddWorkflowApprovalSweep{TStore}"/>.
/// </summary>
public interface IWorkflowApprovalOwnerStore
{
    string EntityType { get; }

    /// <summary>The database whose advisory lock elects one replica per cycle.</summary>
    DbContext LeaderDatabase { get; }

    /// <summary>The advisory lock. One key per type in a database.</summary>
    long LeaderLockKey { get; }

    /// <summary>The tenants that ever saved such a record, from a source outside row security.</summary>
    Task<IReadOnlyList<Guid>> ListTenantsAsync(CancellationToken ct);

    /// <summary>Runs under the tenant of <see cref="TenantIdFlow"/>: the records waiting since before <paramref name="submittedBefore"/>.</summary>
    Task<IReadOnlyList<WorkflowApprovalPendingRecord>> ListPendingAsync(DateTime submittedBefore, int take, CancellationToken ct);

    /// <summary>The same function the outcome consumer runs, guard included.</summary>
    Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct);
}

/// <summary>
/// PLAN-46 (section 1, rule 7): catches the outcome an owner missed, for instance one that arrived before the owner saved
/// <c>PendingApproval</c>. Every <c>WorkflowApproval:Sweep:IntervalMinutes</c>, one replica walks the tenants, asks the engine about each
/// record waiting longer than <c>StaleAfterMinutes</c>, and applies a finished outcome. The tenant is in <see cref="TenantIdFlow"/> before
/// any scope opens a connection: under forced row security a session with no tenant finds no row.
/// </summary>
public sealed class WorkflowApprovalSweepJob<TStore>(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WorkflowApprovalSweepOptions> options,
    ILogger<WorkflowApprovalSweepJob<TStore>> logger) : BackgroundService
    where TStore : class, IWorkflowApprovalOwnerStore
{
    private TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.IntervalMinutes));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workflow approval sweep cycle failed.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TStore>();

        await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            store.LeaderDatabase, store.LeaderLockKey, ct => SweepAllTenantsAsync(store, ct), cancellationToken);
    }

    private async Task SweepAllTenantsAsync(TStore leaderStore, CancellationToken ct)
    {
        WorkflowOutcomeGuard.TouchMetric(leaderStore.EntityType);
        var tenants = await leaderStore.ListTenantsAsync(ct);
        var (stale, applied) = (0, 0);

        foreach (var tenantId in tenants)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenantId);
            try
            {
                var (tenantStale, tenantApplied) = await SweepTenantAsync(tenantId, ct);
                stale += tenantStale;
                applied += tenantApplied;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not hide the others.
                logger.LogError(ex, "Workflow approval sweep failed for tenant {TenantId}.", tenantId);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        WorkflowMetrics.ApprovalSweepApplied.Add(applied, new KeyValuePair<string, object?>("entity_type", leaderStore.EntityType));
        logger.LogInformation(
            "Workflow approval sweep of {EntityType}: {Tenants} tenant(s), {Stale} waiting, {Applied} applied.",
            leaderStore.EntityType, tenants.Count, stale, applied);
    }

    private async Task<(int Stale, int Applied)> SweepTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(0, settings.StaleAfterMinutes));

        IReadOnlyList<WorkflowApprovalPendingRecord> pending;
        using (var listScope = scopeFactory.CreateScope())
        {
            pending = await listScope.ServiceProvider.GetRequiredService<TStore>()
                .ListPendingAsync(cutoff, Math.Max(1, settings.BatchSize), ct);
        }

        var applied = 0;
        foreach (var record in pending)
            applied += await SweepOneAsync(record, tenantId, ct) ? 1 : 0;
        return (pending.Count, applied);
    }

    /// <summary>A scope of its own: a lost race clears the tracker, which must not touch the next record.</summary>
    private async Task<bool> SweepOneAsync(WorkflowApprovalPendingRecord record, Guid tenantId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IWorkflowApprovalClient>();

            var status = await client.GetStatusAsync(record.InstanceId, ct);
            if (status is null || !WorkflowApprovalOutcomes.IsFinal(status.Status))
                return false;

            await scope.ServiceProvider.GetRequiredService<TStore>()
                .ApplyOutcomeAsync(WorkflowOutcomeDecision.From(status, tenantId), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Workflow approval sweep failed for record {EntityId}.", record.EntityId);
            return false;
        }
    }
}
