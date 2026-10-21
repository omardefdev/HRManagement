using System.Diagnostics;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-35: measured scoped lookup performance through the live gateway.</summary>
public class Ac35ScopedLookupPerfLiveTests
{
    [SkippableFact]
    public async Task Br1_manager_lookup_records_reproducible_gateway_percentiles()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-35", correlationId);
        recorder.WriteMetadata("AC-35 scoped lookup perf", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        const string path = "api/hr-core/api/lookup/employees?q=ACC&page=1&pageSize=20";
        const int warmupCount = 5;
        const int sampleCount = 100;

        for (var i = 0; i < warmupCount; i++)
        {
            using var warmup = await http.GetAsync(path);
            Assert.True(warmup.IsSuccessStatusCode, $"Warm-up request {i + 1} failed: {warmup.StatusCode}");
        }

        var samples = new List<double>(sampleCount);
        var errors = 0;
        string? firstBody = null;
        var total = Stopwatch.StartNew();
        for (var i = 0; i < sampleCount; i++)
        {
            var request = Stopwatch.StartNew();
            using var response = await http.GetAsync(path);
            request.Stop();
            samples.Add(request.Elapsed.TotalMilliseconds);
            if (!response.IsSuccessStatusCode)
                errors++;
            if (i == 0)
                firstBody = await response.Content.ReadAsStringAsync();
        }
        total.Stop();

        var ordered = samples.OrderBy(x => x).ToArray();
        var p50 = Percentile(ordered, 0.50);
        var p95 = Percentile(ordered, 0.95);
        var p99 = Percentile(ordered, 0.99);
        var throughput = sampleCount / total.Elapsed.TotalSeconds;

        recorder.CaptureHttp("scoped-lookup", "GET", "/api/hr-core/api/lookup/employees?q=ACC",
            errors == 0 ? 200 : 500, new { length = firstBody?.Length ?? 0, sampleCount, errors });

        Assert.Equal(0, errors);
        recorder.WriteAfter(new
        {
            warmupRequests = warmupCount,
            measuredRequests = sampleCount,
            errors,
            p50_ms = Math.Round(p50, 3),
            p95_ms = Math.Round(p95, 3),
            p99_ms = Math.Round(p99, 3),
            min_ms = Math.Round(ordered[0], 3),
            max_ms = Math.Round(ordered[^1], 3),
            throughput_rps = Math.Round(throughput, 3),
            sloClaimed = false
        });
        recorder.WriteDbChecks(new
        {
            persona = persona.Id,
            tenantId = persona.TenantId,
            scopeOrgUnitId = persona.ScopeOrgUnitId,
            gatewayPath = path,
            successfulResponses = sampleCount
        });
        recorder.RecordTiming("totalElapsedMs", total.Elapsed.TotalMilliseconds);
        recorder.RecordTiming("p50Ms", p50);
        recorder.RecordTiming("p95Ms", p95);
        recorder.RecordTiming("p99Ms", p99);
        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        Assert.NotEmpty(ordered);
        var position = (ordered.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return ordered[lower];

        var weight = position - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * weight;
    }
}
