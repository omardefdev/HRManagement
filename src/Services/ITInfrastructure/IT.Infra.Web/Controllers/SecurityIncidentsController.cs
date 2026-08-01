using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infra.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IT.Infra.Web.Controllers;

public class SecurityIncidentsController(ITInfraDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.SecurityIncidents.AsNoTracking().OrderByDescending(x => x.ReportedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.SecurityIncidents.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new SecurityIncident
    {
        ReportedAt = DateTime.UtcNow,
        Severity = Severity.Medium,
        Status = TicketStatus.Open
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SecurityIncident model)
    {
        if (!ModelState.IsValid) return View(model);
        db.SecurityIncidents.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الحادثة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.SecurityIncidents.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SecurityIncident model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.SecurityIncidents.FindAsync(id);
        if (e is null) return NotFound();
        e.Title = model.Title;
        e.Description = model.Description;
        e.Severity = model.Severity;
        e.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Handle(Guid id, Guid? handledBy)
    {
        var e = await db.SecurityIncidents.FindAsync(id);
        if (e is null) return NotFound();
        e.Status = TicketStatus.Assigned;
        e.HandledBy = handledBy;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تعيين معالج";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.SecurityIncidents.FindAsync(id);
        if (e is null) return NotFound();
        db.SecurityIncidents.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
