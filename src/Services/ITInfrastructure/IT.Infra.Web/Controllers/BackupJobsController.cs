using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infra.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IT.Infra.Web.Controllers;

public class BackupJobsController(ITInfraDbContext db, IConfiguration configuration, IWebHostEnvironment env) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.BackupJobs.AsNoTracking().OrderByDescending(x => x.LastRunAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.BackupJobs.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new BackupJob { Status = BackupStatus.Ok });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BackupJob model)
    {
        if (!ModelState.IsValid) return View(model);
        db.BackupJobs.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة مهمة النسخ الاحتياطي";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.BackupJobs.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, BackupJob model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.BackupJobs.FindAsync(id);
        if (e is null) return NotFound();
        e.Target = model.Target;
        e.Schedule = model.Schedule;
        e.LastRunAt = model.LastRunAt;
        e.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// OPS-003: sync status from backup-catalog.jsonl written by Cron/scripts.
    /// Does not execute pg_dump inside the web process (least privilege / security).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(Guid id)
    {
        var e = await db.BackupJobs.FindAsync(id);
        if (e is null) return NotFound();

        var catalogPath = configuration["Backup:CatalogPath"]
            ?? Environment.GetEnvironmentVariable("BACKUP_CATALOG_PATH")
            ?? Path.Combine(env.ContentRootPath, "backups", "backup-catalog.jsonl");

        var maxAgeHours = configuration.GetValue("Backup:MaxCatalogAgeHours", 36);

        if (!System.IO.File.Exists(catalogPath))
        {
            e.Status = BackupStatus.Failed;
            e.LastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Error"] =
                $"لا يوجد كتالوج نسخ عند {catalogPath}. شغّل سكربتات النسخ/CronJob ثم أعد المزامنة. لا يُنفَّذ dump من الواجهة (OPS-003).";
            return RedirectToAction(nameof(Details), new { id });
        }

        BackupCatalogEntry? match = null;
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var line in System.IO.File.ReadLines(catalogPath).Reverse())
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<BackupCatalogEntry>(line, jsonOpts);
                if (entry is null) continue;
                if (CatalogMatchesTarget(entry.Component, e.Target))
                {
                    match = entry;
                    break;
                }
            }
            catch (JsonException)
            {
                // skip corrupt line
            }
        }

        if (match is null)
        {
            e.Status = BackupStatus.Failed;
            e.LastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Error"] = $"لا إدخال في الكتالوج يطابق الهدف «{e.Target}».";
            return RedirectToAction(nameof(Details), new { id });
        }

        var takenAt = DateTime.TryParse(match.TakenAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
            ? ts.ToUniversalTime()
            : DateTime.UtcNow;
        e.LastRunAt = takenAt;
        var age = DateTime.UtcNow - takenAt;
        var stale = age > TimeSpan.FromHours(maxAgeHours);
        var badStatus = !string.Equals(match.Status, "ok", StringComparison.OrdinalIgnoreCase);
        e.Status = stale || badStatus ? BackupStatus.Failed : BackupStatus.Ok;
        await db.SaveChangesAsync();

        TempData["Success"] = stale || badStatus
            ? $"مزامنة كتالوج: فشل/قديم — component={match.Component} path={match.Path} age={age.TotalHours:F1}h checksum={match.ChecksumSha256}"
            : $"مزامنة كتالوج ناجحة — component={match.Component} path={match.Path} takenAt={match.TakenAt} checksum={match.ChecksumSha256}";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.BackupJobs.FindAsync(id);
        if (e is null) return NotFound();
        db.BackupJobs.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    private static bool CatalogMatchesTarget(string? component, string target)
    {
        if (string.IsNullOrWhiteSpace(component)) return false;
        if (component.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
        if (component.Contains(target, StringComparison.OrdinalIgnoreCase)) return true;
        if (target.Contains(component, StringComparison.OrdinalIgnoreCase)) return true;
        // Common targets: postgres, minio, keycloak
        if (target.StartsWith("pg", StringComparison.OrdinalIgnoreCase) && component.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private sealed class BackupCatalogEntry
    {
        public string? TakenAt { get; set; }
        public string? Component { get; set; }
        public string? Path { get; set; }
        public string? ChecksumSha256 { get; set; }
        public long Bytes { get; set; }
        public string? ExpiresAt { get; set; }
        public string? Status { get; set; }
    }
}
