using File.Storage.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace File.Storage.Infrastructure.Jobs;

/// <summary>
/// Hourly GC: marks unreferenced files for purge, then deletes objects of due purge items.
/// <para>
/// The job has no request tenant, so it only reaches tenant-owned files with
/// <see cref="AllTenantsKey"/> = true (default false). Turn it on only after every holder delivers Register/Release per
/// tenant — HR.Core's file-reference outbox dispatcher with X-Tenant-ID — and its backlog of Pending Register rows is
/// drained: until then a Pending file may be in use with its Register still queued, and collecting it deletes the file.
/// See docs/FILE_SERVICE.md (Garbage collection).
/// </para>
/// </summary>
public sealed class FileGarbageCollectorJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<FileGarbageCollectorJob> logger) : BackgroundService
{
    public const string AllTenantsKey = "FileStorage:GarbageCollection:AllTenants";

    public static bool AllTenantsEnabled(IConfiguration configuration) =>
        configuration.GetValue(AllTenantsKey, false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var allTenants = AllTenantsEnabled(configuration);
        if (!allTenants)
        {
            logger.LogWarning(
                "File GC runs without a tenant and {Key} is false: tenant-owned files are neither collected nor purged. " +
                "Enable it once holders deliver file references per tenant (docs/FILE_SERVICE.md).",
                AllTenantsKey);
        }

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
                        var marked = await platform.CollectGarbageAsync(allTenants, ct);
                        var purged = await platform.ProcessPurgeQueueAsync(allTenants, ct);
                        activity?.SetTag("files.marked", marked);
                        activity?.SetTag("files.purged", purged);
                        activity?.SetTag("files.all_tenants", allTenants);
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
