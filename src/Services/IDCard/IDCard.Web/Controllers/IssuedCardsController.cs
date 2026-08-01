using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace IDCard.Web.Controllers;

public class IssuedCardsController : Controller
{
    private readonly IDCardDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly IFileReferenceClient _fileRefs;

    public IssuedCardsController(IDCardDbContext db, IPublishEndpoint publish, IFileReferenceClient fileRefs)
    {
        _db = db;
        _publish = publish;
        _fileRefs = fileRefs;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _db.IssuedCards
            .Include(c => c.CardDesign)
            .Include(c => c.DeliveryRecord)
            .AsNoTracking()
            .OrderByDescending(c => c.IssuedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var card = await _db.IssuedCards
            .Include(c => c.CardDesign)
            .Include(c => c.DeliveryRecord)
            .Include(c => c.AccessPermissions)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return NotFound();
        return View(card);
    }

    public async Task<IActionResult> Create()
    {
        var designs = await _db.CardDesigns.AsNoTracking().OrderBy(d => d.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        ViewBag.Designs = new SelectList(designs, "Id", "Name");
        return View(new IssuedCard
        {
            IssuedAt = DateTime.UtcNow,
            Status = CardStatus.Active,
            SerialNumber = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            RevokeSlaHours = CardChecklistDefaults.DefaultRevokeSlaHours
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IssuedCard model)
    {
        var designs = await _db.CardDesigns.AsNoTracking().OrderBy(d => d.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        ViewBag.Designs = new SelectList(designs, "Id", "Name", model.CardDesignId);

        if (!ModelState.IsValid) return View(model);
        if (model.RevokeSlaHours <= 0)
            model.RevokeSlaHours = CardChecklistDefaults.DefaultRevokeSlaHours;

        _db.IssuedCards.Add(model);
        await _db.SaveChangesAsync();

        await _publish.Publish<ICardIssued>(new
        {
            EmployeeId = model.EmployeeId,
            IssuedCardId = model.Id,
            SerialNumber = model.SerialNumber,
            IssuedAt = model.IssuedAt
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم إصدار البطاقة ونشر الحدث";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        ViewBag.Designs = new SelectList(await _db.CardDesigns.AsNoTracking().OrderBy(d => d.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name");
        return View(card);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, IssuedCard model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Designs = new SelectList(await _db.CardDesigns.AsNoTracking().OrderBy(d => d.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name");
            return View(model);
        }
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();

        card.EmployeeId = model.EmployeeId;
        card.CardDesignId = model.CardDesignId;
        card.ExpiresAt = model.ExpiresAt;
        card.SerialNumber = model.SerialNumber;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث البطاقة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPrinted(Guid id)
    {
        var card = await _db.IssuedCards.FirstOrDefaultAsync(c => c.Id == id);
        if (card == null) return NotFound();

        if (card.DeliveryRecordId == null)
        {
            var rec = new CardDeliveryRecord { IssuedCardId = id, PrintedAt = DateTime.UtcNow };
            _db.CardDeliveryRecords.Add(rec);
            await _db.SaveChangesAsync();
            card.DeliveryRecordId = rec.Id;
        }
        else
        {
            var rec = await _db.CardDeliveryRecords.FindAsync(card.DeliveryRecordId.Value);
            if (rec != null) rec.PrintedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الطباعة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDelivered(Guid id, Guid? deliveredTo, string? signatureOrReceipt, Guid? receiptFileId)
    {
        var card = await _db.IssuedCards.Include(c => c.DeliveryRecord).FirstOrDefaultAsync(c => c.Id == id);
        if (card == null) return NotFound();
        if (card.DeliveryRecord == null)
        {
            TempData["Warning"] = "سجّل الطباعة أولاً قبل التسليم.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!deliveredTo.HasValue)
        {
            TempData["Error"] = "معرّف المستلم مطلوب";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!CardDeliveryRules.HasDeliveryProof(signatureOrReceipt, receiptFileId))
        {
            TempData["Error"] = "إثبات التسليم مطلوب (نص إيصال واضح أو ملف FILE-002) — لا يُقبل الافتراض «موقّع»";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (receiptFileId.HasValue)
        {
            try
            {
                await _fileRefs.RegisterReferenceAsync(
                    receiptFileId.Value,
                    FileReferenceClientExtensions.BuildHolderKey(
                        HrFileServices.IdCard, nameof(CardDeliveryRecord), card.DeliveryRecord.Id, nameof(CardDeliveryRecord.ReceiptFileId)));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"مرفق مرفوض (FILE-002): {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
            card.DeliveryRecord.ReceiptFileId = receiptFileId;
        }

        card.DeliveryRecord.DeliveredAt = DateTime.UtcNow;
        card.DeliveryRecord.DeliveredTo = deliveredTo;
        card.DeliveryRecord.SignatureOrReceipt = signatureOrReceipt?.Trim();
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل التسليم بإثبات";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!CardIssuanceWorkflow.CanRevoke(card.Status)) return BadRequest();
        card.Status = CardStatus.Revoked;
        card.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء البطاقة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UrgentRevoke(Guid id)
    {
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!CardIssuanceWorkflow.CanRevoke(card.Status)) return BadRequest();
        card.Status = CardStatus.Revoked;
        card.RevokedAt = DateTime.UtcNow;
        card.UrgentRevoke = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم الإبطال العاجل";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkLost(Guid id)
    {
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!CardIssuanceWorkflow.CanMarkLost(card.Status))
        {
            TempData["Error"] = "لا يمكن وسم البطاقة مفقودة";
            return RedirectToAction(nameof(Details), new { id });
        }
        card.Status = CardStatus.Lost;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم وسم البطاقة مفقودة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExpired(Guid id)
    {
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        if (card.Status != CardStatus.Active || !CardIssuanceWorkflow.IsExpired(card.ExpiresAt))
        {
            TempData["Error"] = "البطاقة غير منتهية أو غير نشطة";
            return RedirectToAction(nameof(Details), new { id });
        }
        card.Status = CardStatus.Expired;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم وسم البطاقة منتهية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(Guid id)
    {
        var card = await _db.IssuedCards.Include(c => c.CardDesign).FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return NotFound();
        if (!CardIssuanceWorkflow.CanRenew(card.Status, card.ExpiresAt))
        {
            TempData["Error"] = "لا يمكن التجديد في الحالة الحالية";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (card.Status == CardStatus.Active)
        {
            card.Status = CardStatus.Revoked;
            card.RevokedAt = DateTime.UtcNow;
        }

        var replacement = new IssuedCard
        {
            EmployeeId = card.EmployeeId,
            CardDesignId = card.CardDesignId,
            IssuedAt = DateTime.UtcNow,
            Status = CardStatus.Active,
            SerialNumber = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            ExpiresAt = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(card.CardDesign?.ValidityDays ?? 365)),
            ReplacesCardId = card.Id,
            RevokeSlaHours = card.RevokeSlaHours
        };
        _db.IssuedCards.Add(replacement);
        await _db.SaveChangesAsync();

        await _publish.Publish<ICardIssued>(new
        {
            EmployeeId = replacement.EmployeeId,
            IssuedCardId = replacement.Id,
            SerialNumber = replacement.SerialNumber,
            IssuedAt = replacement.IssuedAt
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم التجديد — البطاقة الجديدة {replacement.SerialNumber}";
        return RedirectToAction(nameof(Details), new { id = replacement.Id });
    }
}
