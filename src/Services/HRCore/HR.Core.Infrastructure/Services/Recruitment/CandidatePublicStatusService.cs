using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-030 (B3): loads what <see cref="CandidatePublicStatusProjection"/> needs for recruitment applications (active
/// offer, interviews, events, appeal window) and projects them. On the anonymous public portal the reads bypass the
/// tenant filter (<see cref="SystemTenantBypassPurpose.PublicCareersPortal"/>) — callers pass ids they already resolved
/// from the candidate's reference and national id.
/// </summary>
public sealed class CandidatePublicStatusService(HRCoreDbContext db, IOptions<RecruitmentOptions> options)
{
    public async Task<CandidatePublicView?> GetViewAsync(
        Guid recruitmentApplicationId,
        bool forPublicPortal = false,
        CancellationToken ct = default) =>
        (await GetViewsAsync([recruitmentApplicationId], forPublicPortal, ct)).GetValueOrDefault(recruitmentApplicationId);

    public async Task<IReadOnlyDictionary<Guid, CandidatePublicView>> GetViewsAsync(
        IReadOnlyCollection<Guid> recruitmentApplicationIds,
        bool forPublicPortal = false,
        CancellationToken ct = default)
    {
        if (recruitmentApplicationIds.Count == 0)
            return new Dictionary<Guid, CandidatePublicView>();

        var ids = recruitmentApplicationIds.Distinct().ToList();
        var applications = await Scoped(db.RecruitmentApplications, forPublicPortal)
            .AsNoTracking()
            .Include(a => a.ActiveOfferLetter)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(ct);
        var interviews = (await Scoped(db.RecruitmentInterviewSlots, forPublicPortal)
                .AsNoTracking()
                .Where(s => ids.Contains(s.RecruitmentApplicationId))
                .Select(s => new { s.RecruitmentApplicationId, s.ScheduledAt, s.Mode, s.Location, s.Status })
                .ToListAsync(ct))
            .ToLookup(s => s.RecruitmentApplicationId, s => new CandidateInterviewInput(s.ScheduledAt, s.Mode, s.Location, s.Status));
        var events = (await Scoped(db.RecruitmentApplicationEvents, forPublicPortal)
                .AsNoTracking()
                .Where(e => ids.Contains(e.RecruitmentApplicationId))
                .Select(e => new { e.RecruitmentApplicationId, e.EventType, e.FromStatus, e.ToStatus, e.OccurredAt })
                .ToListAsync(ct))
            .ToLookup(e => e.RecruitmentApplicationId, e => new CandidateEventInput(e.EventType, e.FromStatus, e.ToStatus, e.OccurredAt));

        var competitionInputs = await LoadCompetitionInputsAsync(
            applications.Where(a => a.CompetitionId is not null).Select(a => a.Id).ToList(), forPublicPortal, ct);

        var settings = options.Value;
        var now = DateTime.UtcNow;
        return applications.ToDictionary(a => a.Id, a =>
        {
            var (appealOpen, appealDeadline) = CandidatePublicStatusProjection.AppealWindowOf(
                a, now, settings.AppealWindowDays, settings.GetAppealableRejectionKinds());
            return CandidatePublicStatusProjection.Project(new CandidatePublicStatusInput
            {
                Status = a.Status,
                AppliedAtUtc = a.AppliedAt,
                AppealStatus = a.AppealStatus,
                ReserveUntil = a.ReserveUntil,
                ActiveOfferStatus = a.ActiveOfferLetter?.Status,
                ActiveOfferExpiryUtc = a.ActiveOfferLetter?.ExpiryDate,
                Interviews = interviews[a.Id].ToList(),
                AppealOpen = appealOpen,
                AppealDeadlineUtc = appealDeadline,
                Events = events[a.Id].ToList(),
                NowUtc = now,
                DisplayZone = TimeZoneInfo.Local,
                Competition = competitionInputs.GetValueOrDefault(a.Id)
            });
        });
    }

    /// <summary>HCM-031 (PLAN-31): the competition state of competition applications (exam, results, objection, allocation).</summary>
    private async Task<Dictionary<Guid, CandidateCompetitionInput>> LoadCompetitionInputsAsync(
        IReadOnlyList<Guid> competitionApplicationIds,
        bool forPublicPortal,
        CancellationToken ct)
    {
        if (competitionApplicationIds.Count == 0)
            return [];
        var ids = competitionApplicationIds.ToList();
        var apps = await Scoped(db.RecruitmentApplications, forPublicPortal).AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.Status,
                a.RejectionKind,
                LineTitle = a.CompetitionLine != null ? a.CompetitionLine.Title : null,
                a.Competition!.CurrentResultVersionId
            })
            .ToListAsync(ct);
        var papers = (await Scoped(db.ExamCandidates, forPublicPortal).AsNoTracking()
                .Where(c => ids.Contains(c.RecruitmentApplicationId))
                .Select(c => new { c.RecruitmentApplicationId, c.ExamSession!.ScheduledAt, c.ExamSession.Status })
                .ToListAsync(ct))
            .ToLookup(p => p.RecruitmentApplicationId);
        var versionIds = apps.Where(a => a.CurrentResultVersionId != null).Select(a => a.CurrentResultVersionId!.Value).Distinct().ToList();
        var published = (await Scoped(db.CompetitionResults, forPublicPortal).AsNoTracking()
                .Where(r => versionIds.Contains(r.ResultVersionId) && ids.Contains(r.RecruitmentApplicationId))
                .Select(r => r.RecruitmentApplicationId)
                .ToListAsync(ct))
            .ToHashSet();
        var objecting = (await Scoped(db.RecruitmentAppeals, forPublicPortal).AsNoTracking()
                .Where(x => ids.Contains(x.RecruitmentApplicationId) && x.Kind == RecruitmentAppealKind.ResultObjection
                    && x.Status == RecruitmentAppealStatus.Pending)
                .Select(x => x.RecruitmentApplicationId)
                .ToListAsync(ct))
            .ToHashSet();

        return apps.ToDictionary(a => a.Id, a =>
        {
            var appPapers = papers[a.Id].ToList();
            var allocated = a.Status is RecruitmentApplicationStatus.Ranked or RecruitmentApplicationStatus.OfferExtended;
            return new CandidateCompetitionInput(
                ExamAtUtc: appPapers.Count == 0 ? null : appPapers.Min(p => p.ScheduledAt),
                ExamHeld: appPapers.Count > 0 && appPapers.All(p => p.Status is ExamSessionStatus.Held or ExamSessionStatus.MarkingClosed),
                ResultsPublished: published.Contains(a.Id),
                ObjectionPending: objecting.Contains(a.Id),
                AllocatedLineTitle: allocated ? a.LineTitle : null,
                NotSuccessful: a.Status == RecruitmentApplicationStatus.Rejected && a.RejectionKind == RecruitmentRejectionKind.CompetitionResult);
        });
    }

    private static IQueryable<T> Scoped<T>(DbSet<T> set, bool forPublicPortal) where T : class =>
        forPublicPortal ? set.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal) : set;
}
