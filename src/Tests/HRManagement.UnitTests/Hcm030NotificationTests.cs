using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// COM-030 (PLAN-30 F4/F5): <see cref="CandidateNotificationDispatcher"/> — the event → message map, consent, Provider=None,
/// retry/backoff, idempotency (AC-HCM-30e negative), the 48-hour offer reminder, offer states, internal candidates and the
/// audit rows. SQLite, no interceptors; "now" is passed explicitly.
/// </summary>
public sealed class Hcm030NotificationTests : IDisposable
{
    private const string CandidateEmail = "candidate@test.sy";
    private const string CandidatePhone = "0999123456";
    private const string CandidatePhoneE164 = "+963999123456";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "hcm030-notifications-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    // ------------------------------------------------------------------ planner map

    [Fact]
    public async Task Received_event_of_a_consenting_public_candidate_plans_one_message_on_the_chosen_channel()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (citizen, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var now = DateTime.UtcNow;

        var inserted = await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(now, default);

        Assert.Equal(1, inserted);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        var ev = await db.RecruitmentApplicationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentApplicationEventTypes.Received, ev.EventType);
        Assert.NotNull(ev.NotificationsPlannedAt);
        Assert.Equal(Hcm030TestSupport.TenantA, row.TenantId);
        Assert.Equal(app.Id, row.RecruitmentApplicationId);
        Assert.Equal(ev.Id, row.EventId);
        Assert.Equal(NotificationChannel.Email, row.Channel); // only the consented channel, not the phone too
        Assert.Equal(CandidateNotificationTemplates.AppReceived, row.TemplateCode);
        Assert.Equal(CandidateNotification.IdempotencyKeyFor(ev.Id, NotificationChannel.Email), row.IdempotencyKey);
        Assert.Equal(PiiMask.Email(CandidateEmail), row.RecipientMasked);
        Assert.DoesNotContain(CandidateEmail, row.RecipientMasked);
        Assert.Contains(citizen.ReferenceNumber, row.Body);
        Assert.Equal(CandidateNotificationStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(now, row.NextAttemptAt);
        Assert.Equal(DateTime.SpecifyKind(ev.OccurredAt, DateTimeKind.Utc).AddDays(30), row.ExpiresAt);
    }

    [Theory]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Rejected, CandidateNotificationTemplates.AppRejected)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.ReserveListed, CandidateNotificationTemplates.AppReserve)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Hired, CandidateNotificationTemplates.AppHired)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Withdrawn, CandidateNotificationTemplates.AppWithdrawn)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Shortlisted, null)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.OfferExtended, null)]
    [InlineData(RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.OfferDeclined, null)]
    [InlineData(RecruitmentApplicationEventTypes.OfferDeclined, (int)OfferStatus.Declined, null)]
    [InlineData(RecruitmentApplicationEventTypes.OfferExpired, (int)OfferStatus.Expired, null)]
    [InlineData(RecruitmentApplicationEventTypes.OfferRevoked, (int)OfferStatus.Revoked, null)]
    [InlineData(RecruitmentApplicationEventTypes.AppealFiled, (int)RecruitmentAppealStatus.Pending, null)]
    public async Task Application_events_follow_the_message_map(string eventType, int toStatus, string? expectedTemplate)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Sms);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now); // the Received event is covered above
        var ev = AddEvent(db, app, eventType, toStatus, now);
        await db.SaveChangesAsync();

        await Dispatcher(db, new FakeSender(NotificationChannel.Sms)).PlanAsync(now, default);

        var rows = await db.CandidateNotifications.AsNoTracking().Where(n => n.EventId == ev.Id).ToListAsync();
        if (expectedTemplate is null)
        {
            Assert.Empty(rows);
        }
        else
        {
            var row = Assert.Single(rows);
            Assert.Equal(expectedTemplate, row.TemplateCode);
            Assert.Equal(NotificationChannel.Sms, row.Channel);
            Assert.Equal(PiiMask.Phone(CandidatePhoneE164), row.RecipientMasked);
            Assert.Null(row.Subject);
            Assert.True(row.Body.Length <= CandidateNotificationTemplates.SmsMaxLength);
        }

        Assert.NotNull((await db.RecruitmentApplicationEvents.AsNoTracking().SingleAsync(e => e.Id == ev.Id)).NotificationsPlannedAt);
    }

    [Fact]
    public async Task Interview_events_recorded_by_the_context_plan_interview_messages_without_panel_notes()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now);
        var slot = new RecruitmentInterviewSlot
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            ScheduledAt = now.AddDays(3),
            Location = "قاعة 2",
            PanelNotes = "ملاحظات اللجنة السرية",
            Mode = InterviewMode.InPerson
        };
        db.RecruitmentInterviewSlots.Add(slot);
        await db.SaveChangesAsync(); // recorder: InterviewScheduled
        slot.Status = InterviewSlotStatus.Cancelled;
        slot.CancelledReason = "سبب داخلي";
        await db.SaveChangesAsync(); // recorder: InterviewCancelled

        await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(now, default);

        var rows = await db.CandidateNotifications.AsNoTracking().OrderBy(n => n.TemplateCode).ToListAsync();
        Assert.Equal(
            [CandidateNotificationTemplates.InterviewCancelled, CandidateNotificationTemplates.InterviewScheduled],
            rows.Select(r => r.TemplateCode).ToArray());
        Assert.All(rows, r =>
        {
            Assert.DoesNotContain("ملاحظات اللجنة", r.Body);
            Assert.DoesNotContain("سبب داخلي", r.Body);
            Assert.Equal(DateTime.SpecifyKind(slot.ScheduledAt, DateTimeKind.Utc), r.ExpiresAt);
        });
        Assert.Contains("قاعة 2", rows.Single(r => r.TemplateCode == CandidateNotificationTemplates.InterviewScheduled).Body);
    }

    [Fact]
    public async Task Without_consent_nothing_is_planned_but_the_events_are_marked()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, channel: null, consent: false);
        var now = DateTime.UtcNow;
        AddEvent(db, app, RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Rejected, now);
        await db.SaveChangesAsync();

        var inserted = await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(now, default);

        Assert.Equal(0, inserted);
        Assert.Empty(await db.CandidateNotifications.ToListAsync());
        Assert.All(await db.RecruitmentApplicationEvents.AsNoTracking().ToListAsync(), e => Assert.NotNull(e.NotificationsPlannedAt));
    }

    [Fact]
    public async Task Consent_without_the_chosen_contact_plans_nothing()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (citizen, _) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var tracked = await db.CitizenApplications.SingleAsync(c => c.Id == citizen.Id);
        tracked.Email = null; // e.g. cleared later by HR or anonymization
        await db.SaveChangesAsync();

        Assert.Equal(0, await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(DateTime.UtcNow, default));
        Assert.Empty(await db.CandidateNotifications.ToListAsync());
    }

    [Theory]
    [InlineData(RecruitmentIntakeChannel.Internal)]
    [InlineData(RecruitmentIntakeChannel.HrEntered)]
    public async Task Internal_and_hr_entered_candidates_get_nothing(RecruitmentIntakeChannel channel)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var tracked = await db.RecruitmentApplications.SingleAsync(a => a.Id == app.Id);
        tracked.IntakeChannel = channel; // even with a consenting channel row linked, only the public channel is notified
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        AddEvent(db, app, RecruitmentApplicationEventTypes.StatusChanged, (int)RecruitmentApplicationStatus.Rejected, now);
        await db.SaveChangesAsync();

        Assert.Equal(0, await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(now, default));
        Assert.Empty(await db.CandidateNotifications.ToListAsync());
        Assert.All(await db.RecruitmentApplicationEvents.AsNoTracking().ToListAsync(), e => Assert.NotNull(e.NotificationsPlannedAt));
    }

    [Fact]
    public async Task Internal_candidate_without_a_channel_row_gets_nothing()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var app = new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "موظف",
            AppliedPosition = "محلل",
            RequisitionId = fx.Requisition.Id,
            IntakeChannel = RecruitmentIntakeChannel.Internal,
            Status = RecruitmentApplicationStatus.Submitted,
            AppliedAt = DateTime.UtcNow
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync(); // Received

        Assert.Equal(0, await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(DateTime.UtcNow, default));
        Assert.Empty(await db.CandidateNotifications.ToListAsync());
    }

    // ------------------------------------------------------------------ offers

    [Fact]
    public async Task Draft_and_pending_approval_offers_produce_no_offer_message_and_issued_offers_carry_no_salary()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now);
        var offer = NewOffer(fx, app, OfferStatus.Draft);
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        offer.Status = OfferStatus.PendingApproval;
        await db.SaveChangesAsync();

        // Draft/PendingApproval record no event (session 1 recorder) …
        Assert.Empty(await db.RecruitmentApplicationEvents.AsNoTracking().Where(e => e.NotificationsPlannedAt == null).ToListAsync());
        // … and even a forged OfferIssued event on a non-issued offer plans nothing.
        var forged = AddEvent(db, app, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, now, offer.Id);
        await db.SaveChangesAsync();
        var dispatcher = Dispatcher(db, new FakeSender(NotificationChannel.Email));
        Assert.Equal(0, await dispatcher.PlanAsync(now, default));

        offer.Status = OfferStatus.Issued;
        offer.IssuedDate = now;
        offer.ExpiryDate = now.AddDays(14);
        await db.SaveChangesAsync(); // recorder: OfferIssued
        Assert.Equal(1, await dispatcher.PlanAsync(now, default));

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.NotEqual(forged.Id, row.EventId);
        Assert.Equal(CandidateNotificationTemplates.OfferIssued, row.TemplateCode);
        Assert.Equal(offer.ExpiryDate, row.ExpiresAt);
        Assert.DoesNotContain("987654", row.Body);
        Assert.DoesNotContain("987,654", row.Body);
        Assert.DoesNotContain("987654", row.Subject ?? string.Empty);
    }

    [Fact]
    public async Task Reminder_is_recorded_once_per_issued_offer_within_the_window_and_planned()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var (_, other) = await ApplyAsync(db, fx, NotificationChannel.Email, nationalId: "0202020202");
        var (_, third) = await ApplyAsync(db, fx, NotificationChannel.Email, nationalId: "0303030303");
        var now = DateTime.UtcNow;
        var due = NewOffer(fx, app, OfferStatus.Issued, expiry: now.AddHours(24));
        var later = NewOffer(fx, other, OfferStatus.Issued, expiry: now.AddHours(72));
        var draft = NewOffer(fx, third, OfferStatus.Draft, expiry: now.AddHours(24));
        db.OfferLetters.AddRange(due, later, draft);
        await db.SaveChangesAsync(); // added offers record no event
        await MarkAllPlannedAsync(db, now);
        var dispatcher = Dispatcher(db, new FakeSender(NotificationChannel.Email), new RecruitmentOptions { OfferReminderHours = 48 });

        Assert.Equal(1, await dispatcher.RemindDueOffersAsync(now, default));
        Assert.Equal(0, await dispatcher.RemindDueOffersAsync(now.AddMinutes(1), default));
        Assert.Equal(0, await dispatcher.RemindDueOffersAsync(now.AddHours(1), default));

        var reminder = await db.RecruitmentApplicationEvents.AsNoTracking()
            .SingleAsync(e => e.EventType == RecruitmentApplicationEventTypes.OfferExpiryReminder);
        Assert.Equal(due.Id, reminder.RelatedEntityId);
        Assert.Equal(app.Id, reminder.RecruitmentApplicationId);
        Assert.Equal(Hcm030TestSupport.TenantA, reminder.TenantId);
        Assert.Equal((int)OfferStatus.Issued, reminder.ToStatus);
        Assert.Equal(CandidateNotificationDispatcher.ReminderEventIdFor(due.Id), reminder.Id);

        // The offer due in 72 h gets its reminder once it enters the window.
        Assert.Equal(1, await dispatcher.RemindDueOffersAsync(now.AddHours(25), default));
        Assert.Equal(2, await db.RecruitmentApplicationEvents.CountAsync(e => e.EventType == RecruitmentApplicationEventTypes.OfferExpiryReminder));

        await dispatcher.PlanAsync(now.AddHours(25), default);
        var rows = await db.CandidateNotifications.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(CandidateNotificationTemplates.OfferReminder, r.TemplateCode));
        Assert.Equal(due.ExpiryDate, rows.Single(r => r.RecruitmentApplicationId == app.Id).ExpiresAt);
    }

    [Fact]
    public async Task Reminder_hours_zero_turns_the_reminder_off()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var now = DateTime.UtcNow;
        db.OfferLetters.Add(NewOffer(fx, app, OfferStatus.Issued, expiry: now.AddHours(2)));
        await db.SaveChangesAsync();

        var dispatcher = Dispatcher(db, new FakeSender(NotificationChannel.Email), new RecruitmentOptions { OfferReminderHours = 0 });

        Assert.Equal(0, await dispatcher.RemindDueOffersAsync(now, default));
    }

    // ------------------------------------------------------------------ sending

    [Fact]
    public async Task Due_message_is_sent_to_the_address_read_from_the_citizen_row_and_audited_without_address_or_body()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await ApplyAsync(db, fx, NotificationChannel.Sms);
        var sender = new FakeSender(NotificationChannel.Sms) { Next = _ => new ChannelSendResult.Sent("prov-1") };
        var dispatcher = Dispatcher(db, sender);
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);

        var summary = await dispatcher.SendDueAsync(now, default);

        Assert.Equal(1, summary.Sent);
        var message = Assert.Single(sender.Messages);
        Assert.Equal(CandidatePhoneE164, message.RecipientAddress);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(row.IdempotencyKey, message.IdempotencyKey);
        Assert.Equal(row.Id.ToString(), message.CorrelationId);
        Assert.Equal(CandidateNotificationStatus.Sent, row.Status);
        Assert.Equal(now, row.SentAt);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("prov-1", row.ProviderMessageId);
        Assert.Null(row.NextAttemptAt);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == row.Id);
        Assert.Equal(CandidateNotificationDispatcher.SentAuditAction, audit.Action);
        Assert.Equal(nameof(CandidateNotification), audit.EntityType);
        Assert.Equal(Hcm030TestSupport.TenantA, audit.TenantId);
        Assert.Null(audit.UserId);
        AssertAuditHasNoPii(audit.ChangedFields, row);

        // Sent rows are never sent again.
        Assert.Equal(0, (await dispatcher.SendDueAsync(now.AddHours(1), default)).Sent);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Legacy_phone_not_yet_in_E164_is_normalized_at_send_time()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (citizen, _) = await ApplyAsync(db, fx, NotificationChannel.Sms);
        var tracked = await db.CitizenApplications.SingleAsync(c => c.Id == citizen.Id);
        tracked.Phone = CandidatePhone; // raw format stored before COM-030
        await db.SaveChangesAsync();
        var sender = new FakeSender(NotificationChannel.Sms);
        var dispatcher = Dispatcher(db, sender);
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);

        await dispatcher.SendDueAsync(now, default);

        Assert.Equal(CandidatePhoneE164, Assert.Single(sender.Messages).RecipientAddress);
    }

    [Fact]
    public async Task Provider_none_keeps_the_message_pending_and_never_sent_until_it_expires()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await ApplyAsync(db, fx, NotificationChannel.Email);
        var dispatcher = new CandidateNotificationDispatcher(
            db,
            Registry(CandidateNotificationProvider.None, maxAttempts: 5),
            Options.Create(new RecruitmentOptions()),
            NullLogger<CandidateNotificationDispatcher>.Instance)
        { DisplayZone = TimeZoneInfo.Utc };
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);

        var first = await dispatcher.SendDueAsync(now, default);

        Assert.Equal(1, first.WaitingForProvider);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.SentAt);
        Assert.Equal(now.Add(CandidateNotificationDispatcher.NoProviderRecheck), row.NextAttemptAt);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == nameof(CandidateNotification)).ToListAsync());

        // Still pending on the next checks …
        var later = now.AddDays(10);
        Assert.Equal(1, (await dispatcher.SendDueAsync(later, default)).WaitingForProvider);
        Assert.Equal(CandidateNotificationStatus.Pending, (await db.CandidateNotifications.AsNoTracking().SingleAsync()).Status);

        // … and Expired (never Sent) once past ExpiresAt (+30 days for APP_RECEIVED).
        var afterExpiry = row.ExpiresAt!.Value.AddMinutes(1);
        var final = await dispatcher.SendDueAsync(afterExpiry, default);

        Assert.Equal(1, final.Expired);
        row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Expired, row.Status);
        Assert.Null(row.SentAt);
        Assert.Null(row.NextAttemptAt);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == nameof(CandidateNotification));
        Assert.Equal(CandidateNotificationDispatcher.ExpiredAuditAction, audit.Action);
        AssertAuditHasNoPii(audit.ChangedFields, row);
    }

    [Fact]
    public async Task Not_configured_sender_is_treated_like_provider_none()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await ApplyAsync(db, fx, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email) { Next = _ => new ChannelSendResult.NotConfigured("SMTP host not configured") };
        var dispatcher = Dispatcher(db, sender);
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);

        Assert.Equal(1, (await dispatcher.SendDueAsync(now, default)).WaitingForProvider);

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task Transient_failures_back_off_and_fail_after_max_attempts_with_a_sanitized_error()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await ApplyAsync(db, fx, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email)
        {
            Next = _ => new ChannelSendResult.Transient($"HTTP 503 for {CandidateEmail} / +963 999 123 456")
        };
        var dispatcher = Dispatcher(db, sender, maxAttempts: 3);
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);

        var clock = now;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            Assert.Equal(1, (await dispatcher.SendDueAsync(clock, default)).Retried);
            var pending = await db.CandidateNotifications.AsNoTracking().SingleAsync();
            Assert.Equal(CandidateNotificationStatus.Pending, pending.Status);
            Assert.Equal(attempt, pending.Attempts);
            Assert.Equal(clock.Add(CandidateNotificationDispatcher.BackoffFor(attempt)), pending.NextAttemptAt);
            Assert.DoesNotContain(CandidateEmail, pending.LastError);
            Assert.DoesNotContain("999 123", pending.LastError);
            Assert.StartsWith("HTTP 503", pending.LastError);

            // Not due before the backoff ends.
            Assert.Equal(new CandidateNotificationSendSummary(0, 0, 0, 0, 0, 0),
                await dispatcher.SendDueAsync(clock.AddSeconds(1), default));
            clock = pending.NextAttemptAt!.Value;
        }

        Assert.Equal(1, (await dispatcher.SendDueAsync(clock, default)).Failed);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Failed, row.Status);
        Assert.Equal(3, row.Attempts);
        Assert.Null(row.NextAttemptAt);
        Assert.Equal(3, sender.Messages.Count);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == nameof(CandidateNotification));
        Assert.Equal(CandidateNotificationDispatcher.FailedAuditAction, audit.Action);
        AssertAuditHasNoPii(audit.ChangedFields, row);

        // Failed rows are not retried.
        Assert.Equal(new CandidateNotificationSendSummary(0, 0, 0, 0, 0, 0), await dispatcher.SendDueAsync(clock.AddDays(1), default));
    }

    [Fact]
    public async Task Permanent_failure_fails_at_once()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await ApplyAsync(db, fx, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email) { Next = _ => new ChannelSendResult.Permanent("SMTP 550") };
        var dispatcher = Dispatcher(db, sender);
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);

        Assert.Equal(1, (await dispatcher.SendDueAsync(now, default)).Failed);

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("SMTP 550", row.LastError);
    }

    [Fact]
    public async Task Consent_withdrawn_after_planning_suppresses_the_message()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (citizen, _) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var dispatcher = Dispatcher(db, sender);
        var now = DateTime.UtcNow;
        await dispatcher.PlanAsync(now, default);
        var tracked = await db.CitizenApplications.SingleAsync(c => c.Id == citizen.Id);
        tracked.NotificationConsentAt = null;
        tracked.NotificationChannel = null;
        await db.SaveChangesAsync();

        Assert.Equal(1, (await dispatcher.SendDueAsync(now, default)).Suppressed);

        Assert.Empty(sender.Messages);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Suppressed, row.Status);
        Assert.Equal(CandidateNotificationDispatcher.SuppressedAuditAction, (await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == nameof(CandidateNotification))).Action);
    }

    [Fact]
    public async Task Offer_message_waiting_for_a_provider_is_suppressed_once_the_offer_is_no_longer_issued()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now);
        var offer = NewOffer(fx, app, OfferStatus.Draft);
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        offer.Status = OfferStatus.Issued;
        offer.IssuedDate = now;
        offer.ExpiryDate = now.AddDays(14);
        await db.SaveChangesAsync(); // recorder: OfferIssued

        // Provider=None: the OFFER_ISSUED row is planned and waits.
        var none = new CandidateNotificationDispatcher(
            db, Registry(CandidateNotificationProvider.None, maxAttempts: 5), Options.Create(new RecruitmentOptions()),
            NullLogger<CandidateNotificationDispatcher>.Instance)
        { DisplayZone = TimeZoneInfo.Utc };
        await none.PlanAsync(now, default);
        Assert.Equal(1, (await none.SendDueAsync(now, default)).WaitingForProvider);

        // The offer is revoked while the row waits; then a provider is configured.
        offer.Status = OfferStatus.Revoked;
        await db.SaveChangesAsync();
        var sender = new FakeSender(NotificationChannel.Email);
        var later = now.AddDays(3);
        var summary = await Dispatcher(db, sender).SendDueAsync(later, default);

        Assert.Equal(1, summary.Suppressed);
        Assert.Empty(sender.Messages);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync(n => n.TemplateCode == CandidateNotificationTemplates.OfferIssued);
        Assert.Equal(CandidateNotificationStatus.Suppressed, row.Status);
        Assert.Equal("Offer no longer issued", row.LastError);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == row.Id);
        Assert.Equal(CandidateNotificationDispatcher.SuppressedAuditAction, audit.Action);
        AssertAuditHasNoPii(audit.ChangedFields, row);
    }

    [Fact]
    public async Task Interview_message_of_a_rescheduled_slot_is_suppressed_and_the_new_time_is_sent()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        var now = DateTime.UtcNow;
        await MarkAllPlannedAsync(db, now);
        var slot = new RecruitmentInterviewSlot
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            ScheduledAt = now.AddDays(5),
            Mode = InterviewMode.Remote
        };
        db.RecruitmentInterviewSlots.Add(slot);
        await db.SaveChangesAsync(); // recorder: InterviewScheduled
        var dispatcher = Dispatcher(db, new FakeSender(NotificationChannel.Email) { Next = _ => new ChannelSendResult.Transient("HTTP 503") });
        await dispatcher.PlanAsync(now, default);
        Assert.Equal(1, (await dispatcher.SendDueAsync(now, default)).Retried);

        // Rescheduled during the retry window: the old slot is Rescheduled and a new Scheduled slot is added.
        slot.Status = InterviewSlotStatus.Rescheduled;
        db.RecruitmentInterviewSlots.Add(new RecruitmentInterviewSlot
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            ScheduledAt = now.AddDays(7),
            Mode = InterviewMode.Remote,
            RescheduledFromSlotId = slot.Id
        });
        await db.SaveChangesAsync(); // recorder: InterviewRescheduled
        var sender = new FakeSender(NotificationChannel.Email);
        dispatcher = Dispatcher(db, sender);
        await dispatcher.PlanAsync(now.AddMinutes(1), default);

        var summary = await dispatcher.SendDueAsync(now.AddMinutes(5), default);

        Assert.Equal(1, summary.Sent);
        Assert.Equal(1, summary.Suppressed);
        var rows = await db.CandidateNotifications.AsNoTracking().ToListAsync();
        Assert.Equal(CandidateNotificationStatus.Suppressed, rows.Single(r => r.TemplateCode == CandidateNotificationTemplates.InterviewScheduled).Status);
        Assert.Equal("Interview no longer scheduled", rows.Single(r => r.TemplateCode == CandidateNotificationTemplates.InterviewScheduled).LastError);
        Assert.Equal(CandidateNotificationStatus.Sent, rows.Single(r => r.TemplateCode == CandidateNotificationTemplates.InterviewRescheduled).Status);
        Assert.Equal(CandidateNotificationTemplates.InterviewRescheduled, Assert.Single(sender.Messages).TemplateCode);
    }

    [Fact]
    public async Task Already_reminded_offers_never_fill_the_reminder_batch()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var now = DateTime.UtcNow;
        // More already-reminded offers (expiring sooner) than the old 5 × batch read-ahead.
        var reminded = new List<(RecruitmentApplication App, OfferLetter Offer)>();
        for (var i = 0; i < CandidateNotificationDispatcher.BatchSize * 5 + 1; i++)
        {
            var app = new RecruitmentApplication
            {
                TenantId = Hcm030TestSupport.TenantA,
                ApplicantFullName = "مرشح " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AppliedPosition = "محلل",
                RequisitionId = fx.Requisition.Id,
                IntakeChannel = RecruitmentIntakeChannel.HrEntered,
                Status = RecruitmentApplicationStatus.OfferExtended,
                AppliedAt = now
            };
            db.RecruitmentApplications.Add(app);
            var offer = NewOffer(fx, app, OfferStatus.Issued, expiry: now.AddHours(1).AddMinutes(i));
            db.OfferLetters.Add(offer);
            reminded.Add((app, offer));
        }
        var newcomerApp = new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "مرشح أخير",
            AppliedPosition = "محلل",
            RequisitionId = fx.Requisition.Id,
            IntakeChannel = RecruitmentIntakeChannel.HrEntered,
            Status = RecruitmentApplicationStatus.OfferExtended,
            AppliedAt = now
        };
        db.RecruitmentApplications.Add(newcomerApp);
        var newcomer = NewOffer(fx, newcomerApp, OfferStatus.Issued, expiry: now.AddHours(40));
        db.OfferLetters.Add(newcomer);
        await db.SaveChangesAsync();
        foreach (var (app, offer) in reminded)
            AddEvent(db, app, RecruitmentApplicationEventTypes.OfferExpiryReminder, (int)OfferStatus.Issued, now, offer.Id);
        await db.SaveChangesAsync();
        var dispatcher = Dispatcher(db, new FakeSender(NotificationChannel.Email), new RecruitmentOptions { OfferReminderHours = 48 });

        Assert.Equal(1, await dispatcher.RemindDueOffersAsync(now, default));

        var reminder = await db.RecruitmentApplicationEvents.AsNoTracking()
            .SingleAsync(e => e.Id == CandidateNotificationDispatcher.ReminderEventIdFor(newcomer.Id));
        Assert.Equal(newcomer.Id, reminder.RelatedEntityId);
    }

    // ------------------------------------------------------------------ idempotency (AC-HCM-30e negative)

    [Fact]
    public async Task Replanning_the_same_event_and_rerunning_the_dispatcher_give_one_row_and_one_file()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await ApplyAsync(db, fx, NotificationChannel.Email);
        var fileDrop = Options.Create(new CandidateNotificationOptions
        {
            Provider = "FileDrop",
            MaxAttempts = 5,
            FileDrop = new FileDropChannelOptions { Path = folder }
        });
        var env = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development && e.ContentRootPath == Path.GetTempPath());
        var registry = new NotificationChannelSenderRegistry(
            [new FileDropNotificationSender(NotificationChannel.Email, fileDrop, env), new FileDropNotificationSender(NotificationChannel.Sms, fileDrop, env)],
            fileDrop);
        var dispatcher = new CandidateNotificationDispatcher(
            db, registry, Options.Create(new RecruitmentOptions()), NullLogger<CandidateNotificationDispatcher>.Instance)
        { DisplayZone = TimeZoneInfo.Utc };
        var now = DateTime.UtcNow;

        await dispatcher.RunOnceAsync(now, default);
        // Re-plan the same event (as if the mark was lost) and run everything again.
        var ev = await db.RecruitmentApplicationEvents.SingleAsync();
        ev.NotificationsPlannedAt = null;
        await db.SaveChangesAsync();
        await dispatcher.RunOnceAsync(now.AddMinutes(1), default);
        await dispatcher.RunOnceAsync(now.AddMinutes(2), default);

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(CandidateNotificationStatus.Sent, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull((await db.RecruitmentApplicationEvents.AsNoTracking().SingleAsync()).NotificationsPlannedAt);

        // At-least-once resend (e.g. the Sent mark was lost): same key → same file, still one message.
        var tracked = await db.CandidateNotifications.SingleAsync();
        tracked.Status = CandidateNotificationStatus.Pending;
        tracked.NextAttemptAt = now;
        await db.SaveChangesAsync();
        await dispatcher.SendDueAsync(now.AddMinutes(3), default);

        var file = Assert.Single(Directory.GetFiles(folder));
        Assert.Equal(FileDropNotificationSender.FileNameFor(row.IdempotencyKey), Path.GetFileName(file));
        using var json = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(file));
        Assert.Equal(CandidateEmail, json.RootElement.GetProperty("recipient").GetString());
        Assert.Equal(CandidateNotificationTemplates.AppReceived, json.RootElement.GetProperty("template").GetString());
    }

    [Fact]
    public async Task Unique_tenant_idempotency_key_refuses_a_second_row()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var (_, app) = await ApplyAsync(db, fx, NotificationChannel.Email);
        await Dispatcher(db, new FakeSender(NotificationChannel.Email)).PlanAsync(DateTime.UtcNow, default);
        var existing = await db.CandidateNotifications.AsNoTracking().SingleAsync();

        db.CandidateNotifications.Add(new CandidateNotification
        {
            TenantId = existing.TenantId,
            RecruitmentApplicationId = app.Id,
            EventId = existing.EventId,
            EventType = existing.EventType,
            Channel = existing.Channel,
            TemplateCode = existing.TemplateCode,
            RecipientMasked = existing.RecipientMasked,
            Body = existing.Body,
            IdempotencyKey = existing.IdempotencyKey
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(HRManagement.ServiceDefaults.ConcurrencyExtensions.IsUniqueViolation(ex));
    }

    [Fact]
    public void Backoff_doubles_from_one_minute_and_is_capped_at_one_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), CandidateNotificationDispatcher.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(2), CandidateNotificationDispatcher.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(8), CandidateNotificationDispatcher.BackoffFor(4));
        Assert.Equal(TimeSpan.FromHours(1), CandidateNotificationDispatcher.BackoffFor(20));
    }

    [Theory]
    [InlineData("HTTP 503", "HTTP 503")]
    [InlineData("SMTP 550", "SMTP 550")]
    [InlineData("rejected a.b@c.sy", "rejected ***")]
    [InlineData("bad number +963999123456", "bad number ***")]
    [InlineData("  ", null)]
    public void Errors_are_sanitized(string reason, string? expected) =>
        Assert.Equal(expected, CandidateNotificationDispatcher.SanitizeError(reason));

    [Fact]
    public void Long_errors_fit_the_column() =>
        Assert.Equal(500, CandidateNotificationDispatcher.SanitizeError(new string('x', 900))!.Length);

    // ------------------------------------------------------------------ helpers

    private static void AssertAuditHasNoPii(string? changedFields, CandidateNotification row)
    {
        Assert.False(string.IsNullOrEmpty(changedFields));
        Assert.DoesNotContain(CandidateEmail, changedFields);
        Assert.DoesNotContain("999123456", changedFields);
        Assert.DoesNotContain(row.RecipientMasked, changedFields);
        Assert.DoesNotContain(row.Body, changedFields);
        if (!string.IsNullOrEmpty(row.Subject))
            Assert.DoesNotContain(row.Subject, changedFields);
        using var json = JsonDocument.Parse(changedFields!);
        Assert.Equal(row.TemplateCode, json.RootElement.GetProperty("templateCode").GetString());
        Assert.Equal(row.Channel.ToString(), json.RootElement.GetProperty("channel").GetString());
    }

    private static async Task<(CitizenApplication Citizen, RecruitmentApplication App)> ApplyAsync(
        HRCoreDbContext db,
        Hcm030TestSupport.Fixture fx,
        NotificationChannel? channel,
        bool consent = true,
        string nationalId = "0101010101")
    {
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id, nationalId);
        vm.Phone = CandidatePhone;
        vm.Email = CandidateEmail;
        vm.PreferredChannel = channel;
        vm.NotificationConsent = consent;
        var result = await Hcm030TestSupport.CreatePublicCareersController(db).Apply(vm);
        Assert.IsType<RedirectToActionResult>(result);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.NationalId == nationalId);
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == citizen.PromotedRecruitmentApplicationId);
        Assert.Equal(RecruitmentIntakeChannel.Public, app.IntakeChannel);
        return (citizen, app);
    }

    private static RecruitmentApplicationEvent AddEvent(
        HRCoreDbContext db, RecruitmentApplication app, string eventType, int? toStatus, DateTime occurredAt, Guid? relatedId = null)
    {
        var ev = new RecruitmentApplicationEvent
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            EventType = eventType,
            ToStatus = toStatus,
            RelatedEntityId = relatedId,
            OccurredAt = occurredAt
        };
        db.RecruitmentApplicationEvents.Add(ev);
        return ev;
    }

    private static async Task MarkAllPlannedAsync(HRCoreDbContext db, DateTime now)
    {
        foreach (var ev in await db.RecruitmentApplicationEvents.Where(e => e.NotificationsPlannedAt == null).ToListAsync())
            ev.NotificationsPlannedAt = now;
        await db.SaveChangesAsync();
    }

    private static OfferLetter NewOffer(Hcm030TestSupport.Fixture fx, RecruitmentApplication app, OfferStatus status, DateTime? expiry = null) => new()
    {
        TenantId = app.TenantId,
        RecruitmentApplicationId = app.Id,
        FundedPositionId = fx.Requisition.FundedPositionId,
        OfferNumber = "OFF-H30-" + Guid.NewGuid().ToString("N")[..8],
        Status = status,
        IssuedDate = status == OfferStatus.Issued ? DateTime.UtcNow : null,
        ExpiryDate = expiry,
        OfferedSalary = 987654m,
        GradeCode = "G5",
        ProposedStartDate = DateTime.UtcNow.AddDays(30)
    };

    private static NotificationChannelSenderRegistry Registry(
        CandidateNotificationProvider provider, int maxAttempts, params INotificationChannelSender[] senders) =>
        new(senders, Options.Create(new CandidateNotificationOptions { Provider = provider.ToString(), MaxAttempts = maxAttempts }));

    private static CandidateNotificationDispatcher Dispatcher(
        HRCoreDbContext db, FakeSender sender, RecruitmentOptions? options = null, int maxAttempts = 5) =>
        new(db,
            Registry(CandidateNotificationProvider.FileDrop, maxAttempts, sender),
            Options.Create(options ?? new RecruitmentOptions()),
            NullLogger<CandidateNotificationDispatcher>.Instance)
        { DisplayZone = TimeZoneInfo.Utc };

    private sealed class FakeSender(NotificationChannel channel) : INotificationChannelSender
    {
        public NotificationChannel Channel { get; } = channel;
        public List<OutboundNotification> Messages { get; } = [];
        public Func<OutboundNotification, ChannelSendResult> Next { get; init; } = _ => new ChannelSendResult.Sent(null);

        public Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult(Next(message));
        }
    }
}
