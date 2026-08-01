using IDCard.Domain.Entities;
using IDCard.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IDCard.Web.Controllers;

public class AccessPermissionsController(IDCardDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.AccessPermissions.AsNoTracking().Include(a => a.IssuedCard).OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.AccessPermissions.Include(a => a.IssuedCard).FirstOrDefaultAsync(a => a.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Cards = new SelectList(await db.IssuedCards.AsNoTracking().OrderByDescending(c => c.IssuedAt).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "SerialNumber");
        return View(new AccessPermission());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccessPermission model)
    {
        if (!ModelState.IsValid) { ViewBag.Cards = new SelectList(await db.IssuedCards.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "SerialNumber"); return View(model); }
        db.AccessPermissions.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل صلاحية الدخول";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.AccessPermissions.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Cards = new SelectList(await db.IssuedCards.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "SerialNumber");
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AccessPermission model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) { ViewBag.Cards = new SelectList(await db.IssuedCards.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "SerialNumber"); return View(model); }
        var e = await db.AccessPermissions.FindAsync(id);
        if (e is null) return NotFound();
        e.IssuedCardId = model.IssuedCardId;
        e.Resource = model.Resource;
        e.AccessLevel = model.AccessLevel;
        e.ValidFrom = model.ValidFrom;
        e.ValidTo = model.ValidTo;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.AccessPermissions.FindAsync(id);
        if (e is null) return NotFound();
        db.AccessPermissions.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}

public class OfficialSealRecordsController(IDCardDbContext db, IFileReferenceClient fileRefs) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.OfficialSealRecords.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.OfficialSealRecords.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new OfficialSealRecord());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OfficialSealRecord model)
    {
        if (!ModelState.IsValid) return View(model);
        model.IssuedBy = ServerAssignedIdentity.ResolveActorName(User);
        db.OfficialSealRecords.Add(model);
        await db.SaveChangesAsync();
        if (model.SealFileId.HasValue)
        {
            await fileRefs.RegisterReferenceAsync(
                model.SealFileId.Value,
                FileReferenceClientExtensions.BuildHolderKey(
                    HrFileServices.IdCard, nameof(OfficialSealRecord), model.Id, nameof(OfficialSealRecord.SealFileId)));
        }
        TempData["Success"] = "تم تسجيل الختم";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.OfficialSealRecords.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, OfficialSealRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.OfficialSealRecords.FindAsync(id);
        if (e is null) return NotFound();
        var oldFileId = e.SealFileId;
        e.Name = model.Name;
        e.SealFileId = model.SealFileId;
        e.ValidFrom = model.ValidFrom;
        e.ValidTo = model.ValidTo;
        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(
            fileRefs,
            HrFileServices.IdCard,
            nameof(OfficialSealRecord),
            e.Id,
            nameof(OfficialSealRecord.SealFileId),
            oldFileId,
            model.SealFileId);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.OfficialSealRecords.FindAsync(id);
        if (e is null) return NotFound();
        await EntityFileReferenceHelper.ReleaseEntityFileAsync(
            fileRefs,
            HrFileServices.IdCard,
            nameof(OfficialSealRecord),
            e.Id,
            nameof(OfficialSealRecord.SealFileId),
            e.SealFileId);
        db.OfficialSealRecords.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}

public class CardPermissionSetsController(IDCardDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.CardPermissionSets.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.CardPermissionSets.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new CardPermissionSet());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CardPermissionSet model)
    {
        if (!ModelState.IsValid) return View(model);
        db.CardPermissionSets.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة مجموعة الصلاحيات";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.CardPermissionSets.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CardPermissionSet model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.CardPermissionSets.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.Permissions = model.Permissions;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.CardPermissionSets.FindAsync(id);
        if (e is null) return NotFound();
        db.CardPermissionSets.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
