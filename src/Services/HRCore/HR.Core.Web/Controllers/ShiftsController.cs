using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class ShiftsController(HRCoreDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? q, ShiftType? shiftType, bool? isActive)
    {
        var query = db.Shifts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(s => s.Code.Contains(term) || s.NameAr.Contains(term));
        }
        if (shiftType is ShiftType st)
            query = query.Where(s => s.ShiftType == st);
        if (isActive is bool active)
            query = query.Where(s => s.IsActive == active);

        var items = await query.OrderBy(s => s.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(new ShiftsIndexViewModel { Items = items, Q = q, ShiftType = shiftType, IsActive = isActive });
    }

    public IActionResult Create() => View(new ShiftCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShiftCreateViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);
        db.Shifts.Add(new Shift
        {
            Code = vm.Code,
            NameAr = vm.NameAr,
            ShiftType = vm.ShiftType,
            StartTime = vm.StartTime,
            EndTime = vm.EndTime,
            IsActive = vm.IsActive
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الوردية";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var shift = await db.Shifts.FindAsync(id);
        if (shift == null) return NotFound();
        return View(new ShiftEditViewModel
        {
            Id = shift.Id,
            Code = shift.Code,
            NameAr = shift.NameAr,
            ShiftType = shift.ShiftType,
            StartTime = shift.StartTime,
            EndTime = shift.EndTime,
            IsActive = shift.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ShiftEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        var shift = await db.Shifts.FindAsync(id);
        if (shift == null) return NotFound();
        if (!ModelState.IsValid)
            return View(vm);
        shift.Code = vm.Code;
        shift.NameAr = vm.NameAr;
        shift.ShiftType = vm.ShiftType;
        shift.StartTime = vm.StartTime;
        shift.EndTime = vm.EndTime;
        shift.IsActive = vm.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الوردية";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var shift = await db.Shifts.FindAsync(id);
        if (shift == null) return NotFound();
        db.Shifts.Remove(shift);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الوردية";
        return RedirectToAction(nameof(Index));
    }
}
