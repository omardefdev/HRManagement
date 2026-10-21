using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Web.Controllers;

public class ContractTypesController : Controller
{
    private readonly ContractsDbContext _db;

    public ContractTypesController(ContractsDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? q)
    {
        var query = _db.ContractTypes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t => t.Code.Contains(term) || t.NameAr.Contains(term) || (t.NameEn != null && t.NameEn.Contains(term)));
        }

        var items = await query.OrderBy(t => t.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Q = q;
        return View(items);
    }

    public IActionResult Create() => View(new ContractTypeCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractTypeCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        if (await _db.ContractTypes.AnyAsync(t => t.Code == vm.Code))
        {
            ModelState.AddModelError(nameof(vm.Code), "الرمز مستخدم مسبقاً");
            return View(vm);
        }

        _db.ContractTypes.Add(new ContractType
        {
            Code = vm.Code.Trim(),
            NameAr = vm.NameAr.Trim(),
            NameEn = string.IsNullOrWhiteSpace(vm.NameEn) ? null : vm.NameEn.Trim(),
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة نوع العقد";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var t = await _db.ContractTypes.FindAsync(id);
        if (t == null) return NotFound();

        return View(new ContractTypeEditViewModel
        {
            Id = t.Id,
            Code = t.Code,
            NameAr = t.NameAr,
            NameEn = t.NameEn,
            Description = t.Description
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ContractTypeEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) return View(vm);

        if (await _db.ContractTypes.AnyAsync(t => t.Code == vm.Code && t.Id != id))
        {
            ModelState.AddModelError(nameof(vm.Code), "الرمز مستخدم مسبقاً");
            return View(vm);
        }

        var t = await _db.ContractTypes.FindAsync(id);
        if (t == null) return NotFound();

        t.Code = vm.Code.Trim();
        t.NameAr = vm.NameAr.Trim();
        t.NameEn = string.IsNullOrWhiteSpace(vm.NameEn) ? null : vm.NameEn.Trim();
        t.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث نوع العقد";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var t = await _db.ContractTypes.FindAsync(id);
        if (t == null) return NotFound();

        if (await _db.Contracts.AnyAsync(c => c.ContractTypeId == id))
        {
            TempData["Error"] = "لا يمكن الحذف: توجد عقود مرتبطة بهذا النوع";
            return RedirectToAction(nameof(Index));
        }

        _db.ContractTypes.Remove(t);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
