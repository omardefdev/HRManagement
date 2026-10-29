using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services.RecruitmentKpi;

/// <summary>HCM-032: stages of the recruitment funnel (STR-007 <c>recruitment_funnel</c>), in funnel order.</summary>
public enum RecruitmentFunnelStage
{
    [Display(Name = "التقديم")]
    Applied = 0,
    [Display(Name = "الفرز")]
    Screened = 1,
    [Display(Name = "القائمة المختصرة")]
    Shortlisted = 2,
    [Display(Name = "المقابلة")]
    Interviewed = 3,
    [Display(Name = "التقييم")]
    Assessed = 4,
    [Display(Name = "الترتيب")]
    Ranked = 5,
    [Display(Name = "إصدار العرض")]
    OfferIssued = 6,
    [Display(Name = "قبول العرض")]
    OfferAccepted = 7,
    [Display(Name = "التعيين")]
    Hired = 8
}

/// <summary>One application event as the funnel sees it (no personal data).</summary>
public sealed record FunnelEvent(string EventType, int? ToStatus, DateTime OccurredAt);

/// <summary>Evidence on the application row for rows recorded before the event history (2026-10-23).</summary>
public sealed record FunnelEvidence(
    RecruitmentApplicationStatus CurrentStatus,
    DateTime? ScreenedAt,
    DateTime? RankedAt,
    bool HasIssuedOffer,
    bool HasAcceptedOffer,
    bool HasHiredEmployee);

/// <summary>
/// HCM-032 (PLAN-32, decision 1): which funnel stages an application reached. Reach is monotone — reaching a stage implies
/// every earlier stage ("StatusChanged to that status or beyond") — so the funnel counts never increase from one stage to
/// the next. Sources: events (<c>Received</c>, <c>StatusChanged</c> to a non-terminal status, <c>InterviewScheduled</c>,
/// <c>OfferIssued</c>, <c>OfferAccepted</c>), the current status, and the documented evidence fallbacks
/// (<c>ScreenedAt</c>, <c>RankedAt</c>, offer dates, hired employee). The terminal statuses Rejected, Withdrawn and
/// OfferDeclined imply nothing.
/// </summary>
public static class FunnelReachRules
{
    public static readonly IReadOnlyList<RecruitmentFunnelStage> Stages = Enum.GetValues<RecruitmentFunnelStage>();

    /// <summary>The funnel stage a status stands for; null for the terminal statuses.</summary>
    public static RecruitmentFunnelStage? StageOf(RecruitmentApplicationStatus status) => status switch
    {
        RecruitmentApplicationStatus.Submitted => RecruitmentFunnelStage.Applied,
        RecruitmentApplicationStatus.UnderReview => RecruitmentFunnelStage.Screened,
        RecruitmentApplicationStatus.Shortlisted => RecruitmentFunnelStage.Shortlisted,
        RecruitmentApplicationStatus.InterviewScheduled => RecruitmentFunnelStage.Interviewed,
        RecruitmentApplicationStatus.Assessed => RecruitmentFunnelStage.Assessed,
        RecruitmentApplicationStatus.Ranked => RecruitmentFunnelStage.Ranked,
        // A reserve list is drawn from ranked candidates.
        RecruitmentApplicationStatus.ReserveListed => RecruitmentFunnelStage.Ranked,
        RecruitmentApplicationStatus.OfferExtended => RecruitmentFunnelStage.OfferIssued,
        RecruitmentApplicationStatus.OfferAccepted => RecruitmentFunnelStage.OfferAccepted,
        RecruitmentApplicationStatus.Hired => RecruitmentFunnelStage.Hired,
        _ => null
    };

    /// <summary>The furthest stage reached (Applied at least: every cohort application applied).</summary>
    public static RecruitmentFunnelStage FurthestStage(IEnumerable<FunnelEvent> events, FunnelEvidence evidence)
    {
        var furthest = RecruitmentFunnelStage.Applied;

        void Reach(RecruitmentFunnelStage? stage)
        {
            if (stage is { } s && s > furthest) furthest = s;
        }

        foreach (var e in events)
            Reach(StageOfEvent(e));

        Reach(StageOf(evidence.CurrentStatus));
        if (evidence.ScreenedAt.HasValue) Reach(RecruitmentFunnelStage.Screened);
        if (evidence.RankedAt.HasValue) Reach(RecruitmentFunnelStage.Ranked);
        if (evidence.HasIssuedOffer) Reach(RecruitmentFunnelStage.OfferIssued);
        if (evidence.HasAcceptedOffer) Reach(RecruitmentFunnelStage.OfferAccepted);
        if (evidence.HasHiredEmployee) Reach(RecruitmentFunnelStage.Hired);
        return furthest;
    }

    public static bool Reached(RecruitmentFunnelStage furthest, RecruitmentFunnelStage stage) => stage <= furthest;

    public static IReadOnlySet<RecruitmentFunnelStage> ReachedStages(IEnumerable<FunnelEvent> events, FunnelEvidence evidence)
    {
        var furthest = FurthestStage(events, evidence);
        return Stages.Where(s => s <= furthest).ToHashSet();
    }

    private static RecruitmentFunnelStage? StageOfEvent(FunnelEvent e) => e.EventType switch
    {
        RecruitmentApplicationEventTypes.Received => RecruitmentFunnelStage.Applied,
        RecruitmentApplicationEventTypes.StatusChanged when e.ToStatus is { } to
            && Enum.IsDefined(typeof(RecruitmentApplicationStatus), to) => StageOf((RecruitmentApplicationStatus)to),
        RecruitmentApplicationEventTypes.InterviewScheduled => RecruitmentFunnelStage.Interviewed,
        RecruitmentApplicationEventTypes.OfferIssued => RecruitmentFunnelStage.OfferIssued,
        RecruitmentApplicationEventTypes.OfferAccepted => RecruitmentFunnelStage.OfferAccepted,
        _ => null
    };
}
