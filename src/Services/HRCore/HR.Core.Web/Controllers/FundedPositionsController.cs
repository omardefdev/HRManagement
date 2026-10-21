using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class FundedPositionsController(
    HRCoreDbContext db,
    EstablishmentScopeService establishmentScope,
    EstablishmentChangeLifecycleService changeLifecycle,
    EstablishmentReferenceValidator referenceValidator,
    IFundedPositionVersionService fundedPositionVersions) : Controller
{
    public async Task<IActionResult> Index(FundedPositionsIndexViewModel filter)
    {
        var scope = await establishmentScope.GetScopeAsync(User);
        if (this.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        filter.NormalizePaging();

        var query = db.FundedPositions
            .AsNoTracking()
            .Include(p => p.OrgUnit)
            .Include(p => p.JobCatalogEntry)
            .Include(p => p.OccupantEmployee)
            .AsQueryable();

        query = await establishmentScope.ApplyOrgAccessFilterAsync(query, scope);

        if (filter.ScopeOrgUnitId is Guid scopeOrgUnitId && scopeOrgUnitId != Guid.Empty)
            query = await establishmentScope.ApplyScopeOrgUnitFilterAsync(query, scopeOrgUnitId);

        if (filter.BudgetYear is int year)
            query = query.Where(p => p.BudgetYear == year);

        if (filter.Status is FundedPositionStatus status)
            query = query.Where(p => p.Status == status);

        if (filter.OrgUnitId is Guid orgUnitId)
            query = query.Where(p => p.OrgUnitId == orgUnitId);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(p =>
                p.PositionCode.Contains(term) ||
                p.GradeCode.Contains(term) ||
                (p.JobCatalogEntry != null && (p.JobCatalogEntry.TitleAr.Contains(term) || p.JobCatalogEntry.JobCode.Contains(term))));
        }

        query = query.OrderByDescending(p => p.BudgetYear).ThenBy(p => p.PositionCode);

        filter.Items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        ViewBag.CanMutate = await establishmentScope.ValidateCentralWriteAsync(User) is null;

        await establishmentScope.PopulateBranchScopesAsync(
            User,
            list => ViewBag.BranchScopes = list,
            _ => { },
            filter.ScopeOrgUnitId);

        var orgUnits = db.OrgUnits.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
            orgUnits = orgUnits.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        ViewBag.OrgUnits = new SelectList(
            await orgUnits.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "NameAr", filter.OrgUnitId);

        return View(filter);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(int? budgetYear)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        var vm = new FundedPositionCreateViewModel
        {
            BudgetYear = budgetYear ?? DateTime.UtcNow.Year
        };
        await establishmentScope.ApplyDefaultScopeOrgUnitAsync(User, id => vm.ScopeOrgUnitId = id);
        await PopulateCreateLookupsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(FundedPositionCreateViewModel vm)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        await ValidateCreateAsync(vm);

        if (!ModelState.IsValid)
        {
            await PopulateCreateLookupsAsync(vm);
            return View(vm);
        }

        var request = await changeLifecycle.CreateDraftAsync(
            EstablishmentChangeType.CreateFundedPosition,
            new FundedPositionCreatePayload
            {
                BudgetYear = vm.BudgetYear,
                OrgUnitId = vm.OrgUnitId,
                JobCatalogEntryId = vm.JobCatalogEntryId,
                GradeCode = vm.GradeCode.Trim(),
                FTE = vm.FTE
            },
            createdBy: ParseUserId(User));

        TempData["Success"] = "تم حفظ مسودة طلب إنشاء منصب ممول";
        return RedirectToAction("Details", "EstablishmentChanges", new { id = request.Id });
    }

    public async Task<IActionResult> Details(Guid id, DateTime? asOf)
    {
        var scope = await establishmentScope.GetScopeAsync(User);
        if (this.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        var position = await db.FundedPositions
            .AsNoTracking()
            .Include(p => p.OrgUnit)
            .Include(p => p.JobCatalogEntry)
            .Include(p => p.OccupantEmployee)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (position is null) return NotFound();

        if (this.ForbidIfOrgUnitOutOfScope(scope, position.OrgUnitId, "FundedPosition", id.ToString("D")) is { } unitDenied)
            return unitDenied;

        var activeRequisitions = await db.RecruitmentRequisitions
            .AsNoTracking()
            .Where(r => r.FundedPositionId == id && RequisitionWorkflowRules.BlockingStatuses.Contains(r.Status))
            .OrderByDescending(r => r.RequestedDate)
            .ToListAsync();

        var changeLogs = await db.EstablishmentChangeLogs
            .AsNoTracking()
            .Where(l => l.EntityType == nameof(FundedPosition) && l.EntityId == id)
            .OrderByDescending(l => l.OccurredAt)
            .Take(20)
            .ToListAsync();

        var versionTimeline = await fundedPositionVersions.ListHistoryAsync(id);
        FundedPositionVersionDto? asOfSnapshot = null;
        if (asOf is DateTime at)
            asOfSnapshot = await fundedPositionVersions.ResolveAsOfAsync(id, at);

        var vm = new FundedPositionDetailsViewModel
        {
            Position = position,
            ActiveRequisitions = activeRequisitions,
            ChangeLogs = changeLogs,
            CanMutate = await establishmentScope.ValidateCentralWriteAsync(User) is null
        };

        ViewBag.VersionTimeline = versionTimeline;
        ViewBag.AsOfDate = asOf;
        ViewBag.AsOfSnapshot = asOfSnapshot;

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Freeze(Guid id, FundedPositionDetailsViewModel vm)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        var position = await db.FundedPositions.FindAsync(id);
        if (position is null) return NotFound();

        if (string.IsNullOrWhiteSpace(vm.FreezeReason))
        {
            TempData["Error"] = "سبب التجميد مطلوب.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var request = await changeLifecycle.CreateDraftAsync(
            EstablishmentChangeType.FreezeFundedPosition,
            new FreezeFundedPositionPayload
            {
                FundedPositionId = id,
                FreezeReason = vm.FreezeReason.Trim()
            },
            targetEntityId: id,
            createdBy: ParseUserId(User));

        TempData["Success"] = "تم حفظ مسودة طلب تجميد المنصب";
        return RedirectToAction("Details", "EstablishmentChanges", new { id = request.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Unfreeze(Guid id)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        var position = await db.FundedPositions.FindAsync(id);
        if (position is null) return NotFound();

        var request = await changeLifecycle.CreateDraftAsync(
            EstablishmentChangeType.UnfreezeFundedPosition,
            new UnfreezeFundedPositionPayload { FundedPositionId = id },
            targetEntityId: id,
            createdBy: ParseUserId(User));

        TempData["Success"] = "تم حفظ مسودة طلب إلغاء تجميد المنصب";
        return RedirectToAction("Details", "EstablishmentChanges", new { id = request.Id });
    }

    private async Task PopulateCreateLookupsAsync(FundedPositionCreateViewModel vm) =>
        await establishmentScope.PopulateBranchScopesAsync(
            User,
            list => ViewBag.BranchScopes = list,
            locked => ViewBag.BranchScopeLocked = locked,
            vm.ScopeOrgUnitId != Guid.Empty ? vm.ScopeOrgUnitId : null);

    private async Task ValidateCreateAsync(FundedPositionCreateViewModel vm)
    {
        if (vm.ScopeOrgUnitId == Guid.Empty)
            ModelState.AddModelError(nameof(vm.ScopeOrgUnitId), "حدّد الفرع أو المركزية.");

        var accessError = await establishmentScope.ValidateScopeOrgUnitAccessAsync(User, vm.ScopeOrgUnitId);
        if (accessError is not null)
            ModelState.AddModelError(nameof(vm.ScopeOrgUnitId), accessError);

        var orgUnit = await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(o => o.Id == vm.OrgUnitId);
        if (orgUnit is null)
        {
            ModelState.AddModelError(nameof(vm.OrgUnitId), "الوحدة التنظيمية غير موجودة.");
        }
        else if (vm.ScopeOrgUnitId != Guid.Empty &&
                 !await establishmentScope.IsOrgUnitUnderScopeAsync(orgUnit.Id, vm.ScopeOrgUnitId))
        {
            ModelState.AddModelError(nameof(vm.OrgUnitId), "الوحدة التنظيمية يجب أن تقع ضمن الفرع/المركزية المحدّدة.");
        }

        var catalog = await db.JobCatalogEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == vm.JobCatalogEntryId);
        if (catalog is null)
            ModelState.AddModelError(nameof(vm.JobCatalogEntryId), "دليل الوظائف غير موجود.");
        else if (!catalog.IsActive)
            ModelState.AddModelError(nameof(vm.JobCatalogEntryId), "دليل الوظائف غير نشط.");
        else
            vm.GradeCode = catalog.GradeCode;

        if (await referenceValidator.ValidateGradeCodeAsync(vm.GradeCode) is { } gradeError)
            ModelState.AddModelError(nameof(vm.GradeCode), gradeError);
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
