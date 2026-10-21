using HR.Core.Domain.Services;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class WorkforcePlansController(
    HR.Core.Infrastructure.Data.HRCoreDbContext db,
    IWorkforcePlanService workforcePlanService,
    IEstablishmentGapAnalysisService gapAnalysisService,
    EstablishmentScopeService establishmentScope,
    OrgScopeService orgScope) : Controller
{
    public async Task<IActionResult> Index(int? fiscalYear, Guid? orgUnitId, string? status)
    {
        var scope = await establishmentScope.GetScopeAsync(User);
        if (scope.IsDenied) return Forbid();

        var query = db.WorkforcePlans.AsNoTracking().Include(p => p.OrgUnit).AsQueryable();
        if (fiscalYear is int fy)
            query = query.Where(p => p.FiscalYear == fy);
        if (orgUnitId is Guid ou)
            query = query.Where(p => p.OrgUnitId == ou);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);

        if (scope.MustFilter)
        {
            var allowed = scope.AllowedOrgUnitIds.ToList();
            query = query.Where(p => p.OrgUnitId.HasValue && allowed.Contains(p.OrgUnitId.Value));
        }

        var items = await query.OrderByDescending(x => x.FiscalYear).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        await PopulateOrgUnitsAsync(orgUnitId);
        return View(new WorkforcePlansIndexViewModel { Items = items, FiscalYear = fiscalYear, OrgUnitId = orgUnitId, Status = status });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create()
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is { } err)
        {
            TempData["Error"] = err;
            return RedirectToAction(nameof(Index));
        }

        await PopulateOrgUnitsAsync();
        var y = DateTime.UtcNow.Year;
        return View(new WorkforcePlanCreateViewModel { FiscalYear = y });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(WorkforcePlanCreateViewModel vm)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is { } err)
        {
            TempData["Error"] = err;
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            await PopulateOrgUnitsAsync(vm.OrgUnitId);
            return View(vm);
        }

        db.WorkforcePlans.Add(new HR.Core.Domain.Entities.WorkforcePlan
        {
            Title = vm.Title,
            FiscalYear = vm.FiscalYear,
            OrgUnitId = vm.OrgUnitId,
            Summary = vm.Summary,
            Status = "draft"
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حفظ خطة القوى العاملة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id, DateTime? asOf)
    {
        var plan = await workforcePlanService.GetWithLinesAsync(id);
        if (plan is null) return NotFound();

        if (plan.OrgUnitId is Guid orgUnitId)
        {
            var scope = await establishmentScope.GetScopeAsync(User);
            if (this.ForbidIfOrgUnitOutOfScope(scope, orgUnitId) is { } denied)
                return denied;
        }

        var gap = plan.OrgUnitId is Guid ou
            ? await gapAnalysisService.AnalyzeOrgAsync(ou, plan.FiscalYear, plan.Lines.ToList(), asOf)
            : null;

        var canEdit = await establishmentScope.ValidateCentralWriteAsync(User) is null && plan.Status == "draft";
        ViewBag.JobCatalog = new SelectList(
            await db.JobCatalogEntries.AsNoTracking().Where(j => j.IsActive).OrderBy(j => j.JobCode).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "TitleAr");

        return View(new WorkforcePlanDetailsViewModel
        {
            Plan = plan,
            GapSummary = gap,
            AsOf = asOf,
            CanEdit = canEdit,
            NewLine = new WorkforcePlanLineEditViewModel { PlanId = id },
            Approve = new WorkforcePlanApproveViewModel { PlanId = id }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AddLine(WorkforcePlanLineEditViewModel vm)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is { } err)
        {
            TempData["Error"] = err;
            return RedirectToAction(nameof(Details), new { id = vm.PlanId });
        }

        try
        {
            await workforcePlanService.AddLineAsync(
                vm.PlanId, vm.GradeCode, vm.JobCatalogEntryId, vm.PlannedCount, vm.PlannedFTE, vm.Notes);
            TempData["Success"] = "تمت إضافة سطر الهدف";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = vm.PlanId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RemoveLine(Guid planId, Guid lineId)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is { } err)
        {
            TempData["Error"] = err;
            return RedirectToAction(nameof(Details), new { id = planId });
        }

        try
        {
            await workforcePlanService.RemoveLineAsync(lineId);
            TempData["Success"] = "تم حذف سطر الهدف";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = planId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Approve(WorkforcePlanApproveViewModel vm)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is { } err)
        {
            TempData["Error"] = err;
            return RedirectToAction(nameof(Details), new { id = vm.PlanId });
        }

        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : (Guid?)null;
        var (ok, error) = await workforcePlanService.ApproveAsync(vm.PlanId, vm.LegalSourceId, userId);
        TempData[ok ? "Success" : "Error"] = ok ? "تم اعتماد الخطة" : error;
        return RedirectToAction(nameof(Details), new { id = vm.PlanId });
    }

    public async Task<IActionResult> GapReport(Guid? orgUnitId, int? fiscalYear, Guid? planId, DateTime? asOf)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (scope.IsDenied) return Forbid();

        var year = fiscalYear ?? DateTime.UtcNow.Year;
        EstablishmentGapSummary? summary = null;
        string? orgName = null;

        if (planId is Guid pid)
        {
            summary = await gapAnalysisService.AnalyzePlanAsync(pid, asOf);
            var plan = await db.WorkforcePlans.AsNoTracking().Include(p => p.OrgUnit).FirstOrDefaultAsync(p => p.Id == pid);
            orgUnitId = plan?.OrgUnitId;
            orgName = plan?.OrgUnit?.NameAr;
            year = plan?.FiscalYear ?? year;
        }
        else if (orgUnitId is Guid ou)
        {
            if (scope.MustFilter && !scope.CanAccess(ou)) return Forbid();
            orgName = await db.OrgUnits.AsNoTracking().Where(o => o.Id == ou).Select(o => o.NameAr).FirstOrDefaultAsync();
            var plan = await db.WorkforcePlans.AsNoTracking().Include(p => p.Lines)
                .Where(p => p.OrgUnitId == ou && p.FiscalYear == year && p.Status == "approved")
                .OrderByDescending(p => p.ApprovedAt)
                .FirstOrDefaultAsync();
            summary = plan is not null
                ? await gapAnalysisService.AnalyzeOrgAsync(ou, year, plan.Lines.ToList(), asOf)
                : await gapAnalysisService.AnalyzeOrgAsync(ou, year, [], asOf);
        }

        await PopulateOrgUnitsAsync(orgUnitId);
        return View(new EstablishmentGapReportViewModel
        {
            OrgUnitId = orgUnitId,
            FiscalYear = year,
            AsOf = asOf,
            WorkforcePlanId = planId,
            Summary = summary,
            OrgUnitName = orgName
        });
    }

    private async Task PopulateOrgUnitsAsync(Guid? selected = null)
    {
        var scope = await establishmentScope.GetScopeAsync(User);
        var query = db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).AsQueryable();
        if (scope.MustFilter)
            query = query.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        ViewBag.OrgUnits = new SelectList(await query.Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", selected);
    }
}
