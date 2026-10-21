using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Caching.Memory;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>Tracks policy snapshot cache keys so domain invalidation works (PLAN-02 / W2.1).</summary>
public interface IPolicyPublishedSnapshotCache
{
    bool TryGet(string cacheKey, out PolicyVersionSnapshot? snapshot);

    void Set(string cacheKey, PolicyVersionSnapshot snapshot, TimeSpan ttl);

    void InvalidateDomain(string domain);

    static string BuildCacheKey(string domain, Guid? tenantId, DateTime asOfUtc) =>
        $"policy:{domain}:{tenantId}:{asOfUtc:yyyyMMdd}";
}

public sealed class PolicyPublishedSnapshotCache(IMemoryCache cache) : IPolicyPublishedSnapshotCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool TryGet(string cacheKey, out PolicyVersionSnapshot? snapshot)
    {
        if (cache.TryGetValue(cacheKey, out PolicyVersionSnapshot? hit) && hit is not null)
        {
            snapshot = hit;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Set(string cacheKey, PolicyVersionSnapshot snapshot, TimeSpan ttl)
    {
        cache.Set(cacheKey, snapshot, ttl);
        lock (_gate)
            _keys.Add(cacheKey);
    }

    public void InvalidateDomain(string domain)
    {
        var prefix = $"policy:{domain}:";
        lock (_gate)
        {
            foreach (var key in _keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                cache.Remove(key);
                _keys.Remove(key);
            }
        }
    }
}
