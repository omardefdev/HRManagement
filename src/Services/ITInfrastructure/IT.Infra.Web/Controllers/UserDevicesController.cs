using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infra.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IT.Infra.Web.Controllers;

public class UserDevicesController(ITInfraDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.UserDevices.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.UserDevices.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new UserDevice { Status = DeviceStatus.InUse });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserDevice model)
    {
        if (!ModelState.IsValid) return View(model);
        db.UserDevices.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الجهاز";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.UserDevices.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UserDevice model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.UserDevices.FindAsync(id);
        if (e is null) return NotFound();
        e.AssetTag = model.AssetTag;
        e.AssignedTo = model.AssignedTo;
        e.DeviceType = model.DeviceType;
        e.SerialNumber = model.SerialNumber;
        e.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
