using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58 (§3, §7, P-4): the four-eyes requests over the API. <c>GET api/payroll/dual-control</c> is the caller's inbox (the pending
/// requests he may decide, without his own); <c>POST api/payroll/dual-control/{id}/decide</c> is the decision (a holder of the kind's
/// policy who is not the requester; the act happens with it). Requests are made on their target's route, each under its kind's policy:
/// a hold's release (<c>payment-holds/{id}/release</c>), a critical alert (<c>fraud-alerts/{id}/resolution-requests</c>), an arrears row
/// (<c>arrears/{id}/adjustment-requests</c>), a bank file's reissue and a batch's settlement or rejection (<c>payment-batches/…</c>). The
/// actor of each is the caller, from his token. Writes validate an antiforgery token (<c>GET api/payroll/dual-control/antiforgery</c>).
/// The requests belong to the whole agency.
/// </summary>
[ApiController]
[Route("api/payroll")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeCentralOnly("a four-eyes request acts on an agency's payroll records, not a unit's")]
public sealed class DualControlApiController(PayrollDualControlService dualControl, IAuthorizationService authorization, PayrollDbContext db)
    : ControllerBase
{
    [HttpGet("dual-control/antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    /// <summary>«طلبات بانتظار قرارك»: the pending requests the caller may decide, without the ones he asked for himself.</summary>
    [HttpGet("dual-control")]
    public async Task<IActionResult> Inbox(CancellationToken ct)
    {
        var items = await dualControl.InboxAsync(PayProfileChangeHttp.RequireUserId(User), MayDecideAsync, ct);
        return Ok(items.Select(i => new
        {
            i.Id,
            kind = i.Kind.ToString(),
            i.TargetId,
            reason = i.Payload.Reason,
            amount = i.Payload.Amount,
            reference = i.Payload.Reference,
            i.RequestedBy,
            i.RequestedAt,
            i.ExpiresAt,
        }));
    }

    /// <summary>One request, whoever asks (it carries no account number).</summary>
    [HttpGet("dual-control/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var request = await db.PayrollDualControlRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
            return NotFound(new { error = PayrollDualControlMessages.RequestNotFound });
        var payload = PayrollDualControlPayload.Read(request.PayloadJson);
        return Ok(new
        {
            request.Id,
            kind = request.Kind.ToString(),
            request.TargetId,
            status = request.Status.ToString(),
            reason = payload.Reason,
            amount = payload.Amount,
            reference = payload.Reference,
            request.RequestedBy,
            request.RequestedAt,
            request.ExpiresAt,
            request.DecidedBy,
            request.DecidedAt,
            request.DecisionNotes,
        });
    }

    /// <summary>
    /// The decision: approve executes the act in the same transaction; reject needs a note. 400 for the requester or an expired request,
    /// 403 for a caller who does not hold the kind's policy, 404 another agency's request, 409 a request decided meanwhile.
    /// </summary>
    [HttpPost("dual-control/{id:guid}/decide")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Decide(Guid id, [FromBody] DecideDualControlApiRequest? request, CancellationToken ct) =>
        PayrollRunActHttp.Answer(await dualControl.DecideAsync(
            id, request?.Approve ?? false, request?.Notes, PayProfileChangeHttp.RequireUserId(User), MayDecideAsync, ct));

    /// <summary>A critical alert's resolution, asked by an approver; another decides it.</summary>
    [HttpPost("fraud-alerts/{id:guid}/resolution-requests")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> RequestAlertResolution(Guid id, [FromBody] DualControlReasonApiRequest? request, CancellationToken ct) =>
        Asked(await dualControl.RequestAsync(PayrollDualControlKind.CriticalAlertResolve, id,
            new PayrollDualControlPayload(request?.Reason ?? string.Empty, TargetStatus: request?.TargetStatus), PayProfileChangeHttp.RequireUserId(User), ct));

    /// <summary>An arrears row's manual adjustment, asked by the processor with the amounts; an approver decides it.</summary>
    [HttpPost("arrears/{id:guid}/adjustment-requests")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> RequestArrearsAdjustment(Guid id, [FromBody] ArrearsAdjustmentApiRequest? request, CancellationToken ct) =>
        Asked(await dualControl.RequestAsync(PayrollDualControlKind.ArrearsAdjust, id,
            new PayrollDualControlPayload(request?.Reason ?? string.Empty, OriginalAmount: request?.OriginalAmount, PaidAmount: request?.PaidAmount),
            PayProfileChangeHttp.RequireUserId(User), ct));

    private async Task<bool> MayDecideAsync(PayrollDualControlKind kind) =>
        (await authorization.AuthorizeAsync(User, PayrollDualControlKinds.DecisionPolicy(kind))).Succeeded;

    internal static IActionResult Asked(Payroll.Application.Features.PayrollRuns.Lifecycle.RunActResult result) =>
        result.Success
            ? new AcceptedResult((string?)null, new { requestId = result.Id, status = PayrollDualControlStatus.Pending.ToString() })
            : PayrollRunActHttp.Answer(result);
}

/// <summary>PLAN-58: a decision. The decider is the caller.</summary>
public sealed record DecideDualControlApiRequest(bool Approve, string? Notes = null);

/// <summary>PLAN-58: a request's reason (and, for an alert, the status it is resolved to: Resolved or Dismissed).</summary>
public sealed record DualControlReasonApiRequest(string? Reason, string? TargetStatus = null);

/// <summary>PLAN-58: an arrears adjustment's amounts and reason.</summary>
public sealed record ArrearsAdjustmentApiRequest(decimal? OriginalAmount, decimal? PaidAmount, string? Reason);
