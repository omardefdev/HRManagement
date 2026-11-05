using System.Text;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-42 (G-POL-08): the only way to a policy decision over HTTP, for the service that owns the domain. The body says what is
/// being decided; who decides, in which mode, for whom and in which tenant is the server's. The reply is 200 for an allow and
/// for a denial, with <see cref="PolicyEnforceResponse.EffectiveMode"/>; 422 is gone.
/// <para>
/// The class demands an antiforgery token, so a POST added here later is protected by default. <c>Enforce</c> opts out: its policy
/// needs a client_credentials token, which no browser holds, so there is no ambient credential a forged request could ride on.
/// </para>
/// </summary>
[ApiController]
[Route("api/internal/policies")]
[Authorize(Policy = ServicePolicies.PolicyEnforceS2S)]
[ValidateAntiForgeryToken]
public sealed class PolicyEnforceInternalController(
    IPolicyOrchestrator orchestrator,
    IPolicyModeResolver modeResolver,
    ITenantContext tenantContext) : ControllerBase
{
    public const int MaxContextJsonBytes = 8 * 1024;

    [HttpPost("enforce")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Enforce([FromBody] PolicyEnforceRequest request, CancellationToken ct)
    {
        var clientId = ServiceCallerPrincipal.GetClientId(User);
        if (!PolicyDomainOwners.Owns(clientId, request.Domain))
            return Forbid();

        if (!PolicyDomainOwners.AllowsSubjectType(request.Domain, request.SubjectType))
            return BadRequest("SubjectType is not allowed in this domain.");

        if (request.ContextJson is not null && Encoding.UTF8.GetByteCount(request.ContextJson) > MaxContextJsonBytes)
            return BadRequest("ContextJson is larger than 8 KB.");

        var tenantId = tenantContext.TenantId;
        // Guid.Empty is an entity not saved yet: its tenant is stamped at save, so it names no other tenant.
        if (request.TenantId is { } sent && sent != Guid.Empty && sent != tenantId)
            return BadRequest("TenantId differs from the tenant of the request.");

        request.TenantId = tenantId;
        request.Mode = modeResolver.Resolve(request.Domain);
        request.ActorUserId = ParseActingUser();
        request.ActorUserName = null;

        return Ok(await orchestrator.EnforceForServiceAsync(request, clientId!, ct));
    }

    private Guid? ParseActingUser() =>
        Guid.TryParse(Request.Headers[ForwardActingUserHandler.HeaderName].ToString(), out var actor) ? actor : null;
}
