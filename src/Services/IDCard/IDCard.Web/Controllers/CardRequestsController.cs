using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IDCard.Web.Controllers;

public class CardRequestsController(IDCardDbContext db, IPolicyEnforceClient policyClient) : Controller
{

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public async Task<IActionResult> Index()
    {
        return View(await db.CardRequests.AsNoTracking()
            .Where(x => !x.IsCancelled)
            .OrderByDescending(x => x.RequestDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.CardRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        ViewBag.Checklist = await db.CardChecklists.AsNoTracking()
            .Where(c => c.CorrelationId == id).OrderBy(c => c.Step).ToListAsync();
        return View(e);
    }

    public IActionResult Create() => View(new CardRequest
    {
        RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = CardRequestStatus.Pending,
        Purpose = CardRequestPurpose.New
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CardRequest model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Status = CardRequestStatus.Pending;
        // Requester identity comes from the session, never from the posted form.
        model.RequestedBy = ParseUserId(User);
        db.CardRequests.Add(model);
        await db.SaveChangesAsync();

        foreach (var step in CardChecklistDefaults.CreateFor(model.Id))
            db.CardChecklists.Add(step);
        await db.SaveChangesAsync();
        var requested = await db.CardChecklists.FirstAsync(c =>
            c.CorrelationId == model.Id && c.Step == CardChecklistStep.Requested);
        CardChecklistDefaults.Mark(requested, null, $"request:{model.Id}");
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل الطلب";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e == null) return NotFound();
        if (e.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CardRequest model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        var e = await db.CardRequests.FindAsync(id);
        if (e == null) return NotFound();
        if (e.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        e.EmployeeId = model.EmployeeId;
        // RequestedBy is deliberately not copied: the original requester is part of the audit
        // trail and of the four-eyes comparison at approval time.
        e.RequestDate = model.RequestDate;
        e.Purpose = model.Purpose;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e == null) return NotFound();
        if (e.IsCancelled)
        {
            TempData["Error"] = "ملغى مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }
        CardRequestRules.SoftCancel(e, reason);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الطلب (soft-cancel)";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessRequest(Guid id, Guid? cardDesignId)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e is null) return NotFound();
        if (e.IsCancelled) return BadRequest();
        if (!CardIssuanceWorkflow.CanProcess(e.Status)) return BadRequest();

        var policyResult = await policyClient.EnforceAsync(
            PolicyDomainFactsBuilders.ForIDCardRequest(id, e.TenantId, e.EmployeeId, e.Purpose.ToString(), "Process"),
            HttpContext.RequestAborted);
        if (!policyResult.Allowed)
        {
            TempData["Error"] = policyResult.Decision.DenyReason ?? "مرفوض بسياسة البطاقة";
            return RedirectToAction(nameof(Details), new { id });
        }

        var designId = cardDesignId
            ?? await db.CardDesigns.AsNoTracking().Select(d => (Guid?)d.Id).FirstOrDefaultAsync();
        if (designId is null)
        {
            TempData["Error"] = "أضف تصميماً قبل المعالجة";
            return RedirectToAction(nameof(Details), new { id });
        }

        var issued = new IssuedCard
        {
            EmployeeId = e.EmployeeId,
            CardDesignId = designId.Value,
            IssuedAt = DateTime.UtcNow,
            Status = CardStatus.Active,
            SerialNumber = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            RevokeSlaHours = CardChecklistDefaults.DefaultRevokeSlaHours
        };
        db.IssuedCards.Add(issued);
        await db.SaveChangesAsync();

        e.Status = CardRequestStatus.Processed;
        e.IssuedCardId = issued.Id;

        var issuedStep = await db.CardChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == CardChecklistStep.Issued);
        if (issuedStep is not null && !issuedStep.IsCompleted)
            CardChecklistDefaults.Mark(issuedStep, null, $"card:{issued.Id}");

        await db.SaveChangesAsync();
        TempData["Success"] = "تمت معالجة الطلب وإصدار البطاقة";
        return RedirectToAction(nameof(Details), new { id });
    }
}
