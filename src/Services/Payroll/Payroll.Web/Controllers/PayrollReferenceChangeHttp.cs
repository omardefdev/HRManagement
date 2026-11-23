using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Features.ReferenceChanges;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-53 (§4): what the salary scale and grade controllers share. Create, Edit and Delete do not write the record: they submit a
/// request that another person approves, and land on the request's page. A refusal shows the form again with the Arabic message and
/// the API's status (400 a broken rule, 409 a conflict, 404 an unknown record); nothing changed.
/// </summary>
internal static class PayrollReferenceChangeHttp
{
    public static Task<PayrollReferenceChangeResult> SubmitAsync(
        this Controller controller, IMediator mediator, PayrollReferenceEntityType type, PayrollReferenceOperation operation, Guid? id,
        PayGradeChange? grade, SalaryScaleChange? scale, PayrollReferenceChangeFieldsViewModel fields) =>
        mediator.Send(
            new SubmitPayrollReferenceChangeCommand(
                type, operation, id, grade, scale, fields.Reason, fields.LegalReference,
                PayProfileChangeHttp.RequireUserId(controller.User), PayProfileChangeHttp.UserName(controller.User)),
            controller.HttpContext.RequestAborted);

    /// <summary>After a submission: the request's page, or null when the form must be shown again (the model state holds the message).</summary>
    public static IActionResult? Submitted(this Controller controller, PayrollReferenceChangeResult result)
    {
        if (result.Success)
        {
            controller.TempData["Success"] = result.State == PayrollReferenceChangeState.Applied
                ? "طُبّق التغيير (بيئة التطوير: الاعتماد معطّل في الإعداد)"
                : "قُدّم طلب التغيير، وهو بانتظار اعتماد معتمد رواتب آخر";
            return controller.RedirectToAction(nameof(PayrollReferenceChangesController.Details), "PayrollReferenceChanges", new { id = result.RequestId });
        }

        if (result.Failure == PayProfileChangeFailure.NotFound)
            return controller.NotFound();
        controller.ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر تقديم الطلب");
        controller.Response.StatusCode = result.StatusCode;
        return null;
    }

    /// <summary>The pending request of each record of a type, by the record's id.</summary>
    public static async Task<Dictionary<Guid, Guid>> OpenRequestsAsync(PayrollDbContext db, PayrollReferenceEntityType type, CancellationToken ct) =>
        (await db.PayrollReferenceChangeRequests.AsNoTracking()
            .Where(r => r.EntityType == type && r.State == PayrollReferenceChangeState.PendingApproval && r.EntityId != null)
            .Select(r => new { EntityId = r.EntityId!.Value, r.Id })
            .ToListAsync(ct))
        .GroupBy(r => r.EntityId).ToDictionary(g => g.Key, g => g.First().Id);
}
