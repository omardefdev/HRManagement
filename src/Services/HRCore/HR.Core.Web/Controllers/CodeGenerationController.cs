using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HR.Core.Domain.Entities.CodeGeneration;

namespace HR.Core.Web.Controllers;

[Route("api/codes")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class CodeGenerationController(
    ICodeGenerator generator,
    HRCoreDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] CodeGenerationRequest request, CancellationToken ct)
    {
        // PLAN-42: a preview consumes no number, and it runs in the tenant of the user, whatever the body says.
        if (tenantContext.TenantId is null)
            return BadRequest("The request has no tenant.");
        request.TenantId = tenantContext.TenantId;
        request.Preview = true;
        var result = await generator.GenerateAsync(request, ct);
        return Ok(result);
    }

    /// <summary>PLAN-48: the agency's overlays and the national templates it inherits (read-only).</summary>
    [HttpGet("policies")]
    public async Task<IActionResult> ListPolicies([FromQuery] CodeEntityKind? entityKind, CancellationToken ct)
    {
        var q = db.ReadableCodePolicyTemplates().AsNoTracking()
            .Include(t => t.Versions)
            .AsQueryable();
        if (entityKind.HasValue)
            q = q.Where(t => t.EntityKind == entityKind.Value);
        var items = await q.OrderBy(t => t.EntityKind).ThenBy(t => t.Name).ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>
    /// PLAN-48: the policy the session's tenant numbers the kind (and key) with. Another tenant's snapshot is for a platform
    /// operator only; anyone else gets their own tenant's, whatever the query says.
    /// </summary>
    [HttpGet("policies/{entityKind}")]
    public async Task<IActionResult> GetPolicySnapshot(
        CodeEntityKind entityKind,
        [FromQuery] Guid? tenantId,
        [FromQuery] string? policyKey,
        CancellationToken ct)
    {
        var tenant = tenantContext.IsPlatformOperator && tenantId is Guid asked ? asked : tenantContext.TenantId;
        var snapshot = await generator.GetPublishedSnapshotAsync(entityKind, tenant, ct, policyKey);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    [HttpGet("issuances")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ListIssuances(
        [FromQuery] CodeEntityKind? entityKind,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var q = db.CodeIssuances.AsNoTracking().AsQueryable();
        if (entityKind.HasValue)
            q = q.Where(i => i.EntityKind == entityKind.Value);
        var items = await q.OrderByDescending(i => i.IssuedAt).Take(take).ToListAsync(ct);
        return Ok(items);
    }
}
