using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Features.ReferenceChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-53 (§4, D-53-4): «طلبات البيانات المرجعية». The requests to create, change or delete a salary scale or a grade: the list, the
/// details as a before-and-after table with Arabic field names (the stored JSON only under «عرض تقني»), and the decision of a payroll
/// approver who is not the requester. An approval applies the change in its own save.
/// </summary>
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeNotApplicable("salary scales and grades belong to the agency, not to a branch")]
public sealed class PayrollReferenceChangesController(PayrollDbContext db, IMediator mediator) : Controller
{
    public async Task<IActionResult> Index(PayrollReferenceChangeState? state, PayrollReferenceEntityType? entityType)
    {
        var query = db.PayrollReferenceChangeRequests.AsNoTracking().AsQueryable();
        if (state.HasValue) query = query.Where(r => r.State == state.Value);
        if (entityType.HasValue) query = query.Where(r => r.EntityType == entityType.Value);
        return View(new PayrollReferenceChangeListViewModel
        {
            Items = await query.OrderByDescending(r => r.RequestedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync(HttpContext.RequestAborted),
            State = state,
            EntityType = entityType,
            CanApprove = PayrollPolicyRules.CanApprove(User),
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await DetailsModelAsync(id);
        return model is null ? NotFound() : View(model);
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
        var result = await mediator.Send(new CancelPayrollReferenceChangeCommand(
            id, reason, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User), PayrollPolicyRules.CanApprove(User)),
            HttpContext.RequestAborted);
        return await AnswerAsync(id, result, "أُلغي الطلب");
    }

    private async Task<IActionResult> DecideAsync(Guid id, PayProfileChangeDecision decision, string? note)
    {
        var result = await mediator.Send(new DecidePayrollReferenceChangeCommand(
            id, decision, note, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User)), HttpContext.RequestAborted);
        return await AnswerAsync(id, result, result.State == PayrollReferenceChangeState.Applied ? "اعتُمد الطلب وطُبّق التغيير" : "رُفض الطلب");
    }

    /// <summary>The same answers as a salary change: 404, 403 for one's own request, and the page again with the message for 400 and 409.</summary>
    private async Task<IActionResult> AnswerAsync(Guid id, PayrollReferenceChangeResult result, string done)
    {
        if (result.Success)
        {
            TempData["Success"] = done;
            return RedirectToAction(nameof(Details), new { id });
        }

        switch (result.Failure)
        {
            case PayProfileChangeFailure.NotFound:
                return NotFound();
            case PayProfileChangeFailure.SelfDecision or PayProfileChangeFailure.Forbidden:
                return new ObjectResult(result.Error) { StatusCode = StatusCodes.Status403Forbidden };
        }

        db.ChangeTracker.Clear();
        var model = await DetailsModelAsync(id);
        if (model is null)
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر تنفيذ الإجراء");
        Response.StatusCode = result.StatusCode;
        return View(nameof(Details), model);
    }

    private async Task<PayrollReferenceChangeDetailsViewModel?> DetailsModelAsync(Guid id)
    {
        var request = await db.PayrollReferenceChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, HttpContext.RequestAborted);
        if (request is null) return null;

        var me = PayProfileChangeHttp.RequireUserId(User);
        var approver = PayrollPolicyRules.CanApprove(User);
        var own = request.RequestedBy == me;
        return new PayrollReferenceChangeDetailsViewModel
        {
            Request = request,
            Rows = await RowsAsync(request),
            CanDecide = approver && !own && request.IsOpen,
            CanCancel = request.IsOpen && (approver || own),
            IsOwnRequest = approver && own && request.IsOpen,
            ProfilesOutsideProposedBounds = await OutsideProposedBoundsAsync(request),
        };
    }

    /// <summary>The fields of the request as a readable table: the Arabic name, the current value, the proposed value.</summary>
    private async Task<IReadOnlyList<PayrollReferenceChangeFieldRow>> RowsAsync(PayrollReferenceChangeRequest request)
    {
        const string none = "—";
        static string Money(decimal? value) => value?.ToString("N2") ?? none;
        static string Date(DateOnly? value) => value is DateOnly d ? HrLocalTime.FormatDate(d) : none;
        static string YesNo(bool? value) => value is null ? none : value.Value ? "نعم" : "لا";

        if (request.EntityType == PayrollReferenceEntityType.PayGrade)
        {
            var current = PayrollReferenceChangeJson.Read<PayGradeChange>(request.CurrentJson);
            var proposed = PayrollReferenceChangeJson.Read<PayGradeChange>(request.ProposedJson);
            var scaleIds = new[] { current?.SalaryScaleId, proposed?.SalaryScaleId }.Where(s => s.HasValue).Select(s => s!.Value).Distinct().ToList();
            var scales = await db.SalaryScales.AsNoTracking().Where(s => scaleIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.NameAr, HttpContext.RequestAborted);
            string Scale(Guid? scaleId) => scaleId is Guid s ? scales.GetValueOrDefault(s, "جدول محذوف") : none;
            return
            [
                new("جدول الرواتب", Scale(current?.SalaryScaleId), Scale(proposed?.SalaryScaleId)),
                new("كود الدرجة", current?.GradeCode ?? none, proposed?.GradeCode ?? none),
                new("اسم الدرجة", current?.GradeNameAr ?? none, proposed?.GradeNameAr ?? none),
                new("الراتب الأساسي", Money(current?.BaseSalary), Money(proposed?.BaseSalary)),
                new("الحد الأدنى", Money(current?.MinSalary), Money(proposed?.MinSalary)),
                new("الحد الأعلى", Money(current?.MaxSalary), Money(proposed?.MaxSalary)),
            ];
        }

        var currentScale = PayrollReferenceChangeJson.Read<SalaryScaleChange>(request.CurrentJson);
        var proposedScale = PayrollReferenceChangeJson.Read<SalaryScaleChange>(request.ProposedJson);
        return
        [
            new("الكود", currentScale?.Code ?? none, proposedScale?.Code ?? none),
            new("الاسم بالعربية", currentScale?.NameAr ?? none, proposedScale?.NameAr ?? none),
            new("ساري من", Date(currentScale?.EffectiveFrom), Date(proposedScale?.EffectiveFrom)),
            new("ساري إلى", Date(currentScale?.EffectiveTo), Date(proposedScale?.EffectiveTo)),
            new("فعّال", YesNo(currentScale?.IsActive), YesNo(proposedScale?.IsActive)),
        ];
    }

    /// <summary>Rule 1: new bounds change no pay profile. The approver sees how many active profiles they would leave outside.</summary>
    private async Task<int?> OutsideProposedBoundsAsync(PayrollReferenceChangeRequest request)
    {
        if (request is not { EntityType: PayrollReferenceEntityType.PayGrade, Operation: PayrollReferenceOperation.Update, EntityId: Guid gradeId })
            return null;
        if (PayrollReferenceChangeJson.Read<PayGradeChange>(request.ProposedJson) is not { } proposed)
            return null;
        var (min, max) = (proposed.MinSalary, proposed.MaxSalary);
        // Counted within the caller's branch scope, as every pay profile he is shown (PLAN-52).
        var profiles = await db.EmployeePayProfiles.AsNoTracking().InOrgScopeAsync(HttpContext, db);
        return await profiles.CountAsync(
            p => p.PayGradeId == gradeId && p.IsActive && p.BaseSalary > 0
                 && ((min > 0 && p.BaseSalary < min) || (max > 0 && p.BaseSalary > max)),
            HttpContext.RequestAborted);
    }
}
