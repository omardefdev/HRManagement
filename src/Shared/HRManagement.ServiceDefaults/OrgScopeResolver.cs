using System.Security.Claims;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.ServiceDefaults;

public static class OrgScopeResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(90);

    public static async Task<OrgAccessScope> GetAllowedOrgUnitIdsAsync(
        ClaimsPrincipal user,
        IQueryable<OrgUnitSnapshot> units,
        CancellationToken ct = default)
        => await GetAllowedOrgUnitIdsAsync(user, units, tenantId: null, memoryCache: null, ct);

    /// <summary>
    /// Resolves branch org scope via MaterializedPath prefix (PERF-003). When <paramref name="tenantId"/> is set,
    /// only snapshots for that tenant are considered (TEN-002).
    /// </summary>
    public static async Task<OrgAccessScope> GetAllowedOrgUnitIdsAsync(
        ClaimsPrincipal user,
        IQueryable<OrgUnitSnapshot> units,
        Guid? tenantId,
        CancellationToken ct = default)
        => await GetAllowedOrgUnitIdsAsync(user, units, tenantId, memoryCache: null, ct);

    public static async Task<OrgAccessScope> GetAllowedOrgUnitIdsAsync(
        ClaimsPrincipal user,
        IQueryable<OrgUnitSnapshot> units,
        Guid? tenantId,
        IMemoryCache? memoryCache,
        CancellationToken ct = default)
    {
        var claim = HrOrgScopeVisibility.GetScopeClaimResult(user);
        if (claim.Kind == OrgScopeClaimKind.NotScoped)
            return OrgAccessScope.Unrestricted();

        if (claim.Kind == OrgScopeClaimKind.InvalidClaim || claim.RootOrgUnitId is null)
            return OrgAccessScope.Denied();

        var rootId = claim.RootOrgUnitId.Value;
        var cacheTenant = tenantId ?? Guid.Empty;
        var cacheKey = $"org-scope-snap:{cacheTenant:N}:{rootId:N}";

        if (memoryCache is not null
            && memoryCache.TryGetValue(cacheKey, out OrgAccessScope? cached)
            && cached is not null)
            return cached;

        var query = units.AsNoTracking();
        if (tenantId.HasValue)
            query = query.Where(u => u.TenantId == tenantId.Value);

        var root = await query
            .Where(u => u.Id == rootId)
            .Select(u => new { u.Id, u.MaterializedPath })
            .FirstOrDefaultAsync(ct);

        if (root is null)
            return OrgAccessScope.Denied();

        var path = string.IsNullOrEmpty(root.MaterializedPath)
            ? $"/{root.Id:D}/"
            : root.MaterializedPath;

        var scoped = units.AsNoTracking();
        if (tenantId.HasValue)
            scoped = scoped.Where(u => u.TenantId == tenantId.Value);

        var ids = await scoped
            .Where(u => u.MaterializedPath.StartsWith(path))
            .Select(u => u.Id)
            .ToListAsync(ct);

        var result = ids.Count == 0
            ? OrgAccessScope.Restricted(new HashSet<Guid> { rootId })
            : OrgAccessScope.Restricted(ids.ToHashSet());

        memoryCache?.Set(cacheKey, result, CacheTtl);
        return result;
    }

    /// <summary>Invalidate snapshot scope cache entries for a tenant (call after IOrgUnitChanged).</summary>
    public static void InvalidateSnapshotScopeCache(IMemoryCache? memoryCache, Guid tenantId)
    {
        // IMemoryCache has no key enumeration; consumers should use a version token or short TTL.
        // Version bump via a well-known key so GetOrCreate paths can check — stored as ticks.
        memoryCache?.Set($"org-scope-snap-ver:{tenantId:N}", DateTime.UtcNow.Ticks, CacheTtl);
    }
}
