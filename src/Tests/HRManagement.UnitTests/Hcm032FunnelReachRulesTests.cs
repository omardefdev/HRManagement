using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.RecruitmentKpi;

namespace HRManagement.UnitTests;

/// <summary>HCM-032: which funnel stages an application reached — events, current status and legacy evidence.</summary>
public class Hcm032FunnelReachRulesTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static FunnelEvidence Evidence(
        RecruitmentApplicationStatus status,
        DateTime? screened = null,
        DateTime? ranked = null,
        bool issued = false,
        bool accepted = false,
        bool hired = false) => new(status, screened, ranked, issued, accepted, hired);

    private static FunnelEvent Changed(RecruitmentApplicationStatus to, int day) =>
        new(RecruitmentApplicationEventTypes.StatusChanged, (int)to, T0.AddDays(day));

    [Fact]
    public void Status_event_reaches_its_stage_and_every_earlier_stage()
    {
        var events = new[] { Changed(RecruitmentApplicationStatus.Assessed, 3) };

        var reached = FunnelReachRules.ReachedStages(events, Evidence(RecruitmentApplicationStatus.Rejected));

        Assert.Equal(
            [RecruitmentFunnelStage.Applied, RecruitmentFunnelStage.Screened, RecruitmentFunnelStage.Shortlisted,
             RecruitmentFunnelStage.Interviewed, RecruitmentFunnelStage.Assessed],
            reached.OrderBy(s => s));
    }

    [Fact]
    public void Terminal_statuses_imply_nothing()
    {
        foreach (var terminal in new[] { RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn, RecruitmentApplicationStatus.OfferDeclined })
        {
            Assert.Null(FunnelReachRules.StageOf(terminal));
            var events = new[] { Changed(terminal, 1) };
            Assert.Equal(RecruitmentFunnelStage.Applied, FunnelReachRules.FurthestStage(events, Evidence(terminal)));
        }
    }

    [Fact]
    public void Rejected_after_an_interview_keeps_the_interview_stage()
    {
        var events = new FunnelEvent[]
        {
            new(RecruitmentApplicationEventTypes.Received, (int)RecruitmentApplicationStatus.Submitted, T0),
            Changed(RecruitmentApplicationStatus.UnderReview, 1),
            new(RecruitmentApplicationEventTypes.InterviewScheduled, 0, T0.AddDays(2)),
            Changed(RecruitmentApplicationStatus.Rejected, 4)
        };

        Assert.Equal(RecruitmentFunnelStage.Interviewed,
            FunnelReachRules.FurthestStage(events, Evidence(RecruitmentApplicationStatus.Rejected)));
    }

    [Fact]
    public void Offer_events_reach_offer_stages_even_when_the_offer_was_declined_later()
    {
        var events = new FunnelEvent[]
        {
            new(RecruitmentApplicationEventTypes.OfferIssued, 2, T0.AddDays(10)),
            Changed(RecruitmentApplicationStatus.OfferDeclined, 12)
        };

        Assert.Equal(RecruitmentFunnelStage.OfferIssued,
            FunnelReachRules.FurthestStage(events, Evidence(RecruitmentApplicationStatus.OfferDeclined)));
        Assert.Equal(RecruitmentFunnelStage.OfferAccepted, FunnelReachRules.FurthestStage(
            [new(RecruitmentApplicationEventTypes.OfferAccepted, 3, T0)], Evidence(RecruitmentApplicationStatus.Rejected)));
    }

    [Fact]
    public void Current_status_implies_every_earlier_stage()
    {
        Assert.Equal(RecruitmentFunnelStage.Ranked,
            FunnelReachRules.FurthestStage([], Evidence(RecruitmentApplicationStatus.Ranked)));
        Assert.Equal(RecruitmentFunnelStage.Ranked,
            FunnelReachRules.FurthestStage([], Evidence(RecruitmentApplicationStatus.ReserveListed)));
        Assert.Equal(RecruitmentFunnelStage.Hired,
            FunnelReachRules.FurthestStage([], Evidence(RecruitmentApplicationStatus.Hired)));
        Assert.Equal(RecruitmentFunnelStage.Applied,
            FunnelReachRules.FurthestStage([], Evidence(RecruitmentApplicationStatus.Submitted)));
    }

    [Fact]
    public void Legacy_rows_without_events_use_the_evidence_fallbacks()
    {
        Assert.Equal(RecruitmentFunnelStage.Screened, FunnelReachRules.FurthestStage([],
            Evidence(RecruitmentApplicationStatus.Rejected, screened: T0)));
        Assert.Equal(RecruitmentFunnelStage.Ranked, FunnelReachRules.FurthestStage([],
            Evidence(RecruitmentApplicationStatus.Rejected, screened: T0, ranked: T0)));
        Assert.Equal(RecruitmentFunnelStage.OfferIssued, FunnelReachRules.FurthestStage([],
            Evidence(RecruitmentApplicationStatus.OfferDeclined, issued: true)));
        Assert.Equal(RecruitmentFunnelStage.OfferAccepted, FunnelReachRules.FurthestStage([],
            Evidence(RecruitmentApplicationStatus.Withdrawn, issued: true, accepted: true)));
        Assert.Equal(RecruitmentFunnelStage.Hired, FunnelReachRules.FurthestStage([],
            Evidence(RecruitmentApplicationStatus.Rejected, hired: true)));
    }

    [Fact]
    public void Events_and_evidence_combine_to_the_furthest_stage()
    {
        // The event history started after screening; the row still shows RankedAt.
        var events = new[] { Changed(RecruitmentApplicationStatus.Withdrawn, 2) };

        Assert.Equal(RecruitmentFunnelStage.Ranked,
            FunnelReachRules.FurthestStage(events, Evidence(RecruitmentApplicationStatus.Withdrawn, ranked: T0)));
    }

    [Fact]
    public void Unrelated_events_and_unknown_statuses_are_ignored()
    {
        var events = new FunnelEvent[]
        {
            new(RecruitmentApplicationEventTypes.AppealFiled, 1, T0),
            new(RecruitmentApplicationEventTypes.OfferRevoked, 6, T0),
            new(RecruitmentApplicationEventTypes.StatusChanged, 99, T0),
            new(RecruitmentApplicationEventTypes.CompetitionReserve, null, T0)
        };

        Assert.Equal(RecruitmentFunnelStage.Applied,
            FunnelReachRules.FurthestStage(events, Evidence(RecruitmentApplicationStatus.Rejected)));
    }

    [Fact]
    public void Stage_durations_are_days_between_consecutive_status_events()
    {
        var events = new FunnelEvent[]
        {
            new(RecruitmentApplicationEventTypes.Received, (int)RecruitmentApplicationStatus.Submitted, T0),
            Changed(RecruitmentApplicationStatus.UnderReview, 2),
            new(RecruitmentApplicationEventTypes.InterviewScheduled, 0, T0.AddDays(3)),
            Changed(RecruitmentApplicationStatus.Shortlisted, 7),
            Changed(RecruitmentApplicationStatus.Rejected, 8)
        };

        var durations = TimeToFillRules.StageDurations(events);

        Assert.Equal(
            [new StageDuration(RecruitmentApplicationStatus.Submitted, 2),
             new StageDuration(RecruitmentApplicationStatus.UnderReview, 5),
             new StageDuration(RecruitmentApplicationStatus.Shortlisted, 1)],
            durations);
    }

    [Fact]
    public void Hired_time_prefers_the_event_and_marks_the_employee_fallback()
    {
        var hiredAt = T0.AddDays(30);
        var withEvent = TimeToFillRules.ResolveHiredTime([Changed(RecruitmentApplicationStatus.Hired, 30)], T0.AddDays(40));
        var fallback = TimeToFillRules.ResolveHiredTime([Changed(RecruitmentApplicationStatus.Ranked, 3)], T0.AddDays(40));

        Assert.Equal(new HiredTime(hiredAt, false), withEvent);
        Assert.Equal(new HiredTime(T0.AddDays(40), true), fallback);
        Assert.Null(TimeToFillRules.ResolveHiredTime([], null));
    }

    [Fact]
    public void Time_to_fill_starts_at_approval_falls_back_to_request_and_needs_a_hire_in_the_period()
    {
        var requested = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        var approved = new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc);
        var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = Guid.NewGuid();
        HiredTime[] hires = [new(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), false), new(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), true)];

        // The first hire (10 Oct, fallback) fills it: 5 Sep → 10 Oct = 35 days.
        Assert.Equal(new TimeToFillResult(id, 35, false, true), TimeToFillRules.TimeToFill(id, approved, requested, hires, from, to));
        // No approval date: 1 Sep → 10 Oct = 39 days, start marked as fallback.
        Assert.Equal(new TimeToFillResult(id, 39, true, true), TimeToFillRules.TimeToFill(id, null, requested, hires, from, to));
        // First hire before the period → not filled in the period.
        Assert.Null(TimeToFillRules.TimeToFill(id, approved, requested,
            [new HiredTime(new DateTime(2026, 9, 30, 23, 0, 0, DateTimeKind.Utc), false), hires[0]], from, to));
        Assert.Null(TimeToFillRules.TimeToFill(id, approved, requested, [], from, to));
    }
}
