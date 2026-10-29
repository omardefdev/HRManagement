using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class TalentMarketplaceController(
    HRCoreDbContext db,
    CandidatePublicStatusService publicStatus) : Controller
{
    private async Task PopulateOrgUnits(Guid? selected = null) =>
        ViewBag.OrgUnits = new SelectList(
            await db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", selected);

    public async Task<IActionResult> Index(Guid? orgUnitId, bool? isOpen, DateOnly? closingFrom, DateOnly? closingTo)
    {
        var query = db.InternalVacancies.AsNoTracking()
            .Include(v => v.OrgUnit)
            .Include(v => v.Requisition)
            .Include(v => v.Applications)
            .AsQueryable();
        if (orgUnitId is Guid ou)
            query = query.Where(v => v.OrgUnitId == ou);
        if (isOpen is bool open)
            query = query.Where(v => v.IsOpen == open);
        if (closingFrom is DateOnly cf)
            query = query.Where(v => v.ClosingDate >= cf);
        if (closingTo is DateOnly ct)
            query = query.Where(v => v.ClosingDate <= ct);

        var items = await query.OrderByDescending(v => v.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        await PopulateOrgUnits(orgUnitId);
        return View(new TalentMarketplaceIndexViewModel { Items = items, OrgUnitId = orgUnitId, IsOpen = isOpen, ClosingFrom = closingFrom, ClosingTo = closingTo });
    }

    public IActionResult Create()
    {
        TempData["Info"] = "تم إيقاف إنشاء الشواغر الداخلية يدوياً. أنشئ طلب شاغر وانشره داخلياً من مسار طلبات الشواغر.";
        return RedirectToAction("Index", "RecruitmentRequisitions");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public IActionResult Create(InternalVacancyCreateViewModel vm) =>
        RedirectToAction("Index", "RecruitmentRequisitions");

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var vacancy = await db.InternalVacancies.FindAsync(id);
        if (vacancy is null) return NotFound();

        if (vacancy.IsDerivedFromRequisition || vacancy.RequisitionId.HasValue)
        {
            TempData["Error"] = "الشواغر المشتقة من طلبات الشواغر تُدار من مسار طلبات الشواغر.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await PopulateOrgUnits();
        return View(new InternalVacancyEditViewModel
        {
            Id = vacancy.Id,
            Title = vacancy.Title,
            OrgUnitId = vacancy.OrgUnitId,
            Description = vacancy.Description,
            RequiredQualifications = vacancy.RequiredQualifications,
            ClosingDate = vacancy.ClosingDate,
            IsOpen = vacancy.IsOpen
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, InternalVacancyEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        var vacancy = await db.InternalVacancies.FindAsync(id);
        if (vacancy is null) return NotFound();

        if (vacancy.IsDerivedFromRequisition || vacancy.RequisitionId.HasValue)
        {
            TempData["Error"] = "الشواغر المشتقة من طلبات الشواغر للقراءة فقط.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // PLAN-25 (G-15): closing-date changes are audited; no retroactive close once applications exist.
        var hasApplications = await db.InternalApplications.AnyAsync(a => a.InternalVacancyId == id);
        var closingError = LegacyPostingClosingDateRules.Validate(
            vacancy.ClosingDate, vm.ClosingDate, hasApplications, DateOnly.FromDateTime(DateTime.UtcNow));
        if (closingError is not null)
            ModelState.AddModelError(nameof(vm.ClosingDate), closingError);

        if (!ModelState.IsValid)
        {
            await PopulateOrgUnits();
            return View(vm);
        }
        LegacyPostingClosingDateRules.AuditIfChanged(
            db, nameof(InternalVacancy), vacancy.Id, vacancy.ClosingDate, vm.ClosingDate,
            Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var uid) ? uid : null);
        vacancy.Title = vm.Title;
        vacancy.OrgUnitId = vm.OrgUnitId;
        vacancy.Description = vm.Description;
        vacancy.RequiredQualifications = vm.RequiredQualifications;
        vacancy.ClosingDate = vm.ClosingDate;
        vacancy.IsOpen = vm.IsOpen;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الشاغر";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var vacancy = await db.InternalVacancies
            .Include(v => v.OrgUnit)
            .Include(v => v.Requisition)
            .Include(v => v.Applications).ThenInclude(a => a.Employee)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id);
        if (vacancy is null) return NotFound();
        // HCM-030 (A4/B2): linked rows show what the candidate sees.
        ViewBag.PublicStatuses = await publicStatus.GetViewsAsync(vacancy.Applications
            .Where(a => a.PromotedRecruitmentApplicationId.HasValue)
            .Select(a => a.PromotedRecruitmentApplicationId!.Value)
            .ToList());
        return View(vacancy);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ChangeApplicationStatus(Guid id, Guid vacancyId, InternalApplicationStatus status)
    {
        var application = await db.InternalApplications.FindAsync(id);
        if (application is null) return NotFound();

        // HCM-030 (A4): a requisition-linked application is screened on its recruitment application only.
        if (application.RequisitionId.HasValue)
        {
            TempData["Error"] = UnifiedIntakeService.ManagedByUnifiedPathMessage;
            return RedirectToAction(nameof(Details), new { id = vacancyId });
        }
        application.Status = status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث حالة الترشّح";
        return RedirectToAction(nameof(Details), new { id = vacancyId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var vacancy = await db.InternalVacancies.FindAsync(id);
        if (vacancy is null) return NotFound();
        if (vacancy.IsDerivedFromRequisition || vacancy.RequisitionId.HasValue)
        {
            TempData["Error"] = "لا يمكن حذف شاغر مرتبط بطلب شاغر.";
            return RedirectToAction(nameof(Details), new { id });
        }
        db.InternalVacancies.Remove(vacancy);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الشاغر";
        return RedirectToAction(nameof(Index));
    }
}
