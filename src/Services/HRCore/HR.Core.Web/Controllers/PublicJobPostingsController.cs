using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class PublicJobPostingsController(HRCoreDbContext db) : Controller
{
    public async Task<IActionResult> Index(bool? isPublished, string? entity, string? q, DateOnly? closingFrom, DateOnly? closingTo)
    {
        var query = db.PublicJobPostings.AsNoTracking()
            .Include(p => p.Applications)
            .Include(p => p.Requisition)
            .AsQueryable();
        if (isPublished is bool pub)
            query = query.Where(p => p.IsPublished == pub);
        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(p => p.Entity != null && p.Entity.Contains(entity.Trim()));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                p.Title.Contains(term) ||
                (p.Description != null && p.Description.Contains(term)) ||
                (p.Location != null && p.Location.Contains(term)));
        }
        if (closingFrom is DateOnly cf)
            query = query.Where(p => p.ClosingDate >= cf);
        if (closingTo is DateOnly ct)
            query = query.Where(p => p.ClosingDate <= ct);

        var items = await query.OrderByDescending(p => p.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(new PublicJobPostingsIndexViewModel { Items = items, IsPublished = isPublished, Entity = entity, Q = q, ClosingFrom = closingFrom, ClosingTo = closingTo });
    }

    public IActionResult Create()
    {
        TempData["Info"] = "يُنشأ الإعلان العام من طلب شاغر معتمد عبر «نشر الإعلان» في تفاصيل الطلب.";
        return RedirectToAction("Index", "RecruitmentRequisitions");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(PublicJobPostingCreateViewModel vm)
    {
        TempData["Info"] = "يُنشأ الإعلان العام من طلب شاغر معتمد — لا إنشاء يدوي.";
        return RedirectToAction("Index", "RecruitmentRequisitions");
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var posting = await db.PublicJobPostings
            .Include(p => p.Requisition)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (posting == null) return NotFound();
        if (posting.RequisitionId.HasValue)
        {
            // HCM-026 (E3): the closing date of a requisition-linked posting is managed on the requisition.
            TempData["Error"] = "إعلان مشتق من طلب شاغر — يُدار تاريخ إغلاق التقديم وبيانات الإعلان من صفحة طلب الشاغر (تمديد تاريخ الإغلاق).";
            return RedirectToAction(nameof(Details), new { id });
        }

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
        if (posting.RequisitionId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل إعلان مشتق من طلب شاغر — تاريخ إغلاق التقديم يُمدَّد من صفحة طلب الشاغر.";
            return RedirectToAction(nameof(Details), new { id });
        }

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
                .ThenInclude(a => a.Requisition)
            .Include(p => p.Applications)
                .ThenInclude(a => a.PromotedRecruitmentApplication)
            .Include(p => p.Requisition)
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
        if (application is null) return NotFound();

        // HCM-030 (A4): a requisition-linked application is screened on its recruitment application only.
        if (application.RequisitionId.HasValue)
        {
            TempData["Error"] = UnifiedIntakeService.ManagedByUnifiedPathMessage;
            return RedirectToAction(nameof(Details), new { id = postingId });
        }

        var error = CitizenSelectionWorkflowRules.ValidateTransition(application, status);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id = postingId });
        }

        application.Status = status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث حالة الطلب";
        return RedirectToAction(nameof(Details), new { id = postingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclareCitizenCoi(Guid id, Guid postingId)
    {
        var application = await db.CitizenApplications.FindAsync(id);
        if (application is null) return NotFound();

        // HCM-030 (A4): a requisition-linked application is screened on its recruitment application only.
        if (application.RequisitionId.HasValue)
        {
            TempData["Error"] = UnifiedIntakeService.ManagedByUnifiedPathMessage;
            return RedirectToAction(nameof(Details), new { id = postingId });
        }

        application.ConflictOfInterestDeclared = true;
        application.ConflictOfInterestDeclaredAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل إقرار عدم تعارض المصالح";
        return RedirectToAction(nameof(Details), new { id = postingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var posting = await db.PublicJobPostings.FindAsync(id);
        if (posting == null) return NotFound();
        if (posting.RequisitionId.HasValue)
        {
            TempData["Error"] = "لا يمكن حذف إعلان مشتق — أغلق طلب الشاغر بدلاً من ذلك.";
            return RedirectToAction(nameof(Details), new { id });
        }

        db.PublicJobPostings.Remove(posting);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الإعلان";
        return RedirectToAction(nameof(Index));
    }
}
