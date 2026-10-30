using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Payroll.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using System.Security.Claims;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

public class AllowanceRecordsController(PayrollDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(Guid? employeeId, Guid? allowanceTypeId, DateTime? effectiveFrom, DateTime? effectiveTo)
    {
        // QA-034 (IAM-002): branch scope. Payroll has no employee snapshots, so the employee's org unit is the one on
        // the active pay profile (PayrollEmployeeScope). A restricted user gets 403 for an employee outside the scope
        // and lists only employees inside it (no active profile = outside, fail-closed); unrestricted is unchanged.
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;
        if (employeeId is { } scopedEmployeeId
            && await this.ForbidIfEmployeeOutOfScopeAsync(scope, scopedEmployeeId, db.EmployeePayProfiles) is { } employeeDenied)
            return employeeDenied;

        var query = db.AllowanceRecords.AsNoTracking().Include(a => a.AllowanceType).AsQueryable();
        if (scope.MustFilter)
        {
            var inScope = db.EmployeePayProfiles.EmployeeIdsInScope(scope);
            query = query.Where(x => inScope.Contains(x.EmployeeId));
        }
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        if (allowanceTypeId.HasValue) query = query.Where(x => x.AllowanceTypeId == allowanceTypeId.Value);
        if (effectiveFrom.HasValue) query = query.Where(x => x.EffectiveFrom >= effectiveFrom.Value);
        if (effectiveTo.HasValue) query = query.Where(x => x.EffectiveFrom <= effectiveTo.Value);

        var items = await query.OrderByDescending(x => x.EffectiveFrom).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.EmployeeId = employeeId;
        ViewBag.AllowanceTypeId = allowanceTypeId;
        ViewBag.EffectiveFrom = effectiveFrom;
        ViewBag.EffectiveTo = effectiveTo;
        ViewBag.AllowanceTypes = new SelectList(
            await db.CompensationTypes.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "NameAr", allowanceTypeId);
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(x => x.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        return View(items);
    }

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

public class DeductionRecordsController(PayrollDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(Guid? employeeId, string? deductionType, Guid? appliedInRunId, DateOnly? dueFrom, DateOnly? dueTo)
    {
        // QA-034 (IAM-002): branch scope. Payroll has no employee snapshots, so the employee's org unit is the one on
        // the active pay profile (PayrollEmployeeScope). A restricted user gets 403 for an employee outside the scope
        // and lists only employees inside it (no active profile = outside, fail-closed); unrestricted is unchanged.
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;
        if (employeeId is { } scopedEmployeeId
            && await this.ForbidIfEmployeeOutOfScopeAsync(scope, scopedEmployeeId, db.EmployeePayProfiles) is { } employeeDenied)
            return employeeDenied;

        var query = db.DeductionRecords.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
        {
            var inScope = db.EmployeePayProfiles.EmployeeIdsInScope(scope);
            query = query.Where(x => inScope.Contains(x.EmployeeId));
        }
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(deductionType)) query = query.Where(x => x.DeductionType == deductionType.Trim());
        if (appliedInRunId.HasValue) query = query.Where(x => x.AppliedInRunId == appliedInRunId.Value);
        if (dueFrom.HasValue) query = query.Where(x => x.DueDate >= dueFrom.Value);
        if (dueTo.HasValue) query = query.Where(x => x.DueDate <= dueTo.Value);

        var items = await query.OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.EmployeeId = employeeId;
        ViewBag.DeductionType = deductionType;
        ViewBag.AppliedInRunId = appliedInRunId;
        ViewBag.DueFrom = dueFrom;
        ViewBag.DueTo = dueTo;
        ViewBag.PayrollRuns = new SelectList(
            await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "RunDate", appliedInRunId);
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(x => x.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        return View(items);
    }

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

public class PayrollAdjustmentsController(PayrollDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(Guid? employeeId, Guid? payrollRunId, DateTime? createdFrom, DateTime? createdTo)
    {
        // QA-034 (IAM-002): branch scope. Payroll has no employee snapshots, so the employee's org unit is the one on
        // the active pay profile (PayrollEmployeeScope). A restricted user gets 403 for an employee outside the scope
        // and lists only employees inside it (no active profile = outside, fail-closed); unrestricted is unchanged.
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;
        if (employeeId is { } scopedEmployeeId
            && await this.ForbidIfEmployeeOutOfScopeAsync(scope, scopedEmployeeId, db.EmployeePayProfiles) is { } employeeDenied)
            return employeeDenied;

        var query = db.PayrollAdjustments.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
        {
            var inScope = db.EmployeePayProfiles.EmployeeIdsInScope(scope);
            query = query.Where(x => inScope.Contains(x.EmployeeId));
        }
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        if (payrollRunId.HasValue) query = query.Where(x => x.PayrollRunId == payrollRunId.Value);
        if (createdFrom.HasValue) query = query.Where(x => x.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue) query = query.Where(x => x.CreatedAt <= createdTo.Value);

        var items = await query.OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.EmployeeId = employeeId;
        ViewBag.PayrollRunId = payrollRunId;
        ViewBag.CreatedFrom = createdFrom;
        ViewBag.CreatedTo = createdTo;
        ViewBag.PayrollRuns = new SelectList(
            await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "RunDate", payrollRunId);
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(x => x.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        return View(items);
    }

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

    public async Task<IActionResult> Index(Guid? payrollRunId, AuditStatus? status, DateTime? createdFrom, DateTime? createdTo)
    {
        var query = db.PayrollAuditRecords.AsNoTracking().Include(a => a.PayrollRun).AsQueryable();
        if (payrollRunId.HasValue) query = query.Where(x => x.PayrollRunId == payrollRunId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (createdFrom.HasValue) query = query.Where(x => x.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue) query = query.Where(x => x.CreatedAt <= createdTo.Value);

        var items = await query.OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.PayrollRunId = payrollRunId;
        ViewBag.Status = status;
        ViewBag.CreatedFrom = createdFrom;
        ViewBag.CreatedTo = createdTo;
        ViewBag.PayrollRuns = new SelectList(
            await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "RunDate", payrollRunId);
        return View(items);
    }

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
    public async Task<IActionResult> Index(Guid? payrollRunId, PaymentBatchStatus? status, DateTime? processedFrom, DateTime? processedTo, bool? hasBankReference)
    {
        var query = db.PaymentBatches.AsNoTracking().Include(p => p.PayrollRun).AsQueryable();
        if (payrollRunId.HasValue) query = query.Where(x => x.PayrollRunId == payrollRunId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (processedFrom.HasValue) query = query.Where(x => x.ProcessedAt >= processedFrom.Value);
        if (processedTo.HasValue) query = query.Where(x => x.ProcessedAt <= processedTo.Value);
        if (hasBankReference == true) query = query.Where(x => x.BankTransferReference != null && x.BankTransferReference != "");
        if (hasBankReference == false) query = query.Where(x => x.BankTransferReference == null || x.BankTransferReference == "");

        var items = await query.OrderByDescending(x => x.ProcessedAt ?? DateTime.MinValue).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.PayrollRunId = payrollRunId;
        ViewBag.Status = status;
        ViewBag.ProcessedFrom = processedFrom;
        ViewBag.ProcessedTo = processedTo;
        ViewBag.HasBankReference = hasBankReference;
        ViewBag.PayrollRuns = new SelectList(
            await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "RunDate", payrollRunId);
        return View(items);
    }
}
