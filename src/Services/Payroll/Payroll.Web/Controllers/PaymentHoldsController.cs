using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Payroll.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Infrastructure.Data;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

public class PaymentHoldsController(
    PayrollDbContext db,
    PaymentHoldService holdService,
    IEmployeeLookup employeeLookup) : Controller
{
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index(Guid? employeeId, string? status, string? holdType, DateTime? heldFrom, DateTime? heldTo)
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

        var query = db.PaymentHolds.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
        {
            var inScope = db.EmployeePayProfiles.EmployeeIdsInScope(scope);
            query = query.Where(h => inScope.Contains(h.EmployeeId));
        }
        if (employeeId.HasValue) query = query.Where(h => h.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(h => h.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(holdType)) query = query.Where(h => h.HoldType == holdType.Trim());
        if (heldFrom.HasValue) query = query.Where(h => h.HeldAtUtc >= heldFrom.Value);
        if (heldTo.HasValue) query = query.Where(h => h.HeldAtUtc <= heldTo.Value);

        var items = await query.OrderByDescending(h => h.HeldAtUtc).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.EmployeeId = employeeId;
        ViewBag.Status = status;
        ViewBag.HoldType = holdType;
        ViewBag.HeldFrom = heldFrom;
        ViewBag.HeldTo = heldTo;
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(h => h.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var hold = await db.PaymentHolds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id);
        if (hold is null) return NotFound();
        ViewBag.EmployeeName = await employeeLookup.GetEmployeeDisplayNameAsync(
            hold.EmployeeId, HttpContext.RequestAborted) ?? "غير متاح";
        return View(hold);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Release(Guid id, Guid? releaseApprovedBy, string releaseReason, Guid? evidenceRecordId)
    {
        var hold = await db.PaymentHolds.FirstOrDefaultAsync(h => h.Id == id);
        if (hold is null) return NotFound();

        var releasedBy = RequireUserId();
        var approvedBy = releaseApprovedBy ?? Guid.Empty;
        if (approvedBy == Guid.Empty)
        {
            var formApprover = Request.Form["ReleaseApprovedBy"].ToString();
            _ = Guid.TryParse(formApprover, out approvedBy);
        }

        try
        {
            holdService.ReleaseHold(hold, releasedBy, approvedBy, releaseReason ?? "", evidenceRecordId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (hold.FraudAlertId.HasValue)
        {
            var alert = await db.FraudAlerts.FirstOrDefaultAsync(a => a.Id == hold.FraudAlertId.Value);
            if (alert is not null && alert.Status is FraudAlertStatus.Open or FraudAlertStatus.UnderReview)
            {
                alert.Status = FraudAlertStatus.Resolved;
                alert.ResolvedBy = releasedBy;
                alert.ApprovedBy = approvedBy;
                alert.ResolutionReason = releaseReason;
                alert.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم إفراج حجز الصرف (أربعة عيون)";
        return RedirectToAction(nameof(Details), new { id });
    }
}
