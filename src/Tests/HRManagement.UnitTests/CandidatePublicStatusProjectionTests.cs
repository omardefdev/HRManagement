using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using Codes = HR.Core.Domain.Services.CandidatePublicStatusProjection.Codes;
using Steps = HR.Core.Domain.Services.CandidatePublicStatusProjection.Steps;

namespace HRManagement.UnitTests;

/// <summary>HCM-030 (B2): the candidate-facing status and timeline.</summary>
public class CandidatePublicStatusProjectionTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

    private static CandidatePublicStatusInput Input(
        RecruitmentApplicationStatus status,
        OfferStatus? offer = null,
        DateTime? offerExpiry = null,
        IReadOnlyList<CandidateInterviewInput>? interviews = null,
        IReadOnlyList<CandidateEventInput>? events = null,
        bool appealOpen = false,
        DateTime? appealDeadline = null,
        RecruitmentAppealStatus appeal = RecruitmentAppealStatus.None,
        DateOnly? reserveUntil = null) => new()
    {
        Status = status,
        AppliedAtUtc = Now.AddDays(-30),
        ActiveOfferStatus = offer,
        ActiveOfferExpiryUtc = offerExpiry,
        Interviews = interviews ?? [],
        Events = events ?? [],
        AppealOpen = appealOpen,
        AppealDeadlineUtc = appealDeadline,
        AppealStatus = appeal,
        ReserveUntil = reserveUntil,
        NowUtc = Now
    };

    [Theory]
    [InlineData(RecruitmentApplicationStatus.Submitted, Codes.UnderReview, "قيد المراجعة")]
    [InlineData(RecruitmentApplicationStatus.UnderReview, Codes.UnderReview, "قيد المراجعة")]
    [InlineData(RecruitmentApplicationStatus.Shortlisted, Codes.InterviewInvited, "مدعو للمقابلة")]
    [InlineData(RecruitmentApplicationStatus.InterviewScheduled, Codes.InterviewInvited, "مدعو للمقابلة")]
    [InlineData(RecruitmentApplicationStatus.Assessed, Codes.FinalEvaluation, "قيد التقييم النهائي")]
    [InlineData(RecruitmentApplicationStatus.Ranked, Codes.FinalEvaluation, "قيد التقييم النهائي")]
    [InlineData(RecruitmentApplicationStatus.OfferAccepted, Codes.Accepted, "مقبول — بانتظار استكمال التعيين")]
    [InlineData(RecruitmentApplicationStatus.Hired, Codes.Accepted, "مقبول — بانتظار استكمال التعيين")]
    [InlineData(RecruitmentApplicationStatus.OfferDeclined, Codes.OfferClosed, "العرض منتهٍ/مرفوض")]
    [InlineData(RecruitmentApplicationStatus.Rejected, Codes.NotAccepted, "غير مقبول")]
    [InlineData(RecruitmentApplicationStatus.Withdrawn, Codes.Withdrawn, "منسحب")]
    public void Maps_each_internal_status(RecruitmentApplicationStatus status, string code, string label)
    {
        var view = CandidatePublicStatusProjection.Project(Input(status));

        Assert.Equal(code, view.Code);
        Assert.Equal(label, view.LabelAr);
    }

    [Theory]
    [InlineData(OfferStatus.Draft)]
    [InlineData(OfferStatus.PendingApproval)]
    public void Draft_or_pending_offer_is_not_shown_as_an_offer(OfferStatus offer)
    {
        var view = CandidatePublicStatusProjection.Project(
            Input(RecruitmentApplicationStatus.OfferExtended, offer, Now.AddDays(10)));

        Assert.Equal(Codes.FinalEvaluation, view.Code);
        Assert.Equal("قيد التقييم النهائي", view.LabelAr);
        Assert.False(view.CanRespondToOffer);
        Assert.Null(view.DeadlineUtc);
        Assert.False(view.Timeline.Single(s => s.Code == Steps.Offer).Reached);
    }

    [Fact]
    public void Issued_offer_waits_for_the_candidate_until_its_expiry()
    {
        var expiry = new DateTime(2026, 11, 6, 21, 0, 0, DateTimeKind.Utc);
        var view = CandidatePublicStatusProjection.Project(
            Input(RecruitmentApplicationStatus.OfferExtended, OfferStatus.Issued, expiry));

        Assert.Equal(Codes.OfferPending, view.Code);
        Assert.Equal("عرض بانتظار ردك حتى 2026/11/06", view.LabelAr);
        Assert.Equal(expiry, view.DeadlineUtc);
        Assert.True(view.CanRespondToOffer);
        Assert.True(view.Timeline.Single(s => s.Code == Steps.Offer).Reached);
    }

    [Fact]
    public void Expired_issued_offer_cannot_be_answered()
    {
        var view = CandidatePublicStatusProjection.Project(
            Input(RecruitmentApplicationStatus.OfferExtended, OfferStatus.Issued, Now.AddMinutes(-1)));

        Assert.False(view.CanRespondToOffer);
    }

    [Fact]
    public void Interview_invitation_shows_the_next_scheduled_interview()
    {
        var at = Now.AddDays(3);
        var view = CandidatePublicStatusProjection.Project(Input(
            RecruitmentApplicationStatus.InterviewScheduled,
            interviews:
            [
                new CandidateInterviewInput(Now.AddDays(-2), InterviewMode.InPerson, "قاعة قديمة", InterviewSlotStatus.Rescheduled),
                new CandidateInterviewInput(at, InterviewMode.Remote, "https://meet.example/x", InterviewSlotStatus.Scheduled),
                new CandidateInterviewInput(Now.AddDays(5), InterviewMode.InPerson, "ملغاة", InterviewSlotStatus.Cancelled)
            ]));

        Assert.Equal(at, view.NextInterview!.ScheduledAtUtc);
        Assert.Equal("عن بُعد", view.NextInterview.ModeAr);
        Assert.Contains("2026/10/26 09:00", view.DetailAr);
        Assert.Contains("https://meet.example/x", view.DetailAr);
    }

    [Fact]
    public void Reserve_list_shows_its_end_date()
    {
        var view = CandidatePublicStatusProjection.Project(
            Input(RecruitmentApplicationStatus.ReserveListed, reserveUntil: new DateOnly(2027, 4, 30)));

        Assert.Equal(Codes.ReserveListed, view.Code);
        Assert.Equal("في قائمة الاحتياط حتى 2027/04/30", view.LabelAr);
        Assert.True(view.CanWithdraw);
    }

    [Fact]
    public void Rejection_offers_the_appeal_only_while_the_window_is_open()
    {
        var deadline = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var open = CandidatePublicStatusProjection.Project(
            Input(RecruitmentApplicationStatus.Rejected, appealOpen: true, appealDeadline: deadline));
        var closed = CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.Rejected));

        Assert.Equal("يمكنك الاستئناف حتى 2026/11/02", open.DetailAr);
        Assert.True(open.CanAppeal);
        Assert.Equal(deadline, open.AppealOpenUntil);
        Assert.Null(closed.DetailAr);
        Assert.False(closed.CanAppeal);
        Assert.Null(closed.AppealOpenUntil);
    }

    [Fact]
    public void Pending_appeal_is_shown_instead_of_the_appeal_offer()
    {
        var view = CandidatePublicStatusProjection.Project(Input(
            RecruitmentApplicationStatus.Rejected, appealOpen: true, appealDeadline: Now.AddDays(3),
            appeal: RecruitmentAppealStatus.Pending));

        Assert.Equal("استئنافك قيد المراجعة", view.DetailAr);
        Assert.False(view.CanAppeal);
        Assert.True(view.Timeline.Single(s => s.Code == Steps.Appeal).Reached);
    }

    [Fact]
    public void Appeal_window_follows_the_appeal_rules()
    {
        var rejectedAt = Now.AddDays(-3);
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.Rejected,
            RejectedAt = rejectedAt,
            RejectionKind = RecruitmentRejectionKind.SelectionDecision
        };

        Assert.Equal((true, rejectedAt.AddDays(14)), CandidatePublicStatusProjection.AppealWindowOf(app, Now));
        Assert.Equal((false, null), CandidatePublicStatusProjection.AppealWindowOf(app, Now.AddDays(12)));
        app.RejectionKind = RecruitmentRejectionKind.RequisitionCancelled;
        Assert.Equal((false, null), CandidatePublicStatusProjection.AppealWindowOf(app, Now));
        app.RejectionKind = RecruitmentRejectionKind.SelectionDecision;
        app.AppealStatus = RecruitmentAppealStatus.Decided;
        Assert.Equal((false, null), CandidatePublicStatusProjection.AppealWindowOf(app, Now));
    }

    [Fact]
    public void Promoted_application_no_longer_shows_false_interview_or_hire_steps()
    {
        // AC-HCM-30b (negative): a linked application at Submitted reached nothing beyond reception.
        var view = CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.Submitted));

        Assert.Equal([Steps.Received], view.Timeline.Where(s => s.Reached).Select(s => s.Code));
        Assert.DoesNotContain(view.Timeline, s => s.LabelAr.Contains("التعيين"));
        Assert.False(view.Timeline.Single(s => s.Code == Steps.Decision).Reached);
    }

    [Fact]
    public void Shortlisted_without_events_reaches_review_and_interview_undated()
    {
        var view = CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.Shortlisted));

        var reached = view.Timeline.Where(s => s.Reached).ToList();
        Assert.Equal([Steps.Received, Steps.Review, Steps.Interview], reached.Select(s => s.Code));
        Assert.Null(reached.Single(s => s.Code == Steps.Interview).AtUtc);
        Assert.Equal(Now.AddDays(-30), reached.Single(s => s.Code == Steps.Received).AtUtc);
    }

    [Fact]
    public void Timeline_from_events_marks_only_what_happened_with_its_date()
    {
        var received = Now.AddDays(-10);
        var rejected = Now.AddDays(-2);
        var view = CandidatePublicStatusProjection.Project(Input(
            RecruitmentApplicationStatus.Rejected,
            events:
            [
                new CandidateEventInput(RecruitmentApplicationEventTypes.Received, null, (int)RecruitmentApplicationStatus.Submitted, received),
                new CandidateEventInput(RecruitmentApplicationEventTypes.StatusChanged,
                    (int)RecruitmentApplicationStatus.Submitted, (int)RecruitmentApplicationStatus.Rejected, rejected)
            ]));

        var byCode = view.Timeline.ToDictionary(s => s.Code);
        Assert.Equal(received, byCode[Steps.Received].AtUtc);
        Assert.False(byCode[Steps.Review].Reached);
        Assert.False(byCode[Steps.Interview].Reached);
        Assert.False(byCode[Steps.Offer].Reached);
        Assert.True(byCode[Steps.Decision].Reached);
        Assert.Equal(rejected, byCode[Steps.Decision].AtUtc);
        Assert.Equal("غير مقبول", byCode[Steps.Decision].DetailAr);
    }

    [Fact]
    public void Timeline_from_events_keeps_reached_steps_after_an_upheld_appeal()
    {
        var view = CandidatePublicStatusProjection.Project(Input(
            RecruitmentApplicationStatus.UnderReview,
            events:
            [
                new CandidateEventInput(RecruitmentApplicationEventTypes.Received, null, 0, Now.AddDays(-20)),
                new CandidateEventInput(RecruitmentApplicationEventTypes.StatusChanged, 0, 1, Now.AddDays(-18)),
                new CandidateEventInput(RecruitmentApplicationEventTypes.StatusChanged, 1, 3, Now.AddDays(-15)),
                new CandidateEventInput(RecruitmentApplicationEventTypes.AppealFiled, null, 1, Now.AddDays(-14)),
                new CandidateEventInput(RecruitmentApplicationEventTypes.StatusChanged, 3, 1, Now.AddDays(-5))
            ]));

        var byCode = view.Timeline.ToDictionary(s => s.Code);
        Assert.Equal(Codes.UnderReview, view.Code);
        Assert.Equal(Now.AddDays(-18), byCode[Steps.Review].AtUtc);
        Assert.True(byCode[Steps.Appeal].Reached);
        Assert.False(byCode[Steps.Decision].Reached);
    }

    [Fact]
    public void Terminal_statuses_cannot_withdraw()
    {
        Assert.False(CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.Hired)).CanWithdraw);
        Assert.False(CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.Rejected)).CanWithdraw);
        // HCM-030 (PLAN-30 I, DECISIONS I): no portal withdrawal once the offer is accepted (no workflow exit).
        Assert.False(CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.OfferAccepted)).CanWithdraw);
        Assert.True(CandidatePublicStatusProjection.Project(Input(RecruitmentApplicationStatus.OfferExtended)).CanWithdraw);
    }

    [Fact]
    public void Public_view_exposes_no_internal_data()
    {
        string[] forbidden = ["Rank", "Score", "Committee", "Panel", "Note", "Assessor", "Salary", "NationalId"];
        var types = new[] { typeof(CandidatePublicView), typeof(CandidatePublicTimelineStep), typeof(CandidatePublicInterview) };
        foreach (var property in types.SelectMany(t => t.GetProperties()))
            Assert.DoesNotContain(forbidden, f => property.Name.Contains(f, StringComparison.OrdinalIgnoreCase));

        var json = JsonSerializer.Serialize(CandidatePublicStatusProjection.Project(Input(
            RecruitmentApplicationStatus.InterviewScheduled,
            interviews: [new CandidateInterviewInput(Now.AddDays(1), InterviewMode.InPerson, "القاعة 2", InterviewSlotStatus.Scheduled)])));
        Assert.DoesNotContain("rank", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score", json, StringComparison.OrdinalIgnoreCase);
    }
}
