using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using IDCard.Infrastructure.Services;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IDCard.Web.Controllers;

/// <summary>
/// PLAN-46 (section 3): a request is a draft until it is submitted to the card office in HR.Core's engine; only an approved request is
/// processed into a card. The approval fields are never bound from a form.
/// </summary>
public class CardRequestsController(IDCardDbContext db, IPolicyEnforceClient policyClient, CardRequestApprovalService approvals) : Controller
{
    public const string NotApprovedMessage = "تُعالَج الطلبات المعتمدة فقط.";
    public const string NotDraftEditMessage = "يُعدَّل الطلب وهو مسودة فقط.";

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

        // PLAN-46: only what the form shows is taken; the status and every approval field start empty.
        var request = new CardRequest
        {
            EmployeeId = model.EmployeeId,
            RequestDate = model.RequestDate,
            Purpose = model.Purpose,
            Status = CardRequestStatus.Pending,
            // Requester identity comes from the session, never from the posted form.
            RequestedBy = ParseUserId(User)
        };
        await AddDraftAsync(db, request, HttpContext.RequestAborted);

        TempData["Success"] = "تم تسجيل الطلب مسودةً. قدّمه للاعتماد من صفحة التفاصيل.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    /// <summary>A draft and its checklist, the first step marked. Renew uses it too.</summary>
    internal static async Task AddDraftAsync(IDCardDbContext db, CardRequest request, CancellationToken ct)
    {
        db.CardRequests.Add(request);
        await db.SaveChangesAsync(ct);

        foreach (var step in CardChecklistDefaults.CreateFor(request.Id))
            db.CardChecklists.Add(step);
        await db.SaveChangesAsync(ct);
        var requested = await db.CardChecklists.FirstAsync(c =>
            c.CorrelationId == request.Id && c.Step == CardChecklistStep.Requested, ct);
        CardChecklistDefaults.Mark(requested, null, $"request:{request.Id}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e == null) return NotFound();
        if (e.IsCancelled || !CardIssuanceWorkflow.CanEdit(e.Status))
        {
            TempData["Error"] = e.IsCancelled ? "الطلب ملغى" : NotDraftEditMessage;
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
        if (e.IsCancelled || !CardIssuanceWorkflow.CanEdit(e.Status))
        {
            TempData["Error"] = e.IsCancelled ? "الطلب ملغى" : NotDraftEditMessage;
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

    /// <summary>PLAN-46: the draft goes to the card office; the requester and the submitter cannot approve it.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e is null) return NotFound();
        if (ParseUserId(User) is not Guid submitter)
            return Forbid();

        var result = await approvals.SubmitAsync(e, submitter, HttpContext.RequestAborted);
        if (result.Success)
            TempData["Success"] = result.Finished ? "انتهى اعتماد هذا الطلب سابقاً وطُبّقت نتيجته." : "قُدّم الطلب للاعتماد.";
        else
            TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>PLAN-46: the instance is withdrawn first; one that had ended keeps its outcome.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(Guid id, string? reason)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e is null) return NotFound();

        var result = await approvals.WithdrawAsync(e, ParseUserId(User), Reason(reason, "سحب طلب البطاقة"), HttpContext.RequestAborted);
        if (result.Success)
            TempData["Success"] = "سُحب الطلب وعاد مسودة.";
        else
            TempData["Error"] = result.Error ?? "انتهى الاعتماد قبل السحب وطُبّقت نتيجته.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var e = await db.CardRequests.FindAsync(id);
        if (e == null) return NotFound();
        if (e.IsCancelled || !CardIssuanceWorkflow.CanCancel(e.Status))
        {
            TempData["Error"] = e.IsCancelled ? "ملغى مسبقاً" : "لا يمكن إلغاء الطلب في حالته الحالية";
            return RedirectToAction(nameof(Details), new { id });
        }

        // PLAN-46 (section 3, rule 3): a pending request is withdrawn from the engine before it is cancelled here.
        if (e.Status == CardRequestStatus.PendingApproval)
        {
            var withdrawn = await approvals.WithdrawAsync(e, ParseUserId(User), Reason(reason, "إلغاء طلب البطاقة"), HttpContext.RequestAborted);
            if (!withdrawn.Success)
            {
                TempData["Error"] = withdrawn.Error ?? "انتهى الاعتماد قبل الإلغاء وطُبّقت نتيجته. أعد المحاولة إن لزم.";
                return RedirectToAction(nameof(Details), new { id });
            }
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
        // PLAN-46: approved in the engine, by someone other than the requester, and linked to that approval.
        if (!CardIssuanceWorkflow.CanProcess(e.Status) || e.WorkflowInstanceId is null || e.IssuedCardId is not null)
        {
            TempData["Error"] = NotApprovedMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        // PLAN-43/46: the policy is a signal here; it starts no instance (the type is started only through the contract).
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
            RevokeSlaHours = CardChecklistDefaults.DefaultRevokeSlaHours,
            ReplacesCardId = e.ReplacesCardId
        };
        db.IssuedCards.Add(issued);
        await RevokeReplacedAsync(db, e);
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

    /// <summary>A replacement takes the place of the old card when it is issued, not when it is asked for.</summary>
    internal static async Task RevokeReplacedAsync(IDCardDbContext db, CardRequest request)
    {
        if (request.ReplacesCardId is not Guid oldId)
            return;

        var old = await db.IssuedCards.FirstOrDefaultAsync(c => c.Id == oldId);
        if (old is { Status: CardStatus.Active })
        {
            old.Status = CardStatus.Revoked;
            old.RevokedAt = DateTime.UtcNow;
        }
    }

    private static string Reason(string? reason, string fallback) => string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();
}
