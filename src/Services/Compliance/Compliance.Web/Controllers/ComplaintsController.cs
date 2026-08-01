using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;
using Compliance.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;

namespace Compliance.Web.Controllers;

public class ComplaintsController(ComplianceDbContext db, IPublishEndpoint _publish, IFileReferenceClient fileRefs) : Controller
{
    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    private bool IsInvestigator() =>
        User.IsInRole(HrManagementRoles.Admin) || User.IsInRole(HrManagementRoles.Compliance);

    public async Task<IActionResult> Index()
    {
        return View(await db.Complaints.AsNoTracking()
            .Where(c => !c.IsCancelled)
            .OrderByDescending(c => c.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    public IActionResult Create() => View(new Complaint { FiledAt = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Complaint model)
    {
        if (!ModelState.IsValid)
            return View(model);

        model.Status = ComplaintStatus.Open;
        model.Id = Guid.NewGuid();
        if (model.IsAnonymous && !model.ReporterProtectedId.HasValue)
            model.ReporterProtectedId = ParseUserId(User);
        db.Complaints.Add(model);
        await db.SaveChangesAsync();

        foreach (var step in InvestigationChecklistDefaults.CreateFor(model.Id))
            db.InvestigationChecklists.Add(step);
        await db.SaveChangesAsync();

        SensitiveDataAudit.Log(HttpContext, "Compliance", "ComplaintFiled", $"ComplaintId={model.Id};Anonymous={model.IsAnonymous}");
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var complaint = await db.Complaints
            .Include(c => c.FollowUps)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (complaint is null)
            return NotFound();

        var viewer = ParseUserId(User);
        ViewBag.CanSeeReporter = ComplaintProtectionRules.CanSeeReporter(complaint, viewer, IsInvestigator());
        ViewBag.Checklist = await db.InvestigationChecklists.AsNoTracking()
            .Where(c => c.ComplaintId == id).OrderBy(c => c.Step).ToListAsync();
        return View(complaint);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid id, string? assignedTo)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null)
            return NotFound();
        if (complaint.IsCancelled || complaint.Status != ComplaintStatus.Open)
            return RedirectToAction(nameof(Details), new { id });

        Guid? g = null;
        if (!string.IsNullOrWhiteSpace(assignedTo) && Guid.TryParse(assignedTo, out var parsed))
            g = parsed;
        if (g.HasValue && !ComplaintProtectionRules.CanAssignInvestigator(complaint, g.Value))
        {
            TempData["Error"] = "لا يمكن تعيين المبلغ عنه كمحقق (فصل واجبات)";
            return RedirectToAction(nameof(Details), new { id });
        }

        complaint.AssignedTo = g;
        var assigned = await db.InvestigationChecklists.FirstOrDefaultAsync(c =>
            c.ComplaintId == id && c.Step == InvestigationChecklistStep.Assigned);
        if (assigned is not null && !assigned.IsCompleted && g.HasValue)
            InvestigationChecklistDefaults.Mark(assigned, ParseUserId(User), $"assignee:{g}");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachEvidence(Guid id, Guid evidenceFileId)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null) return NotFound();
        if (complaint.IsCancelled)
        {
            TempData["Error"] = "الشكوى ملغاة";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await fileRefs.RegisterReferenceAsync(
                evidenceFileId,
                FileReferenceClientExtensions.BuildHolderKey(
                    HrFileServices.Compliance, nameof(Complaint), id, nameof(Complaint.EvidenceFileId)));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"مرفق مرفوض (FILE-002): {ex.Message}";
            return RedirectToAction(nameof(Details), new { id });
        }

        complaint.EvidenceFileId = evidenceFileId;
        var evidence = await db.InvestigationChecklists.FirstOrDefaultAsync(c =>
            c.ComplaintId == id && c.Step == InvestigationChecklistStep.EvidenceCollected);
        if (evidence is not null && !evidence.IsCompleted)
            InvestigationChecklistDefaults.Mark(evidence, ParseUserId(User), $"file:{evidenceFileId}");
        await db.SaveChangesAsync();
        TempData["Success"] = "تم ربط دليل FILE-002";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartInvestigation(Guid id)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null)
            return NotFound();
        if (complaint.IsCancelled)
            return RedirectToAction(nameof(Details), new { id });
        if (complaint.Status is not (ComplaintStatus.Open or ComplaintStatus.Resolved))
            return RedirectToAction(nameof(Details), new { id });

        complaint.Status = ComplaintStatus.Investigating;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid id, string resolution)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null)
            return NotFound();
        if (complaint.IsCancelled || complaint.Status != ComplaintStatus.Investigating)
            return RedirectToAction(nameof(Details), new { id });

        complaint.Resolution = string.IsNullOrWhiteSpace(resolution) ? "بدون نص" : resolution.Trim();
        complaint.Status = ComplaintStatus.Resolved;
        var decision = await db.InvestigationChecklists.FirstOrDefaultAsync(c =>
            c.ComplaintId == id && c.Step == InvestigationChecklistStep.Decision);
        if (decision is not null && !decision.IsCompleted)
            InvestigationChecklistDefaults.Mark(decision, ParseUserId(User), "resolution");
        await db.SaveChangesAsync();

        await _publish.Publish<IComplaintResolved>(new
        {
            ComplaintId = complaint.Id,
            Subject = complaint.Subject,
            Resolution = complaint.Resolution ?? string.Empty,
            ResolvedAt = DateTime.UtcNow,
            SourceSystem = "Compliance"
        });
        await db.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null)
            return NotFound();
        if (complaint.IsCancelled || complaint.Status != ComplaintStatus.Resolved)
            return RedirectToAction(nameof(Details), new { id });

        complaint.Status = ComplaintStatus.Closed;
        var closed = await db.InvestigationChecklists.FirstOrDefaultAsync(c =>
            c.ComplaintId == id && c.Step == InvestigationChecklistStep.Closed);
        if (closed is not null && !closed.IsCompleted)
            InvestigationChecklistDefaults.Mark(closed, ParseUserId(User), "closed");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null) return NotFound();
        if (complaint.IsCancelled)
        {
            TempData["Error"] = "ملغاة مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (complaint.Status == ComplaintStatus.Closed)
        {
            TempData["Error"] = "لا يمكن إلغاء شكوى مغلقة";
            return RedirectToAction(nameof(Details), new { id });
        }
        ComplaintProtectionRules.SoftCancel(complaint, reason);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الشكوى (soft-cancel)";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Appeal(Guid id, string? note)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null)
            return NotFound();
        if (complaint.IsCancelled || complaint.Status != ComplaintStatus.Resolved)
            return RedirectToAction(nameof(Details), new { id });

        complaint.Status = ComplaintStatus.Investigating;
        complaint.Resolution = null;
        db.ComplaintFollowUps.Add(new ComplaintFollowUp
        {
            Id = Guid.NewGuid(),
            ComplaintId = id,
            ActionTaken = string.IsNullOrWhiteSpace(note) ? "طلب استئناف" : note.Trim(),
            PerformedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFollowUp(Guid id, string note)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(note))
            return RedirectToAction(nameof(Details), new { id });

        db.ComplaintFollowUps.Add(new ComplaintFollowUp
        {
            Id = Guid.NewGuid(),
            ComplaintId = id,
            ActionTaken = note.Trim(),
            PerformedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refer(Guid id, string? referredTo, DateTime? referralDate, string? documents)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null) return NotFound();

        db.LegalReferrals.Add(new LegalReferral
        {
            ComplaintId = id,
            ReferredTo = string.IsNullOrWhiteSpace(referredTo) ? "الجهة القانونية" : referredTo.Trim(),
            ReferralDate = referralDate ?? DateTime.UtcNow,
            Documents = documents
        });
        db.ComplaintFollowUps.Add(new ComplaintFollowUp
        {
            Id = Guid.NewGuid(),
            ComplaintId = id,
            ActionTaken = $"إحالة قانونية إلى {referredTo ?? "الجهة القانونية"}",
            PerformedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الإحالة القانونية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRecommendation(Guid id, string? recommendedAction, DateTime? dueDate, Guid? responsibleUnitId)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null) return NotFound();

        db.ComplianceRecommendations.Add(new ComplianceRecommendation
        {
            ComplaintId = id,
            RecommendedAction = string.IsNullOrWhiteSpace(recommendedAction) ? "متابعة الشكوى" : recommendedAction.Trim(),
            DueDate = dueDate,
            ResponsibleUnitId = responsibleUnitId,
            Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Pending
        });
        db.ComplaintFollowUps.Add(new ComplaintFollowUp
        {
            Id = Guid.NewGuid(),
            ComplaintId = id,
            ActionTaken = "توصية رقابية مرتبطة بالشكوى",
            PerformedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة التوصية الرقابية";
        return RedirectToAction(nameof(Details), new { id });
    }
}
