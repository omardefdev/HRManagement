using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace Auth.Directory.Web.Controllers;

public class RolesController(AuthDirectoryDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.Roles.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new Role());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Role model)
    {
        if (!ModelState.IsValid) return View(model);
        db.Roles.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الدور";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var role = await db.Roles.FindAsync(id);
        if (role is null) return NotFound();
        return View(role);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Role model)
    {
        var role = await db.Roles.FindAsync(id);
        if (role is null) return NotFound();

        role.Name = model.Name;
        role.Description = model.Description;
        role.Permissions = model.Permissions;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الدور";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var role = await db.Roles.FindAsync(id);
        if (role is null) return NotFound();

        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الدور";
        return RedirectToAction(nameof(Index));
    }
}

public class DirectoryEntriesController(AuthDirectoryDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.DirectoryEntries.AsNoTracking().OrderBy(x => x.DisplayName).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new DirectoryEntry { LastSyncedAt = DateTime.UtcNow, Status = "active" });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DirectoryEntry model)
    {
        if (!ModelState.IsValid) return View(model);
        db.DirectoryEntries.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة مدخل الدليل";
        return RedirectToAction(nameof(Index));
    }
}

public class DirectorySyncRecordsController(AuthDirectoryDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.DirectorySyncRecords.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());
}

public class PermissionPoliciesController(AuthDirectoryDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PermissionPolicies.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new PermissionPolicy());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PermissionPolicy model)
    {
        if (!ModelState.IsValid) return View(model);
        db.PermissionPolicies.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة السياسة";
        return RedirectToAction(nameof(Index));
    }
}
