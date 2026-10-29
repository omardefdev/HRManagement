using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-030 (G3): runs <see cref="CandidateRetentionService"/> across tenants every
/// <c>Recruitment:Jobs:CandidateRetentionIntervalMinutes</c> (10080, weekly), enabled by
/// <c>Recruitment:Jobs:CandidateRetentionEnabled</c>. A dry run (counts only) unless <c>Recruitment:Retention:Apply</c> is
/// true. Guarded by a PostgreSQL advisory lock, shared with the manual runs of the admin screen, so one run at a time.
/// Checked hourly; a tenant is processed when the run log has no <em>finished</em> job run of it within the interval, so
/// restarts do not re-run it and a cycle cut short (restart, cancellation, failure) resumes with the tenants it missed.
/// </summary>
public sealed class CandidateRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RecruitmentOptions> options,
    ILogger<CandidateRetentionJob> logger) : BackgroundService
{
    /// <summary>
    /// Stable key; 8675309 migrations, 8675311 leave reconciler, 8675312 intake close, 8675313 offer expiry,
    /// 8675314 reserve expiry, 8675315 candidate notifications.
    /// </summary>
    public const long AdvisoryLockKey = 8675316;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            if (options.CurrentValue.Jobs.CandidateRetentionEnabled)
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
                    JobActivitySource.Failures.Add(1, new KeyValuePair<string, object?>("job", "candidate-retention"));
                    logger.LogError(ex, "Candidate retention cycle failed.");
                }
            }

            try
            {
                // Re-checked hourly so a restart or a changed interval takes effect; the run log decides whether to run.
                await Task.Delay(TimeSpan.FromMinutes(Math.Min(60, Interval.TotalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(60, options.CurrentValue.Jobs.CandidateRetentionIntervalMinutes));

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var activity = JobActivitySource.Source.StartActivity("CandidateRetention");
        JobActivitySource.Cycles.Add(1, new KeyValuePair<string, object?>("job", "candidate-retention"));
        // Per tenant, inside the lock: tenants with a finished job run within the interval are skipped, so a cycle cut short
        // resumes with the tenants it did not finish, and two replicas never both run the same week.
        var runs = await RunWithLeaderLockAsync(
            scopeFactory,
            new CandidateRetentionRunRequest(
                DateTime.UtcNow, options.CurrentValue.Retention.Apply, CandidateRetentionRun.JobTrigger,
                SkipTenantsCompletedWithin: Interval),
            cancellationToken);
        if (runs is { Count: > 0 })
        {
            logger.LogInformation(
                "Candidate retention run: {Tenants} tenant(s), dry run {DryRun}, due {Due}, anonymized {Anonymized}, held {Held}, appeal {Appeal}.",
                runs.Count, !options.CurrentValue.Retention.Apply, runs.Sum(r => r.DueCount), runs.Sum(r => r.AnonymizedCount),
                runs.Sum(r => r.SkippedLegalHold), runs.Sum(r => r.SkippedAppeal));
        }
    }

    /// <summary>
    /// Runs <see cref="CandidateRetentionService.RunAsync"/> in a fresh scope under the job's advisory lock
    /// (<see cref="AdvisoryLeaderElection"/> disposes the context's connection, so never with a request's context).
    /// Null when another replica or run holds the lock.
    /// </summary>
    public static async Task<IReadOnlyList<CandidateRetentionRun>?> RunWithLeaderLockAsync(
        IServiceScopeFactory scopeFactory,
        CandidateRetentionRunRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.HRCoreDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<CandidateRetentionService>();
        IReadOnlyList<CandidateRetentionRun>? runs = null;
        var acquired = await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db,
            AdvisoryLockKey,
            async ct => runs = await service.RunAsync(request, ct),
            cancellationToken);
        return acquired ? runs : null;
    }
}
