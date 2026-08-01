using HR.Health.Domain.Entities;
using HR.Health.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Health.Web.Controllers;

public class PeriodicMedicalChecksController(HealthDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PeriodicMedicalChecks.AsNoTracking().OrderByDescending(x => x.CheckDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new PeriodicMedicalCheck { CheckDate = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PeriodicMedicalCheck model)
    {
        if (!ModelState.IsValid) return View(model);
        db.PeriodicMedicalChecks.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الفحص الدوري";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var check = await db.PeriodicMedicalChecks.FirstOrDefaultAsync(x => x.Id == id);
        if (check is null) return NotFound();
        return View(check);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var check = await db.PeriodicMedicalChecks.FindAsync(id);
        if (check is null) return NotFound();
        return View(check);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PeriodicMedicalCheck model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var check = await db.PeriodicMedicalChecks.FindAsync(id);
        if (check is null) return NotFound();

        check.EmployeeId = model.EmployeeId;
        check.CheckDate = model.CheckDate;
        check.Summary = model.Summary;
        check.Vitals = model.Vitals;
        check.NextDue = model.NextDue;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الفحص";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var check = await db.PeriodicMedicalChecks.FindAsync(id);
        if (check is null) return NotFound();
        db.PeriodicMedicalChecks.Remove(check);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الفحص";
        return RedirectToAction(nameof(Index));
    }
}
