using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infra.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace IT.Infra.Web.Controllers;

public class ServerDevicesController(ITInfraDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ServerDevices.AsNoTracking().OrderBy(x => x.Hostname).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.ServerDevices.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new ServerDevice { Status = ServerStatus.Active });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServerDevice model)
    {
        if (!ModelState.IsValid) return View(model);
        db.ServerDevices.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل السيرفر";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.ServerDevices.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ServerDevice model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.ServerDevices.FindAsync(id);
        if (e is null) return NotFound();
        e.Hostname = model.Hostname;
        e.IpAddress = model.IpAddress;
        e.Os = model.Os;
        e.Purpose = model.Purpose;
        e.Location = model.Location;
        e.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.ServerDevices.FindAsync(id);
        if (e is null) return NotFound();
        db.ServerDevices.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}

public class SystemPermissionRecordsController(ITInfraDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.SystemPermissionRecords.AsNoTracking().OrderByDescending(x => x.GrantedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.SystemPermissionRecords.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new SystemPermissionRecord { GrantedAt = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SystemPermissionRecord model)
    {
        if (!ModelState.IsValid) return View(model);
        db.SystemPermissionRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الصلاحية التقنية";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.SystemPermissionRecords.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SystemPermissionRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.SystemPermissionRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.UserId = model.UserId;
        e.System = model.System;
        e.Roles = model.Roles;
        e.GrantedAt = model.GrantedAt;
        e.RevokedAt = model.RevokedAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var e = await db.SystemPermissionRecords.FindAsync(id);
        if (e is null) return NotFound();
        if (e.RevokedAt is not null) return RedirectToAction(nameof(Details), new { id });
        e.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم سحب الصلاحية";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class EnvironmentConfigurationsController(ITInfraDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.EnvironmentConfigurations.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.EnvironmentConfigurations.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new EnvironmentConfiguration { AppliedAt = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EnvironmentConfiguration model)
    {
        if (!ModelState.IsValid) return View(model);
        db.EnvironmentConfigurations.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حفظ وصفة البيئة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.EnvironmentConfigurations.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EnvironmentConfiguration model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.EnvironmentConfigurations.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.Config = model.Config;
        e.Version = model.Version;
        e.AppliedAt = model.AppliedAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
