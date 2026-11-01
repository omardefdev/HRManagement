using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using IDCard.Web.Models;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.ViewModels;

namespace IDCard.Web.Controllers;

public class IssuedCardsController : Controller
{
    private readonly IDCardDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly IFileReferenceClient _fileRefs;
    private readonly ICodeGenerationClient _codeClient;

    public IssuedCardsController(
        IDCardDbContext db,
        IPublishEndpoint publish,
        IFileReferenceClient fileRefs,
        ICodeGenerationClient codeClient)
    {
        _db = db;
        _publish = publish;
        _fileRefs = fileRefs;
        _codeClient = codeClient;
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
        await LoadDesignsAsync(null);
        return View(new IssuedCardFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IssuedCardFormModel model)
    {
        await LoadDesignsAsync(model.CardDesignId);
        if (model.CardDesignId is { } designId && !await _db.CardDesigns.AnyAsync(d => d.Id == designId))
            ModelState.AddModelError(nameof(model.CardDesignId), "التصميم غير موجود");
        if (!ModelState.IsValid) return View(model);

        var (ok, serial, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            _codeClient, CodeGenerationRequests.IdCardSerial());
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, error ?? "تعذّر توليد الرقم التسلسلي");
            return View(model);
        }

        var card = new IssuedCard
        {
            EmployeeId = model.EmployeeId!.Value,
            CardDesignId = model.CardDesignId!.Value,
            IssuedAt = model.IssuedAt,
            ExpiresAt = model.ExpiresAt,
            Status = model.Status,
            SerialNumber = serial!,
            RevokeSlaHours = CardChecklistDefaults.DefaultRevokeSlaHours,
        };
        _db.IssuedCards.Add(card);
        await _db.SaveChangesAsync();

        await _publish.Publish<ICardIssued>(new
        {
            EmployeeId = card.EmployeeId,
            IssuedCardId = card.Id,
            SerialNumber = card.SerialNumber,
            IssuedAt = card.IssuedAt,
            CorrelationId = CorrelationIdFlow.Get()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم إصدار البطاقة ونشر الحدث";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        await LoadDesignsAsync(card.CardDesignId);
        return View(IssuedCardFormModel.From(card));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, IssuedCardFormModel model)
    {
        if (id != model.Id) return NotFound();
        if (model.CardDesignId is { } designId && !await _db.CardDesigns.AnyAsync(d => d.Id == designId))
            ModelState.AddModelError(nameof(model.CardDesignId), "التصميم غير موجود");
        var card = await _db.IssuedCards.FindAsync(id);
        if (card is null) return NotFound();
        if (!ModelState.IsValid)
        {
            await LoadDesignsAsync(model.CardDesignId);
            model.SerialNumber = card.SerialNumber;
            return View(model);
        }

        card.EmployeeId = model.EmployeeId!.Value;
        card.CardDesignId = model.CardDesignId!.Value;
        card.ExpiresAt = model.ExpiresAt;
        // SerialNumber immutable (COD-001)
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث البطاقة";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadDesignsAsync(Guid? selected)
    {
        var designs = await _db.CardDesigns.AsNoTracking().OrderBy(d => d.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        ViewBag.Designs = new SelectList(designs, "Id", "Name", selected);
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

        var (ok, serial, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            _codeClient, CodeGenerationRequests.IdCardSerial());
        if (!ok)
        {
            TempData["Error"] = error ?? "تعذّر توليد الرقم التسلسلي";
            return RedirectToAction(nameof(Details), new { id });
        }

        var replacement = new IssuedCard
        {
            EmployeeId = card.EmployeeId,
            CardDesignId = card.CardDesignId,
            IssuedAt = DateTime.UtcNow,
            Status = CardStatus.Active,
            SerialNumber = serial!,
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
            IssuedAt = replacement.IssuedAt,
            CorrelationId = CorrelationIdFlow.Get()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم التجديد — البطاقة الجديدة {replacement.SerialNumber}";
        return RedirectToAction(nameof(Details), new { id = replacement.Id });
    }
}
