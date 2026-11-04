using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-42 (G-COD-05): an official number, for the service that owns the kind, in the tenant of the request. <c>TenantId</c> in the
/// body is never read here. No interactive user reaches this: a number is consumed the moment it is issued.
/// <para>
/// The class demands an antiforgery token, so a POST added here later is protected by default. These two actions opt out: their
/// policy needs a client_credentials token, which no browser holds, so there is no ambient credential a forged request could ride on.
/// </para>
/// </summary>
[ApiController]
[Route("api/internal/codes")]
[Authorize(Policy = ServicePolicies.CodeGenerate)]
[ValidateAntiForgeryToken]
public sealed class CodeGenerationInternalController(ICodeGenerator generator, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("generate")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Generate([FromBody] CodeGenerationRequest request, CancellationToken ct)
    {
        var refusal = Prepare(request, preview: false);
        return refusal ?? Ok(await generator.GenerateForServiceAsync(request, ServiceCallerPrincipal.GetClientId(User)!, ct));
    }

    [HttpPost("preview")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Preview([FromBody] CodeGenerationRequest request, CancellationToken ct)
    {
        var refusal = Prepare(request, preview: true);
        return refusal ?? Ok(await generator.GenerateAsync(request, ct));
    }

    private IActionResult? Prepare(CodeGenerationRequest request, bool preview)
    {
        if (!CodeKindOwners.Owns(ServiceCallerPrincipal.GetClientId(User), request.EntityKind))
            return Forbid();

        var tenantId = tenantContext.TenantId;
        if (tenantId is null)
            return BadRequest("The request has no tenant (X-Tenant-ID).");

        request.TenantId = tenantId;
        request.Preview = preview;
        return null;
    }
}
