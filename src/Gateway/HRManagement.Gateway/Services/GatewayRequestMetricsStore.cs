using System.Collections.Concurrent;

namespace HRManagement.Gateway.Services;

public sealed class GatewayRequestMetricsStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _latencyByModule = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _requestCountByModule = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _errorCountByModule = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, long> _statusCounters = new();
    private readonly ConcurrentQueue<string> _recentErrors = new();

    public void Record(string path, int statusCode, long elapsedMs)
    {
        var module = ResolveModule(path);
        if (!string.IsNullOrWhiteSpace(module))
        {
            var q = _latencyByModule.GetOrAdd(module, _ => new ConcurrentQueue<long>());
            q.Enqueue(elapsedMs);
            while (q.Count > 500 && q.TryDequeue(out _)) { }

            _requestCountByModule.AddOrUpdate(module, 1, (_, c) => c + 1);
            if (statusCode >= 400)
                _errorCountByModule.AddOrUpdate(module, 1, (_, c) => c + 1);
        }

        _statusCounters.AddOrUpdate(statusCode, 1, (_, current) => current + 1);
        if (statusCode >= 500)
        {
            _recentErrors.Enqueue($"{DateTimeOffset.UtcNow:O} {path} => {statusCode}");
            while (_recentErrors.Count > 100 && _recentErrors.TryDequeue(out _)) { }
        }
    }

    public IReadOnlyList<ModuleMetricsSummary> GetModuleStats()
    {
        var modules = _latencyByModule.Keys.ToList();
        var result = new List<ModuleMetricsSummary>(modules.Count);
        foreach (var module in modules)
        {
            var latencies = _latencyByModule.TryGetValue(module, out var q)
                ? q.ToArray() : [];
            var avgLatency = latencies.Length > 0 ? latencies.Average() : 0;
            _requestCountByModule.TryGetValue(module, out var reqCount);
            _errorCountByModule.TryGetValue(module, out var errCount);
            result.Add(new ModuleMetricsSummary(module, reqCount, errCount, avgLatency));
        }
        return result;
    }

    public GatewayMetricsSnapshot Snapshot()
    {
        var p95 = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (module, q) in _latencyByModule)
        {
            var arr = q.ToArray();
            if (arr.Length == 0) continue;
            Array.Sort(arr);
            var index = (int)Math.Ceiling(arr.Length * 0.95) - 1;
            index = Math.Clamp(index, 0, arr.Length - 1);
            p95[module] = arr[index];
        }

        return new GatewayMetricsSnapshot(
            new Dictionary<int, long>(_statusCounters),
            p95,
            _recentErrors.ToArray());
    }

    private static string ResolveModule(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        var p = path.Trim('/');
        if (p.StartsWith("ui/", StringComparison.OrdinalIgnoreCase))
            p = p[3..];
        else if (p.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            p = p[4..];
        var slash = p.IndexOf('/');
        return slash >= 0 ? p[..slash] : p;
    }
}

public sealed record GatewayMetricsSnapshot(
    IReadOnlyDictionary<int, long> StatusCounters,
    IReadOnlyDictionary<string, long> ModuleP95LatencyMs,
    IReadOnlyList<string> RecentErrors);

public sealed record ModuleMetricsSummary(
    string ModuleName,
    long RequestCount,
    long ErrorCount,
    double AvgLatencyMs);
