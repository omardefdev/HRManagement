namespace HRManagement.ServiceDefaults;

using Microsoft.Extensions.Caching.Memory;

/// <summary>Active platform-admin tenant impersonation session (AC-05).</summary>
public sealed record PlatformTenantSessionEntry(
    Guid TenantId,
    string Reason,
    DateTimeOffset EnteredAtUtc,
    string? CorrelationId);

public interface IPlatformTenantSessionStore
{
    PlatformTenantSessionEntry? Get(string userSub);
    void Set(string userSub, PlatformTenantSessionEntry entry, TimeSpan? ttl = null);
    void Clear(string userSub);
}

public sealed class MemoryPlatformTenantSessionStore(IMemoryCache cache) : IPlatformTenantSessionStore
{
    private static string Key(string userSub) => $"platform-tenant-session:{userSub}";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(8);

    public PlatformTenantSessionEntry? Get(string userSub) =>
        cache.TryGetValue(Key(userSub), out PlatformTenantSessionEntry? entry) ? entry : null;

    public void Set(string userSub, PlatformTenantSessionEntry entry, TimeSpan? ttl = null) =>
        cache.Set(Key(userSub), entry, ttl ?? DefaultTtl);

    public void Clear(string userSub) => cache.Remove(Key(userSub));
}
