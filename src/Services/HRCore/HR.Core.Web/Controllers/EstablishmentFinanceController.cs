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
[OrgScoped(OutOfScope = OrgScopeAnswer.Forbid)]
public class EstablishmentFinanceController(
    HR.Core.Infrastructure.Data.HRCoreDbContext db,
    IFinanceBudgetImportService financeImportService,
    IEstablishmentFinanceReconciliationService reconciliationService,
    EstablishmentScopeService establishmentScope,
    OrgScopeService orgScope) : Controller
{
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public IActionResult Import() =>
        View(new FinanceImportViewModel { FiscalYear = DateTime.UtcNow.Year });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Import(FinanceImportViewModel vm)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is { } err)
        {
            TempData["Error"] = err;
            return RedirectToAction(nameof(Import));
        }

        if (vm.File is null || vm.File.Length == 0)
        {
            TempData["Error"] = "اختر ملف CSV";
            return View(vm);
        }

        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : (Guid?)null;
        await using var stream = vm.File.OpenReadStream();
        var (batch, error) = await financeImportService.ImportCsvAsync(stream, vm.File.FileName, vm.FiscalYear, userId);
        if (batch is null)
        {
            TempData["Error"] = error;
            return View(vm);
        }

        TempData["Success"] = $"تم استيراد {batch.RowCount} سطر (Stub — لا ERP حي)";
        return RedirectToAction(nameof(Reconciliation), new { fiscalYear = vm.FiscalYear, batchId = batch.Id });
    }

    public async Task<IActionResult> Reconciliation(
        int? fiscalYear,
        [NotOrgScopeKey("an agency budget import batch: the reconciliation is narrowed by the unit")] Guid? batchId,
        Guid? orgUnitId)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (scope.IsDenied) return Forbid();
        if (orgUnitId is Guid ou && scope.MustFilter && !scope.CanAccess(ou)) return Forbid();
        // PLAN-52 (P0 finding): without a unit the reconciliation is the agency's; a branch user's is his scope's root, and the imported
        // side shows his unit's cost centers only.
        if (scope.MustFilter)
            orgUnitId ??= await HttpContext.GetRestrictedRootAsync();

        var year = fiscalYear ?? DateTime.UtcNow.Year;
        var result = await reconciliationService.ReconcileAsync(year, batchId, orgUnitId, unitCostCentersOnly: scope.MustFilter);
        await PopulateOrgUnitsAsync(orgUnitId);
        return View(new FinanceReconciliationViewModel
        {
            FiscalYear = year,
            BatchId = batchId ?? result.BatchId,
            OrgUnitId = orgUnitId,
            Result = result
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
