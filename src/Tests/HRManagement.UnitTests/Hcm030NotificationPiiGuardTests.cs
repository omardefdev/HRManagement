using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using T = HR.Core.Domain.Services.CandidateNotificationTemplates;

namespace HRManagement.UnitTests;

/// <summary>
/// COM-030 (PLAN-30 F6): candidate messages never carry the national id, salary, birth date, scores, rank, committee
/// data, internal notes, panel names or the candidate's name; SMS stays one Arabic segment.
/// </summary>
public class Hcm030NotificationPiiGuardTests
{
    private const string Reference = "CIT-2026-000123";
    private static readonly DateTime OccurredAt = new(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc);

    // Sensitive sample values planted in the source rows; none may reach a rendered message.
    private const string FullName = "سامر الخطيب";
    private const string NationalId = "0109876543210";
    private const string BirthYear = "1987";
    private const string Salary = "987654";
    private const string GradeCode = "GX-SECRET";
    private const string Score = "88.75";
    private const string InternalNote = "ملاحظة-داخلية-سرية";
    private const string PanelNote = "رأي-اللجنة-السري";
    private const string OutcomeNote = "تقييم-المقابلة-السري";
    private const string CancelReason = "سبب-الإلغاء-الداخلي";
    private const string RejectionNote = "سبب-الرفض-الداخلي";
    private const string OverrideReason = "مبرر-التجاوز-الداخلي";

    private static readonly string[] Forbidden =
    [
        FullName, "سامر", "الخطيب", NationalId, PiiMask.NationalId(NationalId), BirthYear, Salary, "987,654", GradeCode,
        Score, InternalNote, PanelNote, OutcomeNote, CancelReason, RejectionNote, OverrideReason,
        "راتب", "الراتب", "رتبة", "الترتيب", "درجة", "علامة", "لجنة", "اللجنة"
    ];

    private static readonly string[] ForbiddenPropertyFragments =
        ["Rank", "Score", "Committee", "Panel", "Note", "Assessor", "Salary", "Grade", "NationalId", "Birth", "Name", "Reason"];

    [Fact]
    public void Template_input_has_no_sensitive_field()
    {
        var names = typeof(CandidateNotificationTemplateInput).GetProperties().Select(p => p.Name).ToList();
        foreach (var fragment in ForbiddenPropertyFragments)
            Assert.DoesNotContain(names, n => n.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public static TheoryData<string, NotificationChannel> EveryTemplate()
    {
        var data = new TheoryData<string, NotificationChannel>();
        foreach (var code in T.All)
        {
            data.Add(code, NotificationChannel.Email);
            data.Add(code, NotificationChannel.Sms);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public void Rendered_message_carries_no_sensitive_value(string templateCode, NotificationChannel channel)
    {
        var (app, slot, offer, appeal) = SensitiveWorld();

        var content = T.Render(templateCode, channel, Input(app, slot, offer, appeal));

        var text = (content.Subject ?? string.Empty) + "\n" + content.Body;
        foreach (var value in Forbidden)
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        Assert.Contains(Reference, text, StringComparison.Ordinal);
        Assert.Equal(templateCode, content.TemplateCode);
        Assert.Equal(channel, content.Channel);
    }

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public void Sms_is_one_segment_and_email_fits_the_columns(string templateCode, NotificationChannel channel)
    {
        var (app, slot, offer, appeal) = SensitiveWorld();
        var inputs = new[]
        {
            Input(app, slot, offer, appeal),
            new CandidateNotificationTemplateInput { ReferenceNumber = Reference, OccurredAtUtc = OccurredAt }
        };

        foreach (var input in inputs)
        {
            var content = T.Render(templateCode, channel, input);
            if (channel == NotificationChannel.Sms)
            {
                Assert.Null(content.Subject);
                Assert.True(content.Body.Length <= T.SmsMaxLength,
                    $"{templateCode} SMS is {content.Body.Length} characters: {content.Body}");
                Assert.DoesNotContain("http", content.Body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("\n", content.Body, StringComparison.Ordinal);
            }
            else
            {
                Assert.NotNull(content.Subject);
                Assert.True(content.Subject!.Length <= 200);
                Assert.True(content.Body.Length <= 2000);
            }
        }
    }

    [Fact]
    public void Email_carries_the_track_link_only_when_configured()
    {
        var (app, slot, offer, appeal) = SensitiveWorld();
        var options = new RecruitmentOptions { PublicPortalBaseUrl = "https://hr.example.gov.sy/" };
        Assert.Equal("https://hr.example.gov.sy/careers/track", options.PublicTrackUrl());
        Assert.Null(new RecruitmentOptions().PublicTrackUrl());

        var withLink = T.Render(T.AppReceived, NotificationChannel.Email, Input(app, slot, offer, appeal) with
        {
            TrackUrl = options.PublicTrackUrl()
        });
        var withoutLink = T.Render(T.AppReceived, NotificationChannel.Email, Input(app, slot, offer, appeal));
        var sms = T.Render(T.AppReceived, NotificationChannel.Sms, Input(app, slot, offer, appeal) with
        {
            TrackUrl = options.PublicTrackUrl()
        });

        Assert.Contains("https://hr.example.gov.sy/careers/track", withLink.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", withoutLink.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", sms.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("?", withLink.Body.Split("careers/track")[1].Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Rejection_mentions_the_appeal_deadline_only_when_appealable()
    {
        var appealable = Rejected(RecruitmentRejectionKind.SelectionDecision);
        var notAppealable = Rejected(RecruitmentRejectionKind.RequisitionCancelled);

        var open = T.InputFor(Reference, OccurredAt, appealable);
        var closed = T.InputFor(Reference, OccurredAt, notAppealable);

        Assert.Equal(OccurredAt.AddDays(-1).AddDays(14), open.AppealDeadlineUtc);
        Assert.Null(closed.AppealDeadlineUtc);
        foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Sms })
        {
            var withAppeal = T.Render(T.AppRejected, channel, open).Body;
            var withoutAppeal = T.Render(T.AppRejected, channel, closed).Body;
            Assert.Contains("استئناف", withAppeal, StringComparison.Ordinal);
            Assert.Contains("2026/11/15", withAppeal, StringComparison.Ordinal);
            Assert.DoesNotContain("استئناف", withoutAppeal, StringComparison.Ordinal);
            Assert.Contains("لم يتم قبول طلبك", withoutAppeal, StringComparison.Ordinal);
        }
        Assert.Equal(open.AppealDeadlineUtc, T.ExpiresAt(T.AppRejected, open));
        Assert.Equal(OccurredAt.AddDays(30), T.ExpiresAt(T.AppRejected, closed));
    }

    [Fact]
    public void Offer_messages_show_the_deadline_and_never_the_salary()
    {
        var (app, slot, offer, appeal) = SensitiveWorld();
        var input = Input(app, slot, offer, appeal);

        var issued = T.Render(T.OfferIssued, NotificationChannel.Email, input);
        var reminder = T.Render(T.OfferReminder, NotificationChannel.Sms, input);

        Assert.Contains("صدر لك عرض وظيفي، يرجى الاطلاع والرد عبر البوابة قبل 2026/11/16", issued.Body, StringComparison.Ordinal);
        Assert.Contains("2026/11/16", reminder.Body, StringComparison.Ordinal);
        Assert.Equal(offer.ExpiryDate, issued.ExpiresAtUtc);
        Assert.DoesNotContain(Salary, issued.Body + reminder.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Interview_messages_show_time_mode_and_place_but_no_panel()
    {
        var (app, slot, offer, appeal) = SensitiveWorld();
        var input = Input(app, slot, offer, appeal);

        var email = T.Render(T.InterviewScheduled, NotificationChannel.Email, input).Body;
        var remote = T.Render(T.InterviewRescheduled, NotificationChannel.Sms, input with { InterviewMode = InterviewMode.Remote }).Body;

        Assert.Contains("2026/11/10 10:00", email, StringComparison.Ordinal);
        Assert.Contains("حضوري", email, StringComparison.Ordinal);
        Assert.Contains("قاعة الاجتماعات 2", email, StringComparison.Ordinal);
        Assert.Contains("عن بُعد", remote, StringComparison.Ordinal);
        Assert.Equal(slot.ScheduledAt, T.ExpiresAt(T.InterviewScheduled, input));
    }

    [Fact]
    public void Appeal_outcome_is_a_public_label_only()
    {
        Assert.Equal("قُبل الاستئناف", T.AppealOutcomeAr(RecruitmentAppealOutcome.UpheldRankCorrection));
        Assert.Equal("رُفض الاستئناف", T.AppealOutcomeAr(RecruitmentAppealOutcome.Dismissed));
        Assert.Equal("صدر القرار", T.AppealOutcomeAr(null));
    }

    [Theory]
    [InlineData(RecruitmentApplicationEventTypes.Received, null, T.AppReceived)]
    [InlineData(RecruitmentApplicationEventTypes.InterviewScheduled, null, T.InterviewScheduled)]
    [InlineData(RecruitmentApplicationEventTypes.InterviewRescheduled, null, T.InterviewRescheduled)]
    [InlineData(RecruitmentApplicationEventTypes.InterviewCancelled, null, T.InterviewCancelled)]
    [InlineData(RecruitmentApplicationEventTypes.OfferIssued, null, T.OfferIssued)]
    [InlineData(RecruitmentApplicationEventTypes.OfferExpiryReminder, null, T.OfferReminder)]
    [InlineData(RecruitmentApplicationEventTypes.OfferAccepted, null, T.OfferAccepted)]
    [InlineData(RecruitmentApplicationEventTypes.AppealDecided, null, T.AppealDecided)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Rejected, T.AppRejected)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.ReserveListed, T.AppReserve)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Hired, T.AppHired)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Withdrawn, T.AppWithdrawn)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Shortlisted, null)]
    [InlineData(RecruitmentApplicationEventTypes.OfferDeclined, null, null)]
    [InlineData(RecruitmentApplicationEventTypes.OfferExpired, null, null)]
    [InlineData(RecruitmentApplicationEventTypes.OfferRevoked, null, null)]
    [InlineData(RecruitmentApplicationEventTypes.AppealFiled, null, null)]
    public void Event_map_follows_the_plan(string eventType, int? toStatus, string? expected) =>
        Assert.Equal(expected, T.TemplateCodeFor(eventType, toStatus));

    [Fact]
    public void Expiry_follows_the_plan()
    {
        var input = new CandidateNotificationTemplateInput
        {
            ReferenceNumber = Reference,
            OccurredAtUtc = OccurredAt,
            ReserveUntil = new DateOnly(2027, 5, 1)
        };

        Assert.Equal(OccurredAt.AddDays(30), T.ExpiresAt(T.AppReceived, input));
        Assert.Equal(OccurredAt.AddDays(30), T.ExpiresAt(T.OfferAccepted, input));
        Assert.Equal(OccurredAt.AddDays(30), T.ExpiresAt(T.AppHired, input));
        Assert.Equal(OccurredAt.AddDays(30), T.ExpiresAt(T.AppealDecided, input));
        Assert.Equal(OccurredAt.AddDays(7), T.ExpiresAt(T.AppWithdrawn, input));
        Assert.Equal(new DateOnly(2027, 5, 1).ToDateTime(TimeOnly.MaxValue), T.ExpiresAt(T.AppReserve, input));
        Assert.True(T.All.All(c => c.Length <= T.TemplateCodeMaxLength));
    }

    [Fact]
    public void Interview_messages_expire_at_the_interview_time_even_when_past()
    {
        var past = OccurredAt.AddHours(-3);
        var future = OccurredAt.AddDays(2);
        var noSlot = new CandidateNotificationTemplateInput { ReferenceNumber = Reference, OccurredAtUtc = OccurredAt };

        foreach (var code in new[] { T.InterviewScheduled, T.InterviewRescheduled, T.InterviewCancelled })
        {
            Assert.Equal(future, T.ExpiresAt(code, noSlot with { InterviewAtUtc = future }));
            // A slot cancelled after its time: the row expires at the (past) interview time, so it is never sent.
            Assert.Equal(past, T.ExpiresAt(code, noSlot with { InterviewAtUtc = past }));
            Assert.Equal(OccurredAt.AddDays(1), T.ExpiresAt(code, noSlot));
        }
    }

    [Theory]
    [InlineData("ahmad@example.sy", "a***@example.sy")]
    [InlineData("  x@y.co ", "x***@y.co")]
    [InlineData("0999000012", "09******12")]
    [InlineData("+963999000012", "+9*********12")]
    [InlineData("123", "***")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Recipient_masking(string? value, string expected)
    {
        var masked = value?.Contains('@') == true ? PiiMask.Email(value) : PiiMask.Phone(value);
        Assert.Equal(expected, masked);
    }

    [Fact]
    public void Idempotency_key_is_event_and_channel() =>
        Assert.Equal(
            "11111111-2222-3333-4444-555555555555:Sms",
            CandidateNotification.IdempotencyKeyFor(Guid.Parse("11111111-2222-3333-4444-555555555555"), NotificationChannel.Sms));

    private static CandidateNotificationTemplateInput Input(
        RecruitmentApplication app, RecruitmentInterviewSlot slot, OfferLetter offer, RecruitmentAppeal appeal) =>
        T.InputFor(Reference, OccurredAt, app, slot, offer, appeal);

    private static RecruitmentApplication Rejected(RecruitmentRejectionKind kind) => new()
    {
        ApplicantFullName = FullName,
        NationalId = NationalId,
        Status = RecruitmentApplicationStatus.Rejected,
        RejectionKind = kind,
        RejectedAt = OccurredAt.AddDays(-1),
        RejectionNote = RejectionNote
    };

    /// <summary>Rows full of sensitive values, as the planner would load them.</summary>
    private static (RecruitmentApplication, RecruitmentInterviewSlot, OfferLetter, RecruitmentAppeal) SensitiveWorld()
    {
        var app = new RecruitmentApplication
        {
            ApplicantFullName = FullName,
            NationalId = NationalId,
            NationalIdHash = PiiMask.Sha256Hex(NationalId),
            BirthDate = DateOnly.ParseExact(BirthYear + "-03-15", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            AssessmentScore = decimal.Parse(Score, CultureInfo.InvariantCulture),
            Rank = 3,
            RankOverrideReason = OverrideReason,
            Notes = InternalNote,
            AppealNotes = InternalNote,
            RejectionNote = RejectionNote,
            EligibilityOverrideReason = OverrideReason,
            Status = RecruitmentApplicationStatus.Rejected,
            RejectionKind = RecruitmentRejectionKind.SelectionDecision,
            RejectedAt = OccurredAt,
            ReserveUntil = new DateOnly(2027, 5, 1),
            AppliedPosition = "محلل نظم"
        };
        var slot = new RecruitmentInterviewSlot
        {
            RecruitmentApplicationId = app.Id,
            ScheduledAt = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc),
            Mode = InterviewMode.InPerson,
            Location = "قاعة الاجتماعات 2",
            PanelNotes = PanelNote,
            OutcomeNotes = OutcomeNote,
            CancelledReason = CancelReason
        };
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            OfferedSalary = decimal.Parse(Salary, CultureInfo.InvariantCulture),
            GradeCode = GradeCode,
            ExpiryDate = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)
        };
        var appeal = new RecruitmentAppeal
        {
            RecruitmentApplicationId = app.Id,
            Reason = InternalNote,
            Decision = PanelNote,
            Outcome = RecruitmentAppealOutcome.Dismissed
        };
        return (app, slot, offer, appeal);
    }
}
