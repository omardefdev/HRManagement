using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// COM-030 (PLAN-30 F4): runs <see cref="CandidateNotificationDispatcher"/> (offer reminders, planning, sending) every
/// <c>Recruitment:Jobs:CandidateNotificationsIntervalSeconds</c> (60), enabled by
/// <c>Recruitment:Jobs:CandidateNotificationsEnabled</c>. Guarded by a PostgreSQL advisory lock so only one HR Core replica
/// plans and sends per cycle.
/// </summary>
public sealed class CandidateNotificationJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RecruitmentOptions> options,
    ILogger<CandidateNotificationJob> logger) : BackgroundService
{
    /// <summary>
    /// Stable key; 8675309 migrations, 8675311 leave reconciler, 8675312 intake close, 8675313 offer expiry,
    /// 8675314 reserve expiry.
    /// </summary>
    private const long AdvisoryLockKey = 8675315;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Short stagger: replicas of a rolling deploy do not contend at once, and acceptance sees the first pass quickly.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Jobs.CandidateNotificationsEnabled)
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
                    JobActivitySource.Failures.Add(1, new KeyValuePair<string, object?>("job", "candidate-notifications"));
                    logger.LogError(ex, "Candidate notification cycle failed.");
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(10, options.CurrentValue.Jobs.CandidateNotificationsIntervalSeconds)),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var activity = JobActivitySource.Source.StartActivity("CandidateNotifications");
        JobActivitySource.Cycles.Add(1, new KeyValuePair<string, object?>("job", "candidate-notifications"));
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.HRCoreDbContext>();

        await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db,
            AdvisoryLockKey,
            ct => scope.ServiceProvider.GetRequiredService<CandidateNotificationDispatcher>()
                .RunOnceAsync(DateTime.UtcNow, ct),
            cancellationToken);
    }
}
