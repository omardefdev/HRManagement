using Microsoft.Extensions.Caching.Memory;

namespace HR.Core.Infrastructure;

/// <summary>PERF-003: in-process tenant-scoped org hierarchy / scope cache.</summary>
public interface IOrgUnitHierarchyCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    void InvalidateTenant(Guid tenantId);
    void InvalidateAll();
    static string ScopeKey(Guid tenantId, Guid rootOrgUnitId) => $"org-scope:{tenantId:N}:{rootOrgUnitId:N}";
    static string TreeKey(Guid tenantId) => $"org-tree:{tenantId:N}";
}

public sealed class OrgUnitHierarchyCache(IMemoryCache cache) : IOrgUnitHierarchyCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(90);

    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        if (cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var value = await factory(ct);
        cache.Set(key, value, DefaultTtl);
        lock (_gate)
            _keys.Add(key);
        return value;
    }

    public void InvalidateTenant(Guid tenantId)
    {
        var prefixScope = $"org-scope:{tenantId:N}:";
        var treeKey = IOrgUnitHierarchyCache.TreeKey(tenantId);
        lock (_gate)
        {
            foreach (var key in _keys.Where(k => k.StartsWith(prefixScope, StringComparison.Ordinal) || k == treeKey).ToList())
            {
                cache.Remove(key);
                _keys.Remove(key);
            }
        }
        cache.Remove(treeKey);
    }

    public void InvalidateAll()
    {
        lock (_gate)
        {
            foreach (var key in _keys)
                cache.Remove(key);
            _keys.Clear();
        }
    }
}
