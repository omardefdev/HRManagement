using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-53 (§2): «طلبات تغيير الراتب». The list (the pending ones for an approver, the scheduled ones a cycle could not apply), the
/// details with the current and the proposed values and the decision history, and the actions the caller's policies allow. A restricted
/// user lists his branch's requests, and a foreign request is 404 (the scope filter, before the action runs).
/// </summary>
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScoped(typeof(PayProfileChangeRequest))]
public sealed class PayProfileChangesController(PayrollDbContext db, IMediator mediator) : Controller
{
    public async Task<IActionResult> Index(PayProfileChangeState? state, Guid? employeeId, bool blockedOnly = false)
    {
        var query = await db.PayProfileChangeRequests.AsNoTracking().InOrgScopeAsync(HttpContext, db);
        if (state.HasValue) query = query.Where(r => r.State == state.Value);
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (blockedOnly) query = query.Where(r => r.State == PayProfileChangeState.Scheduled && r.LastApplyError != null);
        var items = await query.OrderByDescending(r => r.RequestedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync(HttpContext.RequestAborted);

        return View(new PayProfileChangeListViewModel
        {
            Items = await RowsAsync(items),
            State = state,
            EmployeeId = employeeId,
            BlockedOnly = blockedOnly,
            CanRequest = PayrollPolicyRules.CanProcess(User),
            CanApprove = PayrollPolicyRules.CanApprove(User),
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await DetailsModelAsync(id);
        return model is null ? NotFound() : View(model);
    }

    private async Task<PayProfileChangeDetailsViewModel?> DetailsModelAsync(Guid id)
    {
        var request = await db.PayProfileChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, HttpContext.RequestAborted);
        if (request is null) return null;

        var me = PayProfileChangeHttp.RequireUserId(User);
        var approver = PayrollPolicyRules.CanApprove(User);
        var own = request.RequestedBy == me;
        return new PayProfileChangeDetailsViewModel
        {
            Row = (await RowsAsync([request])).Single(),
            CanDecide = approver && !own && request.State == PayProfileChangeState.PendingApproval,
            CanCancel = request.IsOpen && (approver || own),
            IsOwnRequest = approver && own && request.State == PayProfileChangeState.PendingApproval,
        };
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Approve(Guid id, string? note) => DecideAsync(id, PayProfileChangeDecision.Approved, note);

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Reject(Guid id, string? note) => DecideAsync(id, PayProfileChangeDecision.Rejected, note);

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollChangeCancel)]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var result = await mediator.Send(new CancelPayProfileChangeCommand(
            id, reason, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User),
            PayrollPolicyRules.CanApprove(User), await HttpContext.GetOrgScopeAsync()), HttpContext.RequestAborted);
        return await AnswerAsync(id, result, "أُلغي الطلب");
    }

    private async Task<IActionResult> DecideAsync(Guid id, PayProfileChangeDecision decision, string? note)
    {
        var result = await mediator.Send(new DecidePayProfileChangeCommand(
            id, decision, note, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User),
            await HttpContext.GetOrgScopeAsync()), HttpContext.RequestAborted);
        var done = result.State switch
        {
            PayProfileChangeState.Applied => "اعتُمد الطلب وطُبّق على ملف الأجر",
            PayProfileChangeState.Scheduled => "اعتُمد الطلب وسيُطبَّق في تاريخ سريانه",
            _ => "رُفض الطلب",
        };
        return await AnswerAsync(id, result, done);
    }

    /// <summary>
    /// The form's answer, with the API's status: 404 outside the scope, 403 for one's own request, and for a broken rule (400) or a
    /// state conflict (409) the details page again with the Arabic message. Nothing changed in any of them.
    /// </summary>
    private async Task<IActionResult> AnswerAsync(Guid id, PayProfileChangeResult result, string done)
    {
        if (result.Success)
        {
            TempData["Success"] = done;
            if (result.Warning is not null) TempData["Warning"] = result.Warning;
            return RedirectToAction(nameof(Details), new { id });
        }

        switch (result.Failure)
        {
            case PayProfileChangeFailure.NotFound:
                return NotFound();
            case PayProfileChangeFailure.SelfDecision or PayProfileChangeFailure.Forbidden:
                return new ObjectResult(result.Error) { StatusCode = StatusCodes.Status403Forbidden };
        }

        // The request as it is now (the unit of work of the refused command is not reused: the page reads without tracking).
        db.ChangeTracker.Clear();
        var model = await DetailsModelAsync(id);
        if (model is null)
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر تنفيذ الإجراء");
        Response.StatusCode = result.StatusCode;
        return View(nameof(Details), model);
    }

    private async Task<IReadOnlyList<PayProfileChangeRow>> RowsAsync(IReadOnlyList<PayProfileChangeRequest> requests)
    {
        var gradeIds = requests.SelectMany(r => new[] { r.CurrentPayGradeId, r.ProposedPayGradeId })
            .Where(g => g.HasValue).Select(g => g!.Value).Distinct().ToList();
        var grades = gradeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.PayGrades.AsNoTracking().Where(g => gradeIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.GradeNameAr, HttpContext.RequestAborted);
        return requests.Select(r => new PayProfileChangeRow(
            r,
            r.CurrentPayGradeId is Guid current ? grades.GetValueOrDefault(current) : null,
            r.ProposedPayGradeId is Guid proposed ? grades.GetValueOrDefault(proposed) : null)).ToList();
    }
}
