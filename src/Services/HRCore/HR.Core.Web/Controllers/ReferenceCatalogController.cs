using HR.Core.Infrastructure.Services.MasterData;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.MasterData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class ReferenceCatalogController(
    IReferenceCatalogAdminService adminService,
    EstablishmentScopeService establishmentScope) : Controller
{
    public async Task<IActionResult> Index(ReferenceCatalogIndexViewModel filter, CancellationToken ct)
    {
        var accessError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (accessError is not null)
            return Forbid();

        if (!IsSupportedKind(filter.Kind))
            filter.Kind = CatalogKind.Grade;

        filter.NormalizePaging();
        filter.Items = await adminService.ListAsync(
            filter.Kind,
            filter.SearchTerm,
            filter.IsActive,
            filter.Page,
            filter.PageSize,
            ct);
        filter.CanWrite = true;

        return View(filter);
    }

    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(CatalogKind kind, CancellationToken ct)
    {
        var writeError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (writeError is not null)
            return Forbid();

        if (!IsSupportedKind(kind))
            kind = CatalogKind.Grade;

        return View(new ReferenceCatalogFormViewModel { Kind = kind });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(ReferenceCatalogFormViewModel model, CancellationToken ct)
    {
        var writeError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (writeError is not null)
            return Forbid();

        if (!IsSupportedKind(model.Kind))
            ModelState.AddModelError(nameof(model.Kind), "نوع المرجع غير مدعوم.");

        if (!ModelState.IsValid)
            return View(model);

        var (entry, error) = await adminService.CreateAsync(
            model.Kind,
            model.Code,
            model.NameAr,
            model.NameEn,
            model.ValidFrom,
            model.ValidTo,
            ct);

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        TempData["Success"] = $"تم إنشاء الرمز {entry!.Code}.";
        return RedirectToAction(nameof(Details), new { id = entry.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var accessError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (accessError is not null)
            return Forbid();

        var entry = await adminService.GetByIdAsync(id, ct);
        if (entry is null)
            return NotFound();

        var canMutate = adminService.CanMutate(entry);
        return View(new ReferenceCatalogEditViewModel
        {
            Id = entry.Id,
            Kind = entry.CatalogKind,
            Code = entry.Code,
            NameAr = entry.NameAr,
            NameEn = entry.NameEn,
            ValidFrom = entry.ValidFrom,
            ValidTo = entry.ValidTo,
            IsReadOnly = !canMutate
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, ReferenceCatalogEditViewModel model, CancellationToken ct)
    {
        var writeError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (writeError is not null)
            return Forbid();

        if (id != model.Id)
            return BadRequest();

        if (model.IsReadOnly)
            return Forbid();

        if (!ModelState.IsValid)
            return View(model);

        var (entry, error) = await adminService.UpdateAsync(
            id,
            model.NameAr,
            model.NameEn,
            model.ValidFrom,
            model.ValidTo,
            ct);

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        TempData["Success"] = $"تم تحديث الرمز {entry!.Code}.";
        return RedirectToAction(nameof(Details), new { id = entry.Id });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var accessError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (accessError is not null)
            return Forbid();

        var entry = await adminService.GetByIdAsync(id, ct);
        if (entry is null)
            return NotFound();

        var impact = await adminService.AnalyzeImpactAsync(id, ct);
        return View(new ReferenceCatalogDetailsViewModel
        {
            Entry = entry,
            IsPlatformOwned = adminService.IsPlatformOwned(entry),
            CanMutate = adminService.CanMutate(entry),
            Impact = impact
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Deactivate(Guid id, string? replacedByCode, CancellationToken ct)
    {
        var writeError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (writeError is not null)
            return Forbid();

        var entryBefore = await adminService.GetByIdAsync(id, ct);
        var kind = entryBefore?.CatalogKind ?? CatalogKind.Grade;

        var (success, error) = await adminService.DeactivateAsync(id, replacedByCode, ct);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم تعطيل الرمز.";
        return RedirectToAction(nameof(Index), new { kind });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var writeError = await establishmentScope.ValidateCentralWriteAsync(User, ct);
        if (writeError is not null)
            return Forbid();

        var (success, error) = await adminService.ReactivateAsync(id, ct);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم إعادة تفعيل الرمز.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static bool IsSupportedKind(CatalogKind kind) =>
        kind is CatalogKind.Grade or CatalogKind.JobClassification;
}
