using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-027 (F4): runs <see cref="RecruitmentReserveExpirer"/> on a schedule
/// (<c>Recruitment:Jobs:ReserveExpiryIntervalMinutes</c>, daily by default; enabled by
/// <c>Recruitment:Jobs:ReserveExpiryEnabled</c>). Guarded by a PostgreSQL advisory lock so only one
/// HR Core replica expires the reserve list per cycle.
/// </summary>
public sealed class RecruitmentReserveExpiryJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RecruitmentOptions> options,
    ILogger<RecruitmentReserveExpiryJob> logger) : BackgroundService
{
    /// <summary>Stable key; 8675311 leave reconciler, 8675312 intake close, 8675313 offer expiry.</summary>
    private const long AdvisoryLockKey = 8675314;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first pass so a rolling deploy does not have every replica contend at once.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Jobs.ReserveExpiryEnabled)
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
                    logger.LogError(ex, "Recruitment reserve expiry cycle failed.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.Jobs.ReserveExpiryIntervalMinutes)), stoppingToken);
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
            ct => scope.ServiceProvider.GetRequiredService<RecruitmentReserveExpirer>()
                .ExpireDueAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct),
            cancellationToken);
    }
}
