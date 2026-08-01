using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Enums;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Web.Controllers;

public class ReportsController(AnalyticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.Reports.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.Reports.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new Report { ReportType = ReportType.AdHoc });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Report model)
    {
        if (!ModelState.IsValid) return View(model);
        db.Reports.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء التقرير";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.Reports.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Report model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.Reports.FindAsync(id);
        if (e is null) return NotFound();
        e.Title = model.Title;
        e.ReportType = model.ReportType;
        e.Parameters = model.Parameters;
        e.GeneratedAt = model.GeneratedAt;
        e.GeneratedBy = model.GeneratedBy;
        e.Url = model.Url;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
