using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Route("api/lookup")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreLookup)]
public class LookupApiController(HRCoreDbContext context, HR.Core.Infrastructure.OrgScopeService orgScope) : ControllerBase
{
    private const int MinSearchLength = 2;

    public sealed record LookupItemDto(Guid Id, string Label, string? Sublabel);

    public sealed record LookupPageDto(IReadOnlyList<LookupItemDto> Items, bool HasMore);

    [HttpGet("employees")]
    public async Task<ActionResult<LookupPageDto>> SearchEmployees(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.LookupMaxPageSize);
        var skip = (page - 1) * pageSize;

        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied) return Forbid();

        var query = context.Employees.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (term.Length < MinSearchLength)
                return Ok(new LookupPageDto([], HasMore: false));

            var normalized = ArabicTextNormalizer.Normalize(term);
            query = query.Where(e =>
                e.FullNameNormalized.Contains(normalized) ||
                e.FullName.Contains(term) ||
                e.FirstName.Contains(term) ||
                e.LastName.Contains(term));
        }

        var items = await query
            .OrderBy(e => e.FullNameNormalized)
            .ThenBy(e => e.FullName)
            .Skip(skip)
            .Take(pageSize + 1)
            .Select(e => new LookupItemDto(
                e.Id,
                e.FullName,
                e.PrimaryOrgUnit != null ? e.PrimaryOrgUnit.NameAr : null))
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        return Ok(new LookupPageDto(items, hasMore));
    }

    [HttpGet("employees/{id:guid}")]
    public async Task<ActionResult<LookupItemDto>> GetEmployee(Guid id, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied) return Forbid();

        var employee = await context.Employees
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.Id, e.FullName, Sublabel = e.PrimaryOrgUnit != null ? e.PrimaryOrgUnit.NameAr : null, e.PrimaryOrgUnitId })
            .FirstOrDefaultAsync(ct);

        if (employee is null) return NotFound();
        if (!scope.CanAccess(employee.PrimaryOrgUnitId)) return Forbid();
        return Ok(new LookupItemDto(employee.Id, employee.FullName, employee.Sublabel));
    }

    [HttpGet("org-units")]
    public async Task<ActionResult<LookupPageDto>> SearchOrgUnits(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.LookupMaxPageSize);
        var skip = (page - 1) * pageSize;

        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied) return Forbid();

        var query = context.OrgUnits.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
            query = query.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (term.Length < MinSearchLength)
                return Ok(new LookupPageDto([], HasMore: false));

            query = query.Where(o =>
                o.NameAr.Contains(term) ||
                o.Code.Contains(term) ||
                (o.NameEn != null && o.NameEn.Contains(term)));
        }

        var items = await query
            .OrderBy(o => o.NameAr)
            .Skip(skip)
            .Take(pageSize + 1)
            .Select(o => new LookupItemDto(o.Id, o.NameAr, o.Code))
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        return Ok(new LookupPageDto(items, hasMore));
    }

    [HttpGet("org-units/{id:guid}")]
    public async Task<ActionResult<LookupItemDto>> GetOrgUnit(Guid id, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied) return Forbid();
        if (!scope.CanAccess(id)) return Forbid();

        var unit = await context.OrgUnits
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new LookupItemDto(o.Id, o.NameAr, o.Code))
            .FirstOrDefaultAsync(ct);

        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpGet("org-units/tree")]
    public async Task<ActionResult<IReadOnlyList<OrgUnitTreeViewModel>>> GetOrgUnitTree(CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied) return Forbid();

        var query = context.OrgUnits
            .Include(o => o.HeadEmployee)
            .AsNoTracking()
            .AsQueryable();

        if (scope.MustFilter)
        {
            var claim = HrOrgScopeVisibility.GetScopeClaimResult(User);
            if (claim.RootOrgUnitId is Guid rootId)
            {
                var rootPath = await context.OrgUnits.AsNoTracking()
                    .Where(o => o.Id == rootId)
                    .Select(o => o.MaterializedPath)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrEmpty(rootPath))
                    query = query.Where(o => o.MaterializedPath.StartsWith(rootPath));
                else
                    query = query.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
            }
            else
                query = query.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        }

        var allUnits = await query
            .OrderBy(o => o.NameAr)
            .Take(OrgUnitsController.MaxTreeNodes)
            .ToListAsync(ct);

        var roots = allUnits.Where(o => o.ParentId == null
            || (scope.MustFilter && o.ParentId.HasValue && !scope.AllowedOrgUnitIds.Contains(o.ParentId.Value))).ToList();
        var tree = roots.Select(r => BuildTree(r, allUnits)).ToList();
        return Ok(tree);
    }

    private static OrgUnitTreeViewModel BuildTree(OrgUnit unit, List<OrgUnit> all) =>
        new()
        {
            Id = unit.Id,
            Code = unit.Code,
            NameAr = unit.NameAr,
            HeadEmployeeName = unit.HeadEmployee?.FullName,
            Children = all
                .Where(o => o.ParentId == unit.Id)
                .Select(child => BuildTree(child, all))
                .ToList()
        };
}
