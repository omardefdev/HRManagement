using System.Runtime.CompilerServices;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.RecruitmentKpi;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment.Analytics;

/// <summary>
/// HCM-032 (PLAN-32): read-only recruitment KPIs (STR-007 <c>recruitment_*</c>, definitions in
/// <c>docs/STR_007_KPI_DEFINITIONS.md</c>). Every query is <c>AsNoTracking</c> and tenant-filtered by the context; every
/// method takes the caller's <see cref="OrgAccessScope"/>: a denied scope, an org-unit filter outside the scope, or a
/// competition filter from a scoped user returns empty results (decision 6). A requisition's unit is its funded
/// position's <c>OrgUnitId</c>; an application's unit is its requisition's (unallocated competition applications have none
/// and are visible to unrestricted users only). Exact counts (no sampling). No national id is ever read; candidate names
/// are read only when the caller passes <c>includeNames</c> (HrCoreWrite, decision 7).
/// The cohort (applications applied in the period) and the hires are loaded once per request and shared by the KPIs.
/// </summary>
public sealed class RecruitmentMetricsService(
    HRCoreDbContext db,
    IOptions<RecruitmentOptions> options,
    TimeProvider? timeProvider = null)
{
    /// <summary>Ids per <c>IN (…)</c> batch when loading events and offers of a known application set.</summary>
    public const int IdBatchSize = 5000;

    /// <summary>Rows kept in the open-vacancy, overdue and competition lists (counts stay exact).</summary>
    public const int ListLimit = SearchFilterBase.MaxPageSize;

    public const string RecruitmentWorkflowPrefix = "RECRUITMENT-";

    public const string DefinitionsVersion = "STR-007/2026-10-29";

    private static readonly string[] FunnelEventTypes =
    [
        RecruitmentApplicationEventTypes.Received,
        RecruitmentApplicationEventTypes.StatusChanged,
        RecruitmentApplicationEventTypes.InterviewScheduled,
        RecruitmentApplicationEventTypes.OfferIssued,
        RecruitmentApplicationEventTypes.OfferAccepted
    ];

    private static readonly RequisitionStatus[] OpenRequisitionStatuses =
        [RequisitionStatus.Draft, RequisitionStatus.PendingApproval, RequisitionStatus.Approved, RequisitionStatus.Published];

    private static readonly RecruitmentApplicationStatus[] AdmittedStatuses =
    [
        RecruitmentApplicationStatus.Shortlisted, RecruitmentApplicationStatus.InterviewScheduled,
        RecruitmentApplicationStatus.Assessed, RecruitmentApplicationStatus.Ranked, RecruitmentApplicationStatus.OfferExtended,
        RecruitmentApplicationStatus.OfferAccepted, RecruitmentApplicationStatus.Hired, RecruitmentApplicationStatus.ReserveListed
    ];

    private readonly RecruitmentAnalyticsOptions _analytics = options.Value.Analytics ?? new RecruitmentAnalyticsOptions();
    private readonly RecruitmentOptions _recruitment = options.Value;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, object> _memo = new(StringComparer.Ordinal);

    public RecruitmentAnalyticsOptions Options => _analytics;

    public DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public RecruitmentMetricsFilter DefaultFilter() =>
        RecruitmentMetricsFilter.DefaultPeriod(UtcNow, _analytics.DefaultPeriodMonths);

    // ------------------------------------------------------------------ scope

    /// <summary>What a request may see: <see cref="Units"/> null = every unit.</summary>
    private sealed record ScopeFilter(Guid[]? Units, bool Unrestricted);

    private async Task<ScopeFilter?> ResolveAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        if (scope.IsDenied)
            return null;
        if (filter.CompetitionId.HasValue && scope.MustFilter)
            return null;
        if (filter.OrgUnitId is not { } unitId)
            return new ScopeFilter(scope.MustFilter ? scope.AllowedOrgUnitIds.ToArray() : null, scope.IsUnrestricted);
        if (!scope.CanAccess(unitId))
            return null;

        var key = "units:" + unitId.ToString("N");
        if (!_memo.TryGetValue(key, out var cached))
        {
            var path = await db.OrgUnits.AsNoTracking()
                .Where(o => o.Id == unitId)
                .Select(o => o.MaterializedPath)
                .FirstOrDefaultAsync(ct);
            if (path is null)
                return null;
            if (string.IsNullOrEmpty(path))
                path = $"/{unitId:D}/";
            var subtree = await db.OrgUnits.AsNoTracking()
                .Where(o => o.MaterializedPath.StartsWith(path))
                .Select(o => o.Id)
                .ToListAsync(ct);
            if (!subtree.Contains(unitId))
                subtree.Add(unitId);
            _memo[key] = cached = subtree;
        }

        var units = ((List<Guid>)cached).Where(u => !scope.MustFilter || scope.AllowedOrgUnitIds.Contains(u)).ToArray();
        return units.Length == 0 ? null : new ScopeFilter(units, scope.IsUnrestricted);
    }

    private IQueryable<RecruitmentApplication> Applications(RecruitmentMetricsFilter filter, ScopeFilter scope, bool applyChannel = true)
    {
        var query = db.RecruitmentApplications.AsNoTracking();
        if (scope.Units is { } units)
            query = query.Where(a => a.RequisitionId != null && units.Contains(a.Requisition!.FundedPosition!.OrgUnitId));
        if (filter.RequisitionId is { } requisitionId)
            query = query.Where(a => a.RequisitionId == requisitionId);
        if (applyChannel && filter.Channel is { } channel)
            query = query.Where(a => a.IntakeChannel == channel);
        if (filter.CompetitionId is { } competitionId)
            query = query.Where(a => a.CompetitionId == competitionId);
        return query;
    }

    private IQueryable<RecruitmentRequisition> Requisitions(RecruitmentMetricsFilter filter, ScopeFilter scope)
    {
        var query = db.RecruitmentRequisitions.AsNoTracking();
        if (scope.Units is { } units)
            query = query.Where(r => units.Contains(r.FundedPosition!.OrgUnitId));
        if (filter.RequisitionId is { } requisitionId)
            query = query.Where(r => r.Id == requisitionId);
        if (filter.CompetitionId is { } competitionId)
            query = query.Where(r => r.CompetitionLine != null && r.CompetitionLine.CompetitionId == competitionId);
        return query;
    }

    /// <summary>Culture-free key (a record's ToString formats dates with the current culture — Hijri under ar-SA).</summary>
    private static string MemoKey(string kind, RecruitmentMetricsFilter filter, OrgAccessScope scope) =>
        string.Join('|', kind, filter.From.DayNumber, filter.To.DayNumber, filter.OrgUnitId?.ToString("N"), filter.RequisitionId?.ToString("N"),
            filter.Channel?.ToString(), filter.CompetitionId?.ToString("N"), RuntimeHelpers.GetHashCode(scope));

    // ------------------------------------------------------------------ shared loads

    private sealed record ReachApp(
        Guid Id,
        Guid? RequisitionId,
        RecruitmentIntakeChannel Channel,
        RecruitmentApplicationStatus Status,
        DateTime AppliedAt,
        DateTime? ScreenedAt,
        DateTime? RankedAt,
        bool HasHiredEmployee);

    private sealed record ReachSet(
        IReadOnlyList<ReachApp> Apps,
        IReadOnlyDictionary<Guid, List<FunnelEvent>> Events,
        IReadOnlyDictionary<Guid, RecruitmentFunnelStage> Furthest);

    private static readonly ReachSet EmptyReach = new([], new Dictionary<Guid, List<FunnelEvent>>(), new Dictionary<Guid, RecruitmentFunnelStage>());

    /// <summary>Applications with their funnel events (batched by id) and offer evidence; furthest stage per application.</summary>
    private async Task<ReachSet> LoadReachAsync(IQueryable<RecruitmentApplication> applications, CancellationToken ct)
    {
        var apps = await applications
            .Select(a => new ReachApp(a.Id, a.RequisitionId, a.IntakeChannel, a.Status, a.AppliedAt, a.ScreenedAt, a.RankedAt,
                a.HiredEmployeeId != null))
            .ToListAsync(ct);

        var events = new Dictionary<Guid, List<FunnelEvent>>();
        var issued = new HashSet<Guid>();
        var accepted = new HashSet<Guid>();
        foreach (var batch in apps.Select(a => a.Id).Chunk(IdBatchSize))
        {
            var rows = await db.RecruitmentApplicationEvents.AsNoTracking()
                .Where(e => batch.Contains(e.RecruitmentApplicationId) && FunnelEventTypes.Contains(e.EventType))
                .Select(e => new { e.RecruitmentApplicationId, e.EventType, e.ToStatus, e.OccurredAt })
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                if (!events.TryGetValue(row.RecruitmentApplicationId, out var list))
                    events[row.RecruitmentApplicationId] = list = [];
                list.Add(new FunnelEvent(row.EventType, row.ToStatus, row.OccurredAt));
            }

            var offers = await db.OfferLetters.AsNoTracking()
                .Where(o => batch.Contains(o.RecruitmentApplicationId) && (o.IssuedDate != null || o.AcceptedDate != null))
                .Select(o => new { o.RecruitmentApplicationId, Issued = o.IssuedDate != null, Accepted = o.AcceptedDate != null })
                .ToListAsync(ct);
            foreach (var offer in offers)
            {
                if (offer.Issued) issued.Add(offer.RecruitmentApplicationId);
                if (offer.Accepted) accepted.Add(offer.RecruitmentApplicationId);
            }
        }

        var furthest = new Dictionary<Guid, RecruitmentFunnelStage>(apps.Count);
        foreach (var app in apps)
        {
            var evidence = new FunnelEvidence(app.Status, app.ScreenedAt, app.RankedAt,
                issued.Contains(app.Id), accepted.Contains(app.Id), app.HasHiredEmployee);
            furthest[app.Id] = FunnelReachRules.FurthestStage(
                events.TryGetValue(app.Id, out var list) ? list : [], evidence);
        }
        return new ReachSet(apps, events, furthest);
    }

    /// <summary>The cohort: applications with <c>AppliedAt</c> in the period (and the filters).</summary>
    private async Task<ReachSet> CohortAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        var key = MemoKey("cohort", filter, scope);
        if (_memo.TryGetValue(key, out var cached))
            return (ReachSet)cached;
        var resolved = await ResolveAsync(filter, scope, ct);
        var reach = resolved is null
            ? EmptyReach
            : await LoadReachAsync(Applications(filter, resolved)
                .Where(a => a.AppliedAt >= filter.FromUtc && a.AppliedAt < filter.ToUtc), ct);
        _memo[key] = reach;
        return reach;
    }

    private sealed record HiredApp(Guid Id, Guid? RequisitionId, RecruitmentIntakeChannel Channel, DateTime AppliedAt, HiredTime Hired);

    /// <summary>
    /// Every hired application in scope (any date; channel not applied — time-to-fill is per requisition): Hired time from
    /// the earliest <c>StatusChanged → Hired</c> event, else the hired employee's <c>CreatedAt</c>.
    /// </summary>
    private async Task<IReadOnlyList<HiredApp>> HiresAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        var periodless = filter with { From = DateOnly.MinValue, To = DateOnly.MaxValue, Channel = null };
        var key = MemoKey("hires", periodless, scope);
        if (_memo.TryGetValue(key, out var cached))
            return (IReadOnlyList<HiredApp>)cached;

        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return (IReadOnlyList<HiredApp>)(_memo[key] = new List<HiredApp>());

        const int hired = (int)RecruitmentApplicationStatus.Hired;
        var candidates = await Applications(filter, resolved, applyChannel: false)
            .Where(a => a.HiredEmployeeId != null
                || a.Status == RecruitmentApplicationStatus.Hired
                || db.RecruitmentApplicationEvents.Any(e => e.RecruitmentApplicationId == a.Id
                    && e.EventType == RecruitmentApplicationEventTypes.StatusChanged && e.ToStatus == hired))
            .Select(a => new
            {
                a.Id,
                a.RequisitionId,
                a.IntakeChannel,
                a.AppliedAt,
                EmployeeCreatedAt = a.HiredEmployee != null ? (DateTime?)a.HiredEmployee.CreatedAt : null
            })
            .ToListAsync(ct);

        var firstHiredEvent = new Dictionary<Guid, DateTime>();
        foreach (var batch in candidates.Select(c => c.Id).Chunk(IdBatchSize))
        {
            var rows = await db.RecruitmentApplicationEvents.AsNoTracking()
                .Where(e => batch.Contains(e.RecruitmentApplicationId)
                    && e.EventType == RecruitmentApplicationEventTypes.StatusChanged && e.ToStatus == hired)
                .GroupBy(e => e.RecruitmentApplicationId)
                .Select(g => new { Id = g.Key, At = g.Min(e => e.OccurredAt) })
                .ToListAsync(ct);
            foreach (var row in rows)
                firstHiredEvent[row.Id] = row.At;
        }

        var result = new List<HiredApp>(candidates.Count);
        foreach (var c in candidates)
        {
            HiredTime? time = firstHiredEvent.TryGetValue(c.Id, out var at)
                ? new HiredTime(at, false)
                : c.EmployeeCreatedAt is { } created ? new HiredTime(created, true) : null;
            if (time is not null)
                result.Add(new HiredApp(c.Id, c.RequisitionId, c.IntakeChannel, c.AppliedAt, time));
        }
        _memo[key] = result;
        return result;
    }

    /// <summary>Offer, appeal and other application-linked rows are scoped through their application.</summary>
    private async Task<IQueryable<Guid>?> ScopedApplicationIdsAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        return resolved is null ? null : Applications(filter, resolved).Select(a => a.Id);
    }

    // ------------------------------------------------------------------ KPIs

    /// <summary><c>recruitment_funnel</c>.</summary>
    public async Task<FunnelResult> GetFunnelAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var cohort = await CohortAsync(filter, scope, ct);
        return BuildFunnel(cohort);
    }

    private static FunnelResult BuildFunnel(ReachSet cohort)
    {
        var stages = FunnelReachRules.Stages
            .Select(stage => new FunnelStageCount(stage, cohort.Furthest.Values.Count(f => f >= stage)))
            .ToList();
        var statuses = cohort.Apps
            .GroupBy(a => a.Status)
            .OrderBy(g => g.Key)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToList();
        var covered = cohort.Apps.Count(a => cohort.Events.ContainsKey(a.Id));
        return new FunnelResult(cohort.Apps.Count, covered, stages, statuses);
    }

    /// <summary><c>recruitment_time_to_hire</c>: applications hired in the period.</summary>
    public async Task<DurationMeasure> GetTimeToHireAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var hires = (await HiresAsync(filter, scope, ct))
            .Where(h => h.Hired.At >= filter.FromUtc && h.Hired.At < filter.ToUtc)
            .Where(h => filter.Channel is null || h.Channel == filter.Channel)
            .ToList();
        return DurationMeasure.From(
            hires.Select(h => (decimal)TimeToFillRules.TimeToHireDays(h.AppliedAt, h.Hired)).ToList(),
            hires.Count(h => h.Hired.IsFallback));
    }

    /// <summary><c>recruitment_time_to_fill</c>: requisitions whose first hire is in the period.</summary>
    public async Task<TimeToFillResult> GetTimeToFillAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var fills = await TimeToFillByRequisitionAsync(filter, scope, ct);
        var inPeriod = fills.Values
            .Select(f => TimeToFillRules.TimeToFill(f.RequisitionId, f.ApprovedDate, f.RequestedDate, f.Hires, filter.FromUtc, filter.ToUtc))
            .OfType<Domain.Services.RecruitmentKpi.TimeToFillResult>()
            .ToList();
        var days = DurationMeasure.From(inPeriod.Select(r => (decimal)r.Days).ToList(),
            inPeriod.Count(r => r.StartIsFallback || r.HiredIsFallback));
        return new TimeToFillResult(days, inPeriod.Count(r => r.StartIsFallback), inPeriod.Count(r => r.HiredIsFallback));
    }

    private sealed record RequisitionHires(Guid RequisitionId, DateTime? ApprovedDate, DateTime RequestedDate, IReadOnlyList<HiredTime> Hires);

    private async Task<IReadOnlyDictionary<Guid, RequisitionHires>> TimeToFillByRequisitionAsync(
        RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        var hires = await HiresAsync(filter, scope, ct);
        var byRequisition = hires.Where(h => h.RequisitionId.HasValue)
            .GroupBy(h => h.RequisitionId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(h => h.Hired).ToList());
        var result = new Dictionary<Guid, RequisitionHires>();
        foreach (var batch in byRequisition.Keys.Chunk(IdBatchSize))
        {
            var rows = await db.RecruitmentRequisitions.AsNoTracking()
                .Where(r => batch.Contains(r.Id))
                .Select(r => new { r.Id, r.ApprovedDate, r.RequestedDate })
                .ToListAsync(ct);
            foreach (var r in rows)
                result[r.Id] = new RequisitionHires(r.Id, r.ApprovedDate, r.RequestedDate, byRequisition[r.Id]);
        }
        return result;
    }

    /// <summary><c>recruitment_stage_time</c>: event-covered cohort rows only.</summary>
    public async Task<StageTimeResult> GetStageTimeAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default) =>
        BuildStageTime(await CohortAsync(filter, scope, ct));

    private static StageTimeResult BuildStageTime(ReachSet cohort)
    {
        var durations = cohort.Apps
            .Where(a => cohort.Events.ContainsKey(a.Id))
            .SelectMany(a => TimeToFillRules.StageDurations(cohort.Events[a.Id]))
            .GroupBy(d => d.Status)
            .OrderBy(g => g.Key)
            .Select(g => new StageTimeRow(g.Key, KpiStatistics.Median(g.Select(d => d.Days)), g.Count()))
            .ToList();
        return new StageTimeResult(cohort.Apps.Count, cohort.Apps.Count(a => cohort.Events.ContainsKey(a.Id)), durations);
    }

    /// <summary><c>recruitment_offer_acceptance_rate</c>: final outcomes in the period; revoked excluded from the rate.</summary>
    public async Task<OfferOutcomeResult> GetOfferOutcomesAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        if (await ScopedApplicationIdsAsync(filter, scope, ct) is not { } appIds)
            return new OfferOutcomeResult(0, 0, 0, 0);
        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var offers = db.OfferLetters.AsNoTracking().Where(o => appIds.Contains(o.RecruitmentApplicationId));
        var accepted = await offers.CountAsync(o => o.Status == OfferStatus.Accepted && o.AcceptedDate >= from && o.AcceptedDate < to, ct);
        var declined = await offers.CountAsync(o => o.Status == OfferStatus.Declined && o.DeclinedAt >= from && o.DeclinedAt < to, ct);
        // ExpiredAt is set by the expiry job; offers expired before it existed fall back to their ExpiryDate.
        var expired = await offers.CountAsync(o => o.Status == OfferStatus.Expired
            && (o.ExpiredAt ?? o.ExpiryDate) >= from && (o.ExpiredAt ?? o.ExpiryDate) < to, ct);
        var revoked = await offers.CountAsync(o => o.Status == OfferStatus.Revoked && o.RevokedAt >= from && o.RevokedAt < to, ct);
        return new OfferOutcomeResult(accepted, declined, expired, revoked);
    }

    /// <summary><c>recruitment_offer_decline_reasons</c>.</summary>
    public async Task<DeclineReasonsResult> GetDeclineReasonsAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        if (await ScopedApplicationIdsAsync(filter, scope, ct) is not { } appIds)
            return new DeclineReasonsResult(0, [], []);
        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var declines = await db.OfferLetters.AsNoTracking()
            .Where(o => appIds.Contains(o.RecruitmentApplicationId)
                && o.Status == OfferStatus.Declined && o.DeclinedAt >= from && o.DeclinedAt < to)
            .Select(o => new { o.DeclineReasonCategory, o.DeclinedBy })
            .ToListAsync(ct);

        var byCategory = declines
            .GroupBy(d => d.DeclineReasonCategory)
            .OrderBy(g => g.Key.HasValue ? (int)g.Key.Value : int.MaxValue)
            .Select(g => new LabelCount(g.Key?.ToString() ?? "Uncategorized", OfferDeclineReasonCategories.LabelAr(g.Key), g.Count()))
            .ToList();
        var byDecliner = declines
            .GroupBy(d => d.DeclinedBy)
            .OrderBy(g => g.Key)
            .Select(g => new LabelCount(g.Key ?? "Unknown", DeclinedByLabel(g.Key), g.Count()))
            .ToList();
        return new DeclineReasonsResult(declines.Count, byCategory, byDecliner);
    }

    public static string DeclinedByLabel(string? declinedBy) => declinedBy switch
    {
        OfferDeclinedBy.Candidate => "المرشح",
        OfferDeclinedBy.HrOnBehalf => "الموارد البشرية نيابة عن المرشح",
        _ => "غير محدد"
    };

    /// <summary><c>recruitment_channel_mix</c>: cohort by intake channel with the hire rate.</summary>
    public async Task<ChannelMixResult> GetChannelMixAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var cohort = await CohortAsync(filter, scope, ct);
        var rows = cohort.Apps
            .GroupBy(a => a.Channel)
            .OrderBy(g => g.Key)
            .Select(g => new ChannelRow(g.Key, g.Count(), g.Count(a => cohort.Furthest[a.Id] == RecruitmentFunnelStage.Hired)))
            .ToList();
        return new ChannelMixResult(cohort.Apps.Count, rows);
    }

    /// <summary><c>recruitment_appeal_rate</c> and result objections.</summary>
    public async Task<AppealResult> GetAppealsAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return new AppealResult(0, 0, [], 0, 0, 0);
        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var applications = Applications(filter, resolved);
        var appIds = applications.Select(a => a.Id);
        var appeals = db.RecruitmentAppeals.AsNoTracking().Where(a => appIds.Contains(a.RecruitmentApplicationId));

        var appealable = _recruitment.GetAppealableRejectionKinds().ToArray();
        var rejections = await applications.CountAsync(a => a.RejectedAt >= from && a.RejectedAt < to
            && a.RejectionKind != null && appealable.Contains(a.RejectionKind.Value), ct);
        var filed = await appeals.CountAsync(a => a.Kind == RecruitmentAppealKind.Rejection && a.CreatedAt >= from && a.CreatedAt < to, ct);
        var decided = await appeals
            .Where(a => a.Kind == RecruitmentAppealKind.Rejection && a.Outcome != null && a.DecidedAt >= from && a.DecidedAt < to)
            .GroupBy(a => a.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var objectionsFiled = await appeals.CountAsync(a => a.Kind == RecruitmentAppealKind.ResultObjection && a.CreatedAt >= from && a.CreatedAt < to, ct);
        var objectionsUpheld = await appeals.CountAsync(a => a.Kind == RecruitmentAppealKind.ResultObjection
            && a.Outcome == RecruitmentAppealOutcome.ObjectionUpheld && a.DecidedAt >= from && a.DecidedAt < to, ct);
        var objectionsDismissed = await appeals.CountAsync(a => a.Kind == RecruitmentAppealKind.ResultObjection
            && a.Outcome == RecruitmentAppealOutcome.ObjectionDismissed && a.DecidedAt >= from && a.DecidedAt < to, ct);

        var outcomes = decided
            .OrderBy(d => d.Outcome)
            .Select(d => new LabelCount(d.Outcome!.Value.ToString(), EnumLabel(d.Outcome.Value), d.Count))
            .ToList();
        return new AppealResult(filed, rejections, outcomes, objectionsFiled, objectionsUpheld, objectionsDismissed);
    }

    /// <summary><c>recruitment_merit_overrides</c>: offers created in the period.</summary>
    public async Task<MeritOverrideResult> GetMeritOverridesAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        if (await ScopedApplicationIdsAsync(filter, scope, ct) is not { } appIds)
            return new MeritOverrideResult(0, 0, 0);
        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var created = db.OfferLetters.AsNoTracking()
            .Where(o => appIds.Contains(o.RecruitmentApplicationId) && o.CreatedAt >= from && o.CreatedAt < to);
        var total = await created.CountAsync(ct);
        var merit = await created.CountAsync(o => o.IsMeritOverride, ct);
        // OfferLetter.IsExceptionOffer (not mapped) = merit override ∨ grade deviation ∨ unverified salary.
        var exception = await created.CountAsync(o => o.IsMeritOverride
            || (o.GradeDeviationReason != null && o.GradeDeviationReason.Trim() != "") || o.SalaryUnverified, ct);
        return new MeritOverrideResult(total, merit, exception);
    }

    /// <summary><c>recruitment_open_vacancies</c> as of today (the period does not apply).</summary>
    public async Task<OpenVacanciesResult> GetOpenVacanciesAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var now = UtcNow;
        var asOf = DateOnly.FromDateTime(now);
        var staleDays = _analytics.StalePublishedDays > 0 ? _analytics.StalePublishedDays : 60;
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return new OpenVacanciesResult(asOf, staleDays, 0, [], 0, [], ListLimit);

        var vacantCount = 0;
        List<VacantPositionRow> vacant = [];
        // A vacant position without a requisition cannot match a requisition or competition filter.
        if (filter.RequisitionId is null && filter.CompetitionId is null)
        {
            var positions = db.FundedPositions.AsNoTracking().Where(p => p.Status == FundedPositionStatus.Vacant);
            if (resolved.Units is { } units)
                positions = positions.Where(p => units.Contains(p.OrgUnitId));
            positions = positions.Where(p =>
                !db.RecruitmentRequisitions.Any(r => r.FundedPositionId == p.Id && OpenRequisitionStatuses.Contains(r.Status))
                && !db.RecruitmentRequisitions.Any(r => r.FundedPositionId == p.Id && r.CompetitionLineId != null
                    && r.CompetitionLine!.Competition!.Status != RecruitmentCompetitionStatus.Finalized
                    && r.CompetitionLine.Competition.Status != RecruitmentCompetitionStatus.Cancelled));
            vacantCount = await positions.CountAsync(ct);
            // Vacant since = the latest close/cancel of a requisition on the position, else the position's creation.
            var rows = await positions
                .Select(p => new
                {
                    p.Id,
                    p.PositionCode,
                    p.GradeCode,
                    OrgUnitName = p.OrgUnit != null ? p.OrgUnit.NameAr : "",
                    Since = db.RecruitmentRequisitions
                        .Where(r => r.FundedPositionId == p.Id && (r.ClosedDate != null || r.CancelledAt != null))
                        .Max(r => (DateTime?)(r.ClosedDate ?? r.CancelledAt)) ?? p.CreatedAt
                })
                .OrderBy(p => p.Since).ThenBy(p => p.PositionCode)
                .Take(ListLimit)
                .ToListAsync(ct);
            vacant = rows.Select(r => new VacantPositionRow(r.Id, r.PositionCode, r.GradeCode, r.OrgUnitName, r.Since,
                KpiStatistics.CalendarDays(r.Since, now))).ToList();
        }

        var staleBefore = now.AddDays(-staleDays);
        var stale = Requisitions(filter, resolved)
            .Where(r => r.Status == RequisitionStatus.Published && (r.PublishedAt ?? r.ApprovedDate ?? r.RequestedDate) < staleBefore);
        var staleCount = await stale.CountAsync(ct);
        var staleRows = await stale
            .Select(r => new
            {
                r.Id,
                r.RequisitionNumber,
                r.JobTitle,
                OrgUnitName = r.FundedPosition != null && r.FundedPosition.OrgUnit != null ? r.FundedPosition.OrgUnit.NameAr : "",
                Published = r.PublishedAt ?? r.ApprovedDate ?? r.RequestedDate
            })
            .OrderBy(r => r.Published).ThenBy(r => r.RequisitionNumber)
            .Take(ListLimit)
            .ToListAsync(ct);
        return new OpenVacanciesResult(asOf, staleDays, vacantCount, vacant, staleCount,
            staleRows.Select(r => new StaleRequisitionRow(r.Id, r.RequisitionNumber, r.JobTitle, r.OrgUnitName, r.Published,
                KpiStatistics.CalendarDays(r.Published, now))).ToList(),
            ListLimit);
    }

    // ------------------------------------------------------------------ approvals

    private sealed record EntityInfo(Guid? OrgUnitId, Guid? RequisitionId, Guid? CompetitionId, string Label);

    /// <summary><c>recruitment_approval_sla</c>.</summary>
    public async Task<ApprovalSlaResult> GetApprovalSlaAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return new ApprovalSlaResult(0, 0, [], [], 0, ListLimit);

        var now = UtcNow;
        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var definitions = (await db.WorkflowDefinitions.AsNoTracking()
                .Where(d => d.Code.StartsWith(RecruitmentWorkflowPrefix))
                .Select(d => new { d.Id, d.Code, d.Name, d.StepsJson })
                .ToListAsync(ct))
            .ToDictionary(d => d.Id, d => (d.Code, d.Name, Steps: DeserializeOrEmpty<WorkflowStepSnapshot>(d.StepsJson)));
        var definitionIds = definitions.Keys.ToArray();

        // Bound: recruitment instances in progress, or started before the period end and not completed before its start.
        var instances = await db.WorkflowInstances.AsNoTracking()
            .Where(i => definitionIds.Contains(i.DefinitionId)
                && (i.Status == WorkflowStatus.InProgress || (i.StartedAt < to && (i.CompletedAt == null || i.CompletedAt >= from))))
            .Select(i => new { i.Id, i.DefinitionId, i.EntityType, i.EntityId, i.CurrentStepLevel, i.Status, i.StepHistoryJson, i.StartedAt })
            .ToListAsync(ct);

        var entities = await LoadEntityInfoAsync(instances.Select(i => (i.EntityType, i.EntityId)).Distinct().ToList(), ct);
        bool Visible(string entityType, Guid entityId)
        {
            if (!entities.TryGetValue((entityType, entityId), out var info))
                return resolved.Units is null && filter.RequisitionId is null && filter.CompetitionId is null;
            if (resolved.Units is { } units && (info.OrgUnitId is not { } unit || !units.Contains(unit)))
                return false;
            if (filter.RequisitionId is { } requisitionId && info.RequisitionId != requisitionId)
                return false;
            if (filter.CompetitionId is { } competitionId && info.CompetitionId != competitionId)
                return false;
            return true;
        }

        var inScope = instances.Where(i => Visible(i.EntityType, i.EntityId)).ToList();
        var overdue = new List<OverdueApprovalRow>();
        var completed = new List<(string Code, string Name, CompletedApprovalStep Step)>();
        var inProgress = 0;
        foreach (var instance in inScope)
        {
            var definition = definitions[instance.DefinitionId];
            var history = DeserializeOrEmpty<WorkflowStepHistoryEntry>(instance.StepHistoryJson);
            foreach (var step in ApprovalSlaRules.CompletedSteps(history, instance.StartedAt))
            {
                if (step.EndedAt >= from && step.EndedAt < to)
                    completed.Add((definition.Code, definition.Name, step));
            }

            if (instance.Status != WorkflowStatus.InProgress)
                continue;
            inProgress++;
            var current = definition.Steps.FirstOrDefault(s => s.Level == instance.CurrentStepLevel);
            var sla = ApprovalSlaRules.ResolveSla(current?.TimeoutHours, definition.Code, _analytics.ApprovalSlaHours, _analytics.DefaultApprovalSlaHours);
            var start = ApprovalSlaRules.CurrentStepStart(history, instance.StartedAt);
            var age = ApprovalSlaRules.AgeHours(start, now);
            if (!ApprovalSlaRules.IsOverdue(age, sla))
                continue;
            overdue.Add(new OverdueApprovalRow(instance.Id, definition.Code, definition.Name, instance.EntityType, instance.EntityId,
                entities.TryGetValue((instance.EntityType, instance.EntityId), out var info) ? info.Label : instance.EntityType,
                instance.CurrentStepLevel, current?.Name ?? $"الخطوة {instance.CurrentStepLevel}", current?.RoleRequired ?? "",
                start, KpiStatistics.Round(age), sla.Hours, sla.Source, info?.RequisitionId, info?.CompetitionId));
        }

        var stepRows = completed
            .GroupBy(c => (c.Code, c.Name, c.Step.Level, c.Step.StepName))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal).ThenBy(g => g.Key.Level)
            .Select(g =>
            {
                var hours = g.Select(c => c.Step.Hours).ToList();
                return new StepDurationRow(g.Key.Code, g.Key.Name, g.Key.Level, g.Key.StepName,
                    KpiStatistics.Median(hours), KpiStatistics.P90(hours), hours.Count);
            })
            .ToList();
        return new ApprovalSlaResult(inProgress, overdue.Count,
            overdue.OrderByDescending(o => o.AgeHours).Take(ListLimit).ToList(), stepRows, inScope.Count, ListLimit);
    }

    /// <summary>Org unit, requisition, competition and a display label of each workflow entity (scope and links).</summary>
    private async Task<Dictionary<(string, Guid), EntityInfo>> LoadEntityInfoAsync(
        IReadOnlyList<(string EntityType, Guid EntityId)> keys, CancellationToken ct)
    {
        var result = new Dictionary<(string, Guid), EntityInfo>();
        foreach (var group in keys.GroupBy(k => k.EntityType))
        {
            foreach (var batch in group.Select(k => k.EntityId).Chunk(IdBatchSize))
            {
                IEnumerable<(Guid Id, EntityInfo Info)> rows = group.Key switch
                {
                    nameof(RecruitmentRequisition) => (await db.RecruitmentRequisitions.AsNoTracking()
                            .Where(r => batch.Contains(r.Id))
                            .Select(r => new { r.Id, Unit = (Guid?)r.FundedPosition!.OrgUnitId, Competition = r.CompetitionLine != null ? (Guid?)r.CompetitionLine.CompetitionId : null, r.RequisitionNumber })
                            .ToListAsync(ct))
                        .Select(r => (r.Id, new EntityInfo(r.Unit, r.Id, r.Competition, r.RequisitionNumber))),
                    nameof(OfferLetter) => (await db.OfferLetters.AsNoTracking()
                            .Where(o => batch.Contains(o.Id))
                            .Select(o => new { o.Id, Unit = (Guid?)o.FundedPosition!.OrgUnitId, o.RecruitmentApplication!.RequisitionId, o.RecruitmentApplication.CompetitionId, o.OfferNumber })
                            .ToListAsync(ct))
                        .Select(o => (o.Id, new EntityInfo(o.Unit, o.RequisitionId, o.CompetitionId, o.OfferNumber))),
                    nameof(RecruitmentApplication) => (await db.RecruitmentApplications.AsNoTracking()
                            .Where(a => batch.Contains(a.Id))
                            .Select(a => new { a.Id, Unit = a.Requisition != null ? (Guid?)a.Requisition.FundedPosition!.OrgUnitId : null, a.RequisitionId, a.CompetitionId, a.IntakeChannel })
                            .ToListAsync(ct))
                        .Select(a => (a.Id, new EntityInfo(a.Unit, a.RequisitionId, a.CompetitionId, ApplicationReference(a.IntakeChannel, a.Id, null)))),
                    nameof(RecruitmentAppeal) => (await db.RecruitmentAppeals.AsNoTracking()
                            .Where(a => batch.Contains(a.Id))
                            .Select(a => new { a.Id, Unit = a.RecruitmentApplication!.Requisition != null ? (Guid?)a.RecruitmentApplication.Requisition.FundedPosition!.OrgUnitId : null, a.RecruitmentApplication.RequisitionId, a.RecruitmentApplication.CompetitionId })
                            .ToListAsync(ct))
                        .Select(a => (a.Id, new EntityInfo(a.Unit, a.RequisitionId, a.CompetitionId, "استئناف " + ShortId(a.Id)))),
                    nameof(SelectionCommitteeSession) => (await db.SelectionCommitteeSessions.AsNoTracking()
                            .Where(s => batch.Contains(s.Id))
                            .Select(s => new { s.Id, Unit = (Guid?)s.Requisition!.FundedPosition!.OrgUnitId, s.RequisitionId, Competition = s.Requisition.CompetitionLine != null ? (Guid?)s.Requisition.CompetitionLine.CompetitionId : null, s.Requisition.RequisitionNumber })
                            .ToListAsync(ct))
                        .Select(s => (s.Id, new EntityInfo(s.Unit, s.RequisitionId, s.Competition, "لجنة " + s.RequisitionNumber))),
                    nameof(RecruitmentCompetition) => (await db.RecruitmentCompetitions.AsNoTracking()
                            .Where(c => batch.Contains(c.Id))
                            .Select(c => new { c.Id, c.Number })
                            .ToListAsync(ct))
                        .Select(c => (c.Id, new EntityInfo(null, null, c.Id, c.Number))),
                    nameof(CompetitionResultVersion) => (await db.CompetitionResultVersions.AsNoTracking()
                            .Where(v => batch.Contains(v.Id))
                            .Select(v => new { v.Id, v.CompetitionId, v.Competition!.Number, v.VersionNo })
                            .ToListAsync(ct))
                        .Select(v => (v.Id, new EntityInfo(null, null, v.CompetitionId, $"{v.Number} — نتائج ن{v.VersionNo}"))),
                    _ => []
                };
                foreach (var (id, info) in rows)
                    result[(group.Key, id)] = info;
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ competitions

    /// <summary><c>recruitment_competitions</c>: unrestricted users only.</summary>
    public async Task<CompetitionsResult> GetCompetitionsAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null || !resolved.Unrestricted)
            return new CompetitionsResult(false, []);

        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var competitions = db.RecruitmentCompetitions.AsNoTracking().AsQueryable();
        if (filter.CompetitionId is { } competitionId)
            competitions = competitions.Where(c => c.Id == competitionId);
        else
            competitions = competitions.Where(c => (c.AnnouncedAt >= from && c.AnnouncedAt < to)
                || (c.AnnouncedAt != null && c.Status != RecruitmentCompetitionStatus.Finalized && c.Status != RecruitmentCompetitionStatus.Cancelled));
        if (filter.RequisitionId is { } requisitionId)
            competitions = competitions.Where(c => c.Lines.Any(l => l.Requisitions.Any(r => r.Id == requisitionId)));
        if (resolved.Units is { } units)
            competitions = competitions.Where(c => c.Lines.Any(l => l.Requisitions.Any(r => units.Contains(r.FundedPosition!.OrgUnitId))));

        var list = await competitions
            .OrderByDescending(c => c.AnnouncedAt).ThenBy(c => c.Number)
            .Take(ListLimit)
            .Select(c => new { c.Id, c.Number, c.Title, c.Status, c.CurrentResultVersionId })
            .ToListAsync(ct);
        if (list.Count == 0)
            return new CompetitionsResult(true, []);

        var ids = list.Select(c => c.Id).ToArray();
        var versionIds = list.Where(c => c.CurrentResultVersionId.HasValue).Select(c => c.CurrentResultVersionId!.Value).ToArray();
        const int shortlisted = (int)RecruitmentApplicationStatus.Shortlisted;

        var applicants = await CountByAsync(db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId != null && ids.Contains(a.CompetitionId.Value))
            .Select(a => new KeyedId { Key = a.CompetitionId!.Value, Id = a.Id }), ct);
        var admitted = await CountByAsync(db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId != null && ids.Contains(a.CompetitionId.Value)
                && (AdmittedStatuses.Contains(a.Status)
                    || db.ExamCandidates.Any(x => x.RecruitmentApplicationId == a.Id)
                    || db.RecruitmentApplicationEvents.Any(e => e.RecruitmentApplicationId == a.Id
                        && e.EventType == RecruitmentApplicationEventTypes.StatusChanged && e.ToStatus == shortlisted)))
            .Select(a => new KeyedId { Key = a.CompetitionId!.Value, Id = a.Id }), ct);
        var present = await CountByAsync(db.ExamCandidates.AsNoTracking()
            .Where(x => ids.Contains(x.CompetitionId) && x.Attendance == ExamAttendance.Present)
            .Select(x => new KeyedId { Key = x.CompetitionId, Id = x.RecruitmentApplicationId }), ct);
        var passedByVersion = await CountByAsync(db.CompetitionResults.AsNoTracking()
            .Where(r => versionIds.Contains(r.ResultVersionId) && r.Outcome == CompetitionResultOutcome.Passed)
            .Select(r => new KeyedId { Key = r.ResultVersionId, Id = r.RecruitmentApplicationId }), ct);
        var allocated = await CountByAsync(db.CompetitionAllocations.AsNoTracking()
            .Where(x => ids.Contains(x.CompetitionId) && !x.FromReserve)
            .Select(x => new KeyedId { Key = x.CompetitionId, Id = x.RecruitmentApplicationId }), ct);
        var reserve = await CountByAsync(db.CompetitionReserveEntries.AsNoTracking()
            .Where(x => ids.Contains(x.CompetitionLine!.CompetitionId))
            .Select(x => new KeyedId { Key = x.CompetitionLine!.CompetitionId, Id = x.RecruitmentApplicationId }), ct);
        var recalled = await CountByAsync(db.CompetitionReserveEntries.AsNoTracking()
            .Where(x => ids.Contains(x.CompetitionLine!.CompetitionId) && x.Status == CompetitionReserveEntryStatus.Recalled)
            .Select(x => new KeyedId { Key = x.CompetitionLine!.CompetitionId, Id = x.RecruitmentApplicationId }), ct);
        var objections = db.RecruitmentAppeals.AsNoTracking()
            .Where(a => a.Kind == RecruitmentAppealKind.ResultObjection
                && a.RecruitmentApplication!.CompetitionId != null && ids.Contains(a.RecruitmentApplication.CompetitionId.Value));
        var objectionsFiled = await CountByAsync(objections
            .Select(a => new KeyedId { Key = a.RecruitmentApplication!.CompetitionId!.Value, Id = a.Id }), ct);
        var objectionsUpheld = await CountByAsync(objections
            .Where(a => a.Outcome == RecruitmentAppealOutcome.ObjectionUpheld)
            .Select(a => new KeyedId { Key = a.RecruitmentApplication!.CompetitionId!.Value, Id = a.Id }), ct);

        static int Get(IReadOnlyDictionary<Guid, int> counts, Guid key) => counts.TryGetValue(key, out var n) ? n : 0;
        return new CompetitionsResult(true, list.Select(c => new CompetitionRow(c.Id, c.Number, c.Title, c.Status,
            Get(applicants, c.Id), Get(admitted, c.Id), Get(present, c.Id),
            c.CurrentResultVersionId is { } v ? Get(passedByVersion, v) : 0,
            Get(allocated, c.Id), Get(reserve, c.Id), Get(recalled, c.Id), Get(objectionsFiled, c.Id), Get(objectionsUpheld, c.Id))).ToList());
    }

    private sealed class KeyedId
    {
        public Guid Key { get; init; }
        public Guid Id { get; init; }
    }

    /// <summary>Distinct ids per key (one grouped query).</summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> CountByAsync(IQueryable<KeyedId> rows, CancellationToken ct) =>
        await rows
            .GroupBy(r => r.Key)
            .Select(g => new { g.Key, Count = g.Select(r => r.Id).Distinct().Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    // ------------------------------------------------------------------ requisitions

    /// <summary>
    /// Requisitions in scope that are open now or were requested, closed or cancelled in the period, oldest first, with ages,
    /// applicants (channel filter applied), funnel stage counts and the time-to-fill when filled.
    /// </summary>
    public async Task<RequisitionTableResult> GetRequisitionTableAsync(
        RecruitmentMetricsFilter filter,
        OrgAccessScope scope,
        int page = 1,
        int pageSize = SearchFilterBase.MaxPageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.MaxPageSize);
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return new RequisitionTableResult(0, page, pageSize, []);

        var (from, to) = (filter.FromUtc, filter.ToUtc);
        var requisitions = Requisitions(filter, resolved).Where(r =>
            (r.Status != RequisitionStatus.Closed && r.Status != RequisitionStatus.Cancelled)
            || (r.RequestedDate >= from && r.RequestedDate < to)
            || (r.ClosedDate >= from && r.ClosedDate < to)
            || (r.CancelledAt >= from && r.CancelledAt < to));
        var total = await requisitions.CountAsync(ct);
        var rows = await requisitions
            .OrderBy(r => r.ApprovedDate ?? r.RequestedDate).ThenBy(r => r.RequisitionNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new RequisitionHeader(r.Id, r.RequisitionNumber, r.JobTitle,
                r.FundedPosition != null && r.FundedPosition.OrgUnit != null ? r.FundedPosition.OrgUnit.NameAr : "",
                r.Status, r.RequestedDate, r.ApprovedDate, r.PublishedAt, r.ClosedDate ?? r.CancelledAt))
            .ToListAsync(ct);
        var built = await BuildRequisitionRowsAsync(rows, filter, resolved, scope, ct);
        return new RequisitionTableResult(total, page, pageSize, built);
    }

    private sealed record RequisitionHeader(
        Guid Id, string Number, string JobTitle, string OrgUnitName, RequisitionStatus Status,
        DateTime RequestedDate, DateTime? ApprovedDate, DateTime? PublishedAt, DateTime? EndedAt);

    private async Task<List<RequisitionTableRow>> BuildRequisitionRowsAsync(
        IReadOnlyList<RequisitionHeader> rows, RecruitmentMetricsFilter filter, ScopeFilter resolved, OrgAccessScope scope, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var ids = rows.Select(r => r.Id).ToArray();
        var applications = Applications(filter with { RequisitionId = null }, resolved)
            .Where(a => a.RequisitionId != null && ids.Contains(a.RequisitionId.Value));
        var reach = await LoadReachAsync(applications, ct);
        var fills = await TimeToFillByRequisitionAsync(filter with { RequisitionId = null }, scope, ct);
        var now = UtcNow;

        return rows.Select(r =>
        {
            var apps = reach.Apps.Where(a => a.RequisitionId == r.Id).ToList();
            var stages = FunnelReachRules.Stages
                .Select(stage => new FunnelStageCount(stage, apps.Count(a => reach.Furthest[a.Id] >= stage)))
                .ToList();
            int? fillDays = null;
            if (fills.TryGetValue(r.Id, out var fill) && fill.Hires.Count > 0)
            {
                var (start, _) = TimeToFillRules.ResolveFillStart(fill.ApprovedDate, fill.RequestedDate);
                fillDays = KpiStatistics.CalendarDays(start, fill.Hires.Min(h => h.At));
            }
            var ageStart = r.ApprovedDate ?? r.RequestedDate;
            return new RequisitionTableRow(r.Id, r.Number, r.JobTitle, r.OrgUnitName, r.Status, r.RequestedDate, r.ApprovedDate,
                r.PublishedAt, r.EndedAt, KpiStatistics.CalendarDays(ageStart, r.EndedAt ?? now), apps.Count, stages, fillDays);
        }).ToList();
    }

    /// <summary>One requisition (scope-checked; null when outside the scope or not found).</summary>
    public async Task<RequisitionDetailResult?> GetRequisitionDetailAsync(
        Guid requisitionId,
        OrgAccessScope scope,
        bool includeNames,
        CancellationToken ct = default)
    {
        var filter = RecruitmentMetricsFilter.ForRequisition(requisitionId);
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return null;
        var header = await Requisitions(filter, resolved)
            .Select(r => new RequisitionHeader(r.Id, r.RequisitionNumber, r.JobTitle,
                r.FundedPosition != null && r.FundedPosition.OrgUnit != null ? r.FundedPosition.OrgUnit.NameAr : "",
                r.Status, r.RequestedDate, r.ApprovedDate, r.PublishedAt, r.ClosedDate ?? r.CancelledAt))
            .FirstOrDefaultAsync(ct);
        if (header is null)
            return null;

        var row = (await BuildRequisitionRowsAsync([header], filter, resolved, scope, ct)).Single();
        var cohort = await CohortAsync(filter, scope, ct);
        var offers = await db.OfferLetters.AsNoTracking()
            .Where(o => o.RecruitmentApplication!.RequisitionId == requisitionId)
            .OrderBy(o => o.CreatedAt)
            .Take(ListLimit)
            .Select(o => new
            {
                o.Id,
                o.OfferNumber,
                o.RecruitmentApplicationId,
                o.RecruitmentApplication!.IntakeChannel,
                Name = includeNames ? o.RecruitmentApplication.ApplicantFullName : null,
                o.Status,
                o.CreatedAt,
                o.IssuedDate,
                o.AcceptedDate,
                o.DeclinedAt,
                o.ExpiredAt,
                o.RevokedAt,
                o.DeclineReasonCategory,
                o.IsMeritOverride,
                o.GradeDeviationReason,
                o.SalaryUnverified
            })
            .ToListAsync(ct);
        var references = await CitizenReferencesAsync(offers.Select(o => o.RecruitmentApplicationId).ToList(), ct);
        var offerRows = offers.Select(o => new RequisitionOfferRow(o.Id, o.OfferNumber,
                ApplicationReference(o.IntakeChannel, o.RecruitmentApplicationId, references.GetValueOrDefault(o.RecruitmentApplicationId)),
                o.Name, o.Status, o.CreatedAt, o.IssuedDate,
                o.Status switch
                {
                    OfferStatus.Accepted => o.AcceptedDate,
                    OfferStatus.Declined => o.DeclinedAt,
                    OfferStatus.Expired => o.ExpiredAt,
                    OfferStatus.Revoked => o.RevokedAt,
                    _ => null
                },
                o.Status == OfferStatus.Declined ? OfferDeclineReasonCategories.LabelAr(o.DeclineReasonCategory) : null,
                o.IsMeritOverride,
                o.IsMeritOverride || !string.IsNullOrWhiteSpace(o.GradeDeviationReason) || o.SalaryUnverified))
            .ToList();
        return new RequisitionDetailResult(row, BuildFunnel(cohort), BuildStageTime(cohort), offerRows);
    }

    // ------------------------------------------------------------------ application detail (exports)

    /// <summary>
    /// The cohort as detail rows for the CSV export (at most <paramref name="limit"/>, oldest first). Names only with
    /// <paramref name="includeNames"/>; never a national id.
    /// </summary>
    public async Task<IReadOnlyList<ApplicationDetailRow>> GetApplicationDetailsAsync(
        RecruitmentMetricsFilter filter,
        OrgAccessScope scope,
        bool includeNames,
        int limit,
        CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            return [];
        var rows = await ApplicationDetailQuery(filter, resolved, includeNames).Take(Math.Max(0, limit)).ToListAsync(ct);
        return await CompleteDetailRowsAsync(rows, filter, scope, ct);
    }

    /// <summary>Count of the cohort rows an application export would contain (for the sync/job decision).</summary>
    public async Task<int> CountApplicationDetailsAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        return resolved is null ? 0 : await CohortQuery(filter, resolved).CountAsync(ct);
    }

    /// <summary>Streams the cohort detail rows in pages (CSV export job).</summary>
    public async IAsyncEnumerable<ApplicationDetailRow> StreamApplicationDetailsAsync(
        RecruitmentMetricsFilter filter,
        OrgAccessScope scope,
        bool includeNames,
        int pageSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(filter, scope, ct);
        if (resolved is null)
            yield break;
        for (var skip = 0; ; skip += pageSize)
        {
            var page = await ApplicationDetailQuery(filter, resolved, includeNames).Skip(skip).Take(pageSize).ToListAsync(ct);
            if (page.Count == 0)
                yield break;
            foreach (var row in await CompleteDetailRowsAsync(page, filter, scope, ct))
                yield return row;
            if (page.Count < pageSize)
                yield break;
        }
    }

    private IQueryable<RecruitmentApplication> CohortQuery(RecruitmentMetricsFilter filter, ScopeFilter resolved) =>
        Applications(filter, resolved).Where(a => a.AppliedAt >= filter.FromUtc && a.AppliedAt < filter.ToUtc);

    private sealed record DetailProjection(
        Guid Id, string? Name, RecruitmentIntakeChannel Channel, string? RequisitionNumber, string? OrgUnitName,
        RecruitmentApplicationStatus Status, DateTime AppliedAt, DateTime? ScreenedAt, DateTime? RankedAt, DateTime? RejectedAt);

    private IQueryable<DetailProjection> ApplicationDetailQuery(RecruitmentMetricsFilter filter, ScopeFilter resolved, bool includeNames) =>
        CohortQuery(filter, resolved)
            .OrderBy(a => a.AppliedAt).ThenBy(a => a.Id)
            .Select(a => new DetailProjection(a.Id, includeNames ? a.ApplicantFullName : null, a.IntakeChannel,
                a.Requisition != null ? a.Requisition.RequisitionNumber : null,
                a.Requisition != null && a.Requisition.FundedPosition != null && a.Requisition.FundedPosition.OrgUnit != null
                    ? a.Requisition.FundedPosition.OrgUnit.NameAr : null,
                a.Status, a.AppliedAt, a.ScreenedAt, a.RankedAt, a.RejectedAt));

    private async Task<IReadOnlyList<ApplicationDetailRow>> CompleteDetailRowsAsync(
        IReadOnlyList<DetailProjection> rows, RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var references = await CitizenReferencesAsync(ids, ct);
        var hires = (await HiresAsync(filter, scope, ct)).ToDictionary(h => h.Id, h => h.Hired.At);
        var latestOffer = new Dictionary<Guid, OfferStatus>();
        foreach (var batch in ids.Chunk(IdBatchSize))
        {
            var offers = await db.OfferLetters.AsNoTracking()
                .Where(o => batch.Contains(o.RecruitmentApplicationId))
                .Select(o => new { o.RecruitmentApplicationId, o.Status, o.CreatedAt })
                .ToListAsync(ct);
            foreach (var group in offers.GroupBy(o => o.RecruitmentApplicationId))
                latestOffer[group.Key] = group.OrderByDescending(o => o.CreatedAt).First().Status;
        }
        return rows.Select(r => new ApplicationDetailRow(r.Id,
                ApplicationReference(r.Channel, r.Id, references.GetValueOrDefault(r.Id)), r.Name, r.Channel,
                r.RequisitionNumber, r.OrgUnitName, r.Status, r.AppliedAt, r.ScreenedAt, r.RankedAt, r.RejectedAt,
                hires.TryGetValue(r.Id, out var hiredAt) ? hiredAt : null,
                latestOffer.TryGetValue(r.Id, out var offer) ? offer.ToString() : null))
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> CitizenReferencesAsync(IReadOnlyList<Guid> applicationIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var batch in applicationIds.Distinct().Chunk(IdBatchSize))
        {
            var rows = await db.CitizenApplications.AsNoTracking()
                .Where(c => c.PromotedRecruitmentApplicationId != null && batch.Contains(c.PromotedRecruitmentApplicationId.Value))
                .Select(c => new { Id = c.PromotedRecruitmentApplicationId!.Value, c.ReferenceNumber })
                .ToListAsync(ct);
            foreach (var row in rows)
                result[row.Id] = row.ReferenceNumber;
        }
        return result;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The application reference shown instead of a name (decision 7): the citizen reference number of a public application,
    /// else the intake channel code and the first eight hex digits of the id (e.g. <c>INT-1A2B3C4D</c>).
    /// </summary>
    public static string ApplicationReference(RecruitmentIntakeChannel channel, Guid applicationId, string? citizenReference) =>
        !string.IsNullOrWhiteSpace(citizenReference)
            ? citizenReference
            : $"{ChannelCode(channel)}-{ShortId(applicationId)}";

    public static string ChannelCode(RecruitmentIntakeChannel channel) => channel switch
    {
        RecruitmentIntakeChannel.Public => "PUB",
        RecruitmentIntakeChannel.Internal => "INT",
        RecruitmentIntakeChannel.HrEntered => "HR",
        _ => "LEG"
    };

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string EnumLabel<TEnum>(TEnum value) where TEnum : struct, Enum =>
        HRManagement.SharedKernel.Localization.EnumDisplayExtensions.GetDisplayName(value);

    private static List<T> DeserializeOrEmpty<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
