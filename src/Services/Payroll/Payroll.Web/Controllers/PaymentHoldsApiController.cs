using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Services;
using Payroll.Domain.Enums;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58 (§3, G-PAY-09): a payment hold's release is four eyes: this call asks for it, with a reason, and another holder of
/// <c>Payroll.Approve</c> decides it from his own session (<c>POST api/payroll/dual-control/{id}/decide</c>). The caller is the requester,
/// from his token; an approver id in the body is not read (the body has no such field). Writes validate an antiforgery token
/// (<c>GET api/payroll/payment-holds/antiforgery</c>).
/// </summary>
[ApiController]
[Route("api/payroll/payment-holds")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
public class PaymentHoldsApiController(PayrollDualControlService dualControl) : ControllerBase
{
    [HttpGet("antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    /// <summary>Asks for the release of a hold (202 with the request's id); 409 while one waits on the hold.</summary>
    [HttpPost("{id:guid}/release")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Release(Guid id, [FromBody] ReleasePaymentHoldApiRequest request, CancellationToken ct)
    {
        var result = await dualControl.RequestAsync(
            PayrollDualControlKind.HoldRelease, id, new PayrollDualControlPayload(request.ReleaseReason ?? string.Empty),
            PayProfileChangeHttp.RequireUserId(User), ct);
        return result.Success
            ? Accepted(new { requestId = result.Id, status = PayrollDualControlStatus.Pending.ToString() })
            : PayrollRunActHttp.Answer(result);
    }
}

/// <summary>PLAN-58: the release's reason. The approver is the person who decides the request, never a field of this body.</summary>
public record ReleasePaymentHoldApiRequest(string? ReleaseReason, Guid? EvidenceRecordId = null);
