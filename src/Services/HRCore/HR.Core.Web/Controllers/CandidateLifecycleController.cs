using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>HCM-006: HR-only candidate application legal lifecycle (anonymize/purge).</summary>
[Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
[Route("CandidateLifecycle")]
public sealed class CandidateLifecycleController(HRCoreDbContext db) : Controller
{
    public record AnonymizeCitizenDto(Guid CitizenApplicationId);
    public record AnonymizeRecruitmentDto(Guid RecruitmentApplicationId);
    public record LegalHoldDto(Guid CitizenApplicationId, bool Hold);

    [HttpPost("citizen/anonymize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AnonymizeCitizen([FromBody] AnonymizeCitizenDto dto, CancellationToken ct)
    {
        var app = await db.CitizenApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == dto.CitizenApplicationId, ct);
        if (app is null) return NotFound();

        var (ok, error) = CandidateApplicationLegalLifecycleService.TryAnonymize(app);
        if (!ok) return Conflict(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { app.Id, app.AnonymizedAt });
    }

    [HttpPost("recruitment/anonymize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AnonymizeRecruitment([FromBody] AnonymizeRecruitmentDto dto, CancellationToken ct)
    {
        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == dto.RecruitmentApplicationId, ct);
        if (app is null) return NotFound();

        var (ok, error) = CandidateApplicationLegalLifecycleService.TryAnonymize(app);
        if (!ok) return Conflict(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { app.Id, app.AnonymizedAt });
    }

    [HttpPost("citizen/legal-hold")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetCitizenLegalHold([FromBody] LegalHoldDto dto, CancellationToken ct)
    {
        var app = await db.CitizenApplications.FirstOrDefaultAsync(a => a.Id == dto.CitizenApplicationId, ct);
        if (app is null) return NotFound();

        var (ok, error) = CandidateApplicationLegalLifecycleService.SetLegalHold(app, dto.Hold, reason: null);
        if (!ok) return Conflict(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { app.Id, app.LegalHold });
    }

    [HttpGet("retention/candidates")]
    public async Task<IActionResult> RetentionCandidates(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var citizens = await db.CitizenApplications
            .Include(a => a.Requisition)
            .AsNoTracking()
            .ToListAsync(ct);

        var flagged = citizens
            .Where(a => CandidateApplicationLegalLifecycleService.CanPurge(a, a.Requisition, now))
            .Select(a => new { a.Id, a.ReferenceNumber, type = "CitizenApplication" })
            .ToList();

        return Ok(new { count = flagged.Count, candidates = flagged });
    }
}
