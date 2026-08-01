using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Infrastructure.Data;
using System.Security.Claims;

namespace Payroll.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
public class ArrearsController(PayrollDbContext db) : Controller
{
    public async Task<IActionResult> Index(Guid? employeeId)
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

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .ToListAsync();

        ViewBag.EmployeeId = employeeId;
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

        ViewBag.Entitled = entry.OriginalAmount;
        ViewBag.Paid = entry.PaidAmount;
        ViewBag.Arrears = entry.ArrearsAmount;
        return View(entry);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Update(Guid id, decimal originalAmount, decimal paidAmount, Guid approverId, string? reason)
    {
        var actorId = RequireUserId();
        var entry = await db.ArrearsLedgerEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry is null)
            return NotFound();

        if (!entry.TryManualUpdate(originalAmount, paidAmount, actorId, approverId, reason))
        {
            TempData["Error"] = "التعديل اليدوي للمتأخرات يتطلب موافقة رباعية العيون (المعتمد ≠ المنفّذ).";
            return RedirectToAction(nameof(Details), new { id });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث قيد المتأخرات بعد الموافقة الرباعية.";
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
