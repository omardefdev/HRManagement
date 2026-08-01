using File.Storage.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace File.Storage.Infrastructure.Jobs;

public sealed class FileScanProcessorJob(
    IServiceScopeFactory scopeFactory,
    ILogger<FileScanProcessorJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = JobActivitySource.Source.StartActivity("FileScanProcessor");
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<File.Storage.Infrastructure.Data.FileStorageDbContext>();
                await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    AdvisoryLockKeys.FileScanProcessor,
                    async ct =>
                    {
                        var platform = scope.ServiceProvider.GetRequiredService<FilePlatformService>();
                        var completed = await platform.ProcessScanQueueAsync(ct);
                        activity?.SetTag("files.scanned", completed);
                        JobActivitySource.Cycles.Add(1, new KeyValuePair<string, object?>("job", "file-scan"));
                        if (completed > 0)
                            logger.LogInformation("File scan processor completed {Count} scan work items.", completed);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                JobActivitySource.Failures.Add(1, new KeyValuePair<string, object?>("job", "file-scan"));
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "File scan processor failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
