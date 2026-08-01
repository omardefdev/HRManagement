using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;
using HR.Health.Domain.Services;
using HR.Health.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Health.Web.Controllers;

public class InsuranceCardsController(HealthDbContext db, ILogger<InsuranceCardsController> logger) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.InsuranceCards.AsNoTracking()
            .Where(x => !x.IsCancelled)
            .OrderByDescending(x => x.IssueDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());

    public async Task<IActionResult> Create()
    {
        await PopulateContracts();
        return View(new InsuranceCard
        {
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = InsuranceCardStatus.Draft
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InsuranceCard model)
    {
        if (!ModelState.IsValid) { await PopulateContracts(); return View(model); }
        model.Status = InsuranceCardStatus.Draft;
        db.InsuranceCards.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل بطاقة التأمين (مسودة)";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var card = await db.InsuranceCards.FirstOrDefaultAsync(x => x.Id == id);
        if (card is null) return NotFound();
        return View(card);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (card.IsCancelled)
        {
            TempData["Error"] = "البطاقة ملغاة";
            return RedirectToAction(nameof(Details), new { id });
        }
        await PopulateContracts();
        return View(card);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, InsuranceCard model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) { await PopulateContracts(); return View(model); }

        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (card.IsCancelled)
        {
            TempData["Error"] = "البطاقة ملغاة";
            return RedirectToAction(nameof(Details), new { id });
        }

        card.EmployeeId = model.EmployeeId;
        card.MemberNumber = model.MemberNumber;
        card.Provider = model.Provider;
        card.IssueDate = model.IssueDate;
        card.ExpiryDate = model.ExpiryDate;
        card.FamilyMembers = model.FamilyMembers;
        card.InsuranceContractId = model.InsuranceContractId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث بطاقة التأمين";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id)
    {
        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!InsuranceLifecycleRules.CanActivate(card))
        {
            TempData["Error"] = "لا يمكن تفعيل البطاقة في حالتها الحالية";
            return RedirectToAction(nameof(Details), new { id });
        }
        card.Status = InsuranceCardStatus.Active;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تفعيل بطاقة التأمين";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(Guid id)
    {
        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!InsuranceLifecycleRules.CanSuspend(card))
        {
            TempData["Error"] = "لا يمكن تعليق البطاقة";
            return RedirectToAction(nameof(Details), new { id });
        }
        card.Status = InsuranceCardStatus.Suspended;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تعليق البطاقة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id, string? reason)
    {
        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!InsuranceLifecycleRules.CanRevoke(card))
        {
            TempData["Error"] = "لا يمكن إلغاء البطاقة";
            return RedirectToAction(nameof(Details), new { id });
        }
        card.Status = InsuranceCardStatus.Revoked;
        InsuranceLifecycleRules.SoftCancel(card, reason ?? "إبطال");
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إبطال بطاقة التأمين";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExpired(Guid id)
    {
        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!InsuranceLifecycleRules.CanMarkExpired(card))
        {
            TempData["Error"] = "البطاقة غير منتهية أو غير نشطة";
            return RedirectToAction(nameof(Details), new { id });
        }
        card.Status = InsuranceCardStatus.Expired;
        await db.SaveChangesAsync();
        logger.LogInformation("Insurance card {Id} expired for employee {EmployeeId}", card.Id, card.EmployeeId);
        TempData["Success"] = "تم وسم البطاقة منتهية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var card = await db.InsuranceCards.FindAsync(id);
        if (card is null) return NotFound();
        if (card.IsCancelled)
        {
            TempData["Error"] = "ملغاة مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }
        InsuranceLifecycleRules.SoftCancel(card, reason);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء البطاقة (soft-cancel)";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateContracts()
    {
        ViewBag.Contracts = new SelectList(
            await db.InsuranceContracts.AsNoTracking().OrderByDescending(c => c.StartDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "Provider");
    }
}
