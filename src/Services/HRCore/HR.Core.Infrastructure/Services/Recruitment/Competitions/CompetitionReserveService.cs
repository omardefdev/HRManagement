using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: outcome of a recall attempt.</summary>
public sealed record CompetitionRecallResult(bool Recalled, Guid? RecalledApplicationId, string? Notice);

/// <summary>
/// HCM-031 (PLAN-31, P5, AC-HCM-31g, decision 8): line-level reserve. When an allocated competition application leaves
/// its requisition (offer declined, expired or revoked, or the candidate withdrew), the first <c>Active</c> reserve entry of
/// the same line with <c>ReserveUntil ≥ today</c> (by line rank) is recalled to the vacated requisition (Ranked, overall
/// rank); the recalled candidate's other entries are removed. With no valid reserve nothing is recalled and an HR notice is
/// recorded on the competition (audit <c>CompetitionReserveExhausted</c>). The PLAN-27 suggestion or auto-draft follows.
/// PLAN-31 follow-up: recall on non-commencement depends on PLAN-29's deferred "cancel hire".
/// </summary>
public sealed class CompetitionReserveService(HRCoreDbContext db)
{
    // PLAN-31 follow-up: recall the line reserve when an allocated candidate does not commence (needs PLAN-29's deferred
    // "cancel hire"; the commencement watch only flags the intervention today).
    public const string RecallAuditAction = "CompetitionReserveRecall";
    public const string ExhaustedAuditAction = "CompetitionReserveExhausted";
    public const string NoValidReserveNotice = "لا يوجد في قائمة احتياط السطر مرشح صالح؛ بقي الشاغر دون استدعاء.";

    /// <summary>
    /// Recalls a reserve candidate to <paramref name="requisitionId"/> when it belongs to a finalized competition line
    /// and no open application holds it any more. Saves. Safe to call for any requisition (no-op otherwise).
    /// </summary>
    public async Task<CompetitionRecallResult> RecallForVacancyAsync(Guid requisitionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var requisition = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Id == requisitionId && r.CompetitionLineId != null)
            .Select(r => new { r.Id, r.TenantId, r.Status, LineId = r.CompetitionLineId!.Value, r.CompetitionLine!.CompetitionId, CompetitionStatus = r.CompetitionLine.Competition!.Status, r.CompetitionLine.Competition.CurrentResultVersionId })
            .FirstOrDefaultAsync(ct);
        if (requisition is null || requisition.CompetitionStatus != RecruitmentCompetitionStatus.Finalized
            || requisition.Status != RequisitionStatus.Published)
            return new(false, null, null);

        var holding = new[]
        {
            RecruitmentApplicationStatus.Ranked, RecruitmentApplicationStatus.OfferExtended, RecruitmentApplicationStatus.OfferAccepted,
            RecruitmentApplicationStatus.Hired
        };
        if (await db.RecruitmentApplications.AnyAsync(a => a.RequisitionId == requisitionId && holding.Contains(a.Status), ct))
            return new(false, null, null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = await db.CompetitionReserveEntries
            .Where(e => e.CompetitionLineId == requisition.LineId && e.Status == CompetitionReserveEntryStatus.Active && e.ReserveUntil >= today
                && e.RecruitmentApplication!.Status == RecruitmentApplicationStatus.ReserveListed)
            .OrderBy(e => e.LineRank)
            .FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        if (entry is null)
        {
            Audit(requisition.TenantId, requisition.CompetitionId, ExhaustedAuditAction, actorUserId, new { requisitionId, lineId = requisition.LineId });
            await db.SaveChangesAsync(ct);
            return new(false, null, NoValidReserveNotice);
        }

        var app = await db.RecruitmentApplications.FirstAsync(a => a.Id == entry.RecruitmentApplicationId, ct);
        var result = requisition.CurrentResultVersionId is Guid versionId
            ? await db.CompetitionResults.AsNoTracking().FirstOrDefaultAsync(r => r.ResultVersionId == versionId && r.RecruitmentApplicationId == app.Id, ct)
            : null;
        var criteria = await db.CompetitionLines.Where(l => l.Id == requisition.LineId).Select(l => l.CriteriaVersionId).FirstAsync(ct);

        app.RequisitionId = requisitionId;
        app.CompetitionLineId = requisition.LineId;
        app.Status = RecruitmentApplicationStatus.Ranked;
        app.Rank = result?.OverallRank;
        app.RankedAt = now;
        app.CriteriaVersionId = criteria;
        app.ReserveUntil = null;
        entry.Status = CompetitionReserveEntryStatus.Recalled;
        entry.StatusChangedAt = now;
        entry.RecalledRequisitionId = requisitionId;
        var others = await db.CompetitionReserveEntries
            .Where(e => e.RecruitmentApplicationId == app.Id && e.Id != entry.Id && e.Status == CompetitionReserveEntryStatus.Active)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            other.Status = CompetitionReserveEntryStatus.Removed;
            other.StatusChangedAt = now;
        }
        db.CompetitionAllocations.Add(new CompetitionAllocation
        {
            TenantId = requisition.TenantId,
            CompetitionId = requisition.CompetitionId,
            RecruitmentApplicationId = app.Id,
            AllocatedLineId = requisition.LineId,
            AllocatedRequisitionId = requisitionId,
            FromReserve = true
        });
        db.RecruitmentApplicationEvents.Add(CompetitionEvents.New(requisition.TenantId, app.Id,
            RecruitmentApplicationEventTypes.CompetitionReserveRecalled, requisition.LineId, now));
        Audit(requisition.TenantId, requisition.CompetitionId, RecallAuditAction, actorUserId,
            new { requisitionId, lineId = requisition.LineId, applicationId = app.Id, entry.LineRank, removed = others.Count });
        await db.SaveChangesAsync(ct);
        return new(true, app.Id, null);
    }

    /// <summary>The recall after a settled offer (its application's requisition is the vacated one).</summary>
    public async Task<CompetitionRecallResult> RecallAfterSettlementAsync(Guid settledOfferId, Guid? actorUserId, CancellationToken ct = default)
    {
        var settled = await db.OfferLetters.AsNoTracking()
            .Where(o => o.Id == settledOfferId && (o.Status == OfferStatus.Declined || o.Status == OfferStatus.Expired || o.Status == OfferStatus.Revoked))
            .Select(o => new { o.RecruitmentApplication!.RequisitionId, o.RecruitmentApplication.CompetitionId, o.RecruitmentApplication.Status })
            .FirstOrDefaultAsync(ct);
        if (settled is not { RequisitionId: Guid requisitionId, CompetitionId: not null }
            || settled.Status is not (RecruitmentApplicationStatus.OfferDeclined or RecruitmentApplicationStatus.Rejected or RecruitmentApplicationStatus.Withdrawn))
            return new(false, null, null);
        return await RecallForVacancyAsync(requisitionId, actorUserId, ct);
    }

    /// <summary>The recall after an allocated competition application was withdrawn without an active offer.</summary>
    public async Task<CompetitionRecallResult> RecallAfterWithdrawalAsync(Guid applicationId, Guid? actorUserId, CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.Id == applicationId && a.CompetitionId != null && a.Status == RecruitmentApplicationStatus.Withdrawn)
            .Select(a => new { a.RequisitionId, a.Id })
            .FirstOrDefaultAsync(ct);
        if (app?.RequisitionId is not Guid requisitionId)
        {
            // A reserve candidate who withdraws leaves every line.
            await RemoveEntriesAsync(applicationId, ct);
            return new(false, null, null);
        }
        return await RecallForVacancyAsync(requisitionId, actorUserId, ct);
    }

    /// <summary>
    /// Expiry pass (run by the reserve expiry job before the requisition pass): Active entries past <c>ReserveUntil</c>
    /// become Expired; a ReserveListed competition application with no Active entry left is rejected (<c>ReserveExpired</c>).
    /// Across tenants, idempotent.
    /// </summary>
    public async Task<int> ExpireAsync(DateOnly todayUtc, CancellationToken ct = default)
    {
        var due = await db.CompetitionReserveEntries
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(e => e.Status == CompetitionReserveEntryStatus.Active && e.ReserveUntil < todayUtc)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var entry in due)
        {
            entry.Status = CompetitionReserveEntryStatus.Expired;
            entry.StatusChangedAt = now;
        }
        await db.SaveChangesAsync(ct);

        var expired = await db.RecruitmentApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(a => a.CompetitionId != null && a.Status == RecruitmentApplicationStatus.ReserveListed
                && !db.CompetitionReserveEntries.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                    .Any(e => e.RecruitmentApplicationId == a.Id && e.Status == CompetitionReserveEntryStatus.Active))
            .ToListAsync(ct);
        foreach (var app in expired)
            ApplicationRejection.Apply(app, RecruitmentRejectionKind.ReserveExpired, "انتهاء صلاحية قائمة احتياط المسابقة", now);
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private async Task RemoveEntriesAsync(Guid applicationId, CancellationToken ct)
    {
        var entries = await db.CompetitionReserveEntries
            .Where(e => e.RecruitmentApplicationId == applicationId && e.Status == CompetitionReserveEntryStatus.Active)
            .ToListAsync(ct);
        if (entries.Count == 0)
            return;
        foreach (var entry in entries)
        {
            entry.Status = CompetitionReserveEntryStatus.Removed;
            entry.StatusChangedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private void Audit(Guid tenantId, Guid competitionId, string action, Guid? actorUserId, object details) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = tenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competitionId,
            Action = action,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(details)
        });
}
