using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Web.Controllers;

public class PayGradesController : Controller
{
    private readonly PayrollDbContext _db;
    public PayGradesController(PayrollDbContext db) => _db = db;

    private async Task PopulateScalesAsync()
    {
        var scales = await _db.SalaryScales.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        ViewBag.Scales = new SelectList(scales, "Id", "NameAr");
    }

    public async Task<IActionResult> Index(Guid? scaleId, string? q)
    {
        var query = _db.PayGrades.Include(g => g.SalaryScale).AsNoTracking().AsQueryable();
        if (scaleId.HasValue) query = query.Where(g => g.SalaryScaleId == scaleId.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(g => g.GradeCode.Contains(term) || g.GradeNameAr.Contains(term));
        }
        var items = await query.OrderBy(g => g.SalaryScale.Code).ThenBy(g => g.GradeCode).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.ScaleId = scaleId;
        ViewBag.Q = q;
        ViewBag.Scales = new SelectList(
            await _db.SalaryScales.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "NameAr", scaleId);
        return View(items);
    }

    public async Task<IActionResult> Create(Guid? scaleId)
    {
        await PopulateScalesAsync();
        return View(new PayGrade { SalaryScaleId = scaleId ?? Guid.Empty });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PayGrade model)
    {
        if (!ModelState.IsValid) { await PopulateScalesAsync(); return View(model); }
        _db.PayGrades.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الدرجة";
        return RedirectToAction(nameof(Index), new { scaleId = model.SalaryScaleId });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.PayGrades.FindAsync(id);
        if (e is null) return NotFound();
        await PopulateScalesAsync();
        return View(e);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PayGrade model)
    {
        if (model.Id != id) return BadRequest();
        if (!ModelState.IsValid) { await PopulateScalesAsync(); return View(model); }
        var e = await _db.PayGrades.FindAsync(id);
        if (e is null) return NotFound();
        e.SalaryScaleId = model.SalaryScaleId; e.GradeCode = model.GradeCode; e.GradeNameAr = model.GradeNameAr;
        e.BaseSalary = model.BaseSalary; e.MinSalary = model.MinSalary; e.MaxSalary = model.MaxSalary;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.PayGrades.FindAsync(id);
        if (e is null) return NotFound();
        _db.PayGrades.Remove(e);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
