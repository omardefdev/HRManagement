using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Web.Controllers;

public class CompensationTypesController : Controller
{
    private readonly PayrollDbContext _db;

    public CompensationTypesController(PayrollDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var items = await _db.CompensationTypes.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(items);
    }

    public IActionResult Create() => View(new CompensationType());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompensationType model)
    {
        if (await _db.CompensationTypes.AnyAsync(x => x.Code == model.Code))
        {
            ModelState.AddModelError(nameof(model.Code), "الرمز مستخدم");
            return View(model);
        }

        if (!ModelState.IsValid) return View(model);

        _db.CompensationTypes.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تمت الإضافة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.CompensationTypes.FindAsync(id);
        if (e == null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CompensationType model)
    {
        if (id != model.Id) return BadRequest();
        if (await _db.CompensationTypes.AnyAsync(x => x.Code == model.Code && x.Id != id))
        {
            ModelState.AddModelError(nameof(model.Code), "الرمز مستخدم");
            return View(model);
        }

        if (!ModelState.IsValid) return View(model);

        var e = await _db.CompensationTypes.FindAsync(id);
        if (e == null) return NotFound();

        e.Code = model.Code;
        e.NameAr = model.NameAr;
        e.NameEn = model.NameEn;
        e.CalculationFormula = model.CalculationFormula;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.CompensationTypes.FindAsync(id);
        if (e == null) return NotFound();

        _db.CompensationTypes.Remove(e);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
