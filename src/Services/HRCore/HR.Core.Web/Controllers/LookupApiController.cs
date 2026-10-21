using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Route("api/lookup")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreLookup)]
public class LookupApiController(
    HRCoreDbContext context,
    HR.Core.Infrastructure.OrgScopeService orgScope,
    IMemoryCache memoryCache,
    IConfiguration configuration,
    ITenantContext tenantContext,
    IReferenceDataProvider referenceData) : ControllerBase
{
    private const int MinSearchLength = 2;
    private const int DefaultCacheSeconds = 120;

    public sealed record LookupItemDto(Guid Id, string Label, string? Sublabel);

    public sealed record LookupPageDto(IReadOnlyList<LookupItemDto> Items, bool HasMore);

    public sealed record LookupByIdsRequest(IReadOnlyList<Guid> Ids);

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
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }

        var query = context.Employees.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (term.Length < MinSearchLength)
                return Ok(new LookupPageDto([], HasMore: false));

            var normalized = ArabicTextNormalizer.Normalize(term);
            if (NationalIdSearch.LooksLikeNationalId(term))
            {
                var hash = NationalIdSearch.ComputeHash(term);
                query = query.Where(e => e.NationalIdHash == hash);
            }
            else
            {
                query = query.Where(e =>
                    e.FullNameNormalized.Contains(normalized) ||
                    e.FullName.Contains(term) ||
                    e.FirstName.Contains(term) ||
                    e.LastName.Contains(term));
            }
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
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }

        var employee = await context.Employees
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.Id, e.FullName, Sublabel = e.PrimaryOrgUnit != null ? e.PrimaryOrgUnit.NameAr : null, e.PrimaryOrgUnitId })
            .FirstOrDefaultAsync(ct);

        if (employee is null) return NotFound();
        if (this.ForbidIfOrgUnitOutOfScope(scope, employee.PrimaryOrgUnitId, "Employee", id.ToString("D")) is not null)
            return Forbid();
        return Ok(new LookupItemDto(employee.Id, employee.FullName, employee.Sublabel));
    }

    [HttpPost("employees/by-ids")]
    public async Task<ActionResult<LookupPageDto>> GetEmployeesByIds(
        [FromBody] LookupByIdsRequest? request,
        CancellationToken ct = default)
    {
        if (request?.Ids is null || request.Ids.Count == 0)
            return Ok(new LookupPageDto([], HasMore: false));
        if (request.Ids.Count > SearchFilterBase.LookupMaxPageSize)
            return BadRequest($"Maximum {SearchFilterBase.LookupMaxPageSize} ids per request.");

        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }

        var tenantKey = tenantContext.TenantId?.ToString("N") ?? "none";
        var cacheSeconds = configuration.GetValue("Lookup:CacheSeconds", DefaultCacheSeconds);
        var cacheTtl = TimeSpan.FromSeconds(cacheSeconds);
        var distinctIds = request.Ids.Distinct().ToList();
        var items = new List<LookupItemDto>();
        var missingIds = new List<Guid>();

        foreach (var id in distinctIds)
        {
            var cacheKey = EmployeeCacheKey(tenantKey, id);
            if (memoryCache.TryGetValue(cacheKey, out LookupItemDto? cached) && cached is not null)
                items.Add(cached);
            else
                missingIds.Add(id);
        }

        if (missingIds.Count > 0)
        {
            var query = context.Employees.AsNoTracking().Where(e => missingIds.Contains(e.Id));
            if (scope.MustFilter)
                query = query.Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

            var fetched = await query
                .Select(e => new
                {
                    e.Id,
                    e.FullName,
                    Sublabel = e.PrimaryOrgUnit != null ? e.PrimaryOrgUnit.NameAr : null,
                    e.PrimaryOrgUnitId
                })
                .ToListAsync(ct);

            foreach (var employee in fetched)
            {
                if (!scope.CanAccess(employee.PrimaryOrgUnitId))
                    continue;

                var dto = new LookupItemDto(employee.Id, employee.FullName, employee.Sublabel);
                items.Add(dto);
                memoryCache.Set(EmployeeCacheKey(tenantKey, employee.Id), dto, cacheTtl);
            }
        }

        return Ok(new LookupPageDto(items, HasMore: false));
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
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }

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
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }
        if (this.ForbidIfOrgUnitOutOfScope(scope, id, "OrgUnit", id.ToString("D")) is not null)
            return Forbid();

        var unit = await context.OrgUnits
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new LookupItemDto(o.Id, o.NameAr, o.Code))
            .FirstOrDefaultAsync(ct);

        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPost("org-units/by-ids")]
    public async Task<ActionResult<LookupPageDto>> GetOrgUnitsByIds(
        [FromBody] LookupByIdsRequest? request,
        CancellationToken ct = default)
    {
        if (request?.Ids is null || request.Ids.Count == 0)
            return Ok(new LookupPageDto([], HasMore: false));
        if (request.Ids.Count > SearchFilterBase.LookupMaxPageSize)
            return BadRequest($"Maximum {SearchFilterBase.LookupMaxPageSize} ids per request.");

        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }

        var tenantKey = tenantContext.TenantId?.ToString("N") ?? "none";
        var cacheSeconds = configuration.GetValue("Lookup:CacheSeconds", DefaultCacheSeconds);
        var cacheTtl = TimeSpan.FromSeconds(cacheSeconds);
        var distinctIds = request.Ids.Distinct().ToList();
        var items = new List<LookupItemDto>();
        var missingIds = new List<Guid>();

        foreach (var id in distinctIds)
        {
            if (!scope.CanAccess(id))
                continue;

            var cacheKey = OrgUnitCacheKey(tenantKey, id);
            if (memoryCache.TryGetValue(cacheKey, out LookupItemDto? cached) && cached is not null)
                items.Add(cached);
            else
                missingIds.Add(id);
        }

        if (missingIds.Count > 0)
        {
            var query = context.OrgUnits.AsNoTracking().Where(o => missingIds.Contains(o.Id));
            if (scope.MustFilter)
                query = query.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));

            var fetched = await query
                .Select(o => new LookupItemDto(o.Id, o.NameAr, o.Code))
                .ToListAsync(ct);

            foreach (var dto in fetched)
            {
                items.Add(dto);
                memoryCache.Set(OrgUnitCacheKey(tenantKey, dto.Id), dto, cacheTtl);
            }
        }

        return Ok(new LookupPageDto(items, HasMore: false));
    }

    [HttpGet("org-units/tree")]
    public async Task<ActionResult<IReadOnlyList<OrgUnitTreeViewModel>>> GetOrgUnitTree(CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
        {
            OrgScopeDenialContext.Record(HttpContext, User);
            return Forbid();
        }

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

    private static string EmployeeCacheKey(string tenantKey, Guid id) => $"lookup:employee:{tenantKey}:{id:N}";

    private static string OrgUnitCacheKey(string tenantKey, Guid id) => $"lookup:org-unit:{tenantKey}:{id:N}";

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

    public sealed record JobCatalogLookupDetailDto(Guid Id, string Label, string? Sublabel, string GradeCode);

    public sealed record ReferenceLookupItemDto(string Code, string Label, string? Sublabel);

    public sealed record ReferenceLookupPageDto(IReadOnlyList<ReferenceLookupItemDto> Items, bool HasMore);

    [HttpGet("reference-data")]
    public async Task<ActionResult<ReferenceLookupPageDto>> SearchReferenceData(
        [FromQuery] string kind,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<CatalogKind>(kind, true, out var catalogKind))
            return BadRequest($"Unknown CatalogKind: {kind}");

        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.LookupMaxPageSize);
        var skip = (page - 1) * pageSize;

        if (!string.IsNullOrWhiteSpace(q) && q.Trim().Length < MinSearchLength)
            return Ok(new ReferenceLookupPageDto([], HasMore: false));

        var matches = await referenceData.SearchActiveAsync(
            catalogKind,
            q,
            skip,
            pageSize + 1,
            ct);

        var hasMore = matches.Count > pageSize;
        var items = hasMore ? matches.Take(pageSize).ToList() : matches;

        return Ok(new ReferenceLookupPageDto(
            items.Select(e => new ReferenceLookupItemDto(e.Code, e.NameAr, e.Code)).ToList(),
            hasMore));
    }

    [HttpGet("reference-data/{kind}/{code}")]
    public async Task<ActionResult<ReferenceLookupItemDto>> GetReferenceDataByCode(
        string kind,
        string code,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<CatalogKind>(kind, true, out var catalogKind))
            return BadRequest($"Unknown CatalogKind: {kind}");

        var entry = await referenceData.GetByCodeAsync(catalogKind, code, ct);
        if (entry is null || !entry.IsActive)
            return NotFound();

        return Ok(new ReferenceLookupItemDto(entry.Code, entry.NameAr, entry.Code));
    }

    [HttpGet("job-catalog")]
    public async Task<ActionResult<LookupPageDto>> SearchJobCatalog(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.LookupMaxPageSize);
        var skip = (page - 1) * pageSize;

        var query = context.JobCatalogEntries.AsNoTracking().Where(e => e.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (term.Length < MinSearchLength)
                return Ok(new LookupPageDto([], HasMore: false));

            query = query.Where(e =>
                e.JobCode.Contains(term) ||
                e.TitleAr.Contains(term) ||
                (e.TitleEn != null && e.TitleEn.Contains(term)) ||
                e.GradeCode.Contains(term));
        }

        var items = await query
            .OrderBy(e => e.TitleAr)
            .Skip(skip)
            .Take(pageSize + 1)
            .Select(e => new LookupItemDto(e.Id, e.TitleAr, e.JobCode))
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        return Ok(new LookupPageDto(items, hasMore));
    }

    [HttpGet("job-catalog/{id:guid}")]
    public async Task<ActionResult<JobCatalogLookupDetailDto>> GetJobCatalogEntry(Guid id, CancellationToken ct)
    {
        var entry = await context.JobCatalogEntries
            .AsNoTracking()
            .Where(e => e.Id == id && e.IsActive)
            .Select(e => new JobCatalogLookupDetailDto(e.Id, e.TitleAr, e.JobCode, e.GradeCode))
            .FirstOrDefaultAsync(ct);

        return entry is null ? NotFound() : Ok(entry);
    }
}
