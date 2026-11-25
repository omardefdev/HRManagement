using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-56 (G-PAY-06): «طلبات العكس». A reversal of a posted run is asked for on the run's page with a reason; here a holder of
/// <c>Payroll.Approve</c> who is neither the requester nor the run's poster approves or rejects it. The list is open to the payroll
/// module; the decision names its policy.
/// </summary>
[OrgScopeCentralOnly("a payroll run and the request to reverse it belong to the whole agency, not to a unit")]
public class PayrollReversalRequestsController(PayrollDbContext db, IMediator mediator, IAuthorizationService authorization) : Controller
{
    public async Task<IActionResult> Index(PayrollReversalRequestStatus? status)
    {
        var query = db.PayrollReversalRequests.AsNoTracking();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var items = await query
            .OrderBy(r => r.Status == PayrollReversalRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.RequestedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .Select(r => new PayrollReversalRequestRow(
                r.Id, r.PayrollRunId, r.PayrollRun.PeriodStart, r.PayrollRun.RunType, r.PayrollRun.Status, r.PayrollRun.TotalNetPay,
                // The poster, or for a run posted before PLAN-56 its last writer (PayrollRun.KnownPoster).
                r.PayrollRun.PostedBy ?? r.PayrollRun.UpdatedBy, r.RequestedBy, r.RequestedAt, r.Reason, r.Status, r.DecidedBy, r.DecidedAt, r.DecisionNotes))
            .ToListAsync();

        var userId = PayProfileChangeHttp.TryUserId(User);
        return View(new PayrollReversalRequestListViewModel(
            items,
            status,
            CanDecide: (await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollApprove)).Succeeded,
            CurrentUserId: userId));
    }

    /// <summary>
    /// The decision. The decider is neither the requester nor the run's poster; an approval is refused when the run's money has left
    /// (D-PAY-16), with the message that points to a correction run; a rejection needs its note.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Decide(Guid id, bool approve, string? notes)
    {
        var result = await mediator.Send(new DecidePayrollReversalCommand(id, PayProfileChangeHttp.RequireUserId(User), approve, notes));
        if (result.Outcome == RunActOutcome.NotFound)
            return NotFound();
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? (approve ? "اعتُمد طلب العكس: عُكس التشغيل وأُبطلت دفعته وتحررت مدخلاته." : "رُفض طلب العكس؛ التشغيل ودفعته كما هما.")
            : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
