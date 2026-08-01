using File.Storage.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace File.Storage.Infrastructure.Jobs;

public sealed class FileGarbageCollectorJob(
    IServiceScopeFactory scopeFactory,
    ILogger<FileGarbageCollectorJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = JobActivitySource.Source.StartActivity("FileGarbageCollector");
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<File.Storage.Infrastructure.Data.FileStorageDbContext>();
                await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    AdvisoryLockKeys.FileGarbageCollector,
                    async ct =>
                    {
                        var platform = scope.ServiceProvider.GetRequiredService<FilePlatformService>();
                        var marked = await platform.CollectGarbageAsync(ct);
                        var purged = await platform.ProcessPurgeQueueAsync(ct);
                        activity?.SetTag("files.marked", marked);
                        activity?.SetTag("files.purged", purged);
                        JobActivitySource.Cycles.Add(1, new KeyValuePair<string, object?>("job", "file-gc"));
                        if (marked > 0 || purged > 0)
                            logger.LogInformation(
                                "File GC marked {Marked} for purge and completed {Purged} object deletes.",
                                marked,
                                purged);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                JobActivitySource.Failures.Add(1, new KeyValuePair<string, object?>("job", "file-gc"));
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "File garbage collector failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
