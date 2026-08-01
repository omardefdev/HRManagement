using System.Text.Json;
using System.Threading.Channels;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRManagement.Gateway.Services;

public interface IGatewayAuditQueue
{
    bool TryEnqueue(GatewayAuditEntry entry);
    long PeekNextSequenceNumber();
    int OverflowFileCount { get; }
    int DlqFileCount { get; }
}

public sealed class AuditDurabilityOptions
{
    public const string SectionName = "AuditDurability";
    public int ChannelCapacity { get; set; } = 8192;
    public string OverflowPath { get; set; } = Path.Combine(Path.GetTempPath(), "hrm-audit-overflow");
    public int MaxRetries { get; set; } = 3;
    public int[] RetryDelaysMs { get; set; } = [1000, 5000, 15000];
    public int OverflowSweepSeconds { get; set; } = 5;
}

public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditRetention";
    public bool Enabled { get; set; }
    /// <summary>OBS-003: purge requires explicit AllowPurge=true and no blocking legal hold.</summary>
    public bool AllowPurge { get; set; }
    public int RetentionDays { get; set; } = 365;
}

public sealed class GatewayAuditQueue : BackgroundService, IGatewayAuditQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GatewayAuditQueue> _logger;
    private readonly AuditDurabilityOptions _options;
    private readonly Channel<GatewayAuditEntry> _channel;
    private Task? _executeTask;

    public GatewayAuditQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<GatewayAuditQueue> logger,
        IOptions<AuditDurabilityOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        Directory.CreateDirectory(_options.OverflowPath);
        _channel = Channel.CreateBounded<GatewayAuditEntry>(new BoundedChannelOptions(
            Math.Clamp(_options.ChannelCapacity, 1, 100_000))
        {
            SingleReader = true,
            SingleWriter = false,
            // Wait + TryWrite: when full, TryWrite returns false without blocking so we can spill.
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public int OverflowFileCount =>
        Directory.Exists(_options.OverflowPath)
            ? Directory.GetFiles(_options.OverflowPath, "overflow-*.jsonl").Length
            : 0;

    public int DlqFileCount =>
        Directory.Exists(_options.OverflowPath)
            ? Directory.GetFiles(_options.OverflowPath, "*_dlq.jsonl").Length
            : 0;

    public long PeekNextSequenceNumber()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
        return ReadNextSequenceFromDb(db);
    }

    public bool TryEnqueue(GatewayAuditEntry entry)
    {
        // Sequence numbers are assigned only at DB persist time via GatewayAuditChainState (HA-DR).
        if (_channel.Writer.TryWrite(entry))
            return true;

        // Channel full: never drop — spill to durable overflow file.
        AppendJsonl(Path.Combine(_options.OverflowPath, $"overflow-{DateTime.UtcNow:yyyyMMddHH}.jsonl"), entry);
        _logger.LogWarning(
            "Gateway audit channel full; spilled event for {Path} to overflow file.",
            entry.Path);
        return true;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _executeTask = RunAsync(stoppingToken);
        return _executeTask;
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<GatewayAuditEntry>(64);
        var lastSweep = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                linked.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.OverflowSweepSeconds)));

                try
                {
                    var first = await _channel.Reader.ReadAsync(linked.Token);
                    buffer.Add(first);
                    while (buffer.Count < 64 && _channel.Reader.TryRead(out var item))
                        buffer.Add(item);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Sweep timeout — continue to overflow sweep below.
                }

                if (buffer.Count > 0)
                {
                    await PersistWithRetryAsync(buffer, stoppingToken);
                    buffer.Clear();
                }

                if (DateTime.UtcNow - lastSweep >= TimeSpan.FromSeconds(_options.OverflowSweepSeconds))
                {
                    await SweepOverflowAsync(stoppingToken);
                    lastSweep = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected gateway audit worker error.");
                try { await Task.Delay(500, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        if (buffer.Count > 0)
        {
            try
            {
                await PersistWithRetryAsync(buffer, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gateway audit failed to flush in-flight buffer on shutdown.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);

        var drained = new List<GatewayAuditEntry>();
        while (_channel.Reader.TryRead(out var item))
            drained.Add(item);

        if (drained.Count > 0)
        {
            await PersistWithRetryAsync(drained, cancellationToken);
            _logger.LogInformation("Gateway audit drained {Count} in-channel events on shutdown.", drained.Count);
        }

        await SweepOverflowAsync(cancellationToken);
    }

    internal async Task PersistWithRetryAsync(List<GatewayAuditEntry> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        var delays = _options.RetryDelaysMs is { Length: > 0 }
            ? _options.RetryDelaysMs
            : [1000, 5000, 15000];
        var maxAttempts = Math.Max(1, _options.MaxRetries);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
                var clone = batch.Select(CloneEntry).ToList();
                await PersistBatchUnderChainLockAsync(db, clone, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                WriteDlq(batch);
                throw;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or TimeoutException)
            {
                _logger.LogWarning(ex,
                    "Gateway audit persistence failed (attempt {Attempt}/{Max}).",
                    attempt, maxAttempts);
                if (attempt >= maxAttempts)
                    break;
                var delayMs = delays[Math.Min(attempt - 1, delays.Length - 1)];
                await Task.Delay(delayMs, ct);
            }
            catch (Exception ex)
            {
                // OBS-003: unexpected failures still go to DLQ so audit is not silently lost.
                _logger.LogError(ex,
                    "Gateway audit persistence unexpected failure (attempt {Attempt}/{Max}).",
                    attempt, maxAttempts);
                if (attempt >= maxAttempts)
                    break;
                var delayMs = delays[Math.Min(attempt - 1, delays.Length - 1)];
                await Task.Delay(delayMs, ct);
            }
        }

        WriteDlq(batch);
        _logger.LogError(
            "Gateway audit batch of {Count} events moved to DLQ after {Attempts} failures.",
            batch.Count, maxAttempts);
    }

    private async Task SweepOverflowAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_options.OverflowPath))
            return;

        foreach (var file in Directory.GetFiles(_options.OverflowPath, "overflow-*.jsonl")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var entries = new List<GatewayAuditEntry>();
            foreach (var line in await File.ReadAllLinesAsync(file, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<GatewayAuditEntry>(line);
                    if (entry is not null)
                        entries.Add(entry);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping corrupt overflow line in {File}", file);
                }
            }

            if (entries.Count == 0)
            {
                File.Delete(file);
                continue;
            }

            try
            {
                await PersistWithRetryAsync(entries, ct);
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overflow sweep deferred for {File}", file);
            }
        }
    }

    private void WriteDlq(IReadOnlyList<GatewayAuditEntry> batch)
    {
        var path = Path.Combine(
            _options.OverflowPath,
            $"audit-{DateTime.UtcNow:yyyyMMddHHmmssfff}_{batch.Count}_dlq.jsonl");
        foreach (var entry in batch)
            AppendJsonl(path, entry);
    }

    private static void AppendJsonl(string path, GatewayAuditEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(entry);
        // FileShare.ReadWrite allows concurrent appends from request threads.
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(json);
    }

    private static async Task PersistBatchUnderChainLockAsync(
        GatewayAuditDbContext db,
        List<GatewayAuditEntry> batch,
        CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        var chainKey = AuditHashChain.DefaultChainKey;

        if (db.Database.IsRelational())
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_xact_lock({AdvisoryLockKeys.GatewayAuditChain})",
                    ct);
            }

            await AssignSequencesCoreAsync(db, batch, chainKey, ct);
            await SealHashChainAsync(db, batch, ct);
            await db.AuditEntries.AddRangeAsync(batch, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        await AssignSequencesCoreAsync(db, batch, chainKey, ct);
        await SealHashChainAsync(db, batch, ct);
        await db.AuditEntries.AddRangeAsync(batch, ct);
        await db.SaveChangesAsync(ct);
    }

    private static long ReadNextSequenceFromDb(GatewayAuditDbContext db)
    {
        var chainKey = AuditHashChain.DefaultChainKey;
        var state = db.AuditChainStates.Find(chainKey);
        if (state is not null)
            return state.LastSequence + 1;

        var maxExisting = db.AuditEntries.AsNoTracking()
            .Where(x => x.ChainKey == chainKey)
            .Select(x => (long?)x.SequenceNumber)
            .Max();
        return (maxExisting ?? 0) + 1;
    }

    private static async Task AssignSequencesCoreAsync(
        GatewayAuditDbContext db,
        List<GatewayAuditEntry> batch,
        string chainKey,
        CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        var state = await db.AuditChainStates.FindAsync([chainKey], ct);
        if (state is null)
        {
            var maxExisting = await db.AuditEntries.AsNoTracking()
                .Where(x => x.ChainKey == chainKey)
                .MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0;
            state = new GatewayAuditChainState { ChainKey = chainKey, LastSequence = maxExisting };
            db.AuditChainStates.Add(state);
        }

        var ordered = batch.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToList();
        foreach (var entry in ordered)
        {
            entry.ChainKey = chainKey;
            entry.SequenceNumber = ++state.LastSequence;
        }

        batch.Clear();
        batch.AddRange(ordered);
    }

    private static async Task SealHashChainAsync(
        GatewayAuditDbContext db,
        List<GatewayAuditEntry> batch,
        CancellationToken ct)
    {
        var ordered = batch.OrderBy(x => x.SequenceNumber).ToList();
        var last = await db.AuditEntries.AsNoTracking()
            .Where(x => x.ChainKey == AuditHashChain.DefaultChainKey)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var prev = last is not null && !string.IsNullOrEmpty(last.EventHash)
            ? last.EventHash
            : AuditHashChain.GenesisPrevHash;
        foreach (var entry in ordered)
        {
            AuditHashChain.Seal(entry, prev);
            prev = entry.EventHash;
        }

        batch.Clear();
        batch.AddRange(ordered);
    }

    private static GatewayAuditEntry CloneEntry(GatewayAuditEntry e) => new()
    {
        OccurredAtUtc = e.OccurredAtUtc,
        CorrelationId = e.CorrelationId,
        UserSub = e.UserSub,
        UserName = e.UserName,
        Method = e.Method,
        Path = e.Path,
        QueryString = e.QueryString,
        StatusCode = e.StatusCode,
        ElapsedMs = e.ElapsedMs,
        Decision = e.Decision,
        ClientIp = e.ClientIp,
        UserAgent = e.UserAgent,
        SequenceNumber = e.SequenceNumber,
        ChainKey = e.ChainKey,
        PrevHash = e.PrevHash,
        EventHash = e.EventHash
    };
}

public sealed class GatewayAuditDlqHealthCheck(IGatewayAuditQueue queue) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var dlq = queue.DlqFileCount;
        var overflow = queue.OverflowFileCount;
        if (dlq > 0)
        {
            return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded(
                $"Audit DLQ has {dlq} file(s); overflow={overflow}."));
        }

        return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
            $"Audit durable queue healthy; overflow={overflow}."));
    }
}
