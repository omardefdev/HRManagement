using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-027 (D): runs <see cref="RecruitmentOfferExpirer"/> on a schedule
/// (<c>Recruitment:Jobs:OfferExpiryIntervalMinutes</c>, enabled by <c>Recruitment:Jobs:OfferExpiryEnabled</c>).
/// Guarded by a PostgreSQL advisory lock so only one HR Core replica expires offers per cycle.
/// </summary>
public sealed class RecruitmentOfferExpiryJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RecruitmentOptions> options,
    ILogger<RecruitmentOfferExpiryJob> logger) : BackgroundService
{
    /// <summary>Stable key; LeaveScheduleReconcilerJob = 8675311, RecruitmentIntakeCloseJob = 8675312.</summary>
    private const long AdvisoryLockKey = 8675313;

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
            if (options.CurrentValue.Jobs.OfferExpiryEnabled)
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
                    logger.LogError(ex, "Recruitment offer expiry cycle failed.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.Jobs.OfferExpiryIntervalMinutes)), stoppingToken);
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
            ct => scope.ServiceProvider.GetRequiredService<RecruitmentOfferExpirer>()
                .ExpireDueAsync(DateTime.UtcNow, ct),
            cancellationToken);
    }
}
