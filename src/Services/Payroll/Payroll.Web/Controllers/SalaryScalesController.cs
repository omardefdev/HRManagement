using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Web.Controllers;

public class SalaryScalesController : Controller
{
    private readonly PayrollDbContext _db;
    public SalaryScalesController(PayrollDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? q, bool? isActive, DateTime? effectiveFrom, DateTime? effectiveTo)
    {
        var query = _db.SalaryScales.AsNoTracking().Include(s => s.PayGrades).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(s => s.Code.Contains(term) || s.NameAr.Contains(term));
        }
        if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive.Value);
        if (effectiveFrom.HasValue) query = query.Where(s => s.EffectiveFrom >= effectiveFrom.Value);
        if (effectiveTo.HasValue) query = query.Where(s => s.EffectiveFrom <= effectiveTo.Value);
        var items = await query.OrderBy(s => s.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Q = q;
        ViewBag.IsActive = isActive;
        ViewBag.EffectiveFrom = effectiveFrom;
        ViewBag.EffectiveTo = effectiveTo;
        return View(items);
    }

    public IActionResult Create() => View(new SalaryScale { EffectiveFrom = DateTime.UtcNow });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SalaryScale model)
    {
        if (!ModelState.IsValid) return View(model);
        _db.SalaryScales.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة جدول الرواتب";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.SalaryScales.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SalaryScale model)
    {
        if (model.Id != id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        var e = await _db.SalaryScales.FindAsync(id);
        if (e is null) return NotFound();
        e.Code = model.Code; e.NameAr = model.NameAr; e.EffectiveFrom = model.EffectiveFrom;
        e.EffectiveTo = model.EffectiveTo; e.IsActive = model.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.SalaryScales.Include(s => s.PayGrades).FirstOrDefaultAsync(s => s.Id == id);
        if (e is null) return NotFound();
        _db.SalaryScales.Remove(e);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
