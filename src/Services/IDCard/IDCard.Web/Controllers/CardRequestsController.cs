using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;

namespace IDCard.Web.Controllers;

public class CardRequestsController : Controller
{
    private readonly IDCardDbContext _db;

    public CardRequestsController(IDCardDbContext db) => _db = db;

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _db.CardRequests.AsNoTracking()
            .Where(x => !x.IsCancelled)
            .OrderByDescending(x => x.RequestDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await _db.CardRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        ViewBag.Checklist = await _db.CardChecklists.AsNoTracking()
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
        _db.CardRequests.Add(model);
        await _db.SaveChangesAsync();

        foreach (var step in CardChecklistDefaults.CreateFor(model.Id))
            _db.CardChecklists.Add(step);
        await _db.SaveChangesAsync();
        var requested = await _db.CardChecklists.FirstAsync(c =>
            c.CorrelationId == model.Id && c.Step == CardChecklistStep.Requested);
        CardChecklistDefaults.Mark(requested, null, $"request:{model.Id}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل الطلب";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.CardRequests.FindAsync(id);
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
        var e = await _db.CardRequests.FindAsync(id);
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
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var e = await _db.CardRequests.FindAsync(id);
        if (e == null) return NotFound();
        if (e.IsCancelled)
        {
            TempData["Error"] = "ملغى مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }
        CardRequestRules.SoftCancel(e, reason);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الطلب (soft-cancel)";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessRequest(Guid id, Guid? cardDesignId)
    {
        var e = await _db.CardRequests.FindAsync(id);
        if (e is null) return NotFound();
        if (e.IsCancelled) return BadRequest();
        if (!CardIssuanceWorkflow.CanProcess(e.Status)) return BadRequest();

        var designId = cardDesignId
            ?? await _db.CardDesigns.AsNoTracking().Select(d => (Guid?)d.Id).FirstOrDefaultAsync();
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
        _db.IssuedCards.Add(issued);
        await _db.SaveChangesAsync();

        e.Status = CardRequestStatus.Processed;
        e.IssuedCardId = issued.Id;

        var issuedStep = await _db.CardChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == id && c.Step == CardChecklistStep.Issued);
        if (issuedStep is not null && !issuedStep.IsCompleted)
            CardChecklistDefaults.Mark(issuedStep, null, $"card:{issued.Id}");

        await _db.SaveChangesAsync();
        TempData["Success"] = "تمت معالجة الطلب وإصدار البطاقة";
        return RedirectToAction(nameof(Details), new { id });
    }
}
