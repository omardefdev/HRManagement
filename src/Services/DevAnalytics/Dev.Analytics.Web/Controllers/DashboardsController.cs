using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Web.Controllers;

public class DashboardsController(AnalyticsDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var dashboards = await db.Dashboards.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        var kpis = await db.KPIs.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        var jobs = await db.ETLJobs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        // HCM-032 (PLAN-32): «التوظيف» — requisitions closed in the last 12 months, small cells suppressed.
        var since = DateTime.UtcNow.AddMonths(-12);
        var facts = await db.RecruitmentRequisitionFacts.AsNoTracking()
            .Where(f => f.ClosedAt >= since)
            .OrderByDescending(f => f.ClosedAt)
            .Take(SearchFilterBase.MaxExportRows)
            .ToListAsync();
        var recruitment = Dev.Analytics.Domain.Services.RecruitmentFactRules.BuildCard(facts);
        return View((dashboards, kpis, jobs, recruitment));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.Dashboards.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new Dashboard());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Dashboard model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Id = Guid.NewGuid();
        db.Dashboards.Add(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.Dashboards.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Dashboard model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.Dashboards.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.Widgets = model.Widgets;
        e.OwnerId = model.OwnerId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
