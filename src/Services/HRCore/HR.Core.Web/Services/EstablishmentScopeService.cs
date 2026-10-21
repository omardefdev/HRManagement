using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public sealed class EstablishmentScopeService(
    HRCoreDbContext db,
    OrgScopeService orgScope)
{
    public async Task<OrgAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        await orgScope.GetAllowedOrgUnitIdsAsync(user, ct);

    public async Task<string?> ValidateCentralWriteAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var scope = await GetScopeAsync(user, ct);
        if (scope.IsDenied)
            return "لا يوجد نطاق تنظيمي مسموح.";
        if (scope.MustFilter)
            return "العملية متاحة للموارد البشرية المركزية فقط.";
        return null;
    }

    public async Task<string?> ValidateJobCatalogAccessAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var scope = await GetScopeAsync(user, ct);
        if (scope.IsDenied)
            return "لا يوجد نطاق تنظيمي مسموح.";
        if (scope.MustFilter)
            return "دليل الوظائف متاح للموارد البشرية المركزية فقط.";
        return null;
    }

    public async Task<string?> ValidateScopeOrgUnitAccessAsync(
        ClaimsPrincipal user,
        Guid scopeOrgUnitId,
        CancellationToken ct = default)
    {
        var scope = await GetScopeAsync(user, ct);
        if (scope.IsDenied)
            return "لا يوجد نطاق تنظيمي مسموح.";

        if (scope.IsUnrestricted)
            return null;

        if (!scope.CanAccess(scopeOrgUnitId))
            return "الفرع/المركزية المحدّدة خارج نطاق صلاحياتك.";

        return null;
    }

    public async Task ApplyDefaultScopeOrgUnitAsync(ClaimsPrincipal user, Action<Guid> setScopeOrgUnitId)
    {
        var scope = await GetScopeAsync(user);
        if (scope.IsDenied || !scope.MustFilter)
            return;

        var claim = HrOrgScopeVisibility.GetScopeClaimResult(user);
        if (claim.RootOrgUnitId is Guid rootId)
            setScopeOrgUnitId(rootId);
    }

    public async Task PopulateBranchScopesAsync(
        ClaimsPrincipal user,
        Action<SelectList> setBranchScopes,
        Action<bool> setBranchScopeLocked,
        Guid? selectedId = null,
        CancellationToken ct = default)
    {
        var options = await BuildBranchScopeOptionsAsync(user, ct);
        setBranchScopes(new SelectList(options, nameof(BranchScopeOption.Id), nameof(BranchScopeOption.Label), selectedId));
        var scope = await GetScopeAsync(user, ct);
        setBranchScopeLocked(scope.MustFilter);
    }

    public async Task<IReadOnlyList<BranchScopeOption>> BuildBranchScopeOptionsAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var scope = await GetScopeAsync(user, ct);
        if (scope.IsDenied)
            return [];

        var hq = await db.OrgUnits.AsNoTracking()
            .Where(o => o.ParentId == null)
            .OrderBy(o => o.NameAr)
            .FirstOrDefaultAsync(ct);
        if (hq is null)
            return [];

        var candidates = new List<BranchScopeOption>
        {
            new(hq.Id, $"{hq.NameAr} — المركزية")
        };

        var branches = await db.OrgUnits.AsNoTracking()
            .Where(o => o.ParentId == hq.Id)
            .OrderBy(o => o.NameAr)
            .Select(o => new BranchScopeOption(o.Id, o.NameAr))
            .ToListAsync(ct);
        candidates.AddRange(branches);

        if (!scope.MustFilter)
            return candidates;

        var claim = HrOrgScopeVisibility.GetScopeClaimResult(user);
        if (claim.RootOrgUnitId is not Guid rootId)
            return [];

        return candidates.Where(c => c.Id == rootId).ToList();
    }

    public async Task<IQueryable<FundedPosition>> ApplyScopeOrgUnitFilterAsync(
        IQueryable<FundedPosition> query,
        Guid scopeOrgUnitId,
        CancellationToken ct = default)
    {
        var scopePath = await GetMaterializedPathAsync(scopeOrgUnitId, ct);
        return query.Where(p => p.OrgUnit != null && p.OrgUnit.MaterializedPath.StartsWith(scopePath));
    }

    public async Task<IQueryable<FundedPosition>> ApplyOrgAccessFilterAsync(
        IQueryable<FundedPosition> query,
        OrgAccessScope scope,
        CancellationToken ct = default)
    {
        if (!scope.MustFilter)
            return query;

        return query.Where(p => scope.AllowedOrgUnitIds.Contains(p.OrgUnitId));
    }

    public async Task<string> GetMaterializedPathAsync(Guid orgUnitId, CancellationToken ct = default)
    {
        var path = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == orgUnitId)
            .Select(o => o.MaterializedPath)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrEmpty(path) ? $"/{orgUnitId:D}/" : path;
    }

    public async Task<bool> IsOrgUnitUnderScopeAsync(
        Guid orgUnitId,
        Guid scopeOrgUnitId,
        CancellationToken ct = default)
    {
        var scopePath = await GetMaterializedPathAsync(scopeOrgUnitId, ct);
        var unitPath = await GetMaterializedPathAsync(orgUnitId, ct);
        return unitPath.StartsWith(scopePath, StringComparison.Ordinal);
    }

    public async Task<Guid?> ResolveScopeOrgUnitIdForOrgUnitAsync(Guid orgUnitId, CancellationToken ct = default)
    {
        var hq = await db.OrgUnits.AsNoTracking()
            .Where(o => o.ParentId == null)
            .Select(o => new { o.Id })
            .FirstOrDefaultAsync(ct);
        if (hq is null)
            return orgUnitId;

        var currentId = orgUnitId;
        while (true)
        {
            var node = await db.OrgUnits.AsNoTracking()
                .Where(o => o.Id == currentId)
                .Select(o => new { o.Id, o.ParentId })
                .FirstOrDefaultAsync(ct);
            if (node is null)
                return null;
            if (node.Id == hq.Id)
                return hq.Id;
            if (node.ParentId == hq.Id)
                return node.Id;
            if (node.ParentId is null)
                return node.Id;
            currentId = node.ParentId.Value;
        }
    }
}
