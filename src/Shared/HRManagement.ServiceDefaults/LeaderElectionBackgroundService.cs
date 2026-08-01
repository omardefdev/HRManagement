using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Runs a periodic job on a single cluster node using PostgreSQL advisory locks (HA-DR).
/// </summary>
public abstract class LeaderElectionBackgroundService(
    IServiceScopeFactory scopeFactory,
    long lockKey,
    ILogger logger) : BackgroundService
{
    protected abstract TimeSpan PollInterval { get; }
    protected abstract string JobName { get; }
    protected abstract Task ExecuteLeaderWorkAsync(IServiceScope scope, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DbContext>();
                var ran = await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    lockKey,
                    ct => ExecuteLeaderWorkAsync(scope, ct),
                    stoppingToken);

                if (ran)
                    logger.LogDebug("{JobName} completed on leader node", JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "{JobName} leader cycle failed", JobName);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
