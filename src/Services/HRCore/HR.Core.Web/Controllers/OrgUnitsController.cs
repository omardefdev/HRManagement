using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HR.Core.Infrastructure.Services.OrgStructure;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class OrgUnitsController : Controller
{
    public const int MaxTreeNodes = 500;

    private readonly HRCoreDbContext _context;
    private readonly OrgScopeService _orgScope;
    private readonly OrgUnitPathService _pathService;
    private readonly HR.Core.Infrastructure.Integration.OrgUnitIntegrationService _integration;
    private readonly IOrgUnitHierarchyCache _hierarchyCache;
    private readonly ICodeGenerator _codeGenerator;
    private readonly IOrgUnitVersionService _versionService;

    public OrgUnitsController(
        HRCoreDbContext context,
        OrgScopeService orgScope,
        OrgUnitPathService pathService,
        HR.Core.Infrastructure.Integration.OrgUnitIntegrationService integration,
        IOrgUnitHierarchyCache hierarchyCache,
        ICodeGenerator codeGenerator,
        IOrgUnitVersionService versionService)
    {
        _context = context;
        _orgScope = orgScope;
        _pathService = pathService;
        _integration = integration;
        _hierarchyCache = hierarchyCache;
        _codeGenerator = codeGenerator;
        _versionService = versionService;
    }

    public async Task<IActionResult> Index(SearchFilterBase filter)
    {
        filter.NormalizePaging();

        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (orgScope.IsDenied) return Forbid();

        var query = _context.OrgUnits
            .Include(o => o.Parent)
            .Include(o => o.HeadEmployee)
            .AsNoTracking()
            .AsQueryable();

        if (orgScope.MustFilter)
            query = query.Where(o => orgScope.AllowedOrgUnitIds.Contains(o.Id));

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(o =>
                o.NameAr.Contains(term) ||
                o.Code.Contains(term) ||
                (o.NameEn != null && o.NameEn.Contains(term)));
        }

        query = query.OrderBy(o => o.Code);

        var count = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var vm = new OrgUnitListViewModel
        {
            OrgUnits = new PaginatedList<OrgUnit>(items, count, filter.Page, filter.PageSize),
            Filter = filter
        };

        return View(vm);
    }

    public async Task<IActionResult> Tree()
    {
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (orgScope.IsDenied) return Forbid();

        var units = await LoadTreeUnitsAsync(orgScope);
        var roots = units.Where(o => o.ParentId == null
            || (orgScope.MustFilter && o.ParentId.HasValue && !orgScope.AllowedOrgUnitIds.Contains(o.ParentId.Value))).ToList();

        var tree = roots.Select(r => BuildTree(r, units)).ToList();
        return View(tree);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var orgUnit = await _context.OrgUnits
            .Include(o => o.Parent)
            .Include(o => o.HeadEmployee)
            .Include(o => o.Children)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (orgUnit == null) return NotFound();
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(scope, orgUnit.Id) is { } denied) return denied;
        return View(orgUnit);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create()
    {
        var vm = new OrgUnitCreateViewModel();
        await PopulateSelectListsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(OrgUnitCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        var createScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (createScope.IsDenied) return Forbid();
        if (vm.ParentId.HasValue && !createScope.CanAccess(vm.ParentId))
        {
            ModelState.AddModelError(nameof(vm.ParentId), "الوحدة الأب خارج نطاق صلاحيتك");
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }
        if (!vm.ParentId.HasValue && createScope.MustFilter)
        {
            ModelState.AddModelError(nameof(vm.ParentId), "يجب اختيار وحدة أب ضمن نطاقك");
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        var (depthOk, depthError) = await _pathService.ValidateDepthAsync(vm.ParentId);
        if (!depthOk)
        {
            ModelState.AddModelError(nameof(vm.ParentId), depthError!);
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        var parentCode = "00";
        if (vm.ParentId.HasValue)
        {
            var parent = await _context.OrgUnits.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == vm.ParentId.Value);
            if (parent is not null)
                parentCode = parent.Code;
        }

        var codeResult = await _codeGenerator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.OrgUnit,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ParentCode"] = parentCode
            }
        });

        var orgUnit = new OrgUnit
        {
            Code = codeResult.Code,
            NameAr = vm.NameAr,
            NameEn = vm.NameEn,
            ParentId = vm.ParentId,
            HeadEmployeeId = vm.HeadEmployeeId
        };

        _context.OrgUnits.Add(orgUnit);
        await _context.SaveChangesAsync();
        await _pathService.AssignPathOnCreateAsync(orgUnit);
        _hierarchyCache.InvalidateTenant(orgUnit.TenantId);
        await _versionService.AppendVersionAsync(orgUnit, OrgOperationType.Create, DateTime.UtcNow);
        await _integration.PublishOrgUnitChangedAsync(orgUnit);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"تم إضافة الوحدة التنظيمية {orgUnit.NameAr} بنجاح";
        return RedirectToAction(nameof(Details), new { id = orgUnit.Id });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var orgUnit = await _context.OrgUnits.FindAsync(id);
        if (orgUnit == null) return NotFound();
        var editScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(editScope, orgUnit.Id) is { } editDenied) return editDenied;

        var vm = new OrgUnitEditViewModel
        {
            Id = orgUnit.Id,
            Code = orgUnit.Code,
            NameAr = orgUnit.NameAr,
            NameEn = orgUnit.NameEn,
            ParentId = orgUnit.ParentId,
            HeadEmployeeId = orgUnit.HeadEmployeeId
        };

        await PopulateSelectListsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, OrgUnitEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        var orgUnit = await _context.OrgUnits.FindAsync(id);
        if (orgUnit == null) return NotFound();
        var postScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(postScope, orgUnit.Id) is { } postDenied) return postDenied;

        var beforeName = orgUnit.NameAr;
        var beforeHead = orgUnit.HeadEmployeeId;

        if (vm.ParentId.HasValue && !postScope.CanAccess(vm.ParentId))
        {
            ModelState.AddModelError(nameof(vm.ParentId), "الوحدة الأب خارج نطاق صلاحيتك");
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        var parentChanged = orgUnit.ParentId != vm.ParentId;
        // Code is immutable after issuance (COD-001)
        orgUnit.NameAr = vm.NameAr;
        orgUnit.NameEn = vm.NameEn;
        orgUnit.HeadEmployeeId = vm.HeadEmployeeId;

        if (parentChanged)
        {
            // Persist non-parent fields first, then atomic move (sets ParentId + paths)
            await _context.SaveChangesAsync();
            var (ok, error, affected) = await _pathService.MoveSubtreeAsync(orgUnit.Id, vm.ParentId);
            if (!ok)
            {
                ModelState.AddModelError(nameof(vm.ParentId), error ?? "فشل نقل الوحدة");
                await PopulateSelectListsAsync(vm);
                return View(vm);
            }

            _hierarchyCache.InvalidateTenant(orgUnit.TenantId);
            await _integration.PublishManyOrgUnitsChangedAsync(affected);
            await _context.SaveChangesAsync();
            await _context.Entry(orgUnit).ReloadAsync();
            await _versionService.AppendVersionAsync(orgUnit, OrgOperationType.Move, DateTime.UtcNow);
        }
        else
        {
            await _context.SaveChangesAsync();
            _hierarchyCache.InvalidateTenant(orgUnit.TenantId);
            await _integration.PublishOrgUnitChangedAsync(orgUnit);
            await _context.SaveChangesAsync();

            if (beforeName != orgUnit.NameAr || beforeHead != orgUnit.HeadEmployeeId)
                await _versionService.AppendVersionAsync(orgUnit, OrgOperationType.Rename, DateTime.UtcNow);
        }

        TempData["Success"] = $"تم تحديث الوحدة التنظيمية {orgUnit.NameAr} بنجاح";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var orgUnit = await _context.OrgUnits
            .Include(o => o.Children)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (orgUnit == null) return NotFound();
        var delScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(delScope, orgUnit.Id) is { } delDenied) return delDenied;

        if (orgUnit.Children.Count != 0)
        {
            TempData["Error"] = "لا يمكن حذف وحدة تحتوي على وحدات فرعية";
            return RedirectToAction(nameof(Details), new { id });
        }

        var tenantId = orgUnit.TenantId;
        await _integration.PublishOrgUnitChangedAsync(orgUnit, isDeleted: true);
        _context.OrgUnits.Remove(orgUnit);
        await _context.SaveChangesAsync();
        _hierarchyCache.InvalidateTenant(tenantId);

        TempData["Success"] = $"تم حذف الوحدة التنظيمية {orgUnit.NameAr} بنجاح";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<OrgUnit>> LoadTreeUnitsAsync(OrgAccessScope orgScope)
    {
        var query = _context.OrgUnits
            .Include(o => o.HeadEmployee)
            .AsNoTracking()
            .AsQueryable();

        if (orgScope.MustFilter)
        {
            var claim = HrOrgScopeVisibility.GetScopeClaimResult(User);
            var rootId = claim.RootOrgUnitId;
            string? rootPath = null;
            if (rootId.HasValue)
            {
                rootPath = await _context.OrgUnits.AsNoTracking()
                    .Where(o => o.Id == rootId.Value)
                    .Select(o => o.MaterializedPath)
                    .FirstOrDefaultAsync();
            }

            if (!string.IsNullOrEmpty(rootPath))
                query = query.Where(o => o.MaterializedPath.StartsWith(rootPath));
            else
                query = query.Where(o => orgScope.AllowedOrgUnitIds.Contains(o.Id));
        }

        return await query
            .OrderBy(o => o.Code)
            .Take(MaxTreeNodes)
            .ToListAsync();
    }

    private static OrgUnitTreeViewModel BuildTree(OrgUnit unit, List<OrgUnit> all)
    {
        return new OrgUnitTreeViewModel
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

    private async Task PopulateSelectListsAsync(OrgUnitCreateViewModel vm)
    {
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        var units = _context.OrgUnits.AsQueryable();
        var employees = _context.Employees.AsQueryable();
        if (scope.MustFilter)
        {
            units = units.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
            employees = employees.Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));
        }
        vm.ParentUnits = new SelectList(
            await units.OrderBy(o => o.NameAr).Take(SearchFilterBase.MaxPageSize).ToListAsync(), "Id", "NameAr");
        vm.Employees = new SelectList(
            await employees.OrderBy(e => e.FullName).Take(SearchFilterBase.MaxPageSize).ToListAsync(), "Id", "FullName");
    }
}
