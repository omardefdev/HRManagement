using HR.Training.Domain.Entities;
using HR.Training.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Training.Web.Controllers;

public class TrainingCoursesController(TrainingDbContext db) : Controller
{
    private async Task PopulateProviders() =>
        ViewBag.Providers = new SelectList(await db.TrainingProviders.AsNoTracking().OrderBy(p => p.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name");

    public async Task<IActionResult> Index() =>
        View(await db.TrainingCourses.AsNoTracking().Include(c => c.Provider).OrderBy(c => c.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        await PopulateProviders();
        return View(new TrainingCourse { DurationDays = 1 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TrainingCourse model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateProviders();
            return View(model);
        }
        db.TrainingCourses.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الدورة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var c = await db.TrainingCourses
            .Include(x => x.Provider)
            .Include(x => x.Enrollments)
            .Include(x => x.Nominations)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        return View(c);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var c = await db.TrainingCourses.FindAsync(id);
        if (c == null) return NotFound();
        await PopulateProviders();
        return View(c);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TrainingCourse model)
    {
        if (id != model.Id) return BadRequest();
        var c = await db.TrainingCourses.FindAsync(id);
        if (c == null) return NotFound();
        if (!ModelState.IsValid)
        {
            await PopulateProviders();
            return View(model);
        }
        c.Code = model.Code;
        c.TitleAr = model.TitleAr;
        c.TitleEn = model.TitleEn;
        c.Description = model.Description;
        c.DurationDays = model.DurationDays;
        c.ProviderId = model.ProviderId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الدورة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await db.TrainingCourses.FindAsync(id);
        if (c == null) return NotFound();
        db.TrainingCourses.Remove(c);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الدورة";
        return RedirectToAction(nameof(Index));
    }
}
