using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRManagement.Gateway.Services;

/// <summary>OBS-003: evaluate policy/siem rules against security_audit_events and POST to alert-sink.</summary>
public sealed class SiemDetectorService(
    IServiceScopeFactory scopeFactory,
    IOptions<SecurityAuditOptions> options,
    ILogger<SiemDetectorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DetectorEnabled)
        {
            logger.LogInformation("SIEM detector disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, options.Value.DetectorIntervalSeconds)), stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
                await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    AdvisoryLockKeys.SiemDetector,
                    ct => EvaluateAsync(scope, ct),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SIEM detector cycle failed");
            }
        }
    }

    internal async Task EvaluateAsync(IServiceScope scope, CancellationToken ct)
    {
        var rules = SiemRuleLoader.Load(options.Value.RulesPath);
        if (rules.Count == 0)
            return;

        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("alert-sink");

        foreach (var rule in rules)
        {
            var since = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, rule.WindowMinutes));
            var count = await db.SecurityAuditEvents.AsNoTracking()
                .CountAsync(e => e.EventType == rule.EventType && e.OccurredAtUtc >= since, ct);

            if (count < rule.Threshold)
                continue;

            var payload = new
            {
                version = "4",
                status = "firing",
                receiver = "webhook",
                commonLabels = new Dictionary<string, string>
                {
                    ["alertname"] = rule.AlertName,
                    ["owner"] = "security",
                    ["severity"] = rule.Severity,
                    ["event_type"] = rule.EventType,
                    ["rule_id"] = rule.Id
                },
                commonAnnotations = new Dictionary<string, string>
                {
                    ["summary"] = $"{rule.AlertName}: {count} events of {rule.EventType} in {rule.WindowMinutes}m",
                    ["runbook_url"] = "docs/OBS_SIEM_RUNBOOKS.md"
                },
                alerts = new[]
                {
                    new
                    {
                        status = "firing",
                        labels = new Dictionary<string, string>
                        {
                            ["alertname"] = rule.AlertName,
                            ["owner"] = "security",
                            ["severity"] = rule.Severity
                        },
                        annotations = new Dictionary<string, string>
                        {
                            ["summary"] = $"count={count} threshold={rule.Threshold}"
                        },
                        startsAt = DateTime.UtcNow.ToString("O")
                    }
                }
            };

            try
            {
                var url = options.Value.AlertSinkUrl.TrimEnd('/') + "/webhook";
                var response = await client.PostAsJsonAsync(url, payload, ct);
                logger.LogInformation(
                    "SIEM rule {RuleId} fired count={Count} sink={Status}",
                    rule.Id, count, (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to post SIEM alert for {RuleId}", rule.Id);
            }
        }
    }
}

public sealed record SiemRule(
    string Id,
    string EventType,
    int WindowMinutes,
    int Threshold,
    string AlertName,
    string Severity);

public static class SiemRuleLoader
{
    public static IReadOnlyList<SiemRule> Load(string? rulesPath)
    {
        var dir = rulesPath
            ?? Environment.GetEnvironmentVariable("HR_SIEM_RULES_PATH")
            ?? FindDefaultRulesPath();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return Array.Empty<SiemRule>();

        var rules = new List<SiemRule>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.yaml").Concat(Directory.EnumerateFiles(dir, "*.yml")))
        {
            var parsed = ParseSimpleYaml(File.ReadAllText(file));
            if (parsed is null) continue;
            rules.Add(parsed);
        }

        return rules;
    }

    private static string? FindDefaultRulesPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "policy", "siem"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "policy", "siem")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "policy", "siem")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "policy", "siem")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "policy", "siem"))
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>Minimal flat YAML key: value parser for SIEM rule files.</summary>
    public static SiemRule? ParseSimpleYaml(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains(':'))
                continue;
            var idx = line.IndexOf(':');
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"', '\'');
            map[key] = value;
        }

        if (!map.TryGetValue("id", out var id) ||
            !map.TryGetValue("eventType", out var eventType))
            return null;

        _ = int.TryParse(map.GetValueOrDefault("windowMinutes", "5"), out var window);
        _ = int.TryParse(map.GetValueOrDefault("threshold", "1"), out var threshold);
        var alertName = map.GetValueOrDefault("alertname") ?? map.GetValueOrDefault("alertName") ?? id;
        var severity = map.GetValueOrDefault("severity") ?? "page";
        return new SiemRule(id, eventType, Math.Max(1, window), Math.Max(1, threshold), alertName, severity);
    }
}

/// <summary>OBS-003: export sealed audit batches to WORM path + catalog.</summary>
public sealed class AuditWormExporter(
    IServiceScopeFactory scopeFactory,
    IOptions<SecurityAuditOptions> options,
    ILogger<AuditWormExporter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(1, options.Value.WormExportIntervalHours)), stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
                await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    AdvisoryLockKeys.AuditWormExporter,
                    ct => ExportAsync(scope, ct),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WORM audit export failed");
            }
        }
    }

    public async Task<int> ExportAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await ExportAsync(scope, ct);
    }

    public async Task<int> ExportAsync(IServiceScope scope, CancellationToken ct = default)
    {
        var root = options.Value.WormExportPath;
        Directory.CreateDirectory(root);
        var auditDir = Path.Combine(root, "audit");
        Directory.CreateDirectory(auditDir);

        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();

        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var rows = await db.AuditEntries.AsNoTracking()
            .Where(x => x.OccurredAtUtc >= since && x.EventHash != "")
            .OrderBy(x => x.Id)
            .Take(50_000)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return 0;

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var batchName = $"audit-batch-{stamp}.jsonl";
        var batchPath = Path.Combine(auditDir, batchName);
        await using (var writer = new StreamWriter(batchPath, append: false, Encoding.UTF8))
        {
            foreach (var row in rows)
                await writer.WriteLineAsync(JsonSerializer.Serialize(row));
        }

        // Object Lock compliance marker (lab): sidecar lock file; prod uses S3/MinIO Object Lock.
        await File.WriteAllTextAsync(batchPath + ".objectlock", "Mode=COMPLIANCE;RetainUntil=INDEFINITE", ct);

        var catalogPath = Path.Combine(root, "audit-export-catalog.jsonl");
        var catalogLine = JsonSerializer.Serialize(new
        {
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Batch = batchName,
            Count = rows.Count,
            FirstId = rows[0].Id,
            LastId = rows[^1].Id,
            FirstHash = rows[0].EventHash,
            LastHash = rows[^1].EventHash,
            ObjectLock = true
        });
        await File.AppendAllTextAsync(catalogPath, catalogLine + Environment.NewLine, ct);

        logger.LogInformation("WORM exported {Count} audit rows to {Path}", rows.Count, batchPath);
        return rows.Count;
    }
}
