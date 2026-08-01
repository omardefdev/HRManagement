using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Domain.Services;
using Records.Archive.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace Records.Archive.Web.Controllers;

public class ArchiveItemsController(ArchiveDbContext db) : Controller
{
    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index()
    {
        return View(await db.ArchiveItems.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var item = await db.ArchiveItems
            .Include(x => x.LoanRecords)
            .Include(x => x.DigitalFiles)
            .Include(x => x.IndexEntries)
            .Include(x => x.CustodyEvents)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        ViewBag.DisposalRecords = await db.DisposalRecords.AsNoTracking()
            .Where(d => d.ArchiveItemId == id).OrderByDescending(d => d.DisposalDate).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Checklist = await db.ArchiveChecklists.AsNoTracking()
            .Where(c => c.CorrelationId == id).OrderBy(c => c.Step).ToListAsync();
        return View(item);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Classifications = new SelectList(
            await db.DocumentClassifications.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        return View(new ArchiveItem());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ArchiveItem model)
    {
        DocumentClassification? classification = null;
        if (model.ClassificationId.HasValue)
            classification = await db.DocumentClassifications.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == model.ClassificationId);

        if (ArchiveRetentionRules.RequiresClassification(model) && !ArchiveRetentionRules.HasValidFilePlan(model, classification))
            ModelState.AddModelError(nameof(model.ClassificationId), "التصنيف ورمز/سنوات الاحتفاظ مطلوبان للمسار الرقمي (file plan / ret-archive-docs)");

        if (!ModelState.IsValid)
        {
            ViewBag.Classifications = new SelectList(
                await db.DocumentClassifications.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
            return View(model);
        }

        model.Id = Guid.NewGuid();
        if (model.RetentionPeriodYears is null && classification?.RetentionYears is > 0)
            model.RetentionPeriodYears = classification.RetentionYears;

        db.ArchiveItems.Add(model);
        foreach (var step in ArchiveChecklistDefaults.CreateFor(model.Id))
            db.ArchiveChecklists.Add(step);

        var actor = CurrentUserId(User);
        await db.SaveChangesAsync();

        var checklist = await db.ArchiveChecklists
            .Where(c => c.CorrelationId == model.Id).ToListAsync();
        var classifiedStep = checklist.FirstOrDefault(c => c.Step == ArchiveChecklistStep.Classified);
        if (classifiedStep is not null && model.ClassificationId.HasValue)
            ArchiveChecklistDefaults.Mark(classifiedStep, actor, $"class:{model.ClassificationId}");
        var ingested = checklist.FirstOrDefault(c => c.Step == ArchiveChecklistStep.Ingested);
        if (ingested is not null)
            ArchiveChecklistDefaults.Mark(ingested, actor, $"item:{model.Id}");

        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(model.Id, "Ingested", $"ref:{model.ReferenceNumber}", actor));
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        ViewBag.Classifications = new SelectList(
            await db.DocumentClassifications.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ArchiveItem model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Classifications = new SelectList(
                await db.DocumentClassifications.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
            return View(model);
        }
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        item.Title = model.Title;
        item.ReferenceNumber = model.ReferenceNumber;
        item.ClassificationId = model.ClassificationId;
        item.StorageLocation = model.StorageLocation;
        item.Format = model.Format;
        item.RetentionPeriodYears = model.RetentionPeriodYears;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartLoan(Guid id, string borrower, DateTime loanDate, DateTime dueDate)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        if (item.IsOnLegalHold)
        {
            TempData["Error"] = "العنصر تحت Legal Hold";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (string.IsNullOrWhiteSpace(borrower)) return RedirectToAction(nameof(Details), new { id });
        db.LoanRecords.Add(new LoanRecord
        {
            Id = Guid.NewGuid(),
            ArchiveItemId = id,
            Borrower = borrower.Trim(),
            LoanDate = loanDate,
            DueDate = dueDate,
            Status = LoanStatus.OnLoan
        });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnLoan(Guid loanId, Guid archiveItemId)
    {
        var loan = await db.LoanRecords.FindAsync(loanId);
        if (loan is null) return NotFound();
        loan.ReturnedDate = DateTime.UtcNow;
        loan.Status = LoanStatus.Returned;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = archiveItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceLegalHold(Guid id, string? reason)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        if (!ArchiveRetentionRules.CanPlaceHold(item))
        {
            TempData["Error"] = "لا يمكن وضع Legal Hold";
            return RedirectToAction(nameof(Details), new { id });
        }
        var actor = CurrentUserId(User);
        ArchiveRetentionRules.PlaceHold(item, reason);
        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(id, "Held", item.LegalHoldReason, actor));
        var held = await db.ArchiveChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == ArchiveChecklistStep.Held);
        if (held is not null)
            ArchiveChecklistDefaults.Mark(held, actor, "legal-hold");
        await db.SaveChangesAsync();
        TempData["Success"] = "تم وضع Legal Hold";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseLegalHold(Guid id)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        if (!ArchiveRetentionRules.CanReleaseHold(item))
        {
            TempData["Error"] = "لا يوجد Hold نشط";
            return RedirectToAction(nameof(Details), new { id });
        }
        var actor = CurrentUserId(User);
        ArchiveRetentionRules.ReleaseHold(item);
        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(id, "Released", "hold-released", actor));
        var released = await db.ArchiveChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == ArchiveChecklistStep.Released);
        if (released is not null)
            ArchiveChecklistDefaults.Mark(released, actor, "hold-released");
        await db.SaveChangesAsync();
        TempData["Success"] = "تم رفع Legal Hold";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestDisposal(Guid id, string? method)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        if (await db.DisposalRecords.AnyAsync(d => d.ArchiveItemId == id))
        {
            TempData["Error"] = "تم الإتلاف مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        var hasActiveLoans = await db.LoanRecords.AnyAsync(l => l.ArchiveItemId == id && l.ReturnedDate == null);
        if (!ArchiveRetentionRules.CanRequestDispose(item, hasActiveLoans))
        {
            TempData["Error"] = item.IsOnLegalHold
                ? "مرفوض بسبب Legal Hold"
                : hasActiveLoans
                    ? "لا يمكن الإتلاف — إعارة نشطة"
                    : "لا يمكن طلب الإتلاف — تحقق من الاحتفاظ/الطلب السابق";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actor = CurrentUserId(User) ?? Guid.Empty;
        item.DisposeRequestedBy = actor == Guid.Empty ? null : actor;
        item.DisposeRequestedAt = DateTime.UtcNow;
        item.IsDisposeCancelled = false;
        item.DisposeCancelReason = method;

        var step = await db.ArchiveChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == ArchiveChecklistStep.DisposeRequested);
        if (step is not null)
            ArchiveChecklistDefaults.Mark(step, actor == Guid.Empty ? null : actor, "dispose-request");
        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(id, "DisposeRequested", method, actor == Guid.Empty ? null : actor));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم طلب الإتلاف — بانتظار موافقة SoD";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDisposal(Guid id, DateTime? disposalDate, string? method, Guid? approvalEvidenceFileId)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        var approver = CurrentUserId(User) ?? Guid.Empty;
        if (!ArchiveRetentionRules.CanApproveDispose(item, item.DisposeRequestedBy, approver))
        {
            TempData["Error"] = "الموافقة مرفوضة (SoD أو حالة الطلب)";
            return RedirectToAction(nameof(Details), new { id });
        }

        var hasActiveLoans = await db.LoanRecords.AnyAsync(l => l.ArchiveItemId == id && l.ReturnedDate == null);
        if (hasActiveLoans || item.IsOnLegalHold)
        {
            TempData["Error"] = "لا يمكن الاعتماد تحت Hold أو إعارة نشطة";
            return RedirectToAction(nameof(Details), new { id });
        }

        item.DisposeApprovedBy = approver;
        item.DisposeApprovedAt = DateTime.UtcNow;

        db.DisposalRecords.Add(new DisposalRecord
        {
            Id = Guid.NewGuid(),
            ArchiveItemId = id,
            DisposalDate = disposalDate ?? DateTime.UtcNow,
            Method = string.IsNullOrWhiteSpace(method) ? "إتلاف معتمد" : method.Trim(),
            ApprovedBy = approver,
            ApprovalEvidenceFileId = approvalEvidenceFileId
        });

        var approved = await db.ArchiveChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == ArchiveChecklistStep.DisposeApproved);
        if (approved is not null)
            ArchiveChecklistDefaults.Mark(approved, approver, $"approver:{approver}");
        var disposed = await db.ArchiveChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == ArchiveChecklistStep.Disposed);
        if (disposed is not null)
            ArchiveChecklistDefaults.Mark(disposed, approver, "disposed");

        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(id, "Disposed", $"approved-by:{approver}", approver));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد وتنفيذ الإتلاف";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelDisposalRequest(Guid id, string? reason)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        if (item.DisposeApprovedAt.HasValue)
        {
            TempData["Error"] = "لا يمكن إلغاء إتلاف معتمد";
            return RedirectToAction(nameof(Details), new { id });
        }
        ArchiveRetentionRules.SoftCancelDispose(item, reason);
        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(id, "DisposeCancelled", reason, CurrentUserId(User)));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء طلب الإتلاف (soft-cancel)";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Legacy single-step disposal — redirects to request/approve flow.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordDisposal(Guid id, DateTime disposalDate, string? method) =>
        RequestDisposal(id, method);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRetention(Guid id, int? retentionPeriodYears)
    {
        var item = await db.ArchiveItems.FindAsync(id);
        if (item is null) return NotFound();
        item.RetentionPeriodYears = retentionPeriodYears;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttestCompleteness(Guid id)
    {
        var item = await db.ArchiveItems
            .Include(a => a.DigitalFiles)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (item is null) return NotFound();
        item.CompletenessAttestation = ArchiveRetentionRules.CompletenessFingerprint(item.DigitalFiles);
        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(id, "CompletenessAttested", item.CompletenessAttestation, CurrentUserId(User)));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إثبات الاكتمال";
        return RedirectToAction(nameof(Details), new { id });
    }
}
