using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class KeyPositionsController(HRCoreDbContext db) : Controller
{
    private async Task PopulateLookups()
    {
        ViewBag.OrgUnits = new SelectList(
            await db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        ViewBag.Employees = new SelectList(
            await db.Employees.AsNoTracking().OrderBy(e => e.FullName).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "FullName");
    }

    public async Task<IActionResult> Index() =>
        View(await db.KeyPositions.AsNoTracking().Include(k => k.OrgUnit).OrderByDescending(k => k.Criticality).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        await PopulateLookups();
        return View(new KeyPositionCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(KeyPositionCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookups();
            return View(vm);
        }
        db.KeyPositions.Add(new KeyPosition
        {
            Title = vm.Title,
            OrgUnitId = vm.OrgUnitId,
            CurrentHolderEmployeeId = vm.CurrentHolderEmployeeId,
            Criticality = vm.Criticality,
            Notes = vm.Notes
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة المنصب الحرج";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var pos = await db.KeyPositions.FindAsync(id);
        if (pos == null) return NotFound();
        await PopulateLookups();
        return View(new KeyPositionEditViewModel
        {
            Id = pos.Id,
            Title = pos.Title,
            OrgUnitId = pos.OrgUnitId,
            CurrentHolderEmployeeId = pos.CurrentHolderEmployeeId,
            Criticality = pos.Criticality,
            Notes = pos.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, KeyPositionEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        var pos = await db.KeyPositions.FindAsync(id);
        if (pos == null) return NotFound();
        if (!ModelState.IsValid)
        {
            await PopulateLookups();
            return View(vm);
        }
        pos.Title = vm.Title;
        pos.OrgUnitId = vm.OrgUnitId;
        pos.CurrentHolderEmployeeId = vm.CurrentHolderEmployeeId;
        pos.Criticality = vm.Criticality;
        pos.Notes = vm.Notes;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث المنصب";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var pos = await db.KeyPositions.FindAsync(id);
        if (pos == null) return NotFound();
        db.KeyPositions.Remove(pos);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف المنصب";
        return RedirectToAction(nameof(Index));
    }
}
