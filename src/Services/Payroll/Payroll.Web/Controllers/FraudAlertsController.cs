using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Payroll.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

public class FraudAlertsController(
    PayrollDbContext db,
    IEmployeeLookup employeeLookup) : Controller
{
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index(string? status, string? severity, string? category, Guid? employeeId, DateTime? detectedFrom, DateTime? detectedTo)
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

        var query = db.FraudAlerts.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
        {
            var inScope = db.EmployeePayProfiles.EmployeeIdsInScope(scope);
            query = query.Where(a => inScope.Contains(a.EmployeeId));
        }
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status.Trim());
        else
            query = query.Where(a => a.Status == FraudAlertStatus.Open || a.Status == FraudAlertStatus.UnderReview);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(a => a.Severity == severity.Trim());
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category.Trim());
        if (employeeId.HasValue) query = query.Where(a => a.EmployeeId == employeeId.Value);
        if (detectedFrom.HasValue) query = query.Where(a => a.DetectedAtUtc >= detectedFrom.Value);
        if (detectedTo.HasValue) query = query.Where(a => a.DetectedAtUtc <= detectedTo.Value);

        var items = await query.OrderByDescending(a => a.DetectedAtUtc).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Status = status;
        ViewBag.Severity = severity;
        ViewBag.Category = category;
        ViewBag.EmployeeId = employeeId;
        ViewBag.DetectedFrom = detectedFrom;
        ViewBag.DetectedTo = detectedTo;
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(a => a.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var alert = await db.FraudAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return NotFound();
        ViewBag.EmployeeName = await employeeLookup.GetEmployeeDisplayNameAsync(
            alert.EmployeeId, HttpContext.RequestAborted) ?? "غير متاح";
        return View(alert);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Assign(Guid id, Guid ownerId)
    {
        var alert = await db.FraudAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return NotFound();

        if (ownerId == Guid.Empty)
            ownerId = RequireUserId();

        alert.OwnerId = ownerId;
        if (alert.Status == FraudAlertStatus.Open)
            alert.Status = FraudAlertStatus.UnderReview;
        alert.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تعيين مالك التنبيه";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// PLAN-58 (§3 rule 1): an alert that is not critical is resolved by one act of its resolver (who is not recorded as its approver, and
    /// releases no hold: a hold is released by four eyes). A critical alert is a request another approver decides from «طلبات بانتظار
    /// قرارك»; its decision resolves the alert and releases its hold. No approver comes from the form.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Resolve(
        Guid id,
        string resolutionReason,
        string targetStatus,
        [FromServices] PayrollDualControlService dualControl)
    {
        var alert = await db.FraudAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return NotFound();
        var status = string.IsNullOrWhiteSpace(targetStatus) ? FraudAlertStatus.Resolved : targetStatus;

        if (alert.Severity == FraudAlertSeverity.Critical)
        {
            var requested = await dualControl.RequestAsync(
                Payroll.Domain.Enums.PayrollDualControlKind.CriticalAlertResolve, id,
                new PayrollDualControlPayload(resolutionReason ?? string.Empty, TargetStatus: status), RequireUserId(), HttpContext.RequestAborted);
            TempData[requested.Success ? "Success" : "Error"] = requested.Success
                ? "التنبيه حرج: سُجّل طلب حله، ويقرّره معتمد آخر من «طلبات بانتظار قرارك»"
                : requested.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (alert.Status is not (FraudAlertStatus.Open or FraudAlertStatus.UnderReview))
        {
            TempData["Error"] = PayrollDualControlMessages.AlertNotOpen;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(resolutionReason) || status is not (FraudAlertStatus.Resolved or FraudAlertStatus.Dismissed))
        {
            TempData["Error"] = string.IsNullOrWhiteSpace(resolutionReason) ? PayrollDualControlMessages.ReasonRequired : PayrollDualControlMessages.AlertTargetStatus;
            return RedirectToAction(nameof(Details), new { id });
        }

        alert.Status = status;
        alert.ResolvedBy = RequireUserId();
        alert.ResolutionReason = resolutionReason.Trim();
        alert.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حل التنبيه";
        return RedirectToAction(nameof(Details), new { id });
    }
}
