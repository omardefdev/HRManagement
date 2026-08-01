using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>OBS-001: outbox depth / age gauges (MassTransit EF outbox + file reference outbox).</summary>
public sealed class OutboxMetrics
{
    public const string MeterName = "HRManagement.Outbox";

    private readonly Meter _meter;

    public OutboxMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        Pending = _meter.CreateObservableGauge(
            "outbox_pending_messages",
            () => _pending,
            unit: "{message}");
        OldestAgeSeconds = _meter.CreateObservableGauge(
            "outbox_oldest_age_seconds",
            () => _oldestAgeSeconds,
            unit: "s");
    }

    private long _pending;
    private double _oldestAgeSeconds;

    // Keep gauges referenced so analyzers don't drop them.
    private ObservableGauge<long> Pending { get; }
    private ObservableGauge<double> OldestAgeSeconds { get; }

    public void Update(long pending, double oldestAgeSeconds)
    {
        _pending = pending;
        _oldestAgeSeconds = oldestAgeSeconds;
    }
}

/// <summary>Periodically samples pending outbox rows when DbContext is available.</summary>
public sealed class OutboxMetricsHostedService(
    IServiceScopeFactory scopeFactory,
    OutboxMetrics metrics,
    ILogger<OutboxMetricsHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetService<DbContext>();
                if (db is not null)
                {
                    long pending = 0;
                    DateTime? oldest = null;

                    var fileOutbox = db.Model.FindEntityType(typeof(FileReferenceOutboxMessage));
                    if (fileOutbox is not null)
                    {
                        var q = db.Set<FileReferenceOutboxMessage>()
                            .Where(x => x.Status == FileReferenceOutboxStatus.Pending);
                        pending += await q.LongCountAsync(stoppingToken);
                        oldest = await q.Select(x => (DateTime?)x.NextAttemptAt).MinAsync(stoppingToken);
                    }

                    metrics.Update(
                        pending,
                        oldest.HasValue ? Math.Max(0, (DateTime.UtcNow - oldest.Value).TotalSeconds) : 0);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Outbox metrics sample failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
