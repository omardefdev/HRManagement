using System.Collections.Concurrent;
using System.Text.Json;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace HRManagement.Gateway.Services;

public interface ISecurityAuditPublisher
{
    Task PublishAsync(SecurityAuditEvent evt, CancellationToken ct = default);
    void RecordLookupHit(string? actorSub, string correlationId);
}

public sealed class SecurityAuditOptions
{
    public const string SectionName = "SecurityAudit";
    public int BulkLookupThresholdPerMinute { get; set; } = 60;
    public string JsonlPath { get; set; } = Path.Combine(Path.GetTempPath(), "hrm-security-audit");
    public string AlertSinkUrl { get; set; } = "http://alert-sink:8089/webhook";
    public bool DetectorEnabled { get; set; } = true;
    public int DetectorIntervalSeconds { get; set; } = 60;
    public string? RulesPath { get; set; }
    public string WormExportPath { get; set; } = Path.Combine(Path.GetTempPath(), "hrm-audit-worm");
    public int WormExportIntervalHours { get; set; } = 24;
}

public sealed class SecurityAuditPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<SecurityAuditOptions> options,
    ILogger<SecurityAuditPublisher> logger) : ISecurityAuditPublisher, ISecurityAuditSink
{
    private readonly ConcurrentDictionary<string, LookupWindow> _lookupWindows = new();

    public bool TryPublish(SecurityAuditPayload payload)
    {
        _ = PublishAsync(new SecurityAuditEvent
        {
            EventType = payload.EventType,
            ActorSub = payload.ActorSub,
            CorrelationId = payload.CorrelationId,
            Resource = payload.Resource,
            DetailJson = payload.DetailJson,
            TenantId = payload.TenantId,
            OccurredAtUtc = payload.OccurredAtUtc == default ? DateTimeOffset.UtcNow : payload.OccurredAtUtc
        });
        return true;
    }

    public async Task PublishAsync(SecurityAuditEvent evt, CancellationToken ct = default)
    {
        evt.OccurredAtUtc = evt.OccurredAtUtc == default ? DateTimeOffset.UtcNow : evt.OccurredAtUtc;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            db.SecurityAuditEvents.Add(evt);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist security audit event {Type}; writing JSONL", evt.EventType);
        }

        try
        {
            var dir = options.Value.JsonlPath;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"security-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(evt) + Environment.NewLine, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write security audit JSONL");
        }
    }

    public void RecordLookupHit(string? actorSub, string correlationId)
    {
        var key = actorSub ?? "anonymous";
        var window = _lookupWindows.GetOrAdd(key, _ => new LookupWindow());
        var count = window.Increment();
        var threshold = Math.Max(1, options.Value.BulkLookupThresholdPerMinute);
        if (count == threshold)
        {
            _ = PublishAsync(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventTypes.BulkLookup,
                ActorSub = actorSub,
                CorrelationId = correlationId,
                Resource = "api/lookup",
                DetailJson = $"{{\"count\":{count},\"window\":\"1m\"}}"
            });
        }
    }

    private sealed class LookupWindow
    {
        private int _count;
        private long _windowStartTicks = DateTime.UtcNow.Ticks;

        public int Increment()
        {
            var now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref _windowStartTicks) > TimeSpan.FromMinutes(1).Ticks)
            {
                Interlocked.Exchange(ref _windowStartTicks, now);
                Interlocked.Exchange(ref _count, 0);
            }

            return Interlocked.Increment(ref _count);
        }
    }
}
