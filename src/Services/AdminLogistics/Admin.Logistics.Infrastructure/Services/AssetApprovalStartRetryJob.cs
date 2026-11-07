using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Admin.Logistics.Infrastructure.Services;

/// <summary>
/// PLAN-45 (G-WF-02): every <c>AssetApproval:Retry:IntervalSeconds</c>, starts the approval of the requests still in
/// <c>ApprovalStarting</c>, after HR.Core was down or its answer was lost. Each request runs in its own scope, with its tenant in
/// <see cref="TenantIdFlow"/> before the connection opens and its requester in <see cref="ActingUserScope"/>: under forced row security a
/// session with no tenant finds no row, and a start with no tenant would land in the national tenant (PLAN-43's L7). Guarded by an
/// advisory lock, so one replica runs a cycle.
/// </summary>
public sealed class AssetApprovalStartRetryJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AssetApprovalOptions> options,
    ILogger<AssetApprovalStartRetryJob> logger) : BackgroundService
{
    private const int BatchSize = 100;

    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.Retry.IntervalSeconds));

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
                logger.LogError(ex, "Asset approval start cycle failed.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();

        await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db, AdvisoryLockKeys.AssetApprovalStartRetry, ct => RetryAllTenantsAsync(db, ct), cancellationToken);
    }

    /// <summary>
    /// This database has no tenant list. The audit log is outside row security, and every saved request has a row in it from the same
    /// save, so the tenants that ever made a request are found with no new trust between services.
    /// </summary>
    public static Task<List<Guid>> FindTenantsAsync(AdminLogisticsDbContext db, CancellationToken ct = default) =>
        db.AuditLogs.AsNoTracking()
            .Where(l => (l.EntityType == nameof(AssetAssignment) || l.EntityType == nameof(AssetLossReport)) && l.TenantId != Guid.Empty)
            .Select(l => l.TenantId)
            .Distinct()
            .ToListAsync(ct);

    private async Task RetryAllTenantsAsync(AdminLogisticsDbContext db, CancellationToken ct)
    {
        var tenants = await FindTenantsAsync(db, ct);
        var (linked, waiting, intervention) = (0, 0, 0);

        foreach (var tenantId in tenants)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenantId);
            try
            {
                linked += await RetryTenantAsync(ct);

                var (tenantWaiting, tenantIntervention) = await CountAsync(ct);
                AssetApprovalMetrics.Set(tenantId, tenantWaiting, tenantIntervention);
                waiting += tenantWaiting;
                intervention += tenantIntervention;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not hide the others.
                logger.LogError(ex, "Asset approval start failed for tenant {TenantId}.", tenantId);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        logger.LogInformation(
            "Asset approval start: {Tenants} tenant(s), {Linked} linked, {Waiting} waiting, {Intervention} need intervention.",
            tenants.Count, linked, waiting, intervention);
    }

    private async Task<int> RetryTenantAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Interval;
        List<Guid> assignmentIds;
        List<Guid> reportIds;
        using (var listScope = scopeFactory.CreateScope())
        {
            var db = listScope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();
            assignmentIds = await db.AssetAssignments.AsNoTracking()
                .Where(a => a.Status == AssetAssignmentStatus.ApprovalStarting
                    && a.ReturnedAt == null
                    && !a.NeedsIntervention
                    && (a.ApprovalStartLastAttemptAt ?? a.CreatedAt) <= cutoff)
                .OrderBy(a => a.CreatedAt)
                .Select(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            reportIds = await db.AssetLossReports.AsNoTracking()
                .Where(r => r.Status == AssetLossReportStatus.ApprovalStarting
                    && !r.NeedsIntervention
                    && (r.ApprovalStartLastAttemptAt ?? r.CreatedAt) <= cutoff)
                .OrderBy(r => r.CreatedAt)
                .Select(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        var linked = 0;
        foreach (var id in assignmentIds)
            linked += await AdvanceOneAsync(id, async (db, ct2) => await db.AssetAssignments.Include(a => a.Asset).FirstOrDefaultAsync(a => a.Id == id, ct2), ct);
        foreach (var id in reportIds)
            linked += await AdvanceOneAsync(id, async (db, ct2) => await db.AssetLossReports.Include(r => r.Asset).FirstOrDefaultAsync(r => r.Id == id, ct2), ct);
        return linked;
    }

    /// <summary>A scope of its own: a lost race clears the tracker, which must not touch the next request.</summary>
    private async Task<int> AdvanceOneAsync(
        Guid id, Func<AdminLogisticsDbContext, CancellationToken, Task<IAssetApprovalRequest?>> load, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();
            var loop = scope.ServiceProvider.GetRequiredService<AssetApprovalLoop>();

            var request = await load(db, ct);
            if (request is null)
                return 0;

            if (request.CreatedBy is not Guid requester)
            {
                await loop.MarkUnstartableAsync(request, ct);
                return 0;
            }

            using var acting = ActingUserScope.Begin(requester, request.RequestedByName);
            var advance = request switch
            {
                AssetAssignment assignment => await loop.AdvanceAsync(assignment, ct),
                AssetLossReport report => await loop.AdvanceAsync(report, ct),
                _ => AssetApprovalAdvance.NotStarting
            };
            return advance is AssetApprovalAdvance.Pending or AssetApprovalAdvance.Finished ? 1 : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Asset approval start failed for request {RequestId}.", id);
            return 0;
        }
    }

    private async Task<(int Waiting, int Intervention)> CountAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();

        var assignments = db.AssetAssignments.AsNoTracking()
            .Where(a => a.Status == AssetAssignmentStatus.ApprovalStarting && a.ReturnedAt == null);
        var reports = db.AssetLossReports.AsNoTracking().Where(r => r.Status == AssetLossReportStatus.ApprovalStarting);

        return (
            await assignments.CountAsync(ct) + await reports.CountAsync(ct),
            await assignments.CountAsync(a => a.NeedsIntervention, ct) + await reports.CountAsync(r => r.NeedsIntervention, ct));
    }
}
