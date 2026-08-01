using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Enums;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace Dev.Analytics.Web.Controllers;

public class KpisController(AnalyticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.KPIs.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.KPIs.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new KPI { MeasuredAt = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(KPI model)
    {
        if (!ModelState.IsValid) return View(model);
        db.KPIs.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة المؤشر";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.KPIs.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, KPI model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.KPIs.FindAsync(id);
        if (e is null) return NotFound();
        e.Code = model.Code;
        e.Name = model.Name;
        e.Definition = model.Definition;
        e.TargetValue = model.TargetValue;
        e.CurrentValue = model.CurrentValue;
        e.MeasuredAt = model.MeasuredAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class EtlJobsController(AnalyticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ETLJobs.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.ETLJobs.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new ETLJob { Status = JobStatus.Ok });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ETLJob model)
    {
        if (!ModelState.IsValid) return View(model);
        db.ETLJobs.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة مهمة ETL";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.ETLJobs.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ETLJob model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.ETLJobs.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.Schedule = model.Schedule;
        e.Status = model.Status;
        e.Log = model.Log;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(Guid id)
    {
        var e = await db.ETLJobs.FindAsync(id);
        if (e is null) return NotFound();
        if (!ETLJobExecutionRules.CanRun(e.Status)) return BadRequest("المهمة في حالة خطأ");

        e.LastRun = DateTime.UtcNow;
        e.Status = JobStatus.Ok;

        var readModel = await db.ReadModels.FirstOrDefaultAsync(r => r.Name == e.Name)
            ?? await db.ReadModels.OrderBy(r => r.Name).FirstOrDefaultAsync();
        if (readModel is not null)
        {
            var events = await db.AnalyticsEvents
                .AsNoTracking()
                .OrderByDescending(x => x.OccurredAt)
                .Take(500)
                .Select(x => new { x.EventName, x.OccurredAt })
                .ToListAsync();
            readModel.Schema = System.Text.Json.JsonSerializer.Serialize(new { extracted = events.Count, events });
            readModel.LastRefreshed = DateTime.UtcNow;
            e.Log = $"Extracted {events.Count} AnalyticsEvents → ReadModel {readModel.Name}";
        }
        else
        {
            e.Log = "No ReadModel target configured";
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم تشغيل المهمة";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class ReadModelsController(AnalyticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ReadModels.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.ReadModels.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new ReadModel { LastRefreshed = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReadModel model)
    {
        if (!ModelState.IsValid) return View(model);
        db.ReadModels.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة نموذج القراءة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.ReadModels.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ReadModel model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.ReadModels.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.Schema = model.Schema;
        e.LastRefreshed = model.LastRefreshed;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>FUN-006: deterministic rebuild from AnalyticsEvents ordered by OccurredAt+Id.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rebuild(Guid id, Guid? orgUnitId)
    {
        var model = await db.ReadModels.FindAsync(id);
        if (model is null) return NotFound();

        var eventName = model.SourceEventName;
        var events = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => eventName == null || e.EventName == eventName)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync();

        var projection = AnalyticsProjectionRules.BuildHeadcountProjection(events, orgUnitId);
        var fingerprint = AnalyticsProjectionRules.Fingerprint(
            orgUnitId.HasValue ? events.Where(e => e.OrgUnitId == orgUnitId) : events);

        model.Schema = projection;
        model.LastFingerprint = fingerprint;
        model.LastRefreshed = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = $"أُعيد البناء — fingerprint={fingerprint[..12]}…";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class DataRefreshSchedulesController(AnalyticsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.DataRefreshSchedules.AsNoTracking().Include(s => s.TargetReadModel).OrderByDescending(x => x.NextRunAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.DataRefreshSchedules
            .Include(s => s.TargetReadModel)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.ReadModels = new SelectList(
            await db.ReadModels.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name");
        return View(new DataRefreshSchedule());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DataRefreshSchedule model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ReadModels = new SelectList(
                await db.ReadModels.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name");
            return View(model);
        }
        db.DataRefreshSchedules.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة جدول التحديث";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.DataRefreshSchedules.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.ReadModels = new SelectList(
            await db.ReadModels.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name", e.TargetReadModelId);
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DataRefreshSchedule model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.ReadModels = new SelectList(
                await db.ReadModels.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name", model.TargetReadModelId);
            return View(model);
        }
        var e = await db.DataRefreshSchedules.FindAsync(id);
        if (e is null) return NotFound();
        e.TargetReadModelId = model.TargetReadModelId;
        e.CronExpression = model.CronExpression;
        e.LastRunAt = model.LastRunAt;
        e.NextRunAt = model.NextRunAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Trigger(Guid id)
    {
        var e = await db.DataRefreshSchedules.FindAsync(id);
        if (e is null) return NotFound();
        e.LastRunAt = DateTime.UtcNow;
        e.NextRunAt = DateTime.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تشغيل التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
