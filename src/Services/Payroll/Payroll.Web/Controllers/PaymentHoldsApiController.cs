using System.Security.Claims;
using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.Fraud;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/payroll/payment-holds")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
public class PaymentHoldsApiController(IMediator mediator) : ControllerBase
{
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    [HttpPost("{id:guid}/release")]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Release(Guid id, [FromBody] ReleasePaymentHoldApiRequest request, CancellationToken ct)
    {
        var (success, error) = await mediator.Send(new ReleasePaymentHoldCommand(
            id,
            RequireUserId(),
            request.ApproverUserId,
            request.ReleaseReason,
            request.EvidenceRecordId), ct);

        if (!success) return BadRequest(new { error });
        return Ok();
    }
}

public record ReleasePaymentHoldApiRequest(
    Guid ApproverUserId,
    string ReleaseReason,
    Guid? EvidenceRecordId = null);
