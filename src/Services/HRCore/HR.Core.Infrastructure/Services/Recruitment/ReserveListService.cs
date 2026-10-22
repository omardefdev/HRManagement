using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-027 (F, D-7): the requisition's reserve list — ranked candidates who did not get the post stay
/// <see cref="RecruitmentApplicationStatus.ReserveListed"/> until <see cref="RecruitmentApplication.ReserveUntil"/>,
/// and the next candidate by merit is suggested after a decline, expiry or revoke.
/// </summary>
public sealed class ReserveListService(
    HRCoreDbContext db,
    IOptions<RecruitmentOptions> options,
    ILogger<ReserveListService> logger)
{
    public const string ReserveExpiryNote = "انتهاء صلاحية قائمة الاحتياط";
    public const string MovedToReserveNote = "نُقل إلى قائمة الاحتياط بعد تعيين مرشح آخر";
    public const string NotSelectedOnHireNote = "تعيين مرشح آخر على الشاغر";

    /// <summary>
    /// The best available candidate by merit (Ranked or ReserveListed, ranked, no pending appeal) — the
    /// application holding <see cref="RecruitmentOfferService.GetBestAvailableRankAsync"/>'s rank.
    /// </summary>
    // PLAN-29: recall from reserve on non-commencement (the requisition is Closed after the hire; re-open the
    // vacancy and offer to the next ReserveListed candidate here).
    public Task<RecruitmentApplication?> GetNextCandidateAsync(Guid requisitionId, CancellationToken ct = default) =>
        RecruitmentOfferService.AvailableForMerit(db.RecruitmentApplications.AsNoTracking(), requisitionId)
            .OrderBy(a => a.Rank)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// HCM-027 (F3): «المرشح التالي بالجدارة» banner — only for a Published requisition where an offer was declined,
    /// expired or revoked (its application now OfferDeclined/Rejected) and no other offer occupies the requisition.
    /// </summary>
    public async Task<NextCandidateSuggestion?> GetNextCandidateSuggestionAsync(Guid requisitionId, CancellationToken ct = default)
    {
        var published = await db.RecruitmentRequisitions.AsNoTracking()
            .AnyAsync(r => r.Id == requisitionId && r.Status == RequisitionStatus.Published, ct);
        if (!published)
            return null;

        var offers = db.OfferLetters.AsNoTracking()
            .Where(o => o.RecruitmentApplication != null && o.RecruitmentApplication.RequisitionId == requisitionId);

        var hasSettledOffer = await offers.AnyAsync(o =>
            (o.Status == OfferStatus.Declined || o.Status == OfferStatus.Expired || o.Status == OfferStatus.Revoked)
            && (o.RecruitmentApplication!.Status == RecruitmentApplicationStatus.OfferDeclined
                || o.RecruitmentApplication.Status == RecruitmentApplicationStatus.Rejected), ct);
        if (!hasSettledOffer)
            return null;

        var occupied = await offers.AnyAsync(o =>
            o.Status == OfferStatus.Draft
            || o.Status == OfferStatus.PendingApproval
            || o.Status == OfferStatus.Issued
            || (o.Status == OfferStatus.Accepted && !o.IsConvertedToEmployee), ct);
        if (occupied)
            return null;

        var next = await GetNextCandidateAsync(requisitionId, ct);
        return next is null ? null : new NextCandidateSuggestion(next.Id, next.ApplicantFullName, next.Rank!.Value, next.Status);
    }

    /// <summary>
    /// HCM-027 (F2): on hire, Ranked siblings move to the reserve list until today + ReserveValidityMonths;
    /// other non-terminal siblings are Rejected. Already ReserveListed siblings keep their reserve date.
    /// Stages the changes only — the hire closure saves them in its transaction. Returns the number reserved.
    /// </summary>
    public async Task<int> MoveSiblingsToReserveAsync(
        Guid requisitionId,
        Guid hiredApplicationId,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        var terminal = SelectionWorkflowRules.TerminalStatuses.ToArray();
        var siblings = await db.RecruitmentApplications
            .Where(a => a.RequisitionId == requisitionId
                        && a.Id != hiredApplicationId
                        && !terminal.Contains(a.Status)
                        && a.Status != RecruitmentApplicationStatus.ReserveListed)
            .ToListAsync(ct);

        var reserveUntil = DateOnly.FromDateTime(nowUtc).AddMonths(Math.Max(0, options.Value.ReserveValidityMonths));
        var reserved = 0;
        foreach (var sibling in siblings)
        {
            if (sibling.Status == RecruitmentApplicationStatus.Ranked
                && SelectionWorkflowRules.CanTransition(sibling, RecruitmentApplicationStatus.ReserveListed))
            {
                sibling.Status = RecruitmentApplicationStatus.ReserveListed;
                sibling.ReserveUntil = reserveUntil;
                sibling.ReserveListedAt = nowUtc;
                sibling.Notes = AppendNote(sibling.Notes, MovedToReserveNote);
                reserved++;
            }
            else
            {
                ApplicationRejection.Apply(sibling, RecruitmentRejectionKind.NotSelectedOnHire, NotSelectedOnHireNote, nowUtc);
            }

            sibling.ReviewedAt = nowUtc;
        }

        return reserved;
    }

    /// <summary>
    /// HCM-027 (F4): ReserveListed applications whose <see cref="RecruitmentApplication.ReserveUntil"/> is before
    /// <paramref name="todayUtc"/> become Rejected, across tenants. Idempotent.
    /// </summary>
    public async Task<int> ExpireReserveAsync(DateOnly todayUtc, CancellationToken ct = default)
    {
        var due = await db.RecruitmentApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(a => a.Status == RecruitmentApplicationStatus.ReserveListed
                        && a.ReserveUntil != null
                        && a.ReserveUntil < todayUtc)
            .ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var app in due)
        {
            ApplicationRejection.Apply(app, RecruitmentRejectionKind.ReserveExpired, ReserveExpiryNote, now);
            app.Notes = AppendNote(app.Notes, ReserveExpiryNote);
            // PLAN-30: notify candidate
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recruitment reserve expiry: {Count} reserve-listed application(s) rejected.", due.Count);
        return due.Count;
    }

    private static string AppendNote(string? notes, string note) =>
        string.IsNullOrWhiteSpace(notes) ? note : $"{notes}\n{note}";
}

/// <summary>HCM-027 (F3): next candidate by merit, suggested after a decline/expiry/revoke.</summary>
public sealed record NextCandidateSuggestion(
    Guid ApplicationId,
    string ApplicantFullName,
    int Rank,
    RecruitmentApplicationStatus Status);
