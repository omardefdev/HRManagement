using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infrastructure.Domain.Services;
using IT.Infra.Infrastructure.Data;
using HRManagement.SharedKernel.Interfaces;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IT.Infra.Web.Controllers;

public class SupportTicketsController(ITInfraDbContext db, IDirectoryEntrySyncService directorySync) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await db.ITSupportTickets.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    /// <summary>FUN-005: visible failed provisioning queue.</summary>
    public async Task<IActionResult> FailedProvisioning()
    {
        var list = await db.ITSupportTickets.AsNoTracking()
            .Where(t => t.ProvisioningStatus == ProvisioningStatus.Failed)
            .OrderByDescending(x => x.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        return View("Index", list);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        return View(t);
    }

    public IActionResult Create() => View(new ITSupportTicket());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ITSupportTicket model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Id = Guid.NewGuid();
        model.Status = TicketStatus.Open;
        model.ProvisioningStatus = ProvisioningStatus.Pending;
        db.ITSupportTickets.Add(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        return View(t);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ITSupportTicket model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        t.IssueSummary = model.IssueSummary;
        t.Description = model.Description;
        t.ReportedBy = model.ReportedBy;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid id, string? assignedTo)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        if (!TicketWorkflowRules.CanAssign(t.Status)) return BadRequest();
        if (!string.IsNullOrWhiteSpace(assignedTo) && Guid.TryParse(assignedTo, out var g))
        {
            t.AssignedTo = g;
            t.Status = TicketStatus.Assigned;
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid id)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        if (!TicketWorkflowRules.CanResolve(t.Status)) return BadRequest();
        t.Status = TicketStatus.Resolved;
        t.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        if (!TicketWorkflowRules.CanClose(t.Status)) return BadRequest();
        t.Status = TicketStatus.Closed;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(Guid id)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        if (!TicketWorkflowRules.CanReopen(t.Status)) return BadRequest();
        t.Status = TicketStatus.Open;
        t.ResolvedAt = null;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>FUN-005: retry directory provisioning for a Failed ticket.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryProvision(Guid id, CancellationToken cancellationToken)
    {
        var t = await db.ITSupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        if (t.ProvisioningStatus != ProvisioningStatus.Failed)
        {
            TempData["Error"] = "إعادة المحاولة متاحة فقط للتذاكر Failed";
            return RedirectToAction(nameof(Details), new { id });
        }

        var employeeId = t.ReportedBy ?? Guid.Empty;
        if (employeeId == Guid.Empty)
        {
            TempData["Error"] = "لا يوجد EmployeeId على التذكرة";
            return RedirectToAction(nameof(Details), new { id });
        }

        t.ProvisioningStatus = ProvisioningStatus.Pending;
        t.ProvisioningError = null;
        await db.SaveChangesAsync(cancellationToken);

        var ok = await directorySync.SyncEmployeeCreatedAsync(
            employeeId,
            t.IssueSummary,
            nationalId: null,
            primaryOrgUnitId: null,
            cancellationToken);

        t.ProvisionedAt = DateTime.UtcNow;
        if (ok)
        {
            t.ProvisioningStatus = ProvisioningStatus.Succeeded;
            t.ProvisioningError = null;
            TempData["Success"] = "نجحت إعادة التزويد — لمزامنة Keycloak استخدم Auth.Directory SyncFromKeycloak عند الحاجة";
        }
        else
        {
            t.ProvisioningStatus = ProvisioningStatus.Failed;
            t.ProvisioningError = "Directory sync retry failed";
            TempData["Error"] = "فشل إعادة التزويد";
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }
}
