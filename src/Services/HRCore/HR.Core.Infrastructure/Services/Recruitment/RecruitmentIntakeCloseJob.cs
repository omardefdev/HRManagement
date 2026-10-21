using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-026 (E4): runs <see cref="RecruitmentIntakeCloser"/> on a schedule
/// (<c>Recruitment:Jobs:IntakeCloseIntervalMinutes</c>, enabled by <c>Recruitment:Jobs:IntakeCloseEnabled</c>).
/// Guarded by a PostgreSQL advisory lock so only one HR Core replica closes intake per cycle.
/// </summary>
public sealed class RecruitmentIntakeCloseJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RecruitmentOptions> options,
    ILogger<RecruitmentIntakeCloseJob> logger) : BackgroundService
{
    /// <summary>Arbitrary but stable key; must not collide with other advisory lock users (see LeaveScheduleReconcilerJob = 8675311).</summary>
    private const long AdvisoryLockKey = 8675312;

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
            if (options.CurrentValue.Jobs.IntakeCloseEnabled)
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
                    logger.LogError(ex, "Recruitment intake close cycle failed.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.Jobs.IntakeCloseIntervalMinutes)), stoppingToken);
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
            ct => scope.ServiceProvider.GetRequiredService<RecruitmentIntakeCloser>()
                .CloseExpiredAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct),
            cancellationToken);
    }
}
