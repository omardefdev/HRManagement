using System.Diagnostics;
using HRManagement.Gateway.Models;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.Gateway.Services;

public interface IGatewayHealthProbeService
{
    Task<IReadOnlyDictionary<string, GatewayModuleStatus>> ProbeAsync(
        IEnumerable<GatewayModule> modules,
        string baseUrl,
        Func<GatewayModule, bool> isAuthorized,
        CancellationToken cancellationToken);
}

public sealed class GatewayHealthProbeService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    IConfiguration configuration) : IGatewayHealthProbeService
{
    public async Task<IReadOnlyDictionary<string, GatewayModuleStatus>> ProbeAsync(
        IEnumerable<GatewayModule> modules,
        string baseUrl,
        Func<GatewayModule, bool> isAuthorized,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(configuration.GetValue("Gateway:HealthProbe:TimeoutSeconds", 4), 1, 15);
        var cacheSeconds = Math.Clamp(configuration.GetValue("Gateway:HealthProbe:CacheSeconds", 8), 1, 60);
        var internalBase = configuration["Gateway:HealthProbe:InternalBaseUrl"];
        var effectiveBaseUrl = string.IsNullOrWhiteSpace(internalBase) ? baseUrl : internalBase;
        var moduleList = modules.ToList();
        var cacheKey = $"gateway-health:{effectiveBaseUrl}";

        if (memoryCache.TryGetValue<IReadOnlyDictionary<string, GatewayModuleStatus>>(cacheKey, out var cached))
            return MergeAuthorization(cached!, moduleList, isAuthorized);

        var client = httpClientFactory.CreateClient(GatewayHttpResilience.HealthProbeClientName);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var statuses = new Dictionary<string, GatewayModuleStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in moduleList)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var probeUrl = ResolveProbeUrl(module, effectiveBaseUrl);
                using var response = await client.GetAsync(probeUrl, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var healthy = response.IsSuccessStatusCode && body.Contains("Healthy", StringComparison.OrdinalIgnoreCase);
                statuses[module.Slug] = new GatewayModuleStatus
                {
                    Module = module,
                    IsAuthorized = isAuthorized(module),
                    IsHealthy = healthy,
                    StatusCode = (int)response.StatusCode,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Error = healthy ? null : body[..Math.Min(body.Length, 180)]
                };
            }
            catch (Exception ex)
            {
                statuses[module.Slug] = new GatewayModuleStatus
                {
                    Module = module,
                    IsAuthorized = isAuthorized(module),
                    IsHealthy = false,
                    StatusCode = -1,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Error = ex.Message
                };
            }
        }

        memoryCache.Set(cacheKey, statuses, TimeSpan.FromSeconds(cacheSeconds));
        return statuses;
    }

    /// <summary>
    /// Probes upstream /health directly via YARP cluster address (Docker/local),
    /// avoiding gateway OIDC on /health/upstreams/* proxy paths.
    /// </summary>
    private string ResolveProbeUrl(GatewayModule module, string effectiveBaseUrl)
    {
        var clusterAddress = configuration[
            $"ReverseProxy:Clusters:{module.Slug}-cluster:Destinations:destination1:Address"];
        if (!string.IsNullOrWhiteSpace(clusterAddress))
            return $"{clusterAddress.TrimEnd('/')}/health";

        return $"{effectiveBaseUrl}{module.HealthPath}";
    }

    private static IReadOnlyDictionary<string, GatewayModuleStatus> MergeAuthorization(
        IReadOnlyDictionary<string, GatewayModuleStatus> cached,
        IEnumerable<GatewayModule> modules,
        Func<GatewayModule, bool> isAuthorized)
    {
        var map = new Dictionary<string, GatewayModuleStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (cached.TryGetValue(module.Slug, out var status))
            {
                map[module.Slug] = new GatewayModuleStatus
                {
                    Module = status.Module,
                    IsAuthorized = isAuthorized(module),
                    IsHealthy = status.IsHealthy,
                    StatusCode = status.StatusCode,
                    LatencyMs = status.LatencyMs,
                    Error = status.Error
                };
            }
        }

        return map;
    }
}
