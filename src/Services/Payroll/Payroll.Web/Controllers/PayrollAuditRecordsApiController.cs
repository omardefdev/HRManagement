using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.PayrollRuns.Review;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58 (§4, D-PAY-3): the independent review over the API — what the acceptance used to write with SQL. An auditor opens the review
/// record of a run (<c>POST api/payroll/runs/{id}/audit-records</c>) and another auditor closes it (<c>POST api/payroll/audit-records/{id}/close</c>);
/// neither made, approved or posts the run (400), the closer is not the opener (400). The reviewer reaches these and a run's read, and no
/// other payroll API (the gateway's <c>GatewayPayrollReviewPaths</c>); a holder of the approval role alone is refused here (403). Writes
/// validate an antiforgery token (<c>GET api/payroll/audit-records/antiforgery</c>).
/// </summary>
[ApiController]
[Route("api/payroll")]
[ModulePolicyOverride(HrManagementPolicies.PayrollAuditReview)]
[Authorize(Policy = HrManagementPolicies.PayrollAuditReview)]
[OrgScopeCentralOnly("a payroll run's review belongs to the whole agency, not to a unit")]
public sealed class PayrollAuditRecordsApiController(IMediator mediator) : ControllerBase
{
    [HttpGet("audit-records/antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpPost("runs/{id:guid}/audit-records")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollAuditReview)]
    public async Task<IActionResult> Open(Guid id, [FromBody] OpenPayrollAuditRecordApiRequest? request, CancellationToken ct)
    {
        var result = await mediator.Send(new OpenPayrollAuditRecordCommand(id, request?.Findings, PayProfileChangeHttp.RequireUserId(User)), ct);
        return PayrollRunActHttp.Answer(result, result.Success ? new { recordId = result.Id } : null);
    }

    [HttpPost("audit-records/{id:guid}/close")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollAuditReview)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        PayrollRunActHttp.Answer(await mediator.Send(new ClosePayrollAuditRecordCommand(id, PayProfileChangeHttp.RequireUserId(User)), ct));
}

/// <summary>PLAN-58: the opening auditor's findings so far.</summary>
public sealed record OpenPayrollAuditRecordApiRequest(string? Findings);
