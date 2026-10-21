using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services;

/// <summary>
/// Runs <see cref="LeaveScheduleReconciler"/> on a schedule so employment status follows approved
/// leave windows without anyone touching it by hand.
/// <para>
/// Guarded by a PostgreSQL advisory lock: with several HR Core replicas every instance would
/// otherwise reconcile the same employees concurrently and write duplicate history rows.
/// </para>
/// </summary>
public sealed class LeaveScheduleReconcilerJob(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveScheduleReconcilerJob> logger) : BackgroundService
{
    /// <summary>Arbitrary but stable key; must not collide with other advisory lock users.</summary>
    private const long AdvisoryLockKey = 8675311;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
                logger.LogError(ex, "Leave schedule reconciliation cycle failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
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
        var db = scope.ServiceProvider.GetRequiredService<Data.HRCoreDbContext>();

        await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db,
            AdvisoryLockKey,
            ct => ReconcileAsync(scope, ct),
            cancellationToken);
    }

    private static Task ReconcileAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var reconciler = scope.ServiceProvider.GetRequiredService<LeaveScheduleReconciler>();
        return reconciler.ReconcileAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    }
}
