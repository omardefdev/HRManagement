using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58 (§3): «طلبات بانتظار قرارك» — the pending four-eyes requests the user may decide, without the ones he asked for himself, each
/// with its target named (the employee by name, the batch by its page), the reason and the amount; and the decision, by the user, from his
/// own session. A request belongs to the whole agency.
/// </summary>
[OrgScopeCentralOnly("a four-eyes request acts on an agency's payroll records, not a unit's")]
public class DualControlController(
    PayrollDualControlService dualControl, IAuthorizationService authorization, PayrollDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Inbox(CancellationToken ct)
    {
        var items = await dualControl.InboxAsync(PayProfileChangeHttp.RequireUserId(User), MayDecideAsync, ct);
        var targets = await TargetsAsync(items, ct);
        return View(items.Select(i => new DualControlInboxRowViewModel(
            i.Id, i.Kind, i.Payload.Reason, i.Payload.Amount, i.Payload.Reference, i.Payload.OriginalAmount, i.Payload.PaidAmount,
            i.RequestedBy, i.RequestedAt, i.ExpiresAt,
            targets.GetValueOrDefault(i.Id))).ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Decide(Guid id, bool approve, string? notes, CancellationToken ct)
    {
        var result = await dualControl.DecideAsync(id, approve, notes, PayProfileChangeHttp.RequireUserId(User), MayDecideAsync, ct);
        if (result.Outcome == Payroll.Application.Features.PayrollRuns.Lifecycle.RunActOutcome.NotFound)
            return NotFound();
        if (result.Outcome == Payroll.Application.Features.PayrollRuns.Lifecycle.RunActOutcome.Forbidden)
            return Forbid();
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? (approve ? "اعتُمد الطلب ونُفّذ أثره" : "رُفض الطلب ولم يتغير شيء")
            : result.Error;
        return RedirectToAction(nameof(Inbox));
    }

    private async Task<bool> MayDecideAsync(PayrollDualControlKind kind) =>
        (await authorization.AuthorizeAsync(User, PayrollDualControlKinds.DecisionPolicy(kind))).Succeeded;

    /// <summary>What each request acts on, as the inbox names it: the employee (by name) and, for a batch's act, the batch's page.</summary>
    private async Task<Dictionary<Guid, DualControlTargetViewModel>> TargetsAsync(IReadOnlyList<PayrollDualControlInboxItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.TargetId).Distinct().ToList();
        var holdEmployees = await db.PaymentHolds.AsNoTracking().Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.EmployeeId, ct);
        var alertEmployees = await db.FraudAlerts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.EmployeeId, ct);
        var arrearsEmployees = await db.ArrearsLedgerEntries.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.EmployeeId, ct);
        var fileBatches = await db.PaymentBatchFiles.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.PaymentBatchId, ct);
        var employees = holdEmployees.Values.Concat(alertEmployees.Values).Concat(arrearsEmployees.Values).Distinct().ToList();
        var names = employees.Count == 0 ? new Dictionary<Guid, string>() : await employeeLookup.GetEmployeeDisplayNamesAsync(employees, ct);

        var result = new Dictionary<Guid, DualControlTargetViewModel>();
        foreach (var item in items)
        {
            Guid? employee = item.Kind switch
            {
                PayrollDualControlKind.HoldRelease => holdEmployees.TryGetValue(item.TargetId, out var e) ? e : null,
                PayrollDualControlKind.CriticalAlertResolve => alertEmployees.TryGetValue(item.TargetId, out var e) ? e : null,
                PayrollDualControlKind.ArrearsAdjust => arrearsEmployees.TryGetValue(item.TargetId, out var e) ? e : null,
                _ => null,
            };
            Guid? batch = item.Kind switch
            {
                PayrollDualControlKind.BatchSettle or PayrollDualControlKind.BatchReject => item.TargetId,
                PayrollDualControlKind.BankFileReissue => fileBatches.TryGetValue(item.TargetId, out var b) ? b : null,
                _ => null,
            };
            result[item.Id] = new DualControlTargetViewModel(
                employee is Guid id ? names.GetValueOrDefault(id) : null,
                batch);
        }

        return result;
    }
}
