using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Payroll.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using System.Security.Claims;

namespace Payroll.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
public class ArrearsController(PayrollDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(
        Guid? employeeId,
        ArrearsLedgerStatus? status,
        Guid? payrollRunId,
        DateTime? createdFrom,
        DateTime? createdTo)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        var query = db.ArrearsLedgerEntries.AsNoTracking().AsQueryable();

        if (employeeId is { } eid && eid != Guid.Empty)
        {
            var orgUnitId = await GetActiveEmployeeOrgUnitIdAsync(eid);
            if (this.ForbidIfOrgUnitOutOfScope(scope, orgUnitId) is { } empDenied)
                return empDenied;
            query = query.Where(e => e.EmployeeId == eid);
        }
        else if (scope.MustFilter)
        {
            var allowedEmployees = db.EmployeePayProfiles.AsNoTracking()
                .Where(p => p.IsActive && p.OrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(p.OrgUnitId.Value))
                .Select(p => p.EmployeeId);
            query = query.Where(e => allowedEmployees.Contains(e.EmployeeId));
        }

        if (status.HasValue) query = query.Where(e => e.Status == status.Value);
        if (payrollRunId.HasValue) query = query.Where(e => e.PayrollRunId == payrollRunId.Value);
        if (createdFrom.HasValue) query = query.Where(e => e.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue) query = query.Where(e => e.CreatedAt <= createdTo.Value);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .ToListAsync();

        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(e => e.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        ViewBag.EmployeeId = employeeId;
        ViewBag.Status = status;
        ViewBag.PayrollRunId = payrollRunId;
        ViewBag.CreatedFrom = createdFrom;
        ViewBag.CreatedTo = createdTo;
        ViewBag.PayrollRuns = new SelectList(
            await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "RunDate", payrollRunId);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var entry = await db.ArrearsLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);
        if (entry is null)
            return NotFound();

        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        var orgUnitId = await GetActiveEmployeeOrgUnitIdAsync(entry.EmployeeId);
        if (this.ForbidIfOrgUnitOutOfScope(scope, orgUnitId) is { } empDenied)
            return empDenied;

        ViewBag.EmployeeName = await employeeLookup.GetEmployeeDisplayNameAsync(
            entry.EmployeeId, HttpContext.RequestAborted) ?? "غير متاح";
        ViewBag.Entitled = entry.OriginalAmount;
        ViewBag.Paid = entry.PaidAmount;
        ViewBag.Arrears = entry.ArrearsAmount;
        return View(entry);
    }

    /// <summary>
    /// PLAN-58 (§3): a manual adjustment of an arrears row is four eyes: the processor asks, with the amounts and a reason, and an approver
    /// decides it from «طلبات بانتظار قرارك». No approver comes from the request.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Update(
        Guid id, decimal originalAmount, decimal paidAmount, string? reason, [FromServices] Payroll.Application.Services.PayrollDualControlService dualControl)
    {
        var result = await dualControl.RequestAsync(
            Payroll.Domain.Enums.PayrollDualControlKind.ArrearsAdjust, id,
            new Payroll.Application.Services.PayrollDualControlPayload(reason ?? string.Empty, OriginalAmount: originalAmount, PaidAmount: paidAmount),
            RequireUserId(), HttpContext.RequestAborted);
        if (result.Outcome == Payroll.Application.Features.PayrollRuns.Lifecycle.RunActOutcome.NotFound)
            return NotFound();
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "سُجّل طلب تعديل قيد المتأخرات؛ يقرّره معتمد آخر من «طلبات بانتظار قرارك»"
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>ESS / shared balance for an employee (entitled / paid / arrears).</summary>
    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.ModuleEss)]
    public async Task<IActionResult> Balance(Guid employeeId)
    {
        var selfEmployeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (selfEmployeeId is { } self && self != employeeId)
            return Forbid();

        var entries = await db.ArrearsLedgerEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .ToListAsync();
        var balance = ArrearsSettlementService.GetBalance(employeeId, entries);
        return Json(balance);
    }

    private async Task<Guid?> GetActiveEmployeeOrgUnitIdAsync(Guid employeeId) =>
        await db.EmployeePayProfiles.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .Select(p => p.OrgUnitId)
            .FirstOrDefaultAsync();

    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }
}

