using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>GOV-003-C: duplicate master-data review queue.</summary>
[Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
[ApiController]
[Route("api/master-data/duplicates")]
public class MasterDataDuplicatesController(HRCoreDbContext db) : ControllerBase
{
    public record OpenCaseDto(
        string? CaseType,
        string? NormalizedKey,
        Guid? CandidateEmployeeIdA,
        Guid? CandidateEmployeeIdB,
        string? Notes);

    public record UpdateStatusDto(MasterDataDuplicateCaseStatus Status, string? Notes);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(
        [FromQuery] MasterDataDuplicateCaseStatus? status,
        CancellationToken ct)
    {
        var q = db.MasterDataDuplicateCases.AsNoTracking().AsQueryable();
        if (status.HasValue)
            q = q.Where(c => c.Status == status.Value);

        var rows = await q.OrderByDescending(c => c.OpenedAt).Take(100).ToListAsync(ct);
        return Ok(rows.Select(c => new
        {
            c.Id,
            c.CaseType,
            c.NormalizedKey,
            c.CandidateEmployeeIdA,
            c.CandidateEmployeeIdB,
            c.Status,
            c.ResolvedIntoEmployeeId,
            c.OpenedAt,
            c.ResolvedAt,
            c.Notes
        }));
    }

    [HttpPost]
    public async Task<ActionResult<object>> Open([FromBody] OpenCaseDto dto, CancellationToken ct)
    {
        var entity = new MasterDataDuplicateCase
        {
            Id = Guid.NewGuid(),
            CaseType = string.IsNullOrWhiteSpace(dto.CaseType) ? "NationalId" : dto.CaseType.Trim(),
            NormalizedKey = string.IsNullOrWhiteSpace(dto.NormalizedKey)
                ? null
                : dto.NormalizedKey.Trim().ToUpperInvariant(),
            CandidateEmployeeIdA = dto.CandidateEmployeeIdA,
            CandidateEmployeeIdB = dto.CandidateEmployeeIdB,
            Notes = dto.Notes,
            Status = MasterDataDuplicateCaseStatus.Open,
            OpenedAt = DateTime.UtcNow
        };
        db.MasterDataDuplicateCases.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { id = entity.Id }, new { entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<object>> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusDto dto,
        CancellationToken ct)
    {
        var entity = await db.MasterDataDuplicateCases.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return NotFound();

        if (!IsAllowed(entity.Status, dto.Status))
            return BadRequest($"Invalid transition {entity.Status} → {dto.Status}");

        entity.Status = dto.Status;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
            entity.Notes = dto.Notes.Trim();

        if (dto.Status is MasterDataDuplicateCaseStatus.Merged or MasterDataDuplicateCaseStatus.Rejected)
        {
            entity.ResolvedAt = DateTime.UtcNow;
            entity.ResolvedBy = ParseUserId(User);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { entity.Id, entity.Status, entity.ResolvedAt });
    }

    /// <summary>Suggest candidates sharing the same NationalId key (should be empty if UK holds).</summary>
    [HttpGet("scan-national-id")]
    public async Task<ActionResult<object>> ScanNationalId([FromQuery] string nationalId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
            return BadRequest("nationalId required");

        var key = nationalId.Trim();
        var matches = await db.Employees.AsNoTracking()
            .Where(e => e.NationalId == key)
            .Select(e => new { e.Id, e.FullName, e.NationalId, e.CurrentStatus })
            .Take(20)
            .ToListAsync(ct);

        return Ok(new { key, count = matches.Count, matches });
    }

    private static bool IsAllowed(MasterDataDuplicateCaseStatus from, MasterDataDuplicateCaseStatus to) =>
        (from, to) switch
        {
            (MasterDataDuplicateCaseStatus.Open, MasterDataDuplicateCaseStatus.UnderReview) => true,
            (MasterDataDuplicateCaseStatus.Open, MasterDataDuplicateCaseStatus.Rejected) => true,
            (MasterDataDuplicateCaseStatus.UnderReview, MasterDataDuplicateCaseStatus.Merged) => true,
            (MasterDataDuplicateCaseStatus.UnderReview, MasterDataDuplicateCaseStatus.Rejected) => true,
            _ => false
        };

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
