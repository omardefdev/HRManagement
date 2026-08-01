using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class PublicJobPostingsController(HRCoreDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PublicJobPostings.AsNoTracking()
            .Include(p => p.Applications)
            .OrderByDescending(p => p.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());

    public IActionResult Create() => View(new PublicJobPostingCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PublicJobPostingCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        db.PublicJobPostings.Add(new PublicJobPosting
        {
            Title = vm.Title,
            Entity = vm.Entity,
            Location = vm.Location,
            Description = vm.Description,
            Requirements = vm.Requirements,
            ClosingDate = vm.ClosingDate,
            IsPublished = vm.IsPublished
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم نشر الإعلان الوظيفي العام";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var posting = await db.PublicJobPostings.FindAsync(id);
        if (posting == null) return NotFound();
        return View(new PublicJobPostingEditViewModel
        {
            Id = posting.Id,
            Title = posting.Title,
            Entity = posting.Entity,
            Location = posting.Location,
            Description = posting.Description,
            Requirements = posting.Requirements,
            ClosingDate = posting.ClosingDate,
            IsPublished = posting.IsPublished
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PublicJobPostingEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        var posting = await db.PublicJobPostings.FindAsync(id);
        if (posting == null) return NotFound();
        if (!ModelState.IsValid) return View(vm);
        posting.Title = vm.Title;
        posting.Entity = vm.Entity;
        posting.Location = vm.Location;
        posting.Description = vm.Description;
        posting.Requirements = vm.Requirements;
        posting.ClosingDate = vm.ClosingDate;
        posting.IsPublished = vm.IsPublished;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الإعلان";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var posting = await db.PublicJobPostings
            .Include(p => p.Applications)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        if (posting == null) return NotFound();
        return View(posting);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeApplicationStatus(Guid id, Guid postingId, CitizenApplicationStatus status)
    {
        var application = await db.CitizenApplications.FindAsync(id);
        if (application == null) return NotFound();
        application.Status = status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث حالة الطلب";
        return RedirectToAction(nameof(Details), new { id = postingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var posting = await db.PublicJobPostings.FindAsync(id);
        if (posting == null) return NotFound();
        db.PublicJobPostings.Remove(posting);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الإعلان";
        return RedirectToAction(nameof(Index));
    }
}
