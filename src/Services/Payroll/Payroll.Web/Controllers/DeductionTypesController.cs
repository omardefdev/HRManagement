using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Web.Controllers;

public class DeductionTypesController : Controller
{
    private readonly PayrollDbContext _db;
    public DeductionTypesController(PayrollDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? q, bool? isStatutory)
    {
        var query = _db.DeductionTypes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.NameAr.Contains(term));
        }
        if (isStatutory.HasValue) query = query.Where(x => x.IsStatutory == isStatutory.Value);
        var items = await query.OrderBy(d => d.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Q = q;
        ViewBag.IsStatutory = isStatutory;
        return View(items);
    }

    public IActionResult Create() => View(new DeductionType());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DeductionType model)
    {
        if (!ModelState.IsValid) return View(model);
        _db.DeductionTypes.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الإضافة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.DeductionTypes.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DeductionType model)
    {
        if (model.Id != id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        var e = await _db.DeductionTypes.FindAsync(id);
        if (e is null) return NotFound();
        e.Code = model.Code; e.NameAr = model.NameAr; e.IsStatutory = model.IsStatutory;
        e.Percentage = model.Percentage; e.FixedAmount = model.FixedAmount;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.DeductionTypes.FindAsync(id);
        if (e is null) return NotFound();
        _db.DeductionTypes.Remove(e);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
