using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>Counts of one <see cref="CandidateNotificationDispatcher.SendDueAsync"/> pass.</summary>
public sealed record CandidateNotificationSendSummary(
    int Sent, int Retried, int Failed, int Expired, int Suppressed, int WaitingForProvider);

/// <summary>
/// COM-030 (PLAN-30 F4/F5): the testable core of <see cref="CandidateNotificationJob"/>. Runs across tenants
/// (<see cref="SystemTenantBypassPurpose.BackgroundQueueProcessing"/>) and sets <c>TenantId</c> explicitly on every row it
/// adds; it never sets <c>TenantIdFlow</c>. Three passes, each taking "now" as a parameter:
/// <list type="number">
/// <item><see cref="RemindDueOffersAsync"/> — one <c>OfferExpiryReminder</c> event per Issued offer expiring within
/// <c>Recruitment:OfferReminderHours</c>.</item>
/// <item><see cref="PlanAsync"/> — for each event with <c>NotificationsPlannedAt IS NULL</c>: renders and inserts one
/// <see cref="CandidateNotification"/> on the candidate's chosen channel (consenting public candidates only), then marks the
/// event planned. Key <c>{eventId}:{channel}</c>, unique per tenant, so re-planning never duplicates.</item>
/// <item><see cref="SendDueAsync"/> — sends due Pending rows: past <c>ExpiresAt</c> → Expired; no sender (Provider=None or
/// not configured) → stays Pending (warning + metric), never Sent; consent gone, or the offer/interview it announces no
/// longer current → Suppressed; otherwise Sent, retried with backoff, or Failed after
/// <c>Notifications:Candidate:MaxAttempts</c>. Each terminal outcome is audited without address or body.</item>
/// </list>
/// Delivery is at-least-once: a crash between the provider accepting a message and the row being saved resends it on the
/// next cycle with the same idempotency key (FileDrop rewrites the same file; SMTP uses a stable Message-ID, which most
/// mail clients but not all servers deduplicate; an SMS gateway sees the key only as <c>{reference}</c>).
/// </summary>
public sealed class CandidateNotificationDispatcher(
    HRCoreDbContext db,
    NotificationChannelSenderRegistry senders,
    IOptions<RecruitmentOptions> options,
    ILogger<CandidateNotificationDispatcher> logger)
{
    /// <summary>Events planned / rows sent / offers reminded per pass.</summary>
    public const int BatchSize = 100;

    /// <summary>How long a row waits when no sender can take it (Provider=None / NotConfigured) before it is checked again.</summary>
    public static readonly TimeSpan NoProviderRecheck = TimeSpan.FromMinutes(15);

    public const string SentAuditAction = "CandidateNotificationSent";
    public const string FailedAuditAction = "CandidateNotificationFailed";
    public const string ExpiredAuditAction = "CandidateNotificationExpired";
    public const string SuppressedAuditAction = "CandidateNotificationSuppressed";

    private static readonly Counter<long> Outcomes =
        JobActivitySource.Meter.CreateCounter<long>("candidate_notifications_total");

    /// <summary>Zone of the dates in the messages; <c>/careers/track</c> uses <see cref="TimeZoneInfo.Local"/> too.</summary>
    public TimeZoneInfo DisplayZone { get; init; } = TimeZoneInfo.Local;

    /// <summary>The three passes in order (reminders, planning, sending).</summary>
    public async Task RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        await RemindDueOffersAsync(nowUtc, ct);
        await PlanAsync(nowUtc, ct);
        await SendDueAsync(nowUtc, ct);
    }

    // ---------------------------------------------------------------- F5: 48-hour offer reminder

    /// <summary>
    /// PLAN-30 (F5): records one <see cref="RecruitmentApplicationEventTypes.OfferExpiryReminder"/> event for each Issued offer
    /// whose <c>ExpiryDate</c> is in (now, now + <c>Recruitment:OfferReminderHours</c>] and that has none yet. The event id is
    /// derived from the offer id (<see cref="ReminderEventIdFor"/>), so even two concurrent passes insert one row (primary
    /// key). Kept out of <see cref="RecruitmentOfferExpirer.ExpireDueAsync"/>, whose contract and tests stay unchanged.
    /// </summary>
    /// <returns>Number of reminder events recorded.</returns>
    public async Task<int> RemindDueOffersAsync(DateTime nowUtc, CancellationToken ct)
    {
        var hours = options.Value.OfferReminderHours;
        if (hours <= 0)
            return 0;

        var horizon = nowUtc.AddHours(hours);
        // "No reminder yet" is part of the query, so offers already reminded never fill the batch and starve newer ones.
        var reminders = db.RecruitmentApplicationEvents
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(e => e.EventType == RecruitmentApplicationEventTypes.OfferExpiryReminder);
        var due = await db.OfferLetters
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .AsNoTracking()
            .Where(o => o.Status == OfferStatus.Issued && o.ExpiryDate != null
                && o.ExpiryDate > nowUtc && o.ExpiryDate <= horizon
                && !reminders.Any(e => e.RelatedEntityId == o.Id))
            .OrderBy(o => o.ExpiryDate)
            .Select(o => new { o.Id, o.TenantId, o.RecruitmentApplicationId })
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        var recorded = 0;
        foreach (var offer in due)
        {
            db.RecruitmentApplicationEvents.Add(new RecruitmentApplicationEvent
            {
                Id = ReminderEventIdFor(offer.Id),
                TenantId = offer.TenantId,
                RecruitmentApplicationId = offer.RecruitmentApplicationId,
                EventType = RecruitmentApplicationEventTypes.OfferExpiryReminder,
                ToStatus = (int)OfferStatus.Issued,
                RelatedEntityId = offer.Id,
                OccurredAt = nowUtc
            });
            try
            {
                await db.SaveChangesAsync(ct);
                recorded++;
            }
            catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
            {
                // Another dispatcher recorded it first: exactly one reminder per offer.
                db.ChangeTracker.Clear();
            }
        }

        if (recorded > 0)
            logger.LogInformation("Candidate notifications: {Count} offer expiry reminder(s) recorded.", recorded);
        return recorded;
    }

    /// <summary>Deterministic id of the reminder event of <paramref name="offerId"/> (name-based, version 5 layout).</summary>
    public static Guid ReminderEventIdFor(Guid offerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            RecruitmentApplicationEventTypes.OfferExpiryReminder + ":" + offerId.ToString("D")));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    // ---------------------------------------------------------------- Stage 1: plan

    /// <summary>
    /// Plans the messages of up to <see cref="BatchSize"/> unplanned events (oldest first) and marks them planned. Events
    /// without a message (no template, internal or HR-entered candidates, no consent or no contact, an offer that is not
    /// Issued) are only marked. One save per event, so a failure never loses the mark of another event.
    /// </summary>
    /// <returns>Number of notification rows inserted.</returns>
    public async Task<int> PlanAsync(DateTime nowUtc, CancellationToken ct)
    {
        var events = await db.RecruitmentApplicationEvents
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(e => e.NotificationsPlannedAt == null)
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (events.Count == 0)
            return 0;

        var context = await LoadPlanningContextAsync(events, ct);
        var opts = options.Value;
        var portalUrl = opts.CandidatePortalUrl();
        var appealableKinds = opts.GetAppealableRejectionKinds();

        var inserted = 0;
        foreach (var ev in events)
        {
            if (db.Entry(ev).State == EntityState.Detached)
                db.Attach(ev); // after a ChangeTracker.Clear() below

            CandidateNotification? notification;
            try
            {
                notification = BuildNotification(ev, context, nowUtc, portalUrl, opts.AppealWindowDays, appealableKinds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never loop on one bad event: log it (no PII in the event) and mark it planned without a message.
                logger.LogError(ex, "Candidate notifications: event {EventId} ({EventType}) could not be rendered; no message planned.",
                    ev.Id, ev.EventType);
                notification = null;
            }

            if (notification is not null)
                db.CandidateNotifications.Add(notification);
            ev.NotificationsPlannedAt = nowUtc;

            try
            {
                await db.SaveChangesAsync(ct);
                if (notification is not null)
                    inserted++;
            }
            catch (DbUpdateException ex) when (notification is not null && ConcurrencyExtensions.IsUniqueViolation(ex))
            {
                // Already planned by another dispatcher (same {eventId}:{channel} key): keep that row, only mark the event.
                db.ChangeTracker.Clear();
                await MarkPlannedAsync(ev.Id, nowUtc, ct);
            }
        }

        if (inserted > 0)
            logger.LogInformation("Candidate notifications: {Count} message(s) planned from {Events} event(s).", inserted, events.Count);
        return inserted;
    }

    private async Task MarkPlannedAsync(Guid eventId, DateTime nowUtc, CancellationToken ct)
    {
        var ev = await db.RecruitmentApplicationEvents
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || ev.NotificationsPlannedAt is not null)
            return;
        ev.NotificationsPlannedAt = nowUtc;
        await db.SaveChangesAsync(ct);
    }

    private sealed record PlanningContext(
        IReadOnlyDictionary<Guid, RecruitmentApplication> Applications,
        IReadOnlyDictionary<Guid, CitizenApplication> Citizens,
        IReadOnlyDictionary<Guid, RecruitmentInterviewSlot> Slots,
        IReadOnlyDictionary<Guid, OfferLetter> Offers,
        IReadOnlyDictionary<Guid, RecruitmentAppeal> Appeals,
        IReadOnlySet<(Guid TenantId, string Key)> ExistingKeys);

    private async Task<PlanningContext> LoadPlanningContextAsync(
        IReadOnlyList<RecruitmentApplicationEvent> events, CancellationToken ct)
    {
        const SystemTenantBypassPurpose purpose = SystemTenantBypassPurpose.BackgroundQueueProcessing;
        var appIds = events.Select(e => e.RecruitmentApplicationId).Distinct().ToList();
        var related = events.Where(e => e.RelatedEntityId.HasValue).Select(e => e.RelatedEntityId!.Value).Distinct().ToList();
        var eventIds = events.Select(e => e.Id).ToList();

        var applications = await db.RecruitmentApplications.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
            .Where(a => appIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var citizens = (await db.CitizenApplications.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(c => c.PromotedRecruitmentApplicationId != null && appIds.Contains(c.PromotedRecruitmentApplicationId.Value))
                .ToListAsync(ct))
            .GroupBy(c => c.PromotedRecruitmentApplicationId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var slots = related.Count == 0
            ? new Dictionary<Guid, RecruitmentInterviewSlot>()
            : await db.RecruitmentInterviewSlots.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(s => related.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var offers = related.Count == 0
            ? new Dictionary<Guid, OfferLetter>()
            : await db.OfferLetters.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(o => related.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var appeals = related.Count == 0
            ? new Dictionary<Guid, RecruitmentAppeal>()
            : await db.RecruitmentAppeals.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(a => related.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var existing = (await db.CandidateNotifications.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(n => n.EventId != null && eventIds.Contains(n.EventId.Value))
                .Select(n => new { n.TenantId, n.IdempotencyKey })
                .ToListAsync(ct))
            .Select(n => (n.TenantId, n.IdempotencyKey))
            .ToHashSet();

        return new PlanningContext(applications, citizens, slots, offers, appeals, existing);
    }

    private CandidateNotification? BuildNotification(
        RecruitmentApplicationEvent ev,
        PlanningContext context,
        DateTime nowUtc,
        string? portalUrl,
        int appealWindowDays,
        IReadOnlyCollection<RecruitmentRejectionKind> appealableKinds)
    {
        var templateCode = CandidateNotificationTemplates.TemplateCodeFor(ev.EventType, ev.ToStatus);
        if (templateCode is null || !context.Applications.TryGetValue(ev.RecruitmentApplicationId, out var app))
            return null;

        // PLAN-30: internal (employee) candidates get no email/SMS — they follow their status in ESS. HR-entered and legacy
        // applications have no consent, so only the public channel is notified; «تم استلام الطلب» only for public intake.
        if (app.IntakeChannel is RecruitmentIntakeChannel.Internal or RecruitmentIntakeChannel.HrEntered)
            return null;
        if (ev.EventType == RecruitmentApplicationEventTypes.Received && app.IntakeChannel != RecruitmentIntakeChannel.Public)
            return null;

        if (!context.Citizens.TryGetValue(app.Id, out var citizen) || citizen.TenantId != ev.TenantId)
            return null;
        var channel = ConsentedChannel(citizen);
        if (channel is null)
            return null;

        RecruitmentInterviewSlot? slot = null;
        OfferLetter? offer = null;
        RecruitmentAppeal? appeal = null;
        if (ev.RelatedEntityId is Guid relatedId)
        {
            context.Slots.TryGetValue(relatedId, out slot);
            context.Offers.TryGetValue(relatedId, out offer);
            context.Appeals.TryGetValue(relatedId, out appeal);
        }

        // Offer messages only for an offer that is Issued now: Draft and PendingApproval offers record no event, and an offer
        // accepted, revoked or expired before the event was planned must not be announced as pending.
        if (templateCode is CandidateNotificationTemplates.OfferIssued or CandidateNotificationTemplates.OfferReminder
            && offer?.Status != OfferStatus.Issued)
            return null;
        if (ev.EventType.StartsWith("Interview", StringComparison.Ordinal) && slot is null)
            return null;
        if (templateCode == CandidateNotificationTemplates.AppealDecided && appeal is null)
            return null;

        // HCM-031 (PLAN-31): a competition application is told by its competition messages — the generic rejection and
        // reserve messages of the same status change would repeat them; a decided result objection has its own message.
        if (app.CompetitionId is not null && ev.EventType == RecruitmentApplicationEventTypes.StatusChanged
            && ((templateCode == CandidateNotificationTemplates.AppRejected
                    && app.RejectionKind == RecruitmentRejectionKind.CompetitionResult)
                || templateCode == CandidateNotificationTemplates.AppReserve))
            return null;
        if (templateCode == CandidateNotificationTemplates.AppealDecided && appeal!.Kind == RecruitmentAppealKind.ResultObjection)
            templateCode = CandidateNotificationTemplates.ObjectionDecided;

        var key = CandidateNotification.IdempotencyKeyFor(ev.Id, channel.Value);
        if (context.ExistingKeys.Contains((ev.TenantId, key)))
            return null;

        var input = CandidateNotificationTemplates.InputFor(
            citizen.ReferenceNumber,
            DateTime.SpecifyKind(ev.OccurredAt, DateTimeKind.Utc),
            app,
            slot,
            offer,
            appeal,
            portalUrl,
            appealWindowDays,
            appealableKinds,
            DisplayZone);
        var content = CandidateNotificationTemplates.Render(templateCode, channel.Value, input);

        return new CandidateNotification
        {
            TenantId = ev.TenantId,
            RecruitmentApplicationId = app.Id,
            EventId = ev.Id,
            EventType = ev.EventType,
            Channel = channel.Value,
            TemplateCode = content.TemplateCode,
            RecipientMasked = Truncate(Masked(citizen, channel.Value), 160)!,
            Subject = Truncate(content.Subject, 200),
            Body = Truncate(content.Body, 2000)!,
            IdempotencyKey = key,
            Status = CandidateNotificationStatus.Pending,
            NextAttemptAt = nowUtc,
            ExpiresAt = content.ExpiresAtUtc
        };
    }

    /// <summary>
    /// The channel the candidate consented to (Email/Sms) when its contact is present; null otherwise. Only the chosen
    /// channel is used: the consent text covers «القناة المختارة», not the other contact.
    /// </summary>
    public static NotificationChannel? ConsentedChannel(CitizenApplication citizen)
    {
        if (citizen.NotificationConsentAt is null || citizen.AnonymizedAt is not null)
            return null;
        return citizen.NotificationChannel switch
        {
            NotificationChannel.Email when !string.IsNullOrWhiteSpace(citizen.Email) => NotificationChannel.Email,
            NotificationChannel.Sms when !string.IsNullOrWhiteSpace(citizen.Phone) => NotificationChannel.Sms,
            _ => null
        };
    }

    private static string Masked(CitizenApplication citizen, NotificationChannel channel) =>
        channel == NotificationChannel.Email ? PiiMask.Email(citizen.Email) : PiiMask.Phone(citizen.Phone);

    // ---------------------------------------------------------------- Stage 2: send

    /// <summary>
    /// Sends up to <see cref="BatchSize"/> due Pending rows (oldest <c>NextAttemptAt</c> first), saving after each row. The
    /// real address is read from the citizen row now (never stored on the notification); old rows whose phone is not in
    /// E.164 yet are normalized with <c>Recruitment:Portal:PhoneCountryCode</c>.
    /// </summary>
    public async Task<CandidateNotificationSendSummary> SendDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        const SystemTenantBypassPurpose purpose = SystemTenantBypassPurpose.BackgroundQueueProcessing;
        var due = await db.CandidateNotifications.IgnoreTenantFilterForSystem(purpose)
            .Where(n => n.Status == CandidateNotificationStatus.Pending && (n.NextAttemptAt == null || n.NextAttemptAt <= nowUtc))
            .OrderBy(n => n.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0)
            return new CandidateNotificationSendSummary(0, 0, 0, 0, 0, 0);

        var appIds = due.Select(n => n.RecruitmentApplicationId).Distinct().ToList();
        var citizens = (await db.CitizenApplications.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(c => c.PromotedRecruitmentApplicationId != null && appIds.Contains(c.PromotedRecruitmentApplicationId.Value))
                .ToListAsync(ct))
            .GroupBy(c => c.PromotedRecruitmentApplicationId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var superseded = await SupersededRowIdsAsync(due, ct);

        int sent = 0, retried = 0, failed = 0, expired = 0, suppressed = 0, waiting = 0;
        var maxAttempts = senders.MaxAttempts;
        foreach (var n in due)
        {
            var outcome = await SendOneAsync(n, citizens, superseded, nowUtc, maxAttempts, ct);
            switch (outcome)
            {
                case SendOutcome.Sent: sent++; break;
                case SendOutcome.Retried: retried++; break;
                case SendOutcome.Failed: failed++; break;
                case SendOutcome.Expired: expired++; break;
                case SendOutcome.Suppressed: suppressed++; break;
                case SendOutcome.WaitingForProvider: waiting++; break;
            }

            Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", OutcomeTag(outcome)),
                new KeyValuePair<string, object?>("channel", n.Channel.ToString()));
            await db.SaveChangesAsync(ct);
        }

        if (waiting > 0)
            logger.LogWarning(
                "Candidate notifications: {Count} message(s) stay pending because no sender is configured (Provider={Provider}); they expire unsent.",
                waiting, senders.Provider);
        if (sent + retried + failed + expired + suppressed > 0)
            logger.LogInformation(
                "Candidate notifications: sent {Sent}, retry {Retried}, failed {Failed}, expired {Expired}, suppressed {Suppressed}.",
                sent, retried, failed, expired, suppressed);
        return new CandidateNotificationSendSummary(sent, retried, failed, expired, suppressed, waiting);
    }

    private static bool IsOfferTemplate(string templateCode) =>
        templateCode is CandidateNotificationTemplates.OfferIssued or CandidateNotificationTemplates.OfferReminder;

    private static bool IsUpcomingInterviewTemplate(string templateCode) =>
        templateCode is CandidateNotificationTemplates.InterviewScheduled or CandidateNotificationTemplates.InterviewRescheduled;

    /// <summary>
    /// Rows whose subject is no longer current at send time (a row can wait in Pending for days with Provider=None, or
    /// minutes between retries): an offer message whose offer is no longer Issued (accepted, declined, revoked, expired),
    /// or an interview message whose slot is no longer Scheduled (rescheduled, cancelled, completed). The planner applies
    /// the same rule; this re-check keeps a stale «يرجى الرد قبل …» or an old interview time from going out.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> SupersededRowIdsAsync(
        IReadOnlyList<CandidateNotification> rows, CancellationToken ct)
    {
        const SystemTenantBypassPurpose purpose = SystemTenantBypassPurpose.BackgroundQueueProcessing;
        var checkedRows = rows
            .Where(n => IsOfferTemplate(n.TemplateCode) || IsUpcomingInterviewTemplate(n.TemplateCode))
            .ToList();
        if (checkedRows.Count == 0)
            return new HashSet<Guid>();

        var eventIds = checkedRows.Where(n => n.EventId != null).Select(n => n.EventId!.Value).Distinct().ToList();
        var relatedByEvent = await db.RecruitmentApplicationEvents.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
            .Where(e => eventIds.Contains(e.Id) && e.RelatedEntityId != null)
            .ToDictionaryAsync(e => e.Id, e => e.RelatedEntityId!.Value, ct);
        var relatedIds = relatedByEvent.Values.Distinct().ToList();
        var issuedOffers = (await db.OfferLetters.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(o => relatedIds.Contains(o.Id) && o.Status == OfferStatus.Issued)
                .Select(o => o.Id)
                .ToListAsync(ct))
            .ToHashSet();
        var scheduledSlots = (await db.RecruitmentInterviewSlots.IgnoreTenantFilterForSystem(purpose).AsNoTracking()
                .Where(s => relatedIds.Contains(s.Id) && s.Status == InterviewSlotStatus.Scheduled)
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var superseded = new HashSet<Guid>();
        foreach (var n in checkedRows)
        {
            // An unknown subject (event or entity gone) is treated as no longer current.
            var current = n.EventId is Guid eventId
                && relatedByEvent.TryGetValue(eventId, out var relatedId)
                && (IsOfferTemplate(n.TemplateCode) ? issuedOffers.Contains(relatedId) : scheduledSlots.Contains(relatedId));
            if (!current)
                superseded.Add(n.Id);
        }
        return superseded;
    }

    private enum SendOutcome
    {
        Sent,
        Retried,
        Failed,
        Expired,
        Suppressed,
        WaitingForProvider
    }

    private static string OutcomeTag(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Sent => "sent",
        SendOutcome.Retried => "retry",
        SendOutcome.Failed => "failed",
        SendOutcome.Expired => "expired",
        SendOutcome.Suppressed => "suppressed",
        _ => "no_provider"
    };

    private async Task<SendOutcome> SendOneAsync(
        CandidateNotification n,
        IReadOnlyDictionary<Guid, CitizenApplication> citizens,
        IReadOnlySet<Guid> superseded,
        DateTime nowUtc,
        int maxAttempts,
        CancellationToken ct)
    {
        if (n.ExpiresAt is DateTime expiresAt && expiresAt <= nowUtc)
        {
            n.Status = CandidateNotificationStatus.Expired;
            n.NextAttemptAt = null;
            AddAudit(n, ExpiredAuditAction, null);
            return SendOutcome.Expired;
        }

        var sender = senders.For(n.Channel);
        if (sender is null)
            return WaitForProvider(n, nowUtc);

        // Consent withdrawn, row anonymized or contact removed since planning: never send.
        citizens.TryGetValue(n.RecruitmentApplicationId, out var citizen);
        var address = citizen is not null && ConsentedChannel(citizen) == n.Channel ? AddressOf(citizen, n.Channel) : null;
        if (address is null)
        {
            n.Status = CandidateNotificationStatus.Suppressed;
            n.NextAttemptAt = null;
            n.LastError = "No consent or contact";
            AddAudit(n, SuppressedAuditAction, n.LastError);
            return SendOutcome.Suppressed;
        }

        // The offer or interview the message announces changed since planning: never send the stale text.
        if (superseded.Contains(n.Id))
        {
            n.Status = CandidateNotificationStatus.Suppressed;
            n.NextAttemptAt = null;
            n.LastError = IsOfferTemplate(n.TemplateCode) ? "Offer no longer issued" : "Interview no longer scheduled";
            AddAudit(n, SuppressedAuditAction, n.LastError);
            return SendOutcome.Suppressed;
        }

        ChannelSendResult result;
        try
        {
            result = await sender.SendAsync(
                new OutboundNotification(address, n.Subject, n.Body, n.IdempotencyKey, n.Id.ToString(), n.TemplateCode), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Senders return results; an unexpected exception is a transient failure (type name only, never the message).
            result = new ChannelSendResult.Transient(ex.GetType().Name);
        }

        switch (result)
        {
            case ChannelSendResult.Sent ok:
                n.Attempts++;
                n.Status = CandidateNotificationStatus.Sent;
                n.SentAt = nowUtc;
                n.NextAttemptAt = null;
                n.LastError = null;
                n.ProviderMessageId = Truncate(ok.ProviderMessageId, 200);
                AddAudit(n, SentAuditAction, null);
                return SendOutcome.Sent;

            case ChannelSendResult.NotConfigured:
                return WaitForProvider(n, nowUtc);

            case ChannelSendResult.Permanent permanent:
                n.Attempts++;
                return Fail(n, SanitizeError(permanent.Reason, address));

            default:
                n.Attempts++;
                n.LastError = SanitizeError(result.Reason, address);
                if (n.Attempts >= maxAttempts)
                    return Fail(n, n.LastError);
                n.NextAttemptAt = nowUtc.Add(BackoffFor(n.Attempts));
                return SendOutcome.Retried;
        }
    }

    private SendOutcome Fail(CandidateNotification n, string? reason)
    {
        n.Status = CandidateNotificationStatus.Failed;
        n.NextAttemptAt = null;
        n.LastError = reason;
        AddAudit(n, FailedAuditAction, reason);
        return SendOutcome.Failed;
    }

    /// <summary>No sender can take the row: it stays Pending (never Sent, no attempt counted) and is checked again later.</summary>
    private static SendOutcome WaitForProvider(CandidateNotification n, DateTime nowUtc)
    {
        var next = nowUtc.Add(NoProviderRecheck);
        n.NextAttemptAt = n.ExpiresAt is DateTime expiresAt && expiresAt < next ? expiresAt : next;
        return SendOutcome.WaitingForProvider;
    }

    private string? AddressOf(CitizenApplication citizen, NotificationChannel channel)
    {
        if (channel == NotificationChannel.Email)
            return string.IsNullOrWhiteSpace(citizen.Email) ? null : citizen.Email.Trim();
        return CandidateContactRules.TryNormalizePhone(citizen.Phone, options.Value.Portal.PhoneCountryCode, out var e164)
            ? e164
            : null;
    }

    /// <summary>Delay before attempt <paramref name="attempts"/> + 1: 1, 2, 4, 8 … minutes, at most one hour.</summary>
    public static TimeSpan BackoffFor(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(3600, 60 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 10))));

    private static readonly Regex EmailLike = new(@"[^\s@<>""]+@[^\s@<>""]+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s\-]{5,}\d", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sender reasons are already sanitized (<c>HTTP 503</c>, <c>SMTP 550</c>, <c>Timeout</c>, exception type); this also
    /// removes the address and anything shaped like an email or phone number, and fits the 500-character column.
    /// </summary>
    public static string? SanitizeError(string? reason, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var text = reason.Trim();
        if (!string.IsNullOrWhiteSpace(address))
            text = text.Replace(address, "***", StringComparison.OrdinalIgnoreCase);
        text = EmailLike.Replace(text, "***");
        text = PhoneLike.Replace(text, "***");
        return Truncate(text, 500);
    }

    /// <summary>Audit row of an outcome: channel, template, status, attempts, provider id and reason — never address or body.</summary>
    private void AddAudit(CandidateNotification n, string action, string? reason) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = n.TenantId,
            EntityType = nameof(CandidateNotification),
            EntityId = n.Id,
            Action = action,
            UserId = null,
            ChangedFields = JsonSerializer.Serialize(new
            {
                applicationId = n.RecruitmentApplicationId,
                eventType = n.EventType,
                channel = n.Channel.ToString(),
                templateCode = n.TemplateCode,
                status = n.Status.ToString(),
                attempts = n.Attempts,
                providerMessageId = n.ProviderMessageId,
                reason
            })
        });

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
