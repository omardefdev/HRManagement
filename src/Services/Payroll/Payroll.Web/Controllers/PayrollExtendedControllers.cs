using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using System.Security.Claims;

namespace Payroll.Web.Controllers;

public class AllowanceRecordsController(PayrollDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.AllowanceRecords.AsNoTracking().Include(a => a.AllowanceType).OrderByDescending(x => x.EffectiveFrom).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.Types = new SelectList(await db.CompensationTypes.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        return View(new AllowanceRecord { EffectiveFrom = DateTime.UtcNow });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(AllowanceRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Types = new SelectList(await db.CompensationTypes.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
            return View(model);
        }
        db.AllowanceRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل البدل";
        return RedirectToAction(nameof(Index));
    }
}

public class DeductionRecordsController(PayrollDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.DeductionRecords.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
        return View(new DeductionRecord());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(DeductionRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
            return View(model);
        }

        if (model.AppliedInRunId.HasValue)
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == model.AppliedInRunId.Value);
            if (run == null)
            {
                ModelState.AddModelError(nameof(model.AppliedInRunId), "تشغيل الرواتب غير موجود.");
                ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
                return View(model);
            }

            var error = run.ValidateCanModifyLines();
            if (error != null)
            {
                ModelState.AddModelError(nameof(model.AppliedInRunId), error);
                ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
                return View(model);
            }
        }

        db.DeductionRecords.Add(model);
        await db.SaveChangesAsync();
        SecurityAudit.Log(
            HttpContext,
            SecurityAudit.EventTypes.PayrollOverride,
            resource: $"Deduction:{model.Id}",
            detailJson: "{\"action\":\"DeductionCreate\"}");
        TempData["Success"] = "تم تسجيل الخصم";
        return RedirectToAction(nameof(Index));
    }
}

public class PayrollAdjustmentsController(PayrollDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PayrollAdjustments.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
        return View(new PayrollAdjustment());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(PayrollAdjustment model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
            return View(model);
        }

        if (model.PayrollRunId.HasValue)
        {
            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == model.PayrollRunId.Value);
            if (run == null)
            {
                ModelState.AddModelError(nameof(model.PayrollRunId), "تشغيل الرواتب غير موجود.");
                ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
                return View(model);
            }

            var error = run.ValidateCanModifyLines();
            if (error != null)
            {
                ModelState.AddModelError(nameof(model.PayrollRunId), error);
                ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
                return View(model);
            }
        }

        db.PayrollAdjustments.Add(model);
        await db.SaveChangesAsync();
        SecurityAudit.Log(
            HttpContext,
            SecurityAudit.EventTypes.PayrollOverride,
            resource: $"Adjustment:{model.Id}",
            detailJson: "{\"action\":\"AdjustmentCreate\"}");
        TempData["Success"] = "تم تسجيل التسوية";
        return RedirectToAction(nameof(Index));
    }
}

public class PayrollAuditRecordsController(PayrollDbContext db) : Controller
{
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index() =>
        View(await db.PayrollAuditRecords.AsNoTracking().Include(a => a.PayrollRun).OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
        return View(new PayrollAuditRecord { Status = AuditStatus.Open });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Create([Bind("PayrollRunId,Findings")] PayrollAuditRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
            return View(model);
        }

        var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == model.PayrollRunId);
        if (run == null)
        {
            ModelState.AddModelError(nameof(model.PayrollRunId), "تشغيل الرواتب غير موجود.");
            ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
            return View(model);
        }

        if (run.Status != PayrollRunStatus.UnderReview && run.Status != PayrollRunStatus.Approved)
        {
            ModelState.AddModelError(nameof(model.PayrollRunId), "يمكن فتح سجل المراجعة فقط لتشغيل قيد المراجعة أو معتمد.");
            ViewBag.Runs = new SelectList(await db.PayrollRuns.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "RunDate");
            return View(model);
        }

        model.Status = AuditStatus.Open;
        model.AuditorId = RequireUserId();
        db.PayrollAuditRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم فتح سجل المراجعة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> CloseAudit(Guid id)
    {
        var audit = await db.PayrollAuditRecords.FirstOrDefaultAsync(a => a.Id == id);
        if (audit == null) return NotFound();
        if (audit.Status != AuditStatus.Open)
        {
            TempData["Warning"] = "سجل المراجعة مغلق بالفعل.";
            return RedirectToAction(nameof(Index));
        }

        var currentUserId = RequireUserId();
        if (audit.AuditorId.HasValue && audit.AuditorId.Value == currentUserId)
        {
            TempData["Error"] = "لا يمكن لنفس المدقق إغلاق سجل المراجعة.";
            return RedirectToAction(nameof(Index));
        }

        audit.Status = AuditStatus.Closed;
        audit.ClosedAt = DateTime.UtcNow;
        audit.ClosedBy = currentUserId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إغلاق سجل المراجعة.";
        return RedirectToAction(nameof(Index));
    }
}

public class PaymentBatchesController(PayrollDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PaymentBatches.AsNoTracking()
            .Include(p => p.PayrollRun)
            .OrderByDescending(x => x.ProcessedAt ?? DateTime.MinValue)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
}
