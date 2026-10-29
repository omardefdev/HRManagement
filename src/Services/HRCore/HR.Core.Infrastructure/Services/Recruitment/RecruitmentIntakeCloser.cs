using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-026 (E4, part of G-20): closes intake on Published requisitions whose application closing date has passed.
/// The posting is unpublished and the internal vacancy closed, but the requisition stays Published because
/// selection (interviews, scoring, ranking, offers) continues. Idempotent.
/// </summary>
public sealed class RecruitmentIntakeCloser(HRCoreDbContext db, ILogger<RecruitmentIntakeCloser> logger)
{
    public async Task<int> CloseExpiredAsync(DateOnly todayUtc, CancellationToken ct)
    {
        var competitions = await CloseExpiredCompetitionsAsync(todayUtc, ct);

        // Requisitions attached to a competition are closed by the competition pass above (their closing date is the
        // competition's; they have no posting or vacancy of their own).
        var expired = await db.RecruitmentRequisitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(r => r.Status == RequisitionStatus.Published
                && r.ApplicationClosingDate != null
                && r.ApplicationClosingDate < todayUtc
                && r.IntakeClosedAt == null
                && r.CompetitionLineId == null)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return competitions;

        var ids = expired.Select(r => r.Id).ToList();
        var postings = await db.PublicJobPostings
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(p => p.RequisitionId != null && ids.Contains(p.RequisitionId.Value) && p.IsPublished)
            .ToListAsync(ct);
        var vacancies = await db.InternalVacancies
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(v => v.RequisitionId != null && ids.Contains(v.RequisitionId.Value) && v.IsOpen)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var posting in postings)
            posting.IsPublished = false;
        foreach (var vacancy in vacancies)
            vacancy.IsOpen = false;
        foreach (var requisition in expired)
            requisition.IntakeClosedAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Recruitment intake closed for {Count} requisition(s) past their application closing date.",
            expired.Count);
        return expired.Count + competitions;
    }

    /// <summary>HCM-031 (PLAN-31): announced competitions past their closing date → IntakeClosed (posting/vacancy closed).</summary>
    private async Task<int> CloseExpiredCompetitionsAsync(DateOnly todayUtc, CancellationToken ct)
    {
        var due = await db.RecruitmentCompetitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(c => c.Status == RecruitmentCompetitionStatus.Announced
                && c.ApplicationClosingDate < todayUtc
                && c.IntakeClosedAt == null)
            .ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var competition in due)
            await Competitions.CompetitionLifecycleService.StageIntakeCloseAsync(db, competition, now, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recruitment intake closed for {Count} competition(s) past their closing date.", due.Count);
        return due.Count;
    }
}
