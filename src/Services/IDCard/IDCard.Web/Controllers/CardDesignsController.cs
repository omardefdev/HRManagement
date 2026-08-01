using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IDCard.Domain.Entities;
using IDCard.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace IDCard.Web.Controllers;

public class CardDesignsController : Controller
{
    private readonly IDCardDbContext _db;

    public CardDesignsController(IDCardDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        return View(await _db.CardDesigns.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await _db.CardDesigns.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new CardDesign { ValidityDays = 365 });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CardDesign model)
    {
        if (!ModelState.IsValid) return View(model);
        _db.CardDesigns.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تمت الإضافة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.CardDesigns.FindAsync(id);
        if (e == null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CardDesign model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        var e = await _db.CardDesigns.FindAsync(id);
        if (e == null) return NotFound();
        e.Name = model.Name;
        e.Template = model.Template;
        e.ValidityDays = model.ValidityDays;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.CardDesigns.FindAsync(id);
        if (e == null) return NotFound();
        _db.CardDesigns.Remove(e);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
