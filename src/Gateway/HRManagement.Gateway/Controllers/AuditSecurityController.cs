using System.Security.Claims;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Gateway.Controllers;

/// <summary>OBS-003 platform audit: ingest, legal hold, chain verify (platform-admin).</summary>
[ApiController]
[Route("api/platform/audit")]
[Authorize(Roles = HrManagementRoles.PlatformAdmin)]
public sealed class AuditSecurityController(
    ISecurityAuditPublisher publisher,
    IAuditChainVerifier chainVerifier,
    IJourneyRebuildService journeyRebuild,
    GatewayAuditDbContext db,
    AuditWormExporter wormExporter) : ControllerBase
{
    public sealed record IngestRequest(
        string EventType,
        string? ActorSub,
        string? CorrelationId,
        string? Resource,
        string? DetailJson,
        Guid? TenantId);

    public sealed record LegalHoldRequest(
        string Reason,
        DateTimeOffset? RangeStartUtc,
        DateTimeOffset? RangeEndUtc,
        string? CorrelationId,
        string? UserSub);

    [HttpPost("security-events")]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.EventType))
            return BadRequest("EventType مطلوب.");

        await publisher.PublishAsync(new SecurityAuditEvent
        {
            EventType = req.EventType.Trim(),
            ActorSub = req.ActorSub,
            CorrelationId = req.CorrelationId,
            Resource = req.Resource,
            DetailJson = req.DetailJson,
            TenantId = req.TenantId,
            OccurredAtUtc = DateTimeOffset.UtcNow
        }, ct);
        return Accepted();
    }

    [HttpPost("legal-holds")]
    public async Task<ActionResult<object>> CreateHold([FromBody] LegalHoldRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest("Reason مطلوب.");

        var hold = new AuditLegalHold
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = User.FindFirstValue("sub") ?? User.Identity?.Name ?? "platform-admin",
            Reason = req.Reason.Trim(),
            RangeStartUtc = req.RangeStartUtc,
            RangeEndUtc = req.RangeEndUtc,
            CorrelationId = req.CorrelationId,
            UserSub = req.UserSub,
            Active = true
        };
        db.AuditLegalHolds.Add(hold);
        await db.SaveChangesAsync(ct);
        return Ok(new { hold.Id, hold.Active });
    }

    [HttpGet("legal-holds")]
    public async Task<ActionResult<IEnumerable<AuditLegalHold>>> ListHolds(CancellationToken ct) =>
        Ok(await db.AuditLegalHolds.AsNoTracking().Where(h => h.Active).OrderByDescending(h => h.CreatedAtUtc).ToListAsync(ct));

    [HttpGet("chain-verify")]
    public async Task<ActionResult<object>> VerifyChain(CancellationToken ct)
    {
        var result = await chainVerifier.VerifyAsync(ct);
        return result.Ok
            ? Ok(new { ok = true, checkedCount = result.Checked })
            : StatusCode(StatusCodes.Status409Conflict, new { ok = false, checkedCount = result.Checked, error = result.Error });
    }

    [HttpGet("journey/{correlationId}")]
    public async Task<ActionResult<JourneyRebuildResult>> RebuildJourney(string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length < 8)
            return BadRequest("correlationId مطلوب (8+ chars).");

        var auth = Request.Headers.Authorization.ToString();
        var journey = await journeyRebuild.RebuildAsync(correlationId.Trim(), auth, ct);
        if (journey.HttpEvents.Count == 0 && journey.PolicyEvents.Count == 0 && journey.SecurityEvents.Count == 0)
            return NotFound(new { message = "لا أحداث لهذا المعرّف.", correlationId });

        return Ok(journey);
    }

    [HttpPost("worm-export")]
    public async Task<ActionResult<object>> TriggerWormExport(CancellationToken ct)
    {
        var count = await wormExporter.ExportAsync(ct);
        return Ok(new { exported = count });
    }
}
