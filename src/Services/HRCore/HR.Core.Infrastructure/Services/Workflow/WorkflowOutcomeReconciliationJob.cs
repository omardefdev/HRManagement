using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>PLAN-44: <c>Workflow:Reconciliation</c>.</summary>
public sealed class WorkflowReconciliationOptions
{
    public const string SectionName = "Workflow:Reconciliation";

    public int IntervalMinutes { get; set; } = 15;

    /// <summary>False: the job detects, logs and updates the metric. Repair goes through the operator endpoint, dry run first.</summary>
    public bool AutoApply { get; set; }
}

/// <summary>
/// PLAN-44 (G-WF-12): every <see cref="WorkflowReconciliationOptions.IntervalMinutes"/>, looks for outcomes that slipped in every tenant.
/// Each tenant runs in its own scope with <see cref="TenantIdFlow"/> set before the connection opens: under forced row security a session
/// with no tenant sees no row, and would report that nothing is wrong. Guarded by an advisory lock, so one replica runs a cycle.
/// </summary>
public sealed class WorkflowOutcomeReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WorkflowReconciliationOptions> options,
    ILogger<WorkflowOutcomeReconciliationJob> logger) : BackgroundService
{
    /// <summary>Stable key; the recruitment jobs use 8675311 to 8675316.</summary>
    private const long AdvisoryLockKey = 8675344;

    private const string ActorName = "workflow-reconciliation";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first pass so a rolling deploy does not have every replica contend at once.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workflow reconciliation cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.IntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();

        await AdvisoryLeaderElection.TryRunAsLeaderAsync(db, AdvisoryLockKey, ct => ReconcileAllTenantsAsync(db, ct), cancellationToken);
    }

    /// <summary>The list of tenants is a platform table, outside row security.</summary>
    internal async Task ReconcileAllTenantsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var tenants = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        var autoApply = options.CurrentValue.AutoApply;
        var found = 0;

        foreach (var tenantId in tenants)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenantId);
            try
            {
                using var tenantScope = scopeFactory.CreateScope();
                var report = await tenantScope.ServiceProvider.GetRequiredService<ApprovalRepairService>()
                    .RunAsync(!autoApply, actorUserId: null, ActorName, ct, ApprovalRepairScope.Outcomes);

                found += report.Found;
                foreach (var item in report.Items)
                {
                    logger.LogWarning(
                        "Workflow reconciliation: {EntityType} {EntityId}, instance {WorkflowInstanceId}: {Result}",
                        item.EntityType, item.EntityId, item.WorkflowInstanceId, item.Result);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not hide the others.
                logger.LogError(ex, "Workflow reconciliation failed for tenant {TenantId}.", tenantId);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        logger.LogInformation(
            "Workflow reconciliation: {Tenants} tenant(s), {Found} item(s), auto-apply {AutoApply}.", tenants.Count, found, autoApply);
    }
}
