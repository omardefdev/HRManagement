using System.Security.Claims;
using System.Text.Json;using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Gateway.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(Roles = HrManagementRoles.PlatformAdmin)]
public sealed class PlatformTenantController(
    IPlatformTenantSessionStore sessionStore,
    ISecurityAuditPublisher securityAudit) : ControllerBase
{
    public sealed record EnterTenantRequest(Guid TenantId, string Reason);
    public sealed record EnterTenantResponse(Guid TenantId, DateTimeOffset EnteredAtUtc);

    [HttpPost("enter-tenant")]
    public async Task<ActionResult<EnterTenantResponse>> EnterTenant([FromBody] EnterTenantRequest request)
    {
        if (request.TenantId == Guid.Empty)
            return BadRequest("tenantId is required.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("reason is required.");

        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return Unauthorized();
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? HttpContext.TraceIdentifier;
        var entry = new PlatformTenantSessionEntry(
            request.TenantId,
            request.Reason.Trim(),
            DateTimeOffset.UtcNow,
            correlationId);

        sessionStore.Set(sub, entry);

        await securityAudit.PublishAsync(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventTypes.PlatformEnterTenant,
            ActorSub = sub,
            CorrelationId = correlationId,
            TenantId = request.TenantId,
            Resource = request.TenantId.ToString("D"),
            DetailJson = JsonSerializer.Serialize(new { reason = entry.Reason }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        return Ok(new EnterTenantResponse(entry.TenantId, entry.EnteredAtUtc));
    }

    [HttpPost("exit-tenant")]
    public async Task<IActionResult> ExitTenant()
    {
        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return Unauthorized();
        var existing = sessionStore.Get(sub);
        sessionStore.Clear(sub);

        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? HttpContext.TraceIdentifier;
        await securityAudit.PublishAsync(new SecurityAuditEvent
        {
            EventType = SecurityAuditEventTypes.PlatformExitTenant,
            ActorSub = sub,
            CorrelationId = correlationId,
            TenantId = existing?.TenantId,
            Resource = existing?.TenantId.ToString("D"),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        return Ok(new { exited = true });
    }
}
