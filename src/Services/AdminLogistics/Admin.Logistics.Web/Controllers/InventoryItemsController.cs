using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Web.Controllers;

public class InventoryItemsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.InventoryItems.AsNoTracking().OrderBy(x => x.Sku).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.InventoryItems.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new InventoryItem { Quantity = 0 });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InventoryItem model)
    {
        if (!ModelState.IsValid) return View(model);
        db.InventoryItems.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة عنصر المخزون";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.InventoryItems.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, InventoryItem model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.InventoryItems.FindAsync(id);
        if (e is null) return NotFound();

        // DATA-001: when decreasing quantity, use atomic check to prevent negative stock under concurrency.
        if (model.Quantity < e.Quantity)
        {
            var delta = e.Quantity - model.Quantity;
            var rows = await db.InventoryItems
                .Where(x => x.Id == id && x.Quantity >= delta)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Quantity, model.Quantity)
                    .SetProperty(x => x.Sku, model.Sku)
                    .SetProperty(x => x.Name, model.Name)
                    .SetProperty(x => x.Unit, model.Unit)
                    .SetProperty(x => x.Location, model.Location)
                    .SetProperty(x => x.ReorderThreshold, model.ReorderThreshold)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
            if (rows == 0)
            {
                ModelState.AddModelError(string.Empty, ConcurrencyConflictMessages.InsufficientBalance);
                return View(model);
            }
        }
        else
        {
            e.Sku = model.Sku;
            e.Name = model.Name;
            e.Quantity = model.Quantity;
            e.Unit = model.Unit;
            e.Location = model.Location;
            e.ReorderThreshold = model.ReorderThreshold;
            await db.SaveChangesAsync();
        }

        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.InventoryItems.FindAsync(id);
        if (e is null) return NotFound();
        db.InventoryItems.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
