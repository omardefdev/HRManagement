# PLAN-30 — Session 2 handoff (E, F, G)

**Date:** 2026-10-29
**Branch:** `feature/plan-30-candidate-experience` (local only, not pushed), on top of the session 1 handoff `4977aac`.
**Source of truth for session 3:** this file + `reports/plan30-session1-handoff.md`.

## 1. Commits

`git log --oneline 4977aac..HEAD` plus the two commits that close the session:

| # | SHA | Message |
|---|-----|---------|
| extra | `2264e0e` | test(hcm030): parse eligibility age-bound dates culture-invariantly (session 1 test fix: `DateOnly.Parse` failed on the ar-SA / Um Al-Qura dev machine; now `ParseExact(..., "yyyy-MM-dd", InvariantCulture)`) |
| 6 | `c79f626` | feat(fs030): service-to-service candidate document upload policy in File.Storage |
| 7 | `2283eb3` | feat(hcm030): candidate documents with required types and HR document card |
| 8 | `cafdee4` | feat(com030): candidate notification channels (SMTP, HTTP SMS, FileDrop), consent and templates |
| 9 | `e25e81b` | feat(hcm030): candidate notification planner/dispatcher job and 48h offer reminder |
| 10 | *(G — this commit)* | feat(hcm030): pair-aware candidate retention job, run log and admin screen |
| 11 | *(this commit)* | docs(hcm030): session 2 handoff |

Never committed (local noise): `global.json`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, `tmp-vendors.html`, `docs/acceptance-plans/execution/PLAN-31-AGENT-PROMPT.md` (untracked, other work), and the `testdata/*/out/*.json` reports rewritten by test runs and validate scripts.

## 2. Migrations

All after session 1's `20261022234315_HCM030_ApplicationProfile`. All additive (new tables / nullable columns, no data step). `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration." for **HR.Core** and **File.Storage** (re-run for this handoff).

### HR.Core `20261028225233_HCM030_CandidateDocuments`

| Table | Column / index |
|-------|----------------|
| `CandidateDocuments` (new, tenant-scoped, RLS from startup hardening) | `Id` uuid PK; `TenantId` uuid; `RecruitmentApplicationId` uuid (FK `RecruitmentApplications`, cascade); `CitizenApplicationId` uuid? (FK `CitizenApplications`, set null); `DocumentType` varchar(20) (`Cv`, `Certificate`, `IdCopy`, `Other`); `FileId` uuid; `OriginalFileName` varchar(256) `[AuditRedact]`; `ContentType` varchar(100); `SizeBytes` bigint; `UploadedAt` timestamptz; `UploadedBy` varchar(64) (`"Candidate"` or the HR user id); `CreatedAt`/`UpdatedAt` |
| | indexes `(TenantId, RecruitmentApplicationId, UploadedAt)`, unique `(TenantId, FileId)`, plus the two FK indexes |
| `RecruitmentRequisitions` | + `RequiredDocumentTypes` varchar(100) null (comma-separated codes, like `EligibilityFlags`) |

### HR.Core `20261028233040_HCM030_CandidateNotifications`

| Table | Column / index |
|-------|----------------|
| `CandidateNotifications` (new, tenant-scoped) | `Id`, `TenantId`; `RecruitmentApplicationId` (FK, cascade); `EventId` uuid? (FK `RecruitmentApplicationEvents`, set null); `EventType` varchar(40); `Channel` varchar(20) (`Email`/`Sms`); `TemplateCode` varchar(40); `RecipientMasked` varchar(160) `[AuditRedact]`; `Subject` varchar(200)? `[AuditRedact]`; `Body` varchar(2000) `[AuditRedact]`; `IdempotencyKey` varchar(200); `Status` varchar(20) (`Pending`/`Sent`/`Failed`/`Expired`/`Suppressed`); `Attempts` int; `NextAttemptAt`?; `LastError` varchar(500)?; `SentAt`?; `ExpiresAt`?; `ProviderMessageId` varchar(200)?; `CreatedAt`/`UpdatedAt` |
| | unique `(TenantId, IdempotencyKey)`; `(TenantId, RecruitmentApplicationId, CreatedAt)`; filtered `IX_CandidateNotifications_PendingDue` on `NextAttemptAt` WHERE `"Status" = 'Pending'`; `IX_CandidateNotifications_EventId` |
| `CitizenApplications` | + `NotificationChannel` varchar(20) null; + `NotificationConsentAt` timestamptz null (both set only together with consent) |

### HR.Core `20261029002527_HCM030_CandidateRetention`

| Table | Column / index |
|-------|----------------|
| `CandidateRetentionRuns` (new, tenant-scoped `BaseEntity`) | `Id`; `StartedAt` timestamptz; `FinishedAt` timestamptz? (null = run did not finish: cancelled or run-level failure); `DryRun` bool; `DueCount`, `AnonymizedCount`, `SkippedLegalHold`, `SkippedAppeal` int; `TriggeredBy` varchar(64) (`"Job"` or the admin's user id); `Error` varchar(1000)? (sanitized); `TenantId`; `CreatedAt`/`UpdatedAt` |
| | index `IX_CandidateRetentionRuns_TenantId_StartedAt` |

### File.Storage

None (no entity change; `has-pending-model-changes` → no changes).

## 3. File.Storage service-upload contract and Keycloak provisioning

**Scope / policy** (`src/Shared/HRManagement.ServiceDefaults/ServiceIdentity.cs`, `ServiceAuthorizationExtensions.cs`):
- `ServiceScopes.FileStorageServiceUpload = "file-storage.service-upload"` (audience `api-file-storage`).
- `ServicePolicies.FileStorageServiceUpload = "Service.FileStorageServiceUpload"`: service account + scope + allowed client **`svc-hr-core` only** (a copy of `PayrollGradeLookup`). No existing policy changed; the new scope grants nothing on `FilesApiController`.

**Controller** `File.Storage.Web/Controllers/FilesServiceUploadController.cs` (route `api/files/service-upload`, only the policy above; logic in `File.Storage.Infrastructure/Services/FileServiceUploadService.cs`). Headers on every call: `Authorization: Bearer <svc-hr-core client_credentials token with the scope>`, `X-Tenant-ID: <tenant, "D" format>` (missing → 400 «تعذر تحديد المستأجر — رأس X-Tenant-ID مطلوب.»), and outside Development the caller proof from `GatewayCallerProof.Sign(request, configuration, "/" + path)` (HR.Core calls File.Storage **directly** at `Integration:FileStorageBaseUrl`, not through the gateway). 401 without/invalid token; 403 wrong client, missing scope or interactive user.

| Call | Contract |
|------|----------|
| `POST /api/files/service-upload` | multipart: `file` + `constraintProfile=CandidateDocument` (trimmed, case-insensitive; anything else → 400 «ملف قيود الرفع غير مدعوم (المسموح: CandidateDocument).»). `[RequestSizeLimit]` = **5,308,416 bytes** (5 MiB + 64 KiB multipart headroom). Goes through `FilePlatformService.UploadAsync` (quarantine bucket + scan queue). Creates one grant: HolderType `Service`, HolderId = caller client id (`svc-hr-core`), Module `hr-core`, Visibility `Module`, GranteeId = caller `sub`. 201 + `Location: /api/files/service-upload/{fileId}/content` + `{"fileId","originalName","size","mimeType"}`; scan status starts `PendingScan`. Arabic text/plain 400s for empty file, long name, size, extension, content type, magic-byte mismatch. |
| `POST /api/files/service-upload/metadata` | `{"fileIds":[...]}` (first 50 distinct). 200 `[{fileId, originalName, mimeType, size, scanStatus, status, uploadedAt}]` — only files this client uploaded through the service upload in this tenant (grant not revoked, file not deleted; the 24 h grant expiry is ignored). Others silently omitted. |
| `GET /api/files/service-upload/{fileId}/content` | 200 bytes (attachment, original name) only when the caller owns it **and** `ScanStatus` is `Clean`; 404 otherwise (no existence leak); 409 on checksum mismatch. |

**Constraint** `FileUploadConstraints.CandidateDocument`: 5,242,880 bytes; `.pdf .jpg .jpeg .png`; `application/pdf`, `image/jpeg`, `image/png` — enforced in File.Storage too.

**HR.Core client** `HR.Core.Web/Services/CandidateDocumentUploadClient.cs`: `ICandidateDocumentUploadClient` (`UploadAsync`, `GetMetadataAsync`, `GetContentAsync`), named client `"CandidateDocumentUpload"` (30 s), service-authenticated, sets `X-Tenant-ID` and signs each request. Empty `Integration:FileStorageBaseUrl` → `UnavailableCandidateDocumentUploadClient` (fails closed). Local checks first in `CandidateDocumentFileValidator.PrepareAsync` (count, total, size, extension, magic bytes, sanitized name).

**Keycloak provisioning** (same as `payroll.grades.read`):
- `scripts/sync-keycloak-dev-realm.ps1:514` creates client scope `file-storage.service-upload` (audience mapper `api-file-storage`); `:519` adds it to `svc-hr-core`'s optional scopes.
- `ServiceIdentity__Scope` for hr-core-web in `docker-compose.acceptance.yml:63` and `docker-compose.local.yml:23` = `file-storage.references payroll.grades.read file-storage.service-upload`; HR.Core `appsettings.json` `ServiceIdentity:Scope` = `file-storage.references file-storage.service-upload`.
- `Integration__FileStorageBaseUrl: http://file-storage-web:8080` in the acceptance and local compose files.
- **No realm re-import is needed**: the sync script patches the running realm, and `scripts/acceptance-up.ps1` (line 135) and `scripts/dev-profile-up.ps1` already run it. Run it **before** recreating hr-core-web on an existing Keycloak, otherwise every HR.Core S2S token request fails with `invalid_scope`. The realm JSON templates (`realm-hrmanagement-dev.template.json`, `realm-hr-services.json`) and `scripts/prepare-keycloak-import.ps1` do **not** carry the scope (as for `payroll.grades.read`); a freshly imported realm needs the sync script, and a host running HR.Core outside docker needs the realm synced first.
- `docker-compose.production.yml` was deliberately not changed (see §9 known issues).

## 4. `INotificationChannelSender` / `OutboundNotification` and the session 3 OTP

`src/Shared/HRManagement.ServiceDefaults/Notifications/` (`INotificationService` unchanged):

```csharp
public interface INotificationChannelSender
{
    NotificationChannel Channel { get; }                       // Email | Sms
    Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct); // never throws for delivery problems
}
public sealed record OutboundNotification(string RecipientAddress, string? Subject, string Body,
    string IdempotencyKey, string? CorrelationId = null, string? TemplateCode = null);
public abstract record ChannelSendResult   // .Reason is sanitized (HTTP 503, SMTP 550, Timeout, exception type)
{ Sent(string? ProviderMessageId); Transient(string Reason); Permanent(string Reason); NotConfigured(string Reason); }
```

`NotificationChannelSenderRegistry` (singleton, registered by `services.AddCandidateNotificationChannels(configuration)` in HR.Core `Program.cs`): `For(NotificationChannel)` returns the sender or **null** when `Provider=None`; `Provider`, `MaxAttempts`, `Channels`. Senders: `FileDropNotificationSender` (lab; `NotConfigured` in Production), `SmtpEmailNotificationSender` (MailKit 4.18.1, STARTTLS / implicit TLS on 465, plain SMTP refused in Production), `HttpSmsNotificationSender` (named client `"CandidateNotifications.Sms"`, `AddHrOutboundResilience`, JSON body template `{to} {sender} {text} {reference}`, token header from secret config).

**How session 3 sends an immediate OTP.** The OTP is a user-requested transactional message: it needs no status-notification consent and must **not** go through the queue (the planner/dispatcher only plans from `RecruitmentApplicationEvents` and resends `Pending` rows). Send synchronously in the request, then record one `CandidateNotification` row with the code redacted and a **terminal** status, so `CandidateNotificationDispatcher.SendDueAsync` never picks it up (it would resend the redacted body):

```csharp
// In the /careers/my OTP request handler (session 3). citizen/app were resolved from reference + national id
// through IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal).
var channel = chosenChannel;                                   // NotificationChannel.Email or .Sms, contact must exist
var address = channel == NotificationChannel.Email
    ? citizen.Email!
    : CandidateContactRules.TryNormalizePhone(citizen.Phone, recruitmentOptions.Value.Portal.PhoneCountryCode, out var e164)
        ? e164 : null;
if (address is null) return OtpUnavailable();                  // Arabic message, no 500

var sender = registry.For(channel);                            // NotificationChannelSenderRegistry
if (sender is null) return OtpUnavailable();                   // Provider=None: no OTP can be sent (D-8)

var key = $"otp:{challenge.Id:D}:{channel}";                   // unique per (TenantId, IdempotencyKey)
var text = $"رمز التحقق: {code}";                              // ≤ CandidateNotificationTemplates.SmsMaxLength, no PII
var result = await sender.SendAsync(
    new OutboundNotification(address, channel == NotificationChannel.Email ? "بوابة التوظيف — رمز التحقق" : null,
        text, key, CorrelationId: challenge.Id.ToString(), TemplateCode: "OTP"), ct);

db.CandidateNotifications.Add(new CandidateNotification
{
    TenantId = app.TenantId,                                   // explicit: the portal has no ambient tenant
    RecruitmentApplicationId = app.Id,
    EventId = null,
    EventType = "OtpRequested",                                // ≤ 40 chars; not a RecruitmentApplicationEvent
    Channel = channel,
    TemplateCode = "OTP",
    RecipientMasked = channel == NotificationChannel.Email ? PiiMask.Email(citizen.Email) : PiiMask.Phone(citizen.Phone),
    Subject = channel == NotificationChannel.Email ? "بوابة التوظيف — رمز التحقق" : null,
    Body = text.Replace(code, "******"),                        // code never stored
    IdempotencyKey = key,
    Status = result is ChannelSendResult.Sent ? CandidateNotificationStatus.Sent : CandidateNotificationStatus.Failed,
    Attempts = 1,
    NextAttemptAt = null,                                      // never Pending → never re-sent by the dispatcher
    SentAt = result is ChannelSendResult.Sent ? now : null,
    ProviderMessageId = (result as ChannelSendResult.Sent)?.ProviderMessageId,
    LastError = CandidateNotificationDispatcher.SanitizeError(result.Reason, address),
    ExpiresAt = challenge.ExpiresAt,
});
await db.SaveChangesAsync(ct);                                 // + optionally an EntityAuditLog row like the dispatcher's AddAudit (no address/body)
```

Notes: `RecipientAddress` must never be logged or stored; `NotConfigured` (e.g. FileDrop in Production) means "no OTP", not "sent". FileDrop writes the **unredacted** body to its file, which is how acceptance reads the code (§5). Retention neutralizes these rows like any other `CandidateNotification` (§7).

## 5. FileDrop path, format and acceptance access

- **Choice: bind mount** (no acceptance-only endpoint). `docker-compose.acceptance.yml` hr-core-web: `Notifications__Candidate__Provider: FileDrop`, `Notifications__Candidate__FileDrop__Path: /var/hrm/candidate-notifications`, volume `./artifacts/acceptance/candidate-notifications:/var/hrm/candidate-notifications`. `scripts/acceptance-up.ps1` (lines 111–116) creates the host folder and chowns it to uid 1654 before `up`.
- Host-side tests read `<repo>/artifacts/acceptance/candidate-notifications/<sha256(idempotencyKey)>.json`, where the file name is `FileDropNotificationSender.FileNameFor(key)` = `PiiMask.Sha256Hex(key) + ".json"`. Status-notification keys are `CandidateNotification.IdempotencyKeyFor(eventId, channel)` = `{eventId:D}:{channel}`.
- Format: one UTF-8 JSON file (no BOM) per message, camelCase: `template`, `channel` (`Email`|`Sms`), `recipient` (real address), `subject` (null for SMS), `body`, `idempotencyKey`, `correlationId` (notification row id), `createdAt` (UTC ISO 8601). Written to a temp file then moved: a resend of the same key rewrites one file (`createdAt` = last write).
- Development (`appsettings.Development.json`): FileDrop to `artifacts/candidate-notifications` (relative to the content root, gitignored). Development docker stack (`docker-compose.yml:132`): `/tmp/candidate-notifications` inside the container (no host access). Production: FileDrop refused (`CandidateNotificationStartupValidator` throws; the sender returns `NotConfigured`).

## 6. Templates, event → message map, options per environment

Templates: one pure class `HR.Core.Domain/Services/CandidateNotificationTemplates.cs` (HR.Core has no resx convention for domain text): `TemplateCodeFor(eventType, toStatus)`, `InputFor(...)`, `Render(code, channel, input)`, `ExpiresAt`, `LabelAr`. The input record has no name, national id, birth date, salary, grade, score, rank, committee, panel, note or reason field. SMS: one UCS-2 segment (`SmsMaxLength = 70`), no link, no location. Email: subject «بوابة التوظيف — …», body, reference, the track link when `Recruitment:PublicPortalBaseUrl` is set (`RecruitmentOptions.PublicTrackUrl()` = `{base}/careers/track`), automated-message footer. Dates `yyyy/MM/dd [HH:mm]`, InvariantCulture, `TimeZoneInfo.Local` (UTC in containers).

| Event | Template | Expires |
|-------|----------|---------|
| `Received` (IntakeChannel `Public` only) | `APP_RECEIVED` | +30 days |
| `InterviewScheduled` / `InterviewRescheduled` / `InterviewCancelled` | `INTERVIEW_SCHEDULED` / `INTERVIEW_RESCHEDULED` / `INTERVIEW_CANCELLED` | the interview time (even when past) |
| `OfferIssued` (offer still `Issued` at planning) | `OFFER_ISSUED` (no salary) | offer expiry |
| `OfferExpiryReminder` | `OFFER_REMINDER` | offer expiry |
| `StatusChanged` → `Rejected` | `APP_REJECTED` (appeal deadline only when appealable, via `CandidatePublicStatusProjection.AppealWindowOf`) | appeal deadline / +30 days |
| `StatusChanged` → `ReserveListed` | `APP_RESERVE` | end of reserve date |
| `OfferAccepted` | `OFFER_ACCEPTED` | +30 days |
| `StatusChanged` → `Hired` | `APP_HIRED` | +30 days |
| `AppealDecided` | `APPEAL_DECIDED` (outcome label only) | +30 days |
| `StatusChanged` → `Withdrawn` | `APP_WITHDRAWN` | +7 days |
| any other | — (event only marked planned) | — |

**Job** `CandidateNotificationJob` (advisory lock **8675315**, 30 s stagger, every `Recruitment:Jobs:CandidateNotificationsIntervalSeconds` = 60) runs `CandidateNotificationDispatcher.RunOnceAsync`: `RemindDueOffersAsync` (Issued offers expiring within `Recruitment:OfferReminderHours` = 48, one `OfferExpiryReminder` event per offer, id derived from the offer id) → `PlanAsync` (one row on the candidate's chosen channel, key `{eventId}:{channel}`) → `SendDueAsync` (address read from the citizen row at send time; backoff 1, 2, 4… min capped 1 h up to `MaxAttempts`; superseded offer/interview rows → `Suppressed`; audit actions `CandidateNotificationSent/Failed/Expired/Suppressed` with no address or body; metric `candidate_notifications_total{outcome,channel}`, `outcome=no_provider` when nothing can send).

| Option | Code default | Development | Acceptance (compose) | Staging | Production |
|--------|--------------|-------------|----------------------|---------|------------|
| `Notifications:Candidate:Provider` | `None` | `FileDrop` | `FileDrop` | `None` (explicit) | `None` (explicit) |
| `Notifications:Candidate:FileDrop:Path` | — | `artifacts/candidate-notifications` | `/var/hrm/candidate-notifications` (bind mount) | — | — (refused) |
| `Notifications:Candidate:MaxAttempts` | 5 | 5 | 5 | 5 | 5 |
| `Notifications:Candidate:Smtp:{Host,Port=587,UseStartTls=true,From,User,TimeoutSeconds=30}`; `Smtp:Password` env only | | | | | until D-8 |
| `Notifications:Candidate:Sms:{Endpoint,SenderId,AuthHeaderName=Authorization,BodyTemplate,TimeoutSeconds=15}`; `Sms:AuthToken` env only | | | | | until D-8 |
| `Recruitment:PublicPortalBaseUrl` | empty (no link) | `http://localhost:5000` | `http://localhost:6200` | empty | empty |
| `Recruitment:Portal:PhoneCountryCode` | `963` | | | | |
| `Recruitment:Jobs:CandidateNotificationsEnabled` / `IntervalSeconds` | true / 60 | | | | (false in integration hosts) |
| `Recruitment:OfferReminderHours` | 48 (≤ 0 disables) | | | | |
| `Recruitment:CandidateDocuments:{MaxFiles,MaxBytes,MaxTotalBytes}` | 5 / 5,242,880 / 9,437,184 | | | | |
| `Integration:FileStorageBaseUrl` | empty (uploads fail closed) | empty | `http://file-storage-web:8080` (also local compose) | — | not set |

Secrets: `Notifications__Candidate__Smtp__Password`, `Notifications__Candidate__Sms__AuthToken` (env / user-secrets only). Consent: «أوافق على تلقي إشعارات حول طلبي عبر القناة المختارة»; no consent → no status notification.

## 7. Retention rule, options and run log

**Pair rule** (`CandidateApplicationLegalLifecycleService.EvaluatePair` in Domain; SQL form in `HR.Core.Infrastructure/Services/Recruitment/CandidateRetentionService.cs`). A pair = the `RecruitmentApplication` + its linked `CitizenApplication` or `InternalApplication` (matched by `PromotedRecruitmentApplicationId` and by `SourceInternalApplicationId`). It is **due** when:
- not hired (`HiredEmployeeId` null);
- requisition `Closed` or `Cancelled` (or no requisition) — open requisitions never trigger;
- trigger = requisition `ClosedDate ?? CancelledAt ?? application.AppliedAt`, and `trigger <= utcNow.AddMonths(-Months)`;
- no `LegalHold` on the application or the citizen row (counted `SkippedLegalHold`); internal rows have no hold of their own;
- no pending appeal, via the mirror field or a `RecruitmentAppeal` row (counted `SkippedAppeal`);
- not fully anonymized (half-anonymized pairs, e.g. after a single-row DSAR erasure, and pre-HCM-030 anonymized pairs that still have documents, un-neutralized messages, profile/free text or internal `Motivation`, count as due).
Verdicts: `Due`, `NotYetDue`, `OpenRequisition`, `Hired`, `AlreadyAnonymized`, `LegalHold`, `PendingAppeal`. Legacy unlinked citizen rows keep the HCM-006 rule (`ClosedDate ?? SubmittedAt`) and are counted/anonymized by the same job. The due-list reason uses `CandidateRetentionService.TriggerKind(status, closedDate, cancelledAt)` («إلغاء الشاغر» for cancelled requisitions).

**Anonymization** (one transaction per pair, after `SELECT … FOR UPDATE` on the application and its citizen rows on Npgsql, then a re-check): both `TryAnonymize` overloads (idempotent, keep `AnonymizedAt`) clear name, national id, phone, email, cover note, notes, appeal notes, the profile fields (`BirthDate`, `EducationLevelCode`, `Specialization`, `GraduationYear`, `YearsOfExperience`), `RejectionNote`, `EligibilityOverrideReason`, `NotificationChannel`, `NotificationConsentAt`; `ClearFreeText(InternalApplication)` clears `Motivation`. Each `CandidateDocument` file is released through `IFileReferenceOutbox.EnqueueRelease` (same holder key as registration, explicit `TenantId`), then the rows are deleted. `CandidateNotification.Body`/`RecipientMasked` → «—», `Subject` → null, `Pending` → `Suppressed` («Anonymized (retention)»). One PII-free `CandidateRetentionAnonymized` audit row. `// PLAN-30 S3: OTP challenges` marks where OTP rows must be deleted.

**Options**

| Key | Default | Notes |
|-----|---------|-------|
| `Recruitment:Retention:Months` | 36 (`DefaultRetentionMonths`, = GOV-001 1095 days) | |
| `Recruitment:Retention:Apply` | **false** (dry run: counts only) | `true` in `docker-compose.acceptance.yml`; explicit `false` in `appsettings.Production.json` and `appsettings.Staging.json` until the DPO approves (runbook). A run applies only when the request **and** the option say Apply. |
| `Recruitment:Retention:BatchSize` | 100 (1..1000) | ids per batch per tenant |
| `Recruitment:Jobs:CandidateRetentionEnabled` | true | checked at registration and every cycle; `false` in the 5 integration host blocks |
| `Recruitment:Jobs:CandidateRetentionIntervalMinutes` | 10080 (weekly, ≥ 60) | |

**Job** `CandidateRetentionJob`: advisory lock **8675316**, 2-minute stagger, hourly check; inside the lock it skips tenants that already have a **finished** `Job` run started within the interval (`CandidateRetentionRunRequest.SkipTenantsCompletedWithin`), so a restart or an interrupted cycle resumes only the unfinished tenants. `RunWithLeaderLockAsync` (fresh DI scope, same lock) is also used by the admin screen.

**Run log** `CandidateRetentionRun`: one row **per tenant per run** (job: every tenant with recruitment or citizen applications; admin run: the admin's tenant). `DueCount` = eligible now (excludes held/appeal pairs); `AnonymizedCount` is incremented after each committed pair (true count even on partial failure); `FinishedAt` null + `Error` «cancelled» / sanitized type names on interruption.

**Admin screen** `CandidateLifecycle/Index` (`HrCoreDelete`, Admin-only nav link «الاحتفاظ ببيانات المرشحين» under «بوابة المواطن للتوظيف»): paged due list (citizen reference number or short `#XXXXXXXX` id, requisition number, trigger date, Arabic reason — no other PII), three count tiles, «تشغيل تجريبي» (POST dry run), «تنفيذ الإخفاء» (POST apply, shown only when Apply is true, `data-hr-confirm` danger modal, refused server-side otherwise), run history (latest 20). Antiforgery on both POSTs; manual runs use `CancellationToken.None` so a started apply finishes and is logged. `retention/candidates` JSON now pages (`page`, `pageSize` ≤ 100) and uses the pair rule. Docs: `GOV_001_RETENTION_SCHEDULE.md` (`ret-recruitment-application`), `GOV_001_RETENTION_RUNBOOK.md` (candidate section), `HCM_001_RECRUITMENT.md` (job + lock).

## 8. Test results; modified tests

Run for this handoff on the working tree (E, F committed; G uncommitted = commit 10), Windows ar-SA, Docker running.

| Check | Result |
|-------|--------|
| `dotnet build HRManagement.slnx` | **0 errors**, 4 warnings (all NU1903 SSH.NET advisories in IntegrationTests, pre-existing) |
| `has-pending-model-changes` HR.Core / File.Storage | **No changes** / **No changes** |
| Full unit suite (PowerShell, `PSExecutionPolicyPreference=Bypass`) | **3539 total: 3528 passed / 11 failed.** All 11 are on the environment-only list: `Str005ProductionBindingTests` ×3 (`GoLiveDeclared…`, `Runtime_validate_script_passes`, `Startup_fails_on_expired…`), `Str005GovernancePackTests.Exceptions_have_expiry…`, `LearningCompletionRulesTests.Certificate_Number…`, `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`, `Syr001ProductionBindingTests.AddHrLegislationRegister…`, `Ten002ArchitecturalTests.IgnoreQueryFilters…` (flags `BenefitPlanSeeder.cs`), `San001SanctionsTests.Validate_script_passes`, `Prv001RopaTests.Ropa_covers…`, `BolaArchitectureTests.Employee_scoped…` (HRContracts/Payroll controllers). Baseline on this machine before session 2: 3218 passed / 12 failed of 3230 (the 12th was the flaky `Hcm029CandidateAcceptedV4Tests.External_hire…`, which passed now). +309 tests. The same suite run from Git Bash shows 18 failures: the 7 extra are `*_validate_script_passes` tests (`Iam002`, `Iam005`, `Psirt001`, `Rel006`, `Doc001`, `ArabicPdf`, `Prv003`) that invoke `powershell -File` without `-ExecutionPolicy Bypass` — shell-only. |
| Integration filter `Hcm030\|Fs030\|Hcm02\|PublicCareers\|EssTalent\|RecruitmentHire\|Fun001\|InternalChannel\|Promote\|Recruitment\|Offer\|File` (PostgreSQL via Testcontainers) | **101 total: 90 passed / 3 failed / 8 skipped.** All new session 2 integration tests pass: `Fs030ServiceUploadHttpTests` ×3, `Hcm030CandidateNotificationConcurrencyTests` ×2, `Hcm030CandidateRetentionIntegrationTests` ×2 (batched cross-tenant run; legal hold committed during anonymization wins), plus session 1's `Hcm030BackfillTests`. |
| — failure 1 | `EstablishmentHireIntegrationTests.HirePartyCompensate_VacatesPosition_RevertsApplicationAndOffer` (`Expected: Vacant, Actual: Occupied`) — same as the session 1 handoff, fails on `main` too. |
| — failure 2 | `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes_become_decided…` (`Expected 2026-09-01…, Actual 1448-03-19…`): the test formats `{reviewedAt:yyyy-MM-dd HH:mm:ss}` with the current culture (ar-SA, Hijri). **Environment-only**: passes with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; the test file is unchanged since HCM-028 (`68c16bc`). Session 1 ran on a non-Hijri locale. |
| — failure 3 | `ContractSeparationChainIntegrationTests.ActivateContract_PublishesEvent_CreatesPayProfile`: DI validation `Unable to resolve service for type 'HRManagement.SharedKernel.Legal.IHrCoreActiveRegimeClient'` for `PublishSeparationHandler` in `MassTransitTestScope`. **Pre-existing, not session 2**: it enters the filter only because `File` matches `PayProfile` (it was outside session 1's filter); session 2 changed no HR.Contracts code (lock files only) and no DI registration; the handler and the test helper are unchanged since `544ba7a`. Deterministic. Not verified on a `main` worktree. |
| — skipped 8 | `Qa003MigrationUpgradeTests.FileStorage_Empty_To_Head…`, `Qa003MinioFileScanTests` ×3, `FileScanLiveE2ETests.Composite_scanner…`, `FileStorageInboxIdempotencyTests…`, `Data001ConcurrencyMatrixTests.FileReference_ConcurrentRegister…`, `Pay023FrozenRunIsolationTests…` — need MinIO/ClamAV/opt-in env flags; unrelated. |
| `powershell -ExecutionPolicy Bypass -File scripts/com001-validate.ps1` | `PASS: COM-001 notification validate` |
| `bash scripts/check-license-policy.sh` | `License policy check OK (no unwaived GPL/AGPL in inventory).` |
| Earlier in the session | `scripts/hcm001-validate.ps1` PASS; `scripts/ux003-no-native-confirm.ps1` only pre-existing violations (none in the new views); `dotnet restore HRManagement.slnx --locked-mode` exit 0; `dotnet list … --vulnerable --include-transitive` only the pre-existing SSH.NET findings. |

**New tests (309 unit cases = the whole +309):** `Fs030ServiceUploadPolicyTests` 45, `Hcm030CandidateDocumentTests` 47, `Hcm030ApplyContactConsentTests` 38, `Hcm030NotificationChannelSenderTests` 29, `Hcm030NotificationPiiGuardTests` 81, `Hcm030NotificationTests` 40, `Hcm030RetentionJobTests` 22, `Hcm030CandidateLifecycleScreenTests` 7. Integration: `Fs030ServiceUploadHttpTests` 3 (real File.Storage host, SQLite), `Hcm030CandidateNotificationConcurrencyTests` 2 and `Hcm030CandidateRetentionIntegrationTests` 2 (PostgreSQL, `Skip.If` without Docker).

**Modified existing tests** (`git diff 4977aac -- src/Tests`, plus the uncommitted G hunks):

| File | Reason |
|------|--------|
| `UnitTests/RecruitmentEligibilityRulesTests.cs` | `2264e0e`: `DateOnly.Parse` → `ParseExact(…, InvariantCulture)` (ar-SA machine); E4: flag count 6 → 7 (`MissingRequiredDocuments`). |
| `UnitTests/Hcm029TestSupport.cs` | `RecruitmentApplicationsController` ctor gained `CandidateDocumentService` (E6); optional `candidateDocuments` parameter so tests can drive the download action. |
| `UnitTests/Hcm030TestSupport.cs` | session 1 helper extended with `FakeCandidateDocumentClient` and `CreateCandidateDocumentService` (E). |
| `IntegrationTests/PublicCareersApplyTests.cs` | `PublicCareersController` ctor gained `CandidateDocumentService` + `IOptions<RecruitmentOptions>` (E5); phone `"099"` → `"0999123456"` because the phone is now validated/normalized (F2). |
| `IntegrationTests/PublicCareersRequisitionGateTests.cs` | same controller ctor change (E5). |
| `IntegrationTests/RecruitmentTestControllers.cs` | `RecruitmentApplicationsController` ctor gained `CandidateDocumentService` (E6), with the unavailable client. |
| `IntegrationTests/HRCoreJwtBearerIntegrationTests.cs`, `CrossTenantApiPenetrationTests.cs`, `Est004EstablishmentPlanningIntegrationTests.cs`, `TenantQuarantineIntegrationTests.cs` (×2 blocks) | only `UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled","false")` (F, committed) and `UseSetting("Recruitment:Jobs:CandidateRetentionEnabled","false")` (G, uncommitted) so no background job runs in those hosts. |
| `UnitTests/IntegrationTests/AcceptanceTests/TestSupport packages.lock.json` | MailKit / MimeKit / BouncyCastle 2.7.0 / System.Security.Cryptography.Pkcs 10.0.13 (F1). |

## 9. Deviations, known issues, markers, session 3

### Deviations

**E — documents**
1. Service upload is a **separate controller** `FilesServiceUploadController` (only the new policy; adding it to `FilesApiController` would AND the class-level `ModuleFileStorage` policy). It also exposes `metadata` and `content`, served only for the caller's own Service grant in the request tenant, content only when `Clean`.
2. HR.Core calls File.Storage **directly** (`Integration:FileStorageBaseUrl`) with its service token and `GatewayCallerProof.Sign(...)` on each request (precedent `HrCoreLookupClient`); gateway and caller-proof middleware unchanged.
3. **HR download proxy**: ordinary HR users lack `ModuleFileStorage`, so downloads go through `RecruitmentApplications/CandidateDocument/{id}` (class policy, tenant check, metadata check, then content via the service identity; buffered ≤ 5 MB; two S2S calls). Labels: Clean «سليم» (link), PendingScan «قيد الفحص», Infected/Quarantined «محجور — غير متاح», ScanError «تعذّر الفحص — غير متاح», unknown/unreachable «غير متاح حالياً»; only Clean gets a link.
4. `Recruitment:CandidateDocuments:MaxTotalBytes` = **9 MiB** (9,437,184) per apply post (Production Kestrel limit 10 MB), Arabic «الحجم الإجمالي للمستندات يجب ألا يتجاوز 9 ميغابايت.», shown as help text; no `[RequestSizeLimit]` on Apply.
5. Required documents apply **only to public applications** (`CandidateDocumentTypes.RequiredFor(channel, stored)`): no flag, badge or alert for Internal, HrEntered or Legacy (they have no upload path).
6. Documents only on requisition-linked postings; legacy postings hide the inputs and the server ignores posted files. One input per type (required first, «(مطلوب)»/«(اختياري)»; Certificate/Other multiple).
7. Any upload failure (File.Storage 4xx/5xx/transport) → the single «تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.»; whole apply fails, nothing saved. Uploads happen before staging (the reference-number generator saves the context).
8. Upload client and validator live in `HR.Core.Web/Services` (only Web references `HRManagement.FileStorage`); File.Storage logic in `FileServiceUploadService` (Infrastructure). Added Arabic 400s (long filename, empty file); 409 on checksum mismatch; `constraintProfile` case-insensitive.
9. Outbox register rows get `TenantId = posting.TenantId` explicitly after `EnqueueRegister` (`EfFileReferenceOutbox` unchanged). `OriginalFileName` is `[AuditRedact]`. `RequiredDocumentTypes` is a comma-separated varchar. `Integration:FileStorageBaseUrl` only in acceptance/local compose.
10. HR.Core `appsettings.json` scope list includes `file-storage.service-upload` (the payroll precedent only touched compose).

**F — notifications**
11. Email/SMS are **not** behind `INotificationService` (new `INotificationChannelSender` beside it), contrary to COM-001 decision 4 / D-8 / PLAN-30 §3; rendered at planning time; templates in one C# class (no resx); salary banned in every message; key `{eventId}:{channel}` — all recorded in `docs/COM_001_NOTIFICATIONS.md`.
12. **Preferred channel only**: one row per event on the chosen channel (consent text covers «القناة المختارة»). Internal and HrEntered intake get no messages; `Received` only for `Public`.
13. MailKit 4.18.1 (not the 6-day-old 4.18.2), referenced from ServiceDefaults → 38 lock files; `System.Security.Cryptography.Pkcs` pinned to 10.0.13 (MimeKit's 10.0.0 has GHSA-846f-f764-76mx High, GHSA-2fmv-rv2c-59x7 Moderate).
14. SMS: no link, no interview location (one 70-char segment). Rejection email wording «يمكنك تقديم استئناف عبر الجهة حتى …» (portal appeals arrive in session 3).
15. F5 reminder is a **dispatcher pass** (`RemindDueOffersAsync`), not inside `RecruitmentOfferExpirer`; reminder event id derived from the offer id (one per offer by primary key, no migration).
16. Offer messages planned only if the offer is `Issued` at planning; superseded offer/interview rows are `Suppressed` at send time. Extra audit actions `CandidateNotificationExpired`/`Suppressed`. `Provider=None`/`NotConfigured` rows stay `Pending` without counting an attempt, rechecked every 15 min. Backoff 60 s × 2^(n−1), cap 1 h. 30 s first-pass stagger. Dates in `TimeZoneInfo.Local`.
17. Phones stored normalized E.164 (`Recruitment:Portal:PhoneCountryCode` 963); channel preference stored only with consent; contact (phone or email) required on every posting, consent only on requisition-linked ones. `PublicPortalBaseUrl` empty in Production/Staging (no link, no HrGateway fallback). Fixed a commit-8 defect (`ChannelSendResult.Reason` infinite recursion). Extra files: `docker-compose.yml`, `scripts/acceptance-up.ps1`.

**G — retention**
18. `CandidateRetentionRun` has **no nullable TenantId**: tenant-scoped `BaseEntity`, **one row per tenant per run**.
19. Cutoff `trigger <= utcNow.AddMonths(-months)` (SQL-translatable) replaces `utcNow >= trigger.AddYears(3)`; the legacy citizen `CanPurge` uses it too (differences only at month-end / 29 Feb). `RetentionYears` removed → `DefaultRetentionMonths = 36`.
20. `CanPurge` overloads keep their signatures + optional `retentionMonths = 36`; `CanPurge(RecruitmentApplication, …)` (no callers) now delegates to the pair rule (open requisition no longer purges; `CancelledAt` is a trigger).
21. `TryAnonymize` (both overloads): optional `utcNow`; idempotent (no early return; keeps `AnonymizedAt`); also clears profile, contact, consent, `RejectionNote`, `EligibilityOverrideReason` — affects the existing DSAR erasure and CandidateLifecycle JSON anonymize endpoints.
22. `retention/candidates` JSON: `page`/`pageSize`, `count` = total; for a pair `Id`/`type` = the RecruitmentApplication; new fields `requisitionNumber`, `triggerDate`, `partiallyAnonymized`; short `#XXXXXXXX` reference for internal/HR-entered pairs.
23. `DueCount` excludes held/appeal pairs (counted separately). Legacy unlinked citizen rows are counted and anonymized by the same job/screen.
24. **Internal pairs**: held only via the RecruitmentApplication's `LegalHold`; the application is anonymized and the internal row's `Motivation` cleared; the employee is kept.
25. Job cadence: hourly check + per-tenant "finished Job run within the interval" guard inside lock 8675316 (`LastJobRunStartedAtAsync` removed); a persistent run-level failure writes one error row per hour for that tenant. Manual runs ignore the request-abort token. Neutralized notifications: «—» (columns required), `Subject` null; Sent/Failed/Expired keep their status. Staging also sets `Apply=false` explicitly. Integration test runs the job core on a fixture context (not the hosted service). Nav link Admin-only.

### Known issues (not fixed here)

- **Outbox dispatcher tenant filter (pre-existing, E.7):** `FileReferenceOutboxDispatcher.ProcessBatchAsync` reads under the tenant filter in a background scope (`EffectiveTenantId = Guid.Empty`) and sends no `X-Tenant-ID`, so tenant-stamped Register/Release rows probably never reach File.Storage (candidate files stay `Pending`, releases never happen). HR access does not depend on it (service grant + service endpoints).
- **File.Storage GC tenant filter (pre-existing):** `FilePlatformService.CollectGarbageAsync` runs without a tenant bypass, so tenant-owned orphan `Pending` files (failed apply, partial upload) are never collected (cutoff otherwise `Pending && UploadedAt < now − OrphanUploadTtlHours`, default 24 h, hourly).
- **`RegisterReference` creates no Module grant** for API-uploaded files (pre-existing).
- **Audit log keeps PII on anonymization:** `AuditSaveChangesInterceptor` copies old values of columns without `[AuditRedact]` (names, contact, cover note, notes, profile except `BirthDate`) into the append-only `EntityAuditLog`. The explicit `CandidateRetentionAnonymized` row is PII-free. Documented in the runbook; interceptor unchanged.
- **Production RLS for cross-tenant jobs:** under `hr_core_app` (NOBYPASSRLS) without `app.current_tenant`, `IgnoreTenantFilterForSystem` jobs (notification dispatcher, retention job, every recruitment job) read no rows on RLS-hardened tables. The admin-screen retention run is tenant-scoped and unaffected.
- **Production compose lacks the new settings:** `docker-compose.production.yml` hr-core `ServiceIdentity__Scope` has no `file-storage.service-upload`, and there is no `Integration__FileStorageBaseUrl` or `Recruitment__PublicPortalBaseUrl`; the production realm needs the client scope linked to `svc-hr-core` first. Until then any apply with files fails with the Arabic message, and messages carry no link.
- **Prv003 script execution policy:** `Prv003BreachTests.Prv003_validate_script_passes` (and six other `*_validate_script_passes` tests) fail when the test host's shell lacks `-ExecutionPolicy Bypass` (e.g. Git Bash); they pass from PowerShell with `PSExecutionPolicyPreference=Bypass`.
- **Environment-only unit failures on ar-SA** (11, listed in §8) and the ar-SA integration failure `Hcm028AppealGovernanceMigrationTests` (culture-formatted SQL date).
- Pre-existing integration failures: `EstablishmentHireIntegrationTests.HirePartyCompensate…` (fails on main) and `ContractSeparationChainIntegrationTests.ActivateContract…` (missing `IHrCoreActiveRegimeClient` in the test DI scope).
- Single-row anonymize endpoints (`CandidateLifecycle` `citizen/anonymize`, `recruitment/anonymize`, DSAR `candidates/{id}/anonymize`) still anonymize one row and neither release documents nor neutralize notifications; the next weekly run completes the pair (half-anonymized pairs are due).
- `HrCoreDelete` is Admin-only, so no service identity can call the "service" JSON endpoints (pre-existing). `RecruitmentApplication.LegalHold` has no endpoint/UI (only `citizen/legal-hold`), and that endpoint takes no row lock: a hold arriving during anonymization lands on an already-anonymized row.
- `AdvisoryLeaderElection` sends `pg_advisory_unlock` with the caller's token (pre-existing).
- Notification sending is protected only by the leader lock (at-least-once; SMTP may duplicate after a lost answer). The CI acceptance job (Development compose, no bind mount) cannot read FileDrop files from the host. San001 offline NuGet cache lacks MailKit/MimeKit/BouncyCastle 2.7.0/Pkcs 10.0.13. HR views still show citizen phone/email unmasked (`PublicJobPostings/Details`, pre-existing).
- Acceptance seed `ReferenceDate` is 2026-08-01, so with `Apply=true` nothing seeded is due at 36 months: session 3 acceptance tests need back-dated data or a lower `Recruitment:Retention:Months`. `scripts/gov001-retention-dry-run.py` still hardcodes 1095 days (= 36 months).
- Partial-failure orphans: if file 1 uploads and file 2 fails, file 1 stays unreferenced `Pending` (and GC cannot remove it, see above). A body above 5,308,416 bytes at File.Storage, or above 10 MB at HR.Core, is refused before the action (English 400/413).

### Markers

`PLAN-30 S2` — **none remain** in code, scripts or docs (grep finds only the session 1 prompt and the session 1 handoff, which quote them historically). All session 1 S2 markers were resolved: `CandidateApplicationLegalLifecycleService.cs` ×3, `CitizenApplication.cs`, `RecruitmentApplication.cs`, `RecruitmentApplicationEvent.cs`, `RecruitmentApplicationEventRecorder.cs`, `PublicCareersController.cs`, `PublicCareers/Details.cshtml`.

`PLAN-30 S3` (grep, file:line):
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateRetentionService.cs:336` — OTP challenges: delete the pair's portal OTP rows during anonymization
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateRetentionService.cs:385` — OTP challenges: same for legacy citizen rows
- `src/Services/HRCore/HR.Core.Domain/Services/CandidateNotificationTemplates.cs:61` — links switch from `/careers/track` to `/careers/my`
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOptions.cs:142` — `PublicTrackUrl()` → `/careers/my`
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/UnifiedIntakeService.cs:270` — `ReevaluateDocumentFlagAsync` called when the candidate uploads missing documents
- `src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentApplicationsController.cs:118` — candidate uploads missing documents from `/careers/my`
- `src/Services/HRCore/HR.Core.Web/Controllers/PublicCareersController.cs:246` — `/careers/my` (OTP) candidate actions
- `src/Services/HRCore/HR.Core.Web/Views/PublicCareers/TrackResult.cshtml:148` — offer reply / withdraw / appeal from the view flags
- tests: `src/Tests/HRManagement.UnitTests/Hcm030CandidateDocumentTests.cs:236`, `:265` (S3 path comments)

Other PLAN-30 comments left outside scope: `RecruitmentOfferService.cs` «notify candidate», `ReserveListService.cs:133`, `RecruitmentRequisitionCascade.cs:81`, `RecruitmentAppealWorkflowHandler.cs:87` (the recorded events already drive the dispatcher).

### What session 3 must add

1. **OTP challenges in anonymization:** add the OTP entity, delete a pair's challenge rows in `CandidateRetentionService` at the two `PLAN-30 S3: OTP challenges` markers (same transaction), and send OTPs as in §4 (synchronous via `NotificationChannelSenderRegistry.For(channel)`, row recorded with the code redacted and a terminal status).
2. **`/careers/my` links in templates:** switch `RecruitmentOptions.PublicTrackUrl()` / `CandidateNotificationTemplates` to `{Recruitment:PublicPortalBaseUrl}/careers/my`; set `PublicPortalBaseUrl` per environment (Production/Staging empty today); update `Hcm030NotificationPiiGuardTests` link assertions.
3. **Candidate uploads of missing documents:** on `/careers/my`, validate with `CandidateDocumentFileValidator`, upload with `CandidateDocumentService.UploadAllAsync`, then `CandidateDocumentService.Stage` (explicit tenant on documents and outbox rows) + `UnifiedIntakeService.ReevaluateDocumentFlagAsync(app)` (reads through the `PublicCareersPortal` bypass scoped to `app.TenantId`) and **one** `SaveChangesAsync`; `UploadedBy = "Candidate"`.
4. Also: the candidate actions on `CandidatePublicView` (offer reply, withdraw, appeal), HR on-behalf acceptance, accessibility, the privacy register (candidate documents, notifications, retention run log), acceptance tests (FileDrop files under `artifacts/acceptance/candidate-notifications/`; back-dated data for retention), and the docs (`docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md` was not changed in session 2).
