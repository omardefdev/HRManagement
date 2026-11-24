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
using MediatR;
using Payroll.Application.Features.ManualInputs;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-54 (§4): what the three manual input controllers share. A person enters an input (<c>Payroll.Process</c>); a second person,
/// who holds <c>Payroll.Approve</c> and did not enter it, approves or rejects it. A refused decision renders the list with the Arabic
/// message and the refusal's status (403 one's own input, 409 not pending, 400 a missing note, 404 no such input); nothing changes.
/// </summary>
internal static class ManualInputActions
{
    public static async Task StampAsync(Controller controller, IAuthorizationService authorization)
    {
        controller.ViewData[ManualInputPage.CanApproveKey] =
            (await authorization.AuthorizeAsync(controller.User, HrManagementPolicies.PayrollApprove)).Succeeded;
        var id = controller.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? controller.User.FindFirstValue("sub");
        controller.ViewData[ManualInputPage.UserIdKey] = Guid.TryParse(id, out var user) ? user : (Guid?)null;
    }

    public static Task<ManualInputResult> DecideAsync(
        Controller controller, IMediator mediator, ManualInputKind kind, Guid id, ManualInputDecision decision, string? note) =>
        mediator.Send(new DecideManualInputCommand(kind, id, decision, note, PayProfileChangeHttp.RequireUserId(controller.User)));
}

public class AllowanceRecordsController(PayrollDbContext db, IEmployeeLookup employeeLookup, IMediator mediator, IAuthorizationService authorization) : Controller
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
        await ManualInputActions.StampAsync(this, authorization);
        return View(nameof(Index), items);
    }

    private async Task FillTypesAsync(Guid? selected) =>
        ViewBag.Types = new SelectList(
            await db.CompensationTypes.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", selected);

    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create()
    {
        await FillTypesAsync(null);
        return View(new ManualAllowanceCreateViewModel { EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    /// <summary>A manual allowance is entered pending: it counts in no calculation before a second person approves it (PLAN-54 §4).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(ManualAllowanceCreateViewModel model)
    {
        var result = ModelState.IsValid
            ? await mediator.Send(new CreateManualInputCommand(
                ManualInputKind.Allowance, model.EmployeeId!.Value, model.Amount!.Value, model.Reason, PayProfileChangeHttp.RequireUserId(User),
                AllowanceTypeId: model.AllowanceTypeId, EffectiveFrom: model.EffectiveFrom, EffectiveTo: model.EffectiveTo))
            : null;
        if (result is not { Success: true })
        {
            if (result is not null) ModelState.AddModelError(string.Empty, result.Error!);
            await FillTypesAsync(model.AllowanceTypeId);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        TempData["Success"] = ManualInputPage.Created(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Approve(Guid id) => DecideAsync(id, ManualInputDecision.Approved, null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Reject(Guid id, string? note) => DecideAsync(id, ManualInputDecision.Rejected, note);

    private async Task<IActionResult> DecideAsync(Guid id, ManualInputDecision decision, string? note)
    {
        var result = await ManualInputActions.DecideAsync(this, mediator, ManualInputKind.Allowance, id, decision, note);
        if (result.Success)
        {
            TempData["Success"] = decision == ManualInputDecision.Approved ? "اعتُمد البدل: يدخل الحساب التالي." : "رُفض البدل.";
            return RedirectToAction(nameof(Index));
        }

        ViewData[ManualInputPage.ErrorKey] = result.Error;
        Response.StatusCode = result.StatusCode;
        return await Index(null, null, null, null);
    }
}

public class DeductionRecordsController(PayrollDbContext db, IEmployeeLookup employeeLookup, IMediator mediator, IAuthorizationService authorization) : Controller
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
        await ManualInputActions.StampAsync(this, authorization);
        return View(nameof(Index), items);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public IActionResult Create() => View(new ManualDeductionCreateViewModel());

    /// <summary>
    /// A manual deduction is entered pending: it counts in no calculation before a second person approves it (PLAN-54 §4). The form
    /// names no run: which run charges it is the calculation's matter, never the client's.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(ManualDeductionCreateViewModel model)
    {
        var result = ModelState.IsValid
            ? await mediator.Send(new CreateManualInputCommand(
                ManualInputKind.Deduction, model.EmployeeId!.Value, model.Amount!.Value, model.Reason, PayProfileChangeHttp.RequireUserId(User),
                DeductionType: model.DeductionType, DueDate: model.DueDate))
            : null;
        if (result is not { Success: true })
        {
            if (result is not null) ModelState.AddModelError(string.Empty, result.Error!);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        SecurityAudit.Log(
            HttpContext,
            SecurityAudit.EventTypes.PayrollOverride,
            resource: $"Deduction:{result.InputId}",
            detailJson: "{\"action\":\"DeductionCreate\"}");
        TempData["Success"] = ManualInputPage.Created(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Approve(Guid id) => DecideAsync(id, ManualInputDecision.Approved, null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Reject(Guid id, string? note) => DecideAsync(id, ManualInputDecision.Rejected, note);

    private async Task<IActionResult> DecideAsync(Guid id, ManualInputDecision decision, string? note)
    {
        var result = await ManualInputActions.DecideAsync(this, mediator, ManualInputKind.Deduction, id, decision, note);
        if (result.Success)
        {
            TempData["Success"] = decision == ManualInputDecision.Approved ? "اعتُمد الخصم: يدخل الحساب التالي." : "رُفض الخصم.";
            return RedirectToAction(nameof(Index));
        }

        ViewData[ManualInputPage.ErrorKey] = result.Error;
        Response.StatusCode = result.StatusCode;
        return await Index(null, null, null, null, null);
    }
}

public class PayrollAdjustmentsController(PayrollDbContext db, IEmployeeLookup employeeLookup, IMediator mediator, IAuthorizationService authorization) : Controller
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
        await ManualInputActions.StampAsync(this, authorization);
        return View(nameof(Index), items);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public IActionResult Create() => View(new ManualAdjustmentCreateViewModel());

    /// <summary>
    /// A manual adjustment is entered pending: it counts in no calculation before a second person approves it (PLAN-54 §4). The form
    /// names no run: which run applies it is the calculation's matter, never the client's.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(ManualAdjustmentCreateViewModel model)
    {
        var result = ModelState.IsValid
            ? await mediator.Send(new CreateManualInputCommand(
                ManualInputKind.Adjustment, model.EmployeeId!.Value, model.Amount!.Value, model.AdjustmentReason, PayProfileChangeHttp.RequireUserId(User)))
            : null;
        if (result is not { Success: true })
        {
            if (result is not null) ModelState.AddModelError(string.Empty, result.Error!);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        SecurityAudit.Log(
            HttpContext,
            SecurityAudit.EventTypes.PayrollOverride,
            resource: $"Adjustment:{result.InputId}",
            detailJson: "{\"action\":\"AdjustmentCreate\"}");
        TempData["Success"] = ManualInputPage.Created(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Approve(Guid id) => DecideAsync(id, ManualInputDecision.Approved, null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Reject(Guid id, string? note) => DecideAsync(id, ManualInputDecision.Rejected, note);

    private async Task<IActionResult> DecideAsync(Guid id, ManualInputDecision decision, string? note)
    {
        var result = await ManualInputActions.DecideAsync(this, mediator, ManualInputKind.Adjustment, id, decision, note);
        if (result.Success)
        {
            TempData["Success"] = decision == ManualInputDecision.Approved ? "اعتُمدت التسوية: تدخل الحساب التالي." : "رُفضت التسوية.";
            return RedirectToAction(nameof(Index));
        }

        ViewData[ManualInputPage.ErrorKey] = result.Error;
        Response.StatusCode = result.StatusCode;
        return await Index(null, null, null, null);
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
