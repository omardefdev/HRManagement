using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HR.Core.Domain.Entities.CodeGeneration;

namespace HR.Core.Web.Controllers;

[Route("api/codes")]
[Route("api/internal/codes")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class CodeGenerationController(
    ICodeGenerator generator,
    HRCoreDbContext db) : ControllerBase
{
    [HttpPost("generate")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Generate([FromBody] CodeGenerationRequest request, CancellationToken ct)
    {
        request.Preview = false;
        var result = await generator.GenerateAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] CodeGenerationRequest request, CancellationToken ct)
    {
        request.Preview = true;
        var result = await generator.GenerateAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("policies")]
    public async Task<IActionResult> ListPolicies([FromQuery] CodeEntityKind? entityKind, CancellationToken ct)
    {
        var q = db.CodePolicyTemplates.AsNoTracking()
            .Include(t => t.Versions)
            .AsQueryable();
        if (entityKind.HasValue)
            q = q.Where(t => t.EntityKind == entityKind.Value);
        var items = await q.OrderBy(t => t.EntityKind).ThenBy(t => t.Name).ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("policies/{entityKind}")]
    public async Task<IActionResult> GetPolicySnapshot(CodeEntityKind entityKind, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var snapshot = await generator.GetPublishedSnapshotAsync(entityKind, tenantId, ct);
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
