using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Caching.Memory;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>Tracks policy snapshot cache keys so domain invalidation works (PLAN-02 / W2.1).</summary>
public interface IPolicyPublishedSnapshotCache
{
    bool TryGet(string cacheKey, out PolicyVersionSnapshot? snapshot);

    void Set(string cacheKey, PolicyVersionSnapshot snapshot, TimeSpan ttl);

    void InvalidateDomain(string domain);

    /// <summary>PLAN-47: the key of the snapshot effective now (evaluations as of another time are not cached).</summary>
    static string BuildCacheKey(string domain, Guid? tenantId) =>
        $"policy:{domain}:{tenantId}:now";
}

public sealed class PolicyPublishedSnapshotCache(IMemoryCache cache) : IPolicyPublishedSnapshotCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>
    /// PLAN-47 (§6): an entry lives at most <see cref="DefaultTtl"/>, or until the next EffectiveFrom of the domain's templates,
    /// whichever comes first, so a version that takes effect later is evaluated from that moment.
    /// </summary>
    public static TimeSpan TimeToLive(DateTime nowUtc, DateTime? nextEffectiveFromUtc)
    {
        if (nextEffectiveFromUtc is not DateTime next)
            return DefaultTtl;
        var untilNext = next - nowUtc;
        return untilNext < DefaultTtl ? untilNext : DefaultTtl;
    }

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
        if (ttl <= TimeSpan.Zero)
            return;
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
