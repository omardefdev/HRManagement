using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-56: the acts of a payroll run's life over the API: a rejection that reopens the run, a cancellation with its reason, and
/// the restart of a calculation that failed or was paused. Every write names its policy and validates an antiforgery token (the API
/// is reached with a gateway session as well as with a bearer token); the token comes from <c>GET api/payroll/run-lifecycle/antiforgery</c>.
/// The answers follow one rule (<see cref="PayrollRunActHttp"/>): 400 a missing note or reason or a state the act cannot leave, 403 a
/// caller the run's state does not admit, 404 another agency's run or job, 409 a request that lost to another.
/// </summary>
[ApiController]
[Route("api/payroll")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeCentralOnly("a payroll run belongs to the whole agency, not to a unit")]
public sealed class PayrollRunLifecycleApiController(IMediator mediator, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>The antiforgery token every POST of this controller must send (the header's name and its value); the cookie is set here.</summary>
    [HttpGet("run-lifecycle/antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    /// <summary>The reviewer rejects the run with a note; the run reopens as a draft with its version one more.</summary>
    [HttpPost("runs/{id:guid}/reject")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPayrollRunApiRequest? request, CancellationToken ct) =>
        PayrollRunActHttp.Answer(
            await mediator.Send(new RejectPayrollRunCommand(id, PayProfileChangeHttp.RequireUserId(User), request?.Notes), ct));

    /// <summary>
    /// Cancels a run that is not posted, with a reason. D-PAY-18: the processor cancels what was not sent for review; a run under
    /// review or approved needs the approval policy (403 otherwise, the run unchanged).
    /// </summary>
    [HttpPost("runs/{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollRunCancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelPayrollRunApiRequest? request, CancellationToken ct)
    {
        var (mayProcess, mayApprove) = await PayrollRunActHttp.CancelRightsAsync(authorization, User);
        return PayrollRunActHttp.Answer(await mediator.Send(
            new CancelPayrollRunCommand(id, PayProfileChangeHttp.RequireUserId(User), request?.Reason, mayProcess, mayApprove), ct));
    }

    /// <summary>
    /// Gives up a failed or paused calculation and starts a new one with a new snapshot: the way out of a fingerprint conflict.
    /// </summary>
    [HttpPost("jobs/{jobId:guid}/restart")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Restart(Guid jobId, CancellationToken ct)
    {
        var (result, lines, newJobId) = await mediator.Send(
            new RestartCalculationCommand(jobId, PayProfileChangeHttp.RequireUserId(User)), ct);
        return PayrollRunActHttp.Answer(result, new { runId = result.Id, jobId = newJobId, linesGenerated = lines });
    }

    /// <summary>
    /// Asks for the reversal of a posted run, with a reason. Another person decides (<c>reversal-requests/{id}/decide</c>). 409 while
    /// a request waits on the run.
    /// </summary>
    [HttpPost("runs/{id:guid}/reversal-requests")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollReverse)]
    public async Task<IActionResult> RequestReversal(Guid id, [FromBody] RequestPayrollReversalApiRequest? request, CancellationToken ct) =>
        PayrollRunActHttp.Answer(await mediator.Send(
            new RequestPayrollReversalCommand(id, PayProfileChangeHttp.RequireUserId(User), request?.Reason), ct));

    /// <summary>
    /// Decides a reversal request. The decider is neither the requester nor the run's poster (400). An approval voids the run's
    /// batches that have not left and reverses the run; if any batch has left it is refused with D-PAY-16's message (400) and
    /// nothing changes. A rejection needs its note. 409 for a request that was decided already.
    /// </summary>
    [HttpPost("reversal-requests/{id:guid}/decide")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> DecideReversal(Guid id, [FromBody] DecidePayrollReversalApiRequest? request, CancellationToken ct) =>
        PayrollRunActHttp.Answer(await mediator.Send(
            new DecidePayrollReversalCommand(id, PayProfileChangeHttp.RequireUserId(User), request?.Approve ?? false, request?.Notes), ct));

    /// <summary>The agency's reversal requests, the newest first; <c>status</c> narrows them.</summary>
    [HttpGet("reversal-requests")]
    public async Task<IActionResult> ReversalRequests(
        [FromQuery] PayrollReversalRequestStatus? status, [FromServices] PayrollDbContext db, CancellationToken ct)
    {
        var query = db.PayrollReversalRequests.AsNoTracking();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        var rows = await query
            .OrderByDescending(r => r.RequestedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .Select(r => new PayrollReversalRequestDto(
                r.Id, r.PayrollRunId, r.PayrollRun.PeriodStart, r.PayrollRun.PeriodEnd, r.PayrollRun.Status,
                r.RequestedBy, r.RequestedAt, r.Reason, r.Status, r.DecidedBy, r.DecidedAt, r.DecisionNotes))
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// Creates the correction run of a posted run from the approved adjustments aimed at it. 409 while that run has a correction not
    /// yet posted; 400 when no approved adjustment is aimed at it, or the run is not posted.
    /// </summary>
    [HttpPost("runs/{id:guid}/corrections")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> CreateCorrection(Guid id, CancellationToken ct)
    {
        var correction = await mediator.Send(new CreateCorrectionRunCommand(id, DateTime.UtcNow), ct);
        return PayrollRunActHttp.Answer(correction.Result, new { id = correction.CorrectionRunId, linesGenerated = correction.LinesGenerated });
    }
}

/// <summary>The body of a reversal request: its reason (mandatory).</summary>
public sealed record RequestPayrollReversalApiRequest(string? Reason);

/// <summary>The body of a decision on a reversal request: approve or reject, with a note (mandatory for a rejection).</summary>
public sealed record DecidePayrollReversalApiRequest(bool Approve, string? Notes);

/// <summary>A reversal request as the API shows it.</summary>
public sealed record PayrollReversalRequestDto(
    Guid Id,
    Guid PayrollRunId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    PayrollRunStatus RunStatus,
    Guid RequestedBy,
    DateTime RequestedAt,
    string Reason,
    PayrollReversalRequestStatus Status,
    Guid? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNotes);

/// <summary>The body of a rejection: the note that says what to correct (mandatory).</summary>
public sealed record RejectPayrollRunApiRequest(string? Notes);

/// <summary>The body of a cancellation: its reason (mandatory).</summary>
public sealed record CancelPayrollRunApiRequest(string? Reason);
