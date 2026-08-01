using System.Text.Json;
using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Compliance.Web.Controllers;

/// <summary>GOV-001-E: DSAR Access/Rectify admin API (v1 — HRCore discovery via runbook for other services).</summary>
[ApiController]
[Route("api/dsar")]
[Authorize(Roles = $"{HrManagementRoles.Compliance},{HrManagementRoles.PlatformAdmin}")]
public sealed class DsarRequestsController(ComplianceDbContext db) : ControllerBase
{
    public sealed record CreateDsarRequest(
        DsarRequestType RequestType,
        Guid? SubjectEmployeeId,
        string? SubjectNationalId,
        string? Notes);

    public sealed record UpdateDsarStatusRequest(
        DsarRequestStatus Status,
        string? Notes,
        object? DiscoveryPackage);

    public sealed record DsarDto(
        Guid Id,
        Guid TenantId,
        DsarRequestType RequestType,
        DsarRequestStatus Status,
        Guid? SubjectEmployeeId,
        string? SubjectNationalIdHash,
        DateTime RequestedAt,
        DateTime? CompletedAt,
        string? Notes,
        string? DiscoveryPackageJson);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DsarDto>>> List(
        [FromQuery] DsarRequestStatus? status,
        CancellationToken ct)
    {
        var q = db.DsarRequests.AsNoTracking().AsQueryable();
        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);

        var rows = await q.OrderByDescending(x => x.RequestedAt).Take(100).ToListAsync(ct);
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DsarDto>> Get(Guid id, CancellationToken ct)
    {
        var entity = await db.DsarRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? NotFound() : Ok(ToDto(entity));
    }

    [HttpPost]
    public async Task<ActionResult<DsarDto>> Create([FromBody] CreateDsarRequest req, CancellationToken ct)
    {
        if (req.SubjectEmployeeId is null && string.IsNullOrWhiteSpace(req.SubjectNationalId))
            return BadRequest("SubjectEmployeeId or SubjectNationalId required.");

        var entity = new DsarRequest
        {
            Id = Guid.NewGuid(),
            RequestType = req.RequestType,
            Status = DsarRequestStatus.Open,
            SubjectEmployeeId = req.SubjectEmployeeId,
            SubjectNationalIdHash = string.IsNullOrWhiteSpace(req.SubjectNationalId)
                ? null
                : PiiMask.Sha256Hex(req.SubjectNationalId),
            RequestedAt = DateTime.UtcNow,
            Notes = req.Notes
        };
        db.DsarRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        SensitiveDataAudit.Log(HttpContext, "Compliance", "DsarCreated", $"DsarId={entity.Id};Type={entity.RequestType}");
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<DsarDto>> UpdateStatus(Guid id, [FromBody] UpdateDsarStatusRequest req, CancellationToken ct)
    {
        var entity = await db.DsarRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        if (!IsAllowedTransition(entity.Status, req.Status))
            return BadRequest($"Invalid transition {entity.Status} → {req.Status}.");

        entity.Status = req.Status;
        if (!string.IsNullOrWhiteSpace(req.Notes))
            entity.Notes = req.Notes.Trim();

        if (req.DiscoveryPackage is not null)
        {
            entity.DiscoveryPackageJson = JsonSerializer.Serialize(req.DiscoveryPackage);
        }
        else if (req.Status == DsarRequestStatus.Fulfilled && string.IsNullOrWhiteSpace(entity.DiscoveryPackageJson))
        {
            entity.DiscoveryPackageJson = JsonSerializer.Serialize(BuildStubDiscovery(entity));
        }

        if (req.Status is DsarRequestStatus.Fulfilled or DsarRequestStatus.Rejected)
            entity.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        SensitiveDataAudit.Log(HttpContext, "Compliance", "DsarStatus", $"DsarId={entity.Id};Status={entity.Status}");
        return Ok(ToDto(entity));
    }

    [HttpPost("{id:guid}/execute-deletion")]
    public async Task<ActionResult<object>> ExecuteDeletion(Guid id, [FromServices] HRManagement.ServiceDefaults.Privacy.IDsarDeletionOrchestrator orchestrator, CancellationToken ct)
    {
        var entity = await db.DsarRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();
        if (entity.RequestType != DsarRequestType.Deletion)
            return BadRequest("Execute-deletion applies only to Deletion requests.");
        if (entity.SubjectEmployeeId is null)
            return BadRequest("SubjectEmployeeId is required for deletion.");
        if (entity.Status != DsarRequestStatus.InProgress)
            return BadRequest("Deletion can run only while request is InProgress.");

        var result = await orchestrator.ExecuteAsync(entity.SubjectEmployeeId.Value, ct);
        if (!result.Success)
            return BadRequest(result.Error);

        entity.Status = DsarRequestStatus.Fulfilled;
        entity.CompletedAt = DateTime.UtcNow;
        entity.DiscoveryPackageJson = JsonSerializer.Serialize(new
        {
            certificateId = result.CertificateId,
            services = result.ServicesProcessed,
            completedAt = result.CompletedAtUtc
        });
        await db.SaveChangesAsync(ct);
        SensitiveDataAudit.Log(HttpContext, "Compliance", "DsarDeletionExecuted", $"DsarId={entity.Id}");
        return Ok(result);
    }

    private static bool IsAllowedTransition(DsarRequestStatus from, DsarRequestStatus to) => (from, to) switch
    {
        (DsarRequestStatus.Open, DsarRequestStatus.InProgress) => true,
        (DsarRequestStatus.Open, DsarRequestStatus.Rejected) => true,
        (DsarRequestStatus.InProgress, DsarRequestStatus.Fulfilled) => true,
        (DsarRequestStatus.InProgress, DsarRequestStatus.Rejected) => true,
        _ => false
    };

    private static object BuildStubDiscovery(DsarRequest entity) => new
    {
        version = 1,
        card = "GOV-001",
        subjectEmployeeId = entity.SubjectEmployeeId,
        subjectNationalIdHash = entity.SubjectNationalIdHash,
        note = "v1 stub — attach HRCore employee fields via runbook; Health/Files indicators manual",
        nationalIdMaskedHint = "use PiiMask.NationalId in exports; never return raw NationalId"
    };

    private static DsarDto ToDto(DsarRequest x) => new(
        x.Id,
        x.TenantId,
        x.RequestType,
        x.Status,
        x.SubjectEmployeeId,
        x.SubjectNationalIdHash,
        x.RequestedAt,
        x.CompletedAt,
        x.Notes,
        x.DiscoveryPackageJson);
}
