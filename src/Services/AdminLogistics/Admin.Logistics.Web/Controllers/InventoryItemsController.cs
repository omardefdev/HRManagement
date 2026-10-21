using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Helpers;
using Admin.Logistics.Web.Services;
using Admin.Logistics.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Mvc;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Web.Controllers;

public class InventoryItemsController(AdminLogisticsDbContext db, ICodeGenerationClient codeClient) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        string? location,
        string? unit,
        int? quantityMin,
        int? quantityMax,
        bool lowStockOnly = false,
        bool outOfStockOnly = false,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var filter = new InventoryItemIndexFilter
        {
            Q = q,
            Location = location,
            Unit = unit,
            QuantityMin = quantityMin,
            QuantityMax = quantityMax,
            LowStockOnly = lowStockOnly,
            OutOfStockOnly = outOfStockOnly,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        var locationOptions = await db.InventoryItems.AsNoTracking()
            .Where(x => x.Location != null && x.Location != "")
            .Select(x => x.Location!)
            .Distinct()
            .OrderBy(l => l)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();
        ViewBag.Locations = new SelectList(locationOptions, location);

        var unitOptions = await db.InventoryItems.AsNoTracking()
            .Where(x => x.Unit != null && x.Unit != "")
            .Select(x => x.Unit!)
            .Distinct()
            .OrderBy(u => u)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();
        ViewBag.Units = new SelectList(unitOptions, unit);

        var paged = await filter.Apply(db.InventoryItems.AsNoTracking())
            .OrderBy(x => x.Sku)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        return View(new InventoryItemIndexViewModel
        {
            Items = paged,
            Q = q,
            Location = location,
            Unit = unit,
            QuantityMin = quantityMin,
            QuantityMax = quantityMax,
            LowStockOnly = lowStockOnly,
            OutOfStockOnly = outOfStockOnly
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.InventoryItems.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new InventoryItemFormViewModel { Quantity = 0 });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InventoryItemFormViewModel model)
    {
        var (skuOk, sku, skuError) = await CodeGenerationGateway.GenerateOrErrorAsync(
            codeClient,
            CodeGenerationRequests.InventorySku(),
            HttpContext.RequestAborted);
        if (!skuOk)
        {
            ModelState.AddModelError(string.Empty, skuError!);
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }

        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }

        await ValidateUniqueSkuAsync(sku, excludeId: null);
        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }

        var entity = new InventoryItem
        {
            Sku = sku!,
            Name = model.Name,
            Quantity = model.Quantity,
            Unit = model.Unit,
            Location = model.Location,
            ReorderThreshold = model.ReorderThreshold
        };

        db.InventoryItems.Add(entity);
        var outcome = await HrSaveChanges.TryAsync(db, ModelState, HttpContext.RequestAborted);
        if (outcome != SaveChangesOutcome.Success)
        {
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }

        TempData["Success"] = "تمت إضافة عنصر المخزون";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.InventoryItems.FindAsync(id);
        if (e is null) return NotFound();
        return View(new InventoryItemFormViewModel
        {
            Id = e.Id,
            Sku = e.Sku,
            Name = e.Name,
            Quantity = e.Quantity,
            Unit = e.Unit,
            Location = e.Location,
            ReorderThreshold = e.ReorderThreshold
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, InventoryItemFormViewModel model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }
        var e = await db.InventoryItems.FindAsync(id);
        if (e is null) return NotFound();

        // Sku immutable after issuance (COD-001)

        // DATA-001: when decreasing quantity, use atomic check to prevent negative stock under concurrency.
        if (model.Quantity < e.Quantity)
        {
            var delta = e.Quantity - model.Quantity;
            var rows = await db.InventoryItems
                .Where(x => x.Id == id && x.Quantity >= delta)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Quantity, model.Quantity)
                    .SetProperty(x => x.Name, model.Name)
                    .SetProperty(x => x.Unit, model.Unit)
                    .SetProperty(x => x.Location, model.Location)
                    .SetProperty(x => x.ReorderThreshold, model.ReorderThreshold)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
            if (rows == 0)
            {
                ModelState.AddModelError(string.Empty, ConcurrencyConflictMessages.InsufficientBalance);
                ViewData["ShowValidationDialog"] = true;
                return View(model);
            }
        }
        else
        {
            e.Name = model.Name;
            e.Quantity = model.Quantity;
            e.Unit = model.Unit;
            e.Location = model.Location;
            e.ReorderThreshold = model.ReorderThreshold;
            var outcome = await HrSaveChanges.TryAsync(db, ModelState, HttpContext.RequestAborted);
            if (outcome != SaveChangesOutcome.Success)
            {
                ViewData["ShowValidationDialog"] = true;
                return View(model);
            }
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
        var outcome = await HrSaveChanges.TryAsync(db, ModelState, HttpContext.RequestAborted);
        if (outcome != SaveChangesOutcome.Success)
        {
            AdminLogisticsFlash.Error(TempData, string.Join(' ', ModelState.Values.SelectMany(v => v.Errors).Select(err => err.ErrorMessage)));
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateUniqueSkuAsync(string? sku, Guid? excludeId)
    {
        var normalized = sku?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var query = db.InventoryItems.AsNoTracking().Where(x => x.Sku == normalized);
        if (excludeId.HasValue)
            query = query.Where(x => x.Id != excludeId.Value);

        if (await query.AnyAsync(HttpContext.RequestAborted))
            ModelState.AddModelError(nameof(InventoryItem.Sku), UniqueConstraintMessageRegistry.MessageForSku);
    }
}
