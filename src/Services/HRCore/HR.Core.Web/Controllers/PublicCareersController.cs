using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[AllowAnonymous]
[Route("careers")]
public class PublicCareersController(HRCoreDbContext db) : Controller
{
    [HttpGet("")]
    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var postings = await db.PublicJobPostings
            .Where(p => p.IsPublished && (p.ClosingDate == null || p.ClosingDate >= today))
            .OrderByDescending(p => p.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();
        return View(postings);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var posting = await db.PublicJobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (posting is null || !posting.IsPublished) return NotFound();
        ViewBag.ApplyModel = new CitizenApplicationApplyViewModel { PublicJobPostingId = id };
        return View(posting);
    }

    [HttpPost("apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(CitizenApplicationApplyViewModel vm)
    {
        var posting = await db.PublicJobPostings.FirstOrDefaultAsync(p => p.Id == vm.PublicJobPostingId);
        if (posting is null || !posting.IsPublished)
        {
            TempData["Error"] = "الإعلان غير متاح للتقديم";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            ViewBag.ApplyModel = vm;
            return View("Details", posting);
        }

        var model = new CitizenApplication
        {
            PublicJobPostingId = vm.PublicJobPostingId,
            FullName = vm.FullName,
            NationalId = vm.NationalId,
            Phone = vm.Phone,
            Email = vm.Email,
            CoverNote = vm.CoverNote,
            ReferenceNumber = await GenerateUniqueReferenceAsync(),
            Status = CitizenApplicationStatus.Received,
            SubmittedAt = DateTime.UtcNow
        };
        db.CitizenApplications.Add(model);
        await db.SaveChangesAsync();

        TempData["Reference"] = model.ReferenceNumber;
        return RedirectToAction(nameof(Submitted));
    }

    [HttpGet("submitted")]
    public IActionResult Submitted()
    {
        ViewBag.Reference = TempData["Reference"];
        return View();
    }

    [HttpGet("track")]
    public IActionResult Track() => View();

    [HttpPost("track")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Track(string referenceNumber, string nationalId)
    {
        var application = await db.CitizenApplications
            .Include(a => a.PublicJobPosting)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ReferenceNumber == referenceNumber && a.NationalId == nationalId);

        if (application is null)
        {
            ViewBag.NotFound = true;
            return View();
        }

        return View("TrackResult", application);
    }

    private async Task<string> GenerateUniqueReferenceAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            var candidate = $"REF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            if (!await db.CitizenApplications.AnyAsync(a => a.ReferenceNumber == candidate))
                return candidate;
        }
        return $"REF-{Guid.NewGuid():N}";
    }
}
