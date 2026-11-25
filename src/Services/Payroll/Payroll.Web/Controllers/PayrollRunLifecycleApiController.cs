using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;

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

/// <summary>The body of a rejection: the note that says what to correct (mandatory).</summary>
public sealed record RejectPayrollRunApiRequest(string? Notes);

/// <summary>The body of a cancellation: its reason (mandatory).</summary>
public sealed record CancelPayrollRunApiRequest(string? Reason);
