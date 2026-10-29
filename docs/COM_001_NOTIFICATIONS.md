# COM-001 — Multi-Channel Notifications

## Purpose

Provide durable, multi-channel notification delivery (in-app, email, SMS, push) with deduplication, delivery tracking, and user preference support. SignalR disconnection does not lose notifications; they are persisted and retried.

## Scope

- In-app / email / SMS / push channels
- Template-based localized content
- Recipient and tenant scoping
- User preferences and mandatory overrides
- Quiet hours
- Retry with deduplication (idempotency key)
- Delivery receipts
- Expiration and escalation
- History for each user

## Key Components

| Component | Location | Role |
|-----------|----------|------|
| `NotificationMessage` | `SharedKernel/Notifications/` | Durable notification entity with channel, status, idempotency |
| `INotificationService` | `ServiceDefaults/Notifications/` | Service contract for send / query / mark-read |
| `InAppNotificationService` | `ServiceDefaults/Notifications/` | In-memory implementation for dev/test; production uses persistent store + SignalR |
| `INotificationChannelSender` | `ServiceDefaults/Notifications/` | COM-030: email/SMS to an external address (candidates); see below |

## Design Decisions

1. **Durable first** — messages are stored before delivery attempt; channel outage does not lose.
2. **Idempotency key** — prevents duplicate sends from the same domain event.
3. **Template codes** — content is localized at send time via L10N-001 infrastructure.
4. **Channel abstraction** — `INotificationService` implementations per channel; in-app is default.

## Acceptance Criteria

- SignalR disconnection does not lose notifications
- Duplicate event does not duplicate notification
- Delivery/failure is audited
- No sensitive PII in insecure channel
- Deep link re-authorizes
- User sees notification history

## Validation

- `scripts/com001-validate.ps1`
- `Com001NotificationTests`

## COM-030 — Candidate email/SMS channels (PLAN-30, HCM-030)

External (citizen) candidates have no user id, so email and SMS for them do **not** go through `INotificationService`
(which stays the in-app inbox contract, unchanged). A channel-sender abstraction lives beside it in
`src/Shared/HRManagement.ServiceDefaults/Notifications/`:

| Type | Role |
|------|------|
| `INotificationChannelSender` | `NotificationChannel Channel { get; }` + `Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)`; never throws for delivery problems |
| `OutboundNotification` | `(RecipientAddress, Subject?, Body, IdempotencyKey, CorrelationId?, TemplateCode?)` — a rendered message; the address is never logged |
| `ChannelSendResult` | `Sent(ProviderMessageId?)` / `Transient(Reason)` / `Permanent(Reason)` / `NotConfigured(Reason)`; reasons are sanitized (`HTTP 503`, `SMTP 550`, `Timeout`, exception type), never provider text or addresses |
| `FileDropNotificationSender` | lab/acceptance; one file per message; refused in Production |
| `SmtpEmailNotificationSender` | MailKit (MIT); STARTTLS by default, implicit TLS on 465, plain SMTP outside Production only |
| `HttpSmsNotificationSender` | generic HTTP POST of a JSON body template; token header from secret configuration; outbound timeout + circuit breaker (`AddHrOutboundResilience`, no retry) |
| `NotificationChannelSenderRegistry` | `For(channel)` → the configured sender or null (Provider=None), `Provider`, `MaxAttempts` |
| `CandidateNotificationStartupValidator` | refuses `Provider=FileDrop` in Production (and FileDrop without a path); warns for `None` |
| `AddCandidateNotificationChannels(IConfiguration)` | binds `Notifications:Candidate` and registers the above (HR.Core `Program.cs`) |

### Options `Notifications:Candidate`

| Key | Default | Notes |
|-----|---------|-------|
| `Provider` | `None` | `None` \| `FileDrop` \| `Smtp+Sms` (string, case-insensitive; unknown → `None`) |
| `MaxAttempts` | 5 | used by the dispatcher |
| `FileDrop:Path` | — | absolute, or relative to the content root |
| `Smtp:Host`, `Smtp:Port` (587), `Smtp:UseStartTls` (true), `Smtp:From`, `Smtp:User`, `Smtp:TimeoutSeconds` (30) | | |
| `Smtp:Password` | — | **secret**: `Notifications__Candidate__Smtp__Password` (env / secret store only) |
| `Sms:Endpoint`, `Sms:SenderId`, `Sms:AuthHeaderName` (`Authorization`), `Sms:BodyTemplate`, `Sms:TimeoutSeconds` (15) | | body template default `{"to":"{to}","from":"{sender}","text":"{text}","reference":"{reference}"}` |
| `Sms:AuthToken` | — | **secret**: `Notifications__Candidate__Sms__AuthToken` (env / secret store only) |

Per environment: `FileDrop` in Development (`appsettings.Development.json`, path `artifacts/candidate-notifications`
under the content root; the Development docker stack overrides it to `/tmp/candidate-notifications`) and in Acceptance
(`docker-compose.acceptance.yml`); `None` in `appsettings.Production.json` and `appsettings.Staging.json` until D-8 is
provisioned (PROD-005: lab stub only, live delivery forbidden). With `None`, messages stay `Pending` until they expire
and a warning is emitted; they are never marked sent. No `UserSecretsId` exists, so secrets come from environment
variables.

### FileDrop file format (acceptance tests read it)

- Folder: `Notifications:Candidate:FileDrop:Path`. The acceptance stack bind-mounts the host folder
  `./artifacts/acceptance/candidate-notifications` (repo root; gitignored; prepared by `scripts/acceptance-up.ps1`) to
  `/var/hrm/candidate-notifications` in `hr-core-web`. The folder survives `acceptance-reset`, so tests filter by their
  own keys.
- File name: lowercase hex SHA-256 of the idempotency key (`PiiMask.Sha256Hex(key)`) + `.json`; e.g. key
  `{eventId}:Email`. A resend of the same key rewrites the same file (temp file + move), so it is idempotent.
- Content: UTF-8 JSON without BOM, camelCase:

```json
{
  "template": "APP_RECEIVED",
  "channel": "Email",
  "recipient": "c@test.sy",
  "subject": "بوابة التوظيف — تم استلام طلب التوظيف",
  "body": "…",
  "idempotencyKey": "6f1c…:Email",
  "correlationId": "…",
  "createdAt": "2026-11-02T09:30:00.0000000Z"
}
```

`subject` is null for SMS. The file holds the real address: FileDrop is refused in Production.

### Delivery semantics

At-least-once. FileDrop is idempotent by file name. SMTP has no server-side de-duplication: the `Message-ID` is derived
from the idempotency key (`{sha256(key)[..32]}@{sender domain}`) so a resend carries the same id, but a retry after a
lost answer may still deliver twice. HTTP SMS sends `reference` = the idempotency key; de-duplication depends on the
provider.

### One-time codes for `/careers/my` (HCM-030, PLAN-30 H)

The sign-in code is a transactional message the candidate asked for: it needs no status-notification consent and does
**not** go through the planner/dispatcher queue. `CandidateOtpService.RequestAsync` sends it synchronously through
`NotificationChannelSenderRegistry.For(channel)` (the preferred channel, else the other one with a usable contact) with
template `OTP`, key `otp:{challengeId}:{channel}`, subject «بوابة التوظيف — رمز التحقق» (email) and the text
«رمز التحقق لبوابة التوظيف: {code} — صالح 10 دقائق، لا تشاركه مع أحد.». It then records one `CandidateNotification` row
(`EventType = OtpRequested`, no event id) with the code replaced by `******` and a **terminal** status (`Sent` or `Failed`,
`NextAttemptAt` null), so the dispatcher never re-sends the redacted body. The FileDrop copy holds the plain code (lab only;
acceptance reads it). With `Provider=None` (Production/Staging until D-8) or `NotConfigured`, no code can be sent and the
portal still answers the neutral message «إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.». The code
itself is stored only as HMAC-SHA256 with `Recruitment:Portal:OtpHmacKey`.

### Providers per environment and PII rules (summary)

| Environment | `Provider` | Candidate messages | OTP |
|-------------|------------|--------------------|-----|
| Development | `FileDrop` (`artifacts/candidate-notifications`) | files | files (plain code) |
| Acceptance | `FileDrop` (bind mount `./artifacts/acceptance/candidate-notifications`) | files | files (plain code) |
| Staging / Production | `None` (D-8 pending; FileDrop refused in Production) | stay `Pending` until expiry, never marked sent | not sent (neutral answer) |

Consent: «أوافق على تلقي إشعارات حول طلبي عبر القناة المختارة» (stored as `NotificationChannel` + `NotificationConsentAt`);
no consent → no status message. Idempotency: unique `(TenantId, IdempotencyKey)`. PII: no name, national id, birth date,
salary/grade, score, rank, committee, panel, note or reason in any message; stored rows keep only the masked recipient;
audit rows never hold the address or the body; retention neutralizes rows of anonymized candidates.

### Planner/dispatcher job (HR.Core, PLAN-30 F4/F5)

`HR.Core.Infrastructure/Services/Recruitment/CandidateNotificationJob` (`BackgroundService`, PostgreSQL advisory lock
**8675315**, first pass 30 s after start, then every `Recruitment:Jobs:CandidateNotificationsIntervalSeconds` = 60 s,
minimum 10; `Recruitment:Jobs:CandidateNotificationsEnabled` = true, read at registration and every cycle; integration
hosts set it to `false`) runs `CandidateNotificationDispatcher.RunOnceAsync(now)` — three passes, across tenants with
`IgnoreTenantFilterForSystem(BackgroundQueueProcessing)`, every inserted row carrying its `TenantId` explicitly:

1. **Offer reminder** (`RemindDueOffersAsync`): for each `Issued` offer with `ExpiryDate` in (now, now +
   `Recruitment:OfferReminderHours` = 48] and no reminder yet, one `RecruitmentApplicationEvent` `OfferExpiryReminder`
   (`RelatedEntityId` = offer id, `ToStatus` = Issued). The event id is derived from the offer id (SHA-256, version-5
   layout), so two concurrent passes still insert one row. `0` turns the reminder off. `RecruitmentOfferExpirer` is
   unchanged.
2. **Plan** (`PlanAsync`, 100 events per pass, oldest first): for each event with `NotificationsPlannedAt IS NULL`,
   when the application's intake channel is `Public` (internal and HR-entered candidates get nothing; `Received` only for
   `Public`), its citizen channel row has consent (`NotificationConsentAt`) and the chosen channel's contact, and the event
   maps to a template (for offer templates the offer must be `Issued` now — Draft/PendingApproval offers record no event
   anyway), it renders the message (dates in `TimeZoneInfo.Local`, like `/careers/track`) and inserts **one**
   `CandidateNotification` on the **chosen channel only** (the consent text covers «القناة المختارة», so the other contact
   is not used) with key `{eventId}:{channel}`, `RecipientMasked` and `NextAttemptAt = now`; then it marks the event planned
   (also when no message is due). A unique violation on `(TenantId, IdempotencyKey)` means another dispatcher planned it:
   the event is only marked.
3. **Send** (`SendDueAsync`, 100 rows per pass): due `Pending` rows (`NextAttemptAt <= now`):
   - past `ExpiresAt` → `Expired`;
   - no sender (`Provider=None`) or `NotConfigured` → stays `Pending`, no attempt counted, checked again after 15 minutes
     (or at `ExpiresAt`), warning log + metric `candidate_notifications_total{outcome="no_provider"}`; never `Sent`;
   - consent withdrawn / contact removed / row anonymized since planning → `Suppressed`;
   - the subject changed since planning (a row can wait for days with `Provider=None`, or minutes between retries): an
     `OFFER_ISSUED`/`OFFER_REMINDER` row whose offer is no longer `Issued`, or an `INTERVIEW_SCHEDULED`/`RESCHEDULED`
     row whose slot is no longer `Scheduled` → `Suppressed` (reason «Offer no longer issued» / «Interview no longer
     scheduled»), so a stale offer deadline or an old interview time is never sent;
   - otherwise the real address is read from the citizen row now (old non-E.164 phones are normalized with
     `Recruitment:Portal:PhoneCountryCode`) and sent: `Sent` (`SentAt`, `ProviderMessageId`); `Transient` → retried after
     1, 2, 4, 8 … minutes (max 1 h) until `Notifications:Candidate:MaxAttempts` (5), then `Failed`; `Permanent` →
     `Failed`. `LastError` is the sender's sanitized reason with any email/phone-like text removed, at most 500 characters.
   - `Sent`/`Failed`/`Expired`/`Suppressed` each add an `EntityAuditLog` row (`CandidateNotificationSent` /
     `CandidateNotificationFailed` / `CandidateNotificationExpired` / `CandidateNotificationSuppressed`, `TenantId` of the
     row, no user) whose `ChangedFields` hold application id, event type, channel, template, status, attempts, provider id
     and reason — never the address, the masked address or the body. Metric `candidate_notifications_total{outcome,channel}`
     on meter `HRManagement.Jobs`.

Each event and each row is saved on its own, so a crash after the provider accepted a message but before the row was
saved resends it on the next cycle with the same key (at-least-once, see above). HR sees the rows on
`RecruitmentApplications/Details` in the «الإشعارات» card (template label, channel, status, masked recipient, time; no
body). Known issue (pre-existing for every cross-tenant recruitment job): under production RLS with the NOBYPASSRLS
`hr_core_app` role, `IgnoreTenantFilterForSystem` reads no rows on RLS-hardened tables without `app.current_tenant`.

### Templates (HR.Core)

`HR.Core.Domain/Services/CandidateNotificationTemplates` — one pure C# class (HR.Core has no `.resx` convention for
domain text). Event → template: `Received`→`APP_RECEIVED`, `InterviewScheduled/Rescheduled/Cancelled`→`INTERVIEW_*`,
`OfferIssued`→`OFFER_ISSUED`, `OfferExpiryReminder`→`OFFER_REMINDER`, `StatusChanged`→`Rejected`/`ReserveListed`/
`Hired`/`Withdrawn`→`APP_REJECTED`/`APP_RESERVE`/`APP_HIRED`/`APP_WITHDRAWN`, `OfferAccepted`→`OFFER_ACCEPTED`,
`AppealDecided`→`APPEAL_DECIDED`; others send nothing. The input record cannot carry the candidate name, national id,
birth date, salary/grade, scores, rank, committee data, panel names or notes (`Hcm030NotificationPiiGuardTests`). SMS is
one Arabic (UCS-2) segment of at most 70 characters, without links; emails carry the reference and the
`{Recruitment:PublicPortalBaseUrl}/careers/my` link (HCM-030 I; omitted when the option is empty). Masked recipients for
history: `PiiMask.Email` / `PiiMask.Phone`. `ExpiresAt`: interview messages expire at the interview time even when it
is already past (a past slot cancelled later expires unsent; +1 day only when no slot time is known), offer messages at
the offer expiry, a rejection at the appeal deadline, reserve at the end of the reserve date, a withdrawal after 7 days,
everything else after 30 days.

### Deviations from COM-001 / PLAN-30 (recorded)

1. **Decision 4 / D-8 / PLAN-30 §3** put email/SMS behind `INotificationService`: external candidates have no user id, so
   they use `INotificationChannelSender` instead; `INotificationService` is unchanged.
2. **Decision 3** (localized at send time): candidate messages are rendered when planned and stored on
   `CandidateNotification` (Subject/Body), so history shows exactly what was sent.
3. PLAN-30 asks for Arabic templates «في Resources»: HR.Core has no resource convention, so one template class.
4. PLAN-30 bans the salary in SMS only: it is banned in every candidate message.
5. Idempotency key `{eventId}:{channel}` (PLAN-30: application+event+version; session 1 handoff: application+event+related
   id/occurred at).
6. MailKit is referenced from ServiceDefaults (all senders live there), which adds MailKit/MimeKit/BouncyCastle and a
   pinned `System.Security.Cryptography.Pkcs` 10.0.13 (MimeKit's 10.0.0 has advisories) to every consumer lock file.
