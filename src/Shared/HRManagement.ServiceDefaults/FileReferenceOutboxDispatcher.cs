using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public interface IFileReferenceOutbox
{
    void EnqueueRegister(Guid fileId, string holderKey);
    void EnqueueRelease(Guid fileId, string holderKey);
}

/// <summary>Writes outbox rows onto the ambient DbContext; caller must SaveChanges in the same TX.</summary>
public sealed class EfFileReferenceOutbox(DbContext db, ITenantContext? tenantContext = null) : IFileReferenceOutbox
{
    public void EnqueueRegister(Guid fileId, string holderKey) =>
        Enqueue(fileId, holderKey, FileReferenceOutboxOps.Register);

    public void EnqueueRelease(Guid fileId, string holderKey) =>
        Enqueue(fileId, holderKey, FileReferenceOutboxOps.Release);

    private void Enqueue(Guid fileId, string holderKey, string operation)
    {
        holderKey = holderKey.Trim();
        if (string.IsNullOrWhiteSpace(holderKey))
            throw new ArgumentException("holderKey is required.", nameof(holderKey));

        db.Set<FileReferenceOutboxMessage>().Add(new FileReferenceOutboxMessage
        {
            FileId = fileId,
            HolderKey = holderKey,
            Operation = operation,
            Status = FileReferenceOutboxStatus.Pending,
            NextAttemptAt = DateTime.UtcNow,
            TenantId = tenantContext?.TenantId ?? Guid.Empty
        });
    }
}

/// <summary>Enqueues into the local outbox instead of calling File.Storage synchronously.</summary>
public sealed class OutboxFileReferenceClient(IFileReferenceOutbox outbox) : IFileReferenceClient
{
    public Task RegisterReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default)
    {
        outbox.EnqueueRegister(fileId, holderKey);
        return Task.CompletedTask;
    }

    public Task ReleaseReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default)
    {
        outbox.EnqueueRelease(fileId, holderKey);
        return Task.CompletedTask;
    }
}

public sealed class FileReferenceOutboxDispatcherJob<TContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<FileReferenceOutboxDispatcherJob<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static int MaxAttempts =>
        int.TryParse(Environment.GetEnvironmentVariable("FileStorage__ReferenceOutbox__MaxAttempts"), out var n) ? n : 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                using var activity = JobActivitySource.Source.StartActivity("FileReferenceOutboxDispatcher");
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    AdvisoryLockKeys.FileReferenceOutbox,
                    async ct =>
                    {
                        var http = scope.ServiceProvider.GetRequiredService<HttpFileReferenceClient>();
                        var processed = await ProcessBatchAsync(db, http, ct);
                        activity?.SetTag("outbox.processed", processed);
                        JobActivitySource.Cycles.Add(1, new KeyValuePair<string, object?>("job", "file-ref-outbox"));
                        if (processed > 0)
                            logger.LogInformation("File reference outbox processed {Count} messages", processed);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                JobActivitySource.Failures.Add(1, new KeyValuePair<string, object?>("job", "file-ref-outbox"));
                logger.LogError(ex, "File reference outbox dispatcher failed.");
            }
        }
    }

    public static async Task<int> ProcessBatchAsync(
        DbContext db,
        IFileReferenceClient delivery,
        CancellationToken cancellationToken,
        int batchSize = 50)
    {
        var now = DateTime.UtcNow;
        var items = await db.Set<FileReferenceOutboxMessage>()
            .Where(x => x.Status == FileReferenceOutboxStatus.Pending && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var item in items)
        {
            try
            {
                if (item.Operation == FileReferenceOutboxOps.Register)
                    await delivery.RegisterReferenceAsync(item.FileId, item.HolderKey, cancellationToken);
                else if (item.Operation == FileReferenceOutboxOps.Release)
                    await delivery.ReleaseReferenceAsync(item.FileId, item.HolderKey, cancellationToken);
                else
                    throw new InvalidOperationException($"Unknown outbox operation '{item.Operation}'.");

                item.Status = FileReferenceOutboxStatus.Succeeded;
                item.ProcessedAt = DateTime.UtcNow;
                item.LastError = null;
                processed++;
            }
            catch (Exception ex)
            {
                item.Attempt++;
                item.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (item.Attempt >= MaxAttempts)
                {
                    item.Status = FileReferenceOutboxStatus.Dead;
                }
                else
                {
                    var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(item.Attempt, 10)));
                    item.NextAttemptAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return processed;
    }
}

public static class FileReferenceOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Holder-side durable file reference registration: outbox rows + dispatcher that delivers via HTTP.
    /// Call after AddDbContext and AddHrFileReferenceClient (replaces IFileReferenceClient with outbox writer).
    /// Set <c>FileReferenceOutbox:Enabled=false</c> to skip the background dispatcher (dev/UAT).
    /// </summary>
    public static IServiceCollection AddFileReferenceOutbox<TContext>(
        this IServiceCollection services,
        IConfiguration? configuration = null)
        where TContext : DbContext
    {
        services.AddScoped<HttpFileReferenceClient>();
        services.AddScoped<IFileReferenceOutbox>(sp =>
            new EfFileReferenceOutbox(
                sp.GetRequiredService<TContext>(),
                sp.GetService<ITenantContext>()));
        services.AddScoped<IFileReferenceClient, OutboxFileReferenceClient>();

        var enabled = configuration?.GetValue("FileReferenceOutbox:Enabled", true) ?? true;
        if (enabled)
            services.AddHostedService<FileReferenceOutboxDispatcherJob<TContext>>();

        return services;
    }
}
