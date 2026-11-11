using HR.Core.Infrastructure.Services.Delegation;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-50 (§5, P-5, P-8): the delegations in force of one user, for the services whose approvals honour them (svc-hr-movements with
/// <c>hr-core.lookup</c>); an interactive user gets 403. The tenant is the call's <c>X-Tenant-ID</c> only (resolved by the tenant
/// middleware into the tenant context, which the EF filter and row security read): a call that names no tenant gets nothing.
/// </summary>
[ApiController]
[Route("api/internal/approval-delegations")]
[Authorize(Policy = ServicePolicies.ApprovalDelegationRead)]
public sealed class ApprovalDelegationsInternalController(DbApprovalDelegationLookup lookup, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("active")]
    [Authorize(Policy = ServicePolicies.ApprovalDelegationRead)]
    public async Task<ActionResult<ApprovalDelegationsResponse>> Active(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? employeeId,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenant || tenant == Guid.Empty)
            return Ok(new ApprovalDelegationsResponse([]));

        var delegations = await lookup.GetActiveForAsync(
            userId is Guid u && u != Guid.Empty ? u : null,
            employeeId is Guid e && e != Guid.Empty ? e : null,
            ct);
        return Ok(new ApprovalDelegationsResponse(delegations));
    }
}
