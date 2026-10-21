using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Policy;

public interface IPolicySnapshotStore
{
    PolicyVersionSnapshot? Get(string domain);
    void Set(string domain, PolicyVersionSnapshot snapshot);
    void Invalidate(string domain);
}

public sealed class PolicySnapshotStore(IMemoryCache cache, ILogger<PolicySnapshotStore> logger) : IPolicySnapshotStore
{
    private static string Key(string domain) => $"policy-snapshot:{domain}";

    public PolicyVersionSnapshot? Get(string domain) =>
        cache.TryGetValue(Key(domain), out PolicyVersionSnapshot? snap) ? snap : null;

    public void Set(string domain, PolicyVersionSnapshot snapshot)
    {
        cache.Set(Key(domain), snapshot, TimeSpan.FromHours(24));
        logger.LogDebug("Policy snapshot cached for {Domain}", domain);
    }

    public void Invalidate(string domain)
    {
        cache.Remove(Key(domain));
        logger.LogInformation("Policy snapshot invalidated for {Domain}", domain);
    }
}
