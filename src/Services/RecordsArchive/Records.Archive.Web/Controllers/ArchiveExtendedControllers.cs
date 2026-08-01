using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Domain.Services;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Web.Controllers;

public class DocumentClassificationsController(ArchiveDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.DocumentClassifications.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.DocumentClassifications.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new DocumentClassification());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentClassification model)
    {
        if (!ModelState.IsValid) return View(model);
        db.DocumentClassifications.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة التصنيف";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.DocumentClassifications.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DocumentClassification model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.DocumentClassifications.FindAsync(id);
        if (e is null) return NotFound();
        e.Code = model.Code;
        e.NameAr = model.NameAr;
        e.NameEn = model.NameEn;
        e.RetentionYears = model.RetentionYears;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class LoanRecordsController(ArchiveDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.LoanRecords.AsNoTracking().Include(l => l.ArchiveItem).OrderByDescending(x => x.LoanDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.LoanRecords.Include(l => l.ArchiveItem).FirstOrDefaultAsync(l => l.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(new LoanRecord { LoanDate = DateTime.UtcNow, DueDate = DateTime.UtcNow.AddDays(14), Status = LoanStatus.OnLoan });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LoanRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        db.LoanRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الإعارة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.LoanRecords.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LoanRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        var e = await db.LoanRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.ArchiveItemId = model.ArchiveItemId;
        e.Borrower = model.Borrower;
        e.LoanDate = model.LoanDate;
        e.DueDate = model.DueDate;
        e.ReturnedDate = model.ReturnedDate;
        e.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OverdueLoanCheck()
    {
        var overdue = await db.LoanRecords
            .Where(l => l.ReturnedDate == null && l.DueDate < DateTime.UtcNow && l.Status != LoanStatus.Overdue)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        foreach (var l in overdue) l.Status = LoanStatus.Overdue;
        if (overdue.Count > 0) await db.SaveChangesAsync();
        TempData["Success"] = $"تم تحديث {overdue.Count} إعارة متأخرة";
        return RedirectToAction(nameof(Index));
    }
}

public class PhysicalFileRecordsController(ArchiveDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PhysicalFileRecords.AsNoTracking().Include(p => p.ArchiveItem).OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.PhysicalFileRecords.Include(p => p.ArchiveItem).FirstOrDefaultAsync(p => p.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(new PhysicalFileRecord());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PhysicalFileRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        db.PhysicalFileRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الملف الورقي";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.PhysicalFileRecords.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PhysicalFileRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        var e = await db.PhysicalFileRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.ArchiveItemId = model.ArchiveItemId;
        e.BoxNumber = model.BoxNumber;
        e.PageCount = model.PageCount;
        e.Condition = model.Condition;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class DigitalFileRecordsController(ArchiveDbContext db, IFileReferenceClient fileRefs) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.DigitalFileRecords.AsNoTracking().Include(d => d.ArchiveItem).OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.DigitalFileRecords.Include(d => d.ArchiveItem).FirstOrDefaultAsync(d => d.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(new DigitalFileRecord());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DigitalFileRecord model)
    {
        if (!ArchiveRetentionRules.HasChecksum(model.Checksum))
            ModelState.AddModelError(nameof(model.Checksum), "المجموع الاختباري مطلوب (8 أحرف على الأقل)");

        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        db.DigitalFileRecords.Add(model);
        await db.SaveChangesAsync();
        if (model.FileId.HasValue)
        {
            await fileRefs.RegisterReferenceAsync(
                model.FileId.Value,
                FileReferenceClientExtensions.BuildHolderKey(
                    HrFileServices.RecordsArchive, nameof(DigitalFileRecord), model.Id, nameof(DigitalFileRecord.FileId)));
        }

        db.CustodyEvents.Add(ArchiveEvidenceRules.CreateCustody(
            model.ArchiveItemId, "DigitalFileLinked", $"checksum:{model.Checksum}", null));
        var item = await db.ArchiveItems.Include(a => a.DigitalFiles).FirstOrDefaultAsync(a => a.Id == model.ArchiveItemId);
        if (item is not null)
            item.CompletenessAttestation = ArchiveRetentionRules.CompletenessFingerprint(item.DigitalFiles);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الملف الرقمي مع إثبات hash";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.DigitalFileRecords.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DigitalFileRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        var e = await db.DigitalFileRecords.FindAsync(id);
        if (e is null) return NotFound();
        var oldFileId = e.FileId;
        e.ArchiveItemId = model.ArchiveItemId;
        e.FileId = model.FileId;
        e.FileSizeBytes = model.FileSizeBytes;
        e.Checksum = model.Checksum;
        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(
            fileRefs,
            HrFileServices.RecordsArchive,
            nameof(DigitalFileRecord),
            e.Id,
            nameof(DigitalFileRecord.FileId),
            oldFileId,
            model.FileId);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.DigitalFileRecords.FindAsync(id);
        if (e is null) return NotFound();
        await EntityFileReferenceHelper.ReleaseEntityFileAsync(
            fileRefs,
            HrFileServices.RecordsArchive,
            nameof(DigitalFileRecord),
            e.Id,
            nameof(DigitalFileRecord.FileId),
            e.FileId);
        db.DigitalFileRecords.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}

public class ArchiveIndexEntriesController(ArchiveDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ArchiveIndexEntries.AsNoTracking().Include(a => a.ArchiveItem).OrderBy(x => x.ArchiveItemId).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.ArchiveIndexEntries.Include(a => a.ArchiveItem).FirstOrDefaultAsync(a => a.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(new ArchiveIndexEntry());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ArchiveIndexEntry model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        db.ArchiveIndexEntries.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الفهرس";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.ArchiveIndexEntries.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ArchiveIndexEntry model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Items = new SelectList(await db.ArchiveItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
            return View(model);
        }
        var e = await db.ArchiveIndexEntries.FindAsync(id);
        if (e is null) return NotFound();
        e.ArchiveItemId = model.ArchiveItemId;
        e.Keywords = model.Keywords;
        e.Summary = model.Summary;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
