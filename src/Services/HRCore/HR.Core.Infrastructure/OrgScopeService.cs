using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure;

/// <summary>PERF-003: org scope via MaterializedPath prefix (no full-adjacency BFS).</summary>
public class OrgScopeService(
    HRCoreDbContext db,
    IOrgUnitHierarchyCache cache,
    ITenantContext? tenantContext = null)
{
    public async Task<OrgAccessScope> GetAllowedOrgUnitIdsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var claim = HrOrgScopeVisibility.GetScopeClaimResult(user);
        if (claim.Kind == OrgScopeClaimKind.NotScoped)
            return OrgAccessScope.Unrestricted();

        if (claim.Kind == OrgScopeClaimKind.InvalidClaim || claim.RootOrgUnitId is null)
            return OrgAccessScope.Denied();

        var rootId = claim.RootOrgUnitId.Value;
        var tenantId = tenantContext?.TenantId
            ?? await db.OrgUnits.AsNoTracking()
                .Where(o => o.Id == rootId)
                .Select(o => (Guid?)o.TenantId)
                .FirstOrDefaultAsync(ct)
            ?? Guid.Empty;

        var cacheKey = IOrgUnitHierarchyCache.ScopeKey(tenantId, rootId);
        return await cache.GetOrCreateAsync(cacheKey, async token =>
        {
            var root = await db.OrgUnits.AsNoTracking()
                .Where(o => o.Id == rootId)
                .Select(o => new { o.Id, o.MaterializedPath, o.TenantId })
                .FirstOrDefaultAsync(token);

            if (root is null)
                return OrgAccessScope.Denied();

            var path = root.MaterializedPath;
            if (string.IsNullOrEmpty(path))
                path = $"/{root.Id:D}/";

            var idsQuery = db.OrgUnits.AsNoTracking().AsQueryable();
            if (tenantId != Guid.Empty)
                idsQuery = idsQuery.Where(o => o.TenantId == tenantId);

            var ids = await idsQuery
                .Where(o => o.MaterializedPath.StartsWith(path))
                .Select(o => o.Id)
                .ToListAsync(token);

            if (ids.Count == 0)
                return OrgAccessScope.Restricted(new HashSet<Guid> { rootId });

            return OrgAccessScope.Restricted(ids.ToHashSet());
        }, ct);
    }
}
