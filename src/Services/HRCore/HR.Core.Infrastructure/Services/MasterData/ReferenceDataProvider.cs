using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.MasterData;

public sealed class ReferenceDataProvider(HRCoreDbContext db) : IReferenceDataProvider
{
    public async Task<ReferenceCatalogEntry?> GetByCodeAsync(
        CatalogKind kind,
        string code,
        CancellationToken ct = default)
    {
        var tenantId = db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var entries = await (await QueryAsync(kind, ct))
            .Where(e => e.Code == code)
            .OrderBy(e => e.TenantId == tenantId ? 0 : 1)
            .ToListAsync(ct);

        return entries.FirstOrDefault();
    }

    public Task<IReadOnlyList<ReferenceCatalogEntry>> ListActiveAsync(
        CatalogKind kind,
        CancellationToken ct = default) =>
        SearchActiveAsync(kind, query: null, skip: 0, take: int.MaxValue, ct);

    public async Task<IReadOnlyList<ReferenceCatalogEntry>> SearchActiveAsync(
        CatalogKind kind,
        string? query,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var tenantId = db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var scoped = (await QueryAsync(kind, ct)).Where(e => e.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            scoped = scoped.Where(e =>
                e.Code.Contains(term) ||
                e.NameAr.Contains(term) ||
                (e.NameEn != null && e.NameEn.Contains(term)));
        }

        var matches = await scoped
            .OrderBy(e => e.TenantId == tenantId ? 0 : 1)
            .ThenBy(e => e.NameAr)
            .ToListAsync(ct);

        return matches
            .GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.NameAr)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(0, take))
            .ToList();
    }

    public async Task<ReferenceCatalogEntry?> ResolveEffectiveAsync(
        CatalogKind kind,
        string code,
        DateTime asOfDate,
        CancellationToken ct = default)
    {
        var current = await GetByCodeAsync(kind, code, ct);
        const int maxChain = 10;
        for (var i = 0; i < maxChain && current != null; i++)
        {
            if (ReferenceDataLifecycleService.IsEffectiveAt(current, asOfDate.ToUniversalTime()))
                return current;

            if (string.IsNullOrWhiteSpace(current.ReplacedByCode))
                break;

            current = await GetByCodeAsync(kind, current.ReplacedByCode, ct);
        }

        return current is not null && ReferenceDataLifecycleService.IsEffectiveAt(current, asOfDate.ToUniversalTime())
            ? current
            : null;
    }

    private async Task<IQueryable<ReferenceCatalogEntry>> QueryAsync(
        CatalogKind kind,
        CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var platformId = TenantDefaults.DefaultTenantId;
        var bypass = db.ReferenceCatalogEntries
            .AsNoTracking()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalReferenceLookup)
            .Where(e => e.CatalogKind == kind);

        if (tenantId == platformId)
            return bypass.Where(e => e.TenantId == platformId);

        var hasTenantCopy = await bypass.AnyAsync(e => e.TenantId == tenantId, ct);
        return hasTenantCopy
            ? bypass.Where(e => e.TenantId == tenantId)
            : bypass.Where(e => e.TenantId == platformId);
    }
}
