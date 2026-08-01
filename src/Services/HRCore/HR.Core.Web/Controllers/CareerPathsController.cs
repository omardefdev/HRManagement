using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class CareerPathsController(HRCoreDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.CareerPaths.AsNoTracking().Include(p => p.Steps).OrderBy(p => p.Title).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new CareerPathCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CareerPathCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var model = new CareerPath { Title = vm.Title, Description = vm.Description };
        db.CareerPaths.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء المسار الوظيفي";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var path = await db.CareerPaths
            .Include(p => p.Steps)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        if (path == null) return NotFound();
        return View(path);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStep(Guid careerPathId, int order, string positionTitle, int minYears, string? requirements)
    {
        var path = await db.CareerPaths.FindAsync(careerPathId);
        if (path == null) return NotFound();
        if (string.IsNullOrWhiteSpace(positionTitle))
        {
            TempData["Error"] = "المسمى الوظيفي مطلوب";
            return RedirectToAction(nameof(Details), new { id = careerPathId });
        }

        db.CareerPathSteps.Add(new CareerPathStep
        {
            CareerPathId = careerPathId,
            Order = order,
            PositionTitle = positionTitle,
            MinYears = minYears < 0 ? 0 : minYears,
            Requirements = requirements
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة المرحلة";
        return RedirectToAction(nameof(Details), new { id = careerPathId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveStep(Guid id, Guid careerPathId)
    {
        var step = await db.CareerPathSteps.FindAsync(id);
        if (step != null)
        {
            db.CareerPathSteps.Remove(step);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم حذف المرحلة";
        }
        return RedirectToAction(nameof(Details), new { id = careerPathId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var path = await db.CareerPaths.FindAsync(id);
        if (path == null) return NotFound();
        db.CareerPaths.Remove(path);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف المسار";
        return RedirectToAction(nameof(Index));
    }
}
