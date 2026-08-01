using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

public class FraudAlertsController(PayrollDbContext db, PaymentHoldService holdService) : Controller
{
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index()
    {
        var items = await db.FraudAlerts.AsNoTracking()
            .Where(a => a.Status == FraudAlertStatus.Open || a.Status == FraudAlertStatus.UnderReview)
            .OrderByDescending(a => a.DetectedAtUtc)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var alert = await db.FraudAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return NotFound();
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

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Resolve(
        Guid id,
        string resolutionReason,
        string targetStatus,
        Guid? approvedBy,
        bool releaseHold = false)
    {
        var alert = await db.FraudAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return NotFound();

        var resolvedBy = RequireUserId();
        var approver = approvedBy ?? Guid.Empty;
        if (approver == Guid.Empty)
        {
            var formApprover = Request.Form["ApprovedBy"].ToString();
            _ = Guid.TryParse(formApprover, out approver);
        }

        PaymentHold? hold = null;
        if (releaseHold && alert.PaymentHoldId.HasValue)
        {
            hold = await db.PaymentHolds.FirstOrDefaultAsync(h => h.Id == alert.PaymentHoldId.Value);
        }

        try
        {
            holdService.ResolveAlert(
                alert,
                hold,
                resolvedBy,
                approver == Guid.Empty ? resolvedBy : approver,
                resolutionReason ?? "",
                string.IsNullOrWhiteSpace(targetStatus) ? FraudAlertStatus.Resolved : targetStatus,
                releaseHold);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم حل التنبيه";
        return RedirectToAction(nameof(Details), new { id });
    }
}
