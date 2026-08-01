using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Web.Controllers;

public class AssetsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await db.Assets.AsNoTracking()
            .Where(x => !x.IsCancelled)
            .OrderByDescending(x => x.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var asset = await db.Assets
            .Include(a => a.Assignments)
            .Include(a => a.MaintenanceRecords)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        return View(asset);
    }

    public IActionResult Create() => View(new Asset { Status = AssetStatus.InStorage });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Asset entity)
    {
        if (!ModelState.IsValid) return View(entity);
        db.Assets.Add(entity);
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة الأصل";
        return RedirectToAction(nameof(Details), new { id = entity.Id });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.Assets.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Asset entity)
    {
        if (id != entity.Id) return NotFound();
        if (!ModelState.IsValid) return View(entity);
        var e = await db.Assets.FindAsync(id);
        if (e is null) return NotFound();
        e.TagNumber = entity.TagNumber;
        e.Name = entity.Name;
        e.Category = entity.Category;
        e.Brand = entity.Brand;
        e.Model = entity.Model;
        e.PurchaseDate = entity.PurchaseDate;
        e.Value = entity.Value;
        e.Status = entity.Status;
        e.IsPrivileged = entity.IsPrivileged;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignToEmployee(Guid id, Guid employeeId, DateTime? dueBackDate, string? conditionOnAssign, string? evidenceRef)
    {
        var asset = await db.Assets.FindAsync(id);
        if (asset is null) return NotFound();

        var open = await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == id && a.ReturnedAt == null)
            .ToListAsync();
        if (!AssetAssignmentRules.CanAssign(asset, open))
        {
            TempData["Error"] = "لا يمكن التعيين: يوجد حائز مفتوح أو الأصل غير قابل للتسليم";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (AssetAssignmentRules.RequiresEvidence(asset) && string.IsNullOrWhiteSpace(evidenceRef))
        {
            TempData["Error"] = "أصل مميز يتطلب دليل تسليم (EvidenceRef)";
            return RedirectToAction(nameof(Details), new { id });
        }

        db.AssetAssignments.Add(new AssetAssignment
        {
            Id = Guid.NewGuid(),
            AssetId = id,
            EmployeeId = employeeId,
            AssignedAt = DateTime.UtcNow,
            DueBackDate = dueBackDate,
            ConditionOnAssign = conditionOnAssign,
            EvidenceRef = evidenceRef?.Trim()
        });
        asset.Status = AssetStatus.InUse;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التسليم للموظف";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnAssignment(Guid assignmentId, Guid assetId, string? evidenceRef)
    {
        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        if (assignment is null) return NotFound();
        var asset = await db.Assets.FindAsync(assetId);
        if (asset is not null && AssetAssignmentRules.RequiresEvidence(asset) && string.IsNullOrWhiteSpace(evidenceRef))
        {
            TempData["Error"] = "أصل مميز يتطلب دليل إعادة";
            return RedirectToAction(nameof(Details), new { id = assetId });
        }
        assignment.ReturnedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(evidenceRef))
            assignment.EvidenceRef = evidenceRef.Trim();
        if (asset is not null) asset.Status = AssetStatus.InStorage;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = assetId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkLost(Guid id, string? evidenceRef)
    {
        var asset = await db.Assets.FindAsync(id);
        if (asset is null) return NotFound();
        if (asset.Status is AssetStatus.Disposed or AssetStatus.Lost)
        {
            TempData["Error"] = "الأصل غير قابل لوسم الفقدان";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (asset.IsPrivileged && string.IsNullOrWhiteSpace(evidenceRef))
        {
            TempData["Error"] = "أصل مميز يتطلب دليل فقدان";
            return RedirectToAction(nameof(Details), new { id });
        }

        var open = await db.AssetAssignments
            .Where(a => a.AssetId == id && a.ReturnedAt == null)
            .ToListAsync();
        foreach (var a in open)
        {
            a.ReturnedAt = DateTime.UtcNow;
            a.EvidenceRef = evidenceRef ?? "lost";
        }
        asset.Status = AssetStatus.Lost;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم وسم الأصل مفقوداً";
        return RedirectToAction(nameof(Details), new { id });
    }
}
