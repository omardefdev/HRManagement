using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;

namespace Admin.Logistics.Web.Controllers;

public class SupplyOrdersController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.SupplyOrders.AsNoTracking().OrderByDescending(x => x.OrderedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.SupplyOrders.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new SupplyOrder { Status = SupplyOrderStatus.Pending, OrderedAt = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplyOrder model)
    {
        if (!ModelState.IsValid) return View(model);
        // Requester comes from the session so the order cannot be attributed to someone else.
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawUserId, out var requesterId))
            model.RequestedBy = requesterId;
        db.SupplyOrders.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل طلب التجهيز";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SupplyOrder model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        // RequestedBy stays as originally recorded; it is audit data, not an editable field.
        e.OrgUnitId = model.OrgUnitId;
        e.Items = model.Items;
        e.Status = model.Status;
        e.OrderedAt = model.OrderedAt;
        e.FulfilledAt = model.FulfilledAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanApprove(e.Status)) return BadRequest();
        e.Status = SupplyOrderStatus.Approved;
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت الموافقة على الطلب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fulfill(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanFulfill(e.Status)) return BadRequest();
        e.Status = SupplyOrderStatus.Fulfilled;
        e.FulfilledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تنفيذ الطلب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanCancel(e.Status)) return BadRequest();
        e.Status = SupplyOrderStatus.Cancelled;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الطلب";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class VendorsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.Vendors.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.Vendors.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new Vendor());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Vendor model)
    {
        if (!ModelState.IsValid) return View(model);
        db.Vendors.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة المورد";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.Vendors.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Vendor model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.Vendors.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.ContactInfo = model.ContactInfo;
        e.Country = model.Country;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.Vendors.FindAsync(id);
        if (e is null) return NotFound();
        db.Vendors.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}

public class AssetAssignmentsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.AssetAssignments.AsNoTracking().Include(a => a.Asset).OrderByDescending(x => x.AssignedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.AssetAssignments.Include(a => a.Asset).FirstOrDefaultAsync(a => a.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().OrderBy(a => a.TagNumber).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
        return View(new AssetAssignment { AssignedAt = DateTime.UtcNow });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetAssignment model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }

        var asset = await db.Assets.FindAsync(model.AssetId);
        if (asset is null)
        {
            ModelState.AddModelError(nameof(model.AssetId), "الأصل غير موجود");
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }

        var open = await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == model.AssetId && a.ReturnedAt == null)
            .ToListAsync();
        if (!AssetAssignmentRules.CanAssign(asset, open))
        {
            ModelState.AddModelError(string.Empty, "لا يمكن التعيين: يوجد حائز مفتوح أو الأصل غير قابل للتسليم");
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }
        if (AssetAssignmentRules.RequiresEvidence(asset) && string.IsNullOrWhiteSpace(model.EvidenceRef))
        {
            ModelState.AddModelError(nameof(model.EvidenceRef), "أصل مميز يتطلب دليل تسليم");
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }

        db.AssetAssignments.Add(model);
        asset.Status = AssetStatus.InUse;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل العهدة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.AssetAssignments.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AssetAssignment model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }
        var e = await db.AssetAssignments.FindAsync(id);
        if (e is null) return NotFound();
        e.AssetId = model.AssetId;
        e.EmployeeId = model.EmployeeId;
        e.AssignedAt = model.AssignedAt;
        e.DueBackDate = model.DueBackDate;
        e.ConditionOnAssign = model.ConditionOnAssign;
        e.ReturnedAt = model.ReturnedAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class MaintenanceRecordsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.MaintenanceRecords.AsNoTracking().Include(m => m.Asset).OrderByDescending(x => x.PerformedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.MaintenanceRecords.Include(m => m.Asset).FirstOrDefaultAsync(m => m.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().OrderBy(a => a.TagNumber).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
        return View(new MaintenanceRecord { PerformedAt = DateTime.UtcNow });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MaintenanceRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }
        db.MaintenanceRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الصيانة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.MaintenanceRecords.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, MaintenanceRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Assets = new SelectList(await db.Assets.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TagNumber");
            return View(model);
        }
        var e = await db.MaintenanceRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.AssetId = model.AssetId;
        e.MaintenanceType = model.MaintenanceType;
        e.PerformedAt = model.PerformedAt;
        e.PerformedBy = model.PerformedBy;
        e.Cost = model.Cost;
        e.Notes = model.Notes;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class ReceiptRecordsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ReceiptRecords.AsNoTracking().Include(r => r.SupplyOrder).OrderByDescending(x => x.ReceivedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.ReceiptRecords.Include(r => r.SupplyOrder).FirstOrDefaultAsync(r => r.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Orders = new SelectList(await db.SupplyOrders.AsNoTracking().OrderByDescending(o => o.OrderedAt).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Items");
        return View(new ReceiptRecord { ReceivedAt = DateTime.UtcNow });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReceiptRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Orders = new SelectList(await db.SupplyOrders.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Items");
            return View(model);
        }
        db.ReceiptRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الاستلام";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.ReceiptRecords.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Orders = new SelectList(await db.SupplyOrders.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Items");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ReceiptRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Orders = new SelectList(await db.SupplyOrders.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Items");
            return View(model);
        }
        var e = await db.ReceiptRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.SupplyOrderId = model.SupplyOrderId;
        e.ReceivedBy = model.ReceivedBy;
        e.ReceivedAt = model.ReceivedAt;
        e.ItemsReceived = model.ItemsReceived;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
