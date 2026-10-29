using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static HRManagement.UnitTests.Hcm031TestSupport;
using T = HR.Core.Domain.Services.CandidateNotificationTemplates;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P6, decision 4): the seven competition messages and the dispatcher rules for competition
/// applications — no generic rejection/reserve duplicate, a decided result objection has its own message. The PII guard
/// (<see cref="Hcm030NotificationPiiGuardTests"/>) iterates <see cref="T.All"/>, so it covers the new templates too.
/// </summary>
public sealed class Hcm031NotificationTests
{
    private const string Reference = "CIT-2026-000777";
    private static readonly string[] Banned = ["درجة", "علامة", "الترتيب", "رتبة", "لجنة", "اللجنة", "الرقم الوطني"];

    public static TheoryData<string, string> CompetitionEvents() => new()
    {
        { RecruitmentApplicationEventTypes.AdmissionCardReady, T.AdmissionCardReady },
        { RecruitmentApplicationEventTypes.ResultsPublished, T.ResultsPublished },
        { RecruitmentApplicationEventTypes.ObjectionDecided, T.ObjectionDecided },
        { RecruitmentApplicationEventTypes.CompetitionAllocated, T.CompetitionAllocated },
        { RecruitmentApplicationEventTypes.CompetitionReserve, T.CompetitionReserve },
        { RecruitmentApplicationEventTypes.CompetitionReserveRecalled, T.CompetitionReserveRecalled },
        { RecruitmentApplicationEventTypes.CompetitionNotSuccessful, T.CompetitionNotSuccessful }
    };

    [Theory]
    [MemberData(nameof(CompetitionEvents))]
    public void Each_competition_event_has_its_template_rendered_on_both_channels(string eventType, string templateCode)
    {
        Assert.Equal(templateCode, T.TemplateCodeFor(eventType, null));
        Assert.Contains(templateCode, T.All);
        Assert.True(templateCode.Length <= T.TemplateCodeMaxLength);
        Assert.NotEqual(templateCode, T.LabelAr(templateCode));

        var input = new CandidateNotificationTemplateInput
        {
            ReferenceNumber = Reference,
            OccurredAtUtc = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
            PortalUrl = "https://careers.example.sy/careers/my",
            ReserveUntil = new DateOnly(2027, 5, 1)
        };
        var email = T.Render(templateCode, NotificationChannel.Email, input);
        var sms = T.Render(templateCode, NotificationChannel.Sms, input);

        Assert.False(string.IsNullOrWhiteSpace(email.Subject));
        Assert.Contains(Reference, email.Body, StringComparison.Ordinal);
        Assert.Contains("/careers/my", email.Body, StringComparison.Ordinal);
        Assert.True(sms.Body.Length <= T.SmsMaxLength, $"{templateCode}: {sms.Body.Length}");
        Assert.DoesNotContain("http", sms.Body, StringComparison.Ordinal);
        foreach (var word in Banned)
        {
            Assert.DoesNotContain(word, email.Subject + email.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(word, sms.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_competition_reserve_message_expires_with_the_reserve()
    {
        var input = new CandidateNotificationTemplateInput
        {
            ReferenceNumber = Reference,
            OccurredAtUtc = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
            ReserveUntil = new DateOnly(2027, 5, 1)
        };

        var expires = T.ExpiresAt(T.CompetitionReserve, input);

        Assert.Equal(new DateTime(2027, 5, 1, 23, 59, 59, DateTimeKind.Utc), expires.AddTicks(-(expires.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Contains("2027", T.Render(T.CompetitionReserve, NotificationChannel.Email, input).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finalization_plans_competition_messages_and_no_generic_rejection_or_reserve_duplicate()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m, 75m, 70m, 65m, 40m]);
        foreach (var application in fx.Applications)
            await Hcm031PortalTests.LinkAsync(db, application, consent: true);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now); // «تم استلام الطلب» went out at intake
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
        var (allocated, reserve, notSuccessful) = (fx.Applications[0], fx.Applications[5], fx.Applications[6]);
        Assert.Equal(RecruitmentRejectionKind.CompetitionResult,
            (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == notSuccessful.Id)).RejectionKind);
        // The event recorder logs the generic status changes next to the competition events; they must stay silent.
        var statusEvents = await db.RecruitmentApplicationEvents.AsNoTracking()
            .Where(e => e.EventType == RecruitmentApplicationEventTypes.StatusChanged).ToListAsync();
        Assert.Contains(statusEvents, e => e.RecruitmentApplicationId == notSuccessful.Id && e.ToStatus == (int)RecruitmentApplicationStatus.Rejected);
        Assert.Contains(statusEvents, e => e.RecruitmentApplicationId == reserve.Id && e.ToStatus == (int)RecruitmentApplicationStatus.ReserveListed);

        await Dispatcher(db).PlanAsync(now.AddMinutes(1), CancellationToken.None);

        var rows = await db.CandidateNotifications.AsNoTracking().ToListAsync();
        string[] Codes(Guid applicationId) => rows.Where(r => r.RecruitmentApplicationId == applicationId)
            .Select(r => r.TemplateCode).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal([T.CompetitionAllocated, T.ResultsPublished], Codes(allocated.Id));
        Assert.Equal([T.CompetitionReserve, T.ResultsPublished], Codes(reserve.Id));
        Assert.Equal([T.CompetitionNotSuccessful, T.ResultsPublished], Codes(notSuccessful.Id));
        Assert.DoesNotContain(rows, r => r.TemplateCode is T.AppRejected or T.AppReserve);
        Assert.All(rows, r =>
        {
            foreach (var word in Banned)
                Assert.DoesNotContain(word, (r.Subject ?? string.Empty) + r.Body, StringComparison.Ordinal);
            Assert.All(fx.Applications, a => Assert.DoesNotContain(a.NationalId!, r.Body, StringComparison.Ordinal));
        });
        // Every event is marked planned, the suppressed ones included.
        Assert.Empty(await db.RecruitmentApplicationEvents.AsNoTracking().Where(e => e.NotificationsPlannedAt == null).ToListAsync());
    }

    [Fact]
    public async Task A_decided_result_objection_sends_the_objection_message_not_the_appeal_message()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 40m]);
        var candidate = fx.Applications[1];
        await Hcm031PortalTests.LinkAsync(db, candidate, consent: true);
        await PublishResultsAsync(db, fx.Competition.Id);
        var objections = new CompetitionObjectionService(db, Hcm028AppealTestSupport.CreateService(db), CreateResults(db));
        var (appeal, error) = await objections.FileAsync(candidate.Id, fx.Papers[candidate.Id].Id,
            "أطلب إعادة تدقيق ورقة الامتحان الكتابي لوجود خطأ في الجمع", RecruitmentAppealFiledBy.Candidate, actorUserId: null);
        Assert.True(appeal is not null, error);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now);
        var tracked = await db.RecruitmentAppeals.SingleAsync(a => a.Id == appeal!.Id);
        tracked.Status = RecruitmentAppealStatus.Decided;
        tracked.Outcome = RecruitmentAppealOutcome.ObjectionDismissed;
        await db.SaveChangesAsync(); // the event recorder logs AppealDecided (related: the appeal)
        Assert.Single(await db.RecruitmentApplicationEvents.AsNoTracking()
            .Where(e => e.EventType == RecruitmentApplicationEventTypes.AppealDecided && e.RelatedEntityId == appeal!.Id).ToListAsync());

        Assert.Equal(1, await Dispatcher(db).PlanAsync(now.AddMinutes(1), CancellationToken.None));

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(T.ObjectionDecided, row.TemplateCode);
        Assert.Equal(candidate.Id, row.RecruitmentApplicationId);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task MarkAllPlannedAsync(HRCoreDbContext db, DateTime now)
    {
        foreach (var ev in await db.RecruitmentApplicationEvents.Where(e => e.NotificationsPlannedAt == null).ToListAsync())
            ev.NotificationsPlannedAt = now;
        await db.SaveChangesAsync();
    }

    private static CandidateNotificationDispatcher Dispatcher(HRCoreDbContext db) =>
        new(db,
            new NotificationChannelSenderRegistry(
                [new NullSender(NotificationChannel.Email), new NullSender(NotificationChannel.Sms)],
                Options.Create(new CandidateNotificationOptions { Provider = nameof(CandidateNotificationProvider.FileDrop), MaxAttempts = 5 })),
            Options.Create(new RecruitmentOptions()),
            NullLogger<CandidateNotificationDispatcher>.Instance)
        { DisplayZone = TimeZoneInfo.Utc };

    private sealed class NullSender(NotificationChannel channel) : INotificationChannelSender
    {
        public NotificationChannel Channel { get; } = channel;

        public Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct) =>
            Task.FromResult<ChannelSendResult>(new ChannelSendResult.Sent(null));
    }
}
