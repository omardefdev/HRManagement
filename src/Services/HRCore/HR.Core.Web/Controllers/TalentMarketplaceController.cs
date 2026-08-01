using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class TalentMarketplaceController(HRCoreDbContext db) : Controller
{
    private async Task PopulateOrgUnits() =>
        ViewBag.OrgUnits = new SelectList(
            await db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");

    public async Task<IActionResult> Index() =>
        View(await db.InternalVacancies.AsNoTracking()
            .Include(v => v.OrgUnit)
            .Include(v => v.Applications)
            .OrderByDescending(v => v.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());

    public async Task<IActionResult> Create()
    {
        await PopulateOrgUnits();
        return View(new InternalVacancyCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InternalVacancyCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOrgUnits();
            return View(vm);
        }
        db.InternalVacancies.Add(new InternalVacancy
        {
            Title = vm.Title,
            OrgUnitId = vm.OrgUnitId,
            Description = vm.Description,
            RequiredQualifications = vm.RequiredQualifications,
            ClosingDate = vm.ClosingDate,
            IsOpen = vm.IsOpen
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم نشر الشاغر الداخلي";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var vacancy = await db.InternalVacancies.FindAsync(id);
        if (vacancy == null) return NotFound();
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
    public async Task<IActionResult> Edit(Guid id, InternalVacancyEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        var vacancy = await db.InternalVacancies.FindAsync(id);
        if (vacancy == null) return NotFound();
        if (!ModelState.IsValid)
        {
            await PopulateOrgUnits();
            return View(vm);
        }
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
            .Include(v => v.Applications).ThenInclude(a => a.Employee)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id);
        if (vacancy == null) return NotFound();
        return View(vacancy);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeApplicationStatus(Guid id, Guid vacancyId, InternalApplicationStatus status)
    {
        var application = await db.InternalApplications.FindAsync(id);
        if (application == null) return NotFound();
        application.Status = status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث حالة الترشّح";
        return RedirectToAction(nameof(Details), new { id = vacancyId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var vacancy = await db.InternalVacancies.FindAsync(id);
        if (vacancy == null) return NotFound();
        db.InternalVacancies.Remove(vacancy);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الشاغر";
        return RedirectToAction(nameof(Index));
    }
}
