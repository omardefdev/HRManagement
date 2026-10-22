# Task (Session 2 of 3): PLAN-30 Part 2 — Candidate documents via File.Storage, candidate notifications, automated retention

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) / MassTransit engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 2 of 3 for PLAN-30.** Session 1, on branch `feature/plan-30-candidate-experience`, implemented:

- unified intake: every requisition-linked public or internal application is a `RecruitmentApplication` at `Submitted`, with `IntakeChannel`;
- the `RecruitmentApplicationEvent` log (written in `HRCoreDbContext.SaveChanges*`);
- `CandidatePublicStatusProjection`;
- the backfill;
- profile fields and eligibility flags.

**This session implements:**

- **E** — candidate documents through File.Storage, with a server-to-server upload and per-requisition required types;
- **F** — candidate notifications: channel senders, consent, a durable notification table, the planner/dispatcher job, the 48-hour reminder and Arabic templates without PII;
- **G** — automated retention: a pair-aware anonymization job, a run log and an admin screen;
- unit and integration tests.

**Session 3 does the following. Do not do it here:**

- OTP, `/careers/my` and candidate actions
- HR on-behalf acceptance
- accessibility
- the privacy register
- acceptance tests and docs

Do not redo session 1 work. Fix it only if a test proves a defect, and report it.

## Read first (mandatory)

1. **`reports/plan30-session1-handoff.md`** — the source of truth, especially the event types and trigger rules (your input contract) and the projection shape.
2. `docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md` (Arabic), including the «تعديلات قبل التنفيذ» note. This prompt refines the plan. **Where they differ, this prompt wins.**
3. `docs/HCM_001_GAP_STUDY.md` — gaps **G-17**, **G-18**, **G-20**, and decision **D-8**.
4. Notifications:
   - `src/Shared/HRManagement.ServiceDefaults/Notifications/{INotificationService,InAppNotificationService}.cs`
   - `src/Shared/HRManagement.SharedKernel/Notifications/NotificationMessage.cs` (`NotificationChannel.Email/Sms` exist)
   - `docs/COM_001_NOTIFICATIONS.md`
   - `docs/EXT_007_NOTIFICATION_PII.md`
   - `docs/PROD_005_NOTIFICATIONS.md`
   - `scripts/com001-validate.ps1`
5. Files:
   - `src/Services/FileStorage/File.Storage.Web/Controllers/FilesApiController.cs` — `POST /api/files` requires `HrManagementPolicies.ModuleFileStorage` (an interactive user) and always uses `FileUploadConstraints.Document` (25 MB);
   - `File.Storage.Infrastructure/Services/FilePlatformService.cs` — upload to the quarantine bucket, the scan queue, and the GC of unreferenced `Pending` files;
   - `src/Shared/HRManagement.FileStorage/{FileUploadConstraints,FileUploadHelper,FileContentProbe,MalwareScanner}.cs`;
   - `src/Shared/HRManagement.ServiceDefaults/{FileReferenceClient,FileReferenceOutbox,FileReferenceOutboxDispatcher,EntityFileReferenceHelper,ServiceAuthorizationExtensions,ServiceIdentity}.cs`;
   - `HR.Core.Web/Controllers/DocumentsController.cs` — how HR registers a file reference today;
   - `docs/{FILE_SERVICE,FILE_SCANNING,SEC_003_FILE_AUTHORIZATION}.md`;
   - `src/Tests/HRManagement.AcceptanceTests/{Ac28FileScanTests,Ac30S2sFileReferenceRejectionTests}.cs`.
6. Retention:
   - `HR.Core.Domain/Services/CandidateApplicationLegalLifecycleService.cs`
   - `HR.Core.Web/Controllers/CandidateLifecycleController.cs`
   - `docs/{GOV_001_RETENTION_SCHEDULE,GOV_001_RETENTION_RUNBOOK}.md`
   - `scripts/gov001-retention-dry-run.py`
7. Jobs: `RecruitmentOfferExpiryJob`/`RecruitmentOfferExpirer` (the pattern, and the `// PLAN-30: reminder 48h before expiry` marker), `AdvisoryLeaderElection`. Lock keys in use: `8675311–8675313` plus whatever `RecruitmentReserveExpiryJob` uses — grep and pick the next free keys.
8. Licensing and supply chain: `docs/LICENSE_POLICY.md`, `docs/THIRD_PARTY_LICENSES.md`, `Directory.Packages.props` (central versions), `packages.lock.json` files, `scripts/check-license-policy.sh`.

## Context and scope boundaries

- **PLAN-25 is deferred.** No generic gate changes.
- **No production server and no SMTP/SMS provider exist yet (D-8 is open).**
  - Implement the adapters configurable and **off** in Production/Staging.
  - The lab and acceptance use the FileDrop adapter.
  - Never drop a message silently.
- **Out of scope:**
  - OTP and portal pages (session 3 — leave `// PLAN-30 S3:` markers);
  - notifications for internal (employee) candidates (they see status in ESS; marker);
  - interview **outcome** messages (internal assessment).
- Keep diffs minimal; Arabic UI; nonce CSP; no native `confirm()`.
- **Never commit:**
  - `global.json`
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets (SMTP/SMS credentials live only in user-secrets/env)
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

## Verified repository facts

- **Uploads from candidates.** The candidate is anonymous, so the browser cannot call `/api/files`. The upload must be relayed by HR.Core using its **service identity**.
  - `ServicePolicies` has no upload policy today.
  - Precedent for adding one: HCM-027's `ServicePolicies.PayrollGradeLookup` with scope `payroll.grades.read`, client `svc-hr-core`. Follow how that scope was provisioned in the Keycloak realm, `scripts/prepare-keycloak-import.ps1` and `scripts/sync-keycloak-dev-realm.ps1` (grep for `payroll.grades.read`).
- **References.** HR.Core already registers references through `AddFileReferenceOutbox<HRCoreDbContext>` (`IFileReferenceOutbox.EnqueueRegister/Release`, same transaction). The holder key format is `FileReferenceClientExtensions.BuildHolderKey(HrFileServices.HrCore, entityType, id, field)`. Registering makes the file `Active` and creates a module grant.
- **`INotificationService`** is an in-app inbox contract (`SendAsync(NotificationMessage)`, `GetUnreadAsync`, `MarkReadAsync`). Its only implementation is in-memory, and HR.Core does not register it.
  - `NotificationMessage.RecipientId` is a `Guid`, with no address.
  - External candidates have no user id. **Do not force email/SMS into this interface.** Add a channel-sender abstraction beside it and record the deviation in `COM_001_NOTIFICATIONS.md`.
- **Retention.**
  - `CandidateApplicationLegalLifecycleService.CanPurge(CitizenApplication)` returns false whenever `PromotedRecruitmentApplicationId` is set. After session 1 every new citizen row is linked, so citizen rows would never be anonymized.
  - `RetentionYears` is a constant `3`.
  - `TryAnonymize` clears only name, national ID, phone, email and cover note (citizen), and name, national ID, notes and appeal notes (recruitment). It does not clear the new profile fields (`BirthDate`, …), `RejectionNote` or `EligibilityOverrideReason`.
  - `CandidateLifecycleController.RetentionCandidates` loads **all** citizen rows into memory.
- **Offers.** `RecruitmentOfferExpirer.ExpireDueAsync` runs in `RecruitmentOfferExpiryJob` and carries the 48-hour reminder marker. The offer salary must **not** appear in any message (see F6).

## Stop and ask if

- adding the File.Storage service-upload policy requires changing an existing policy's semantics or another service's access;
- MailKit cannot be added without breaking locked restore or the license scripts, **and** the BCL `SmtpClient` is unacceptable for a reason you find;
- any migration would rewrite or delete business data;
- the branch state does not match the session 1 handoff.

---

## Step 0 — Verify session 1 state

1. `git checkout -- testdata`. Check out `feature/plan-30-candidate-experience` at the handoff SHA or a descendant, clean apart from local noise.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm030|CandidatePublicStatusProjection|RecruitmentEligibility|Hcm02|PublicCareers"
   ```

   It must match the handoff. Otherwise stop and report.

---

## Workstream E — Candidate documents (G-17)

**E1. File.Storage service upload**

- New `ServiceScopes.FileStorageServiceUpload` (for example `file-storage.service-upload`) and `ServicePolicies.FileStorageServiceUpload`:
  - service account
  - scope required
  - allowed client `svc-hr-core` only
- Provision the scope like `payroll.grades.read`.
- New endpoint `POST /api/files/service-upload` (`[RequestSizeLimit]` matching the constraint). It accepts `file` plus `constraintProfile` (only `CandidateDocument` for now) and calls the same `FilePlatformService.UploadAsync`, so the file goes through quarantine and the scan queue.
- Add `FileUploadConstraints.CandidateDocument`: 5 MB; `.pdf`, `.jpg`, `.jpeg`, `.png`; matching content types. Enforce it **in File.Storage too**, not only in HR.Core.
- Grant holder: `Service` / `svc-hr-core`. HR users get access later through the module grant created on reference registration (as with employee documents).
- Negative test: another client or a missing scope gets 403 (pattern `Ac30S2sFileReferenceRejectionTests`).

**E2. HR.Core client.** `CandidateDocumentUploadClient`, a named service-authenticated `HttpClient` like the existing `"FileStorage"` client.

- Validate locally first: count, size, extension, and magic bytes with `FileContentProbe` if it fits.
- Upload, and return the `FileId`.
- On File.Storage errors, return an Arabic error and never a 500.

**E3. Entity `CandidateDocument`** (migration `HCM030_CandidateDocuments`), tenant-scoped:

| Column | Notes |
|--------|-------|
| `Id` | |
| `TenantId` | |
| `RecruitmentApplicationId` | |
| `CitizenApplicationId?` | |
| `DocumentType` | string: `Cv` «سيرة ذاتية», `Certificate` «شهادة», `IdCopy` «صورة الهوية», `Other` «أخرى» |
| `FileId` | |
| `OriginalFileName` | sanitized with `FileUploadHelper.SanitizeFileName` |
| `ContentType` | |
| `SizeBytes` | |
| `UploadedAt` | |
| `UploadedBy` | `"Candidate"` or the HR user id |

- Register the reference with `IFileReferenceOutbox.EnqueueRegister(fileId, BuildHolderKey(HrFileServices.HrCore, nameof(CandidateDocument), id, nameof(CandidateDocument.FileId)))` **in the same save** as the entity.
- Limits: `Recruitment:CandidateDocuments:MaxFiles` (5) and `MaxBytes` (5 MB).

**E4. Required types**

- `RecruitmentRequisition.RequiredDocumentTypes` (string list), editable while the requisition is editable, shown on the public posting.
- Missing required types add the eligibility flag `MissingRequiredDocuments` «مستندات مطلوبة ناقصة». **Not a rejection.** Re-evaluate the flags when documents change.

**E5. Apply flow**

- The apply form becomes `multipart/form-data`, with one input per required type plus optional extra files, and accessible labels.
- Upload first and collect the `FileId`s. Then **one** `SaveChangesAsync` creates the citizen row, the recruitment application, the documents and the outbox rows.
- If the save fails, the uploaded files stay unreferenced `Pending` and File.Storage's garbage collector removes them. Verify the cutoff in `CollectGarbageAsync` and document it.
- If File.Storage is unreachable, the whole apply fails with «تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.» and nothing is saved. Report this choice.

**E6. HR view**

- `RecruitmentApplications/Details` gets a «المستندات» card: type, name, size, upload time, and scan status (batch metadata from File.Storage; reuse an existing metadata client if there is one).
- Download goes through the existing gateway files route, for `Clean` files only. Infected or quarantined files show «محجور — غير متاح» and no link.
- `Index` shows a completeness badge.
- Leave `// PLAN-30 S3: candidate uploads missing documents from /careers/my`.

---

## Workstream F — Candidate notifications (G-18, D-8)

**F1. Channel senders** (`src/Shared/HRManagement.ServiceDefaults/Notifications/`)

- `INotificationChannelSender { NotificationChannel Channel { get; } Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct); }`, where `OutboundNotification` carries recipient address, subject, body, idempotency key and correlation id.
- **Senders:**
  - `SmtpEmailNotificationSender` — prefer **MailKit** (MIT, allowed by `LICENSE_POLICY.md`). Pin it in `Directory.Packages.props`, update the affected `packages.lock.json`, add it to `THIRD_PARTY_LICENSES.md`, and run the license script. If that is not possible, use the BCL `System.Net.Mail.SmtpClient`. Report the choice.
  - `HttpSmsNotificationSender` — a generic HTTP POST: configurable URL, auth header from secret config, JSON body template, timeout, and Polly resilience if it is already used (`HttpClientResilienceExtensions`).
  - `FileDropNotificationSender` — lab only. It writes one UTF-8 file per message, named by a safe hash of the idempotency key, so a rewrite is harmless. It refuses to run in Production.
- **Options** `Notifications:Candidate`:
  - `Provider` (`FileDrop` | `Smtp+Sms` | `None`)
  - `FileDrop:Path`
  - `Smtp:{Host,Port,UseStartTls,From,User}` (password from secret config)
  - `Sms:{Endpoint,SenderId,AuthHeaderName}` (token from secret config)
- **Defaults:**
  - `FileDrop` in Development and Acceptance;
  - `None` in `appsettings.Production.json` / `appsettings.Staging.json` until D-8 is provisioned.
  - With `None`, rows stay `Pending` until they expire, and a warning metric/log is emitted. They are never marked sent.
- Do **not** change `INotificationService`. Document the new abstraction in `COM_001_NOTIFICATIONS.md`.

**F2. Contact and consent** (citizen channel)

- The apply form requires at least one contact (phone or email) for new applications. Validate formats; normalize the phone with an option-driven country code.
- Add preferred channel (`Email`/`Sms`) and a consent checkbox «أوافق على تلقي إشعارات حول طلبي عبر القناة المختارة».
- Store `NotificationChannel` and `NotificationConsentAt` on `CitizenApplication` (in the notifications migration).
- No consent means no status notifications. Session 3's OTP is a user-requested transactional message and does not need this consent.

**F3. Entity `CandidateNotification`** (migration `HCM030_CandidateNotifications`), tenant-scoped:

| Column | Notes |
|--------|-------|
| `Id`, `TenantId` | |
| `RecruitmentApplicationId` | |
| `EventId?` | |
| `EventType` | |
| `Channel` | string |
| `TemplateCode` | |
| `RecipientMasked` | **masked only** — the real address is read from the citizen row at send time |
| `Subject`, `Body` | rendered |
| `IdempotencyKey` | |
| `Status` | `Pending`/`Sent`/`Failed`/`Expired`/`Suppressed` |
| `Attempts`, `NextAttemptAt` | |
| `LastError` | sanitized |
| `SentAt`, `ExpiresAt` | |
| `ProviderMessageId?` | |

Unique index `(TenantId, IdempotencyKey)`.

**F4. `CandidateNotificationJob`**

- `BackgroundService` with leader election.
- Options:
  - `Recruitment:Jobs:CandidateNotificationsEnabled` (true)
  - `…IntervalSeconds` (60)
  - `Notifications:Candidate:MaxAttempts` (5), with backoff
- Disabled in unit/integration hosts unless a test enables it. The core logic lives in a testable `CandidateNotificationDispatcher`.
- **Stage 1 — plan.** For each `RecruitmentApplicationEvent` with `NotificationsPlannedAt IS NULL`, across tenants (`IgnoreTenantFilterForSystem(BackgroundQueueProcessing)`; set `TenantId` explicitly):
  - if the application has a citizen channel row with consent and a contact, render and insert one row per channel with key `{eventId}:{channel}`;
  - mark the event planned;
  - events with no message (see the table) are just marked.
- **Stage 2 — send.** For due `Pending` rows: past `ExpiresAt` → `Expired`; otherwise resolve the address, send, then mark `Sent` or reschedule / `Failed`. Audit each outcome in `EntityAuditLog` (`CandidateNotificationSent`/`Failed`) with no address and no body.
- Delivery is at-least-once. FileDrop is idempotent by file name; document SMTP's limits.

**Event → message map:**

| Event (session 1) | Template | Content (Arabic, short) | Expires |
|-------------------|----------|-------------------------|---------|
| `Received` (channel `Public`) | `APP_RECEIVED` | reference number + track link | +30 days |
| `InterviewScheduled` / `InterviewRescheduled` / `InterviewCancelled` | `INTERVIEW_*` | date/time (local), place or «عن بُعد»; no panel names | interview time |
| `OfferIssued` | `OFFER_ISSUED` | «صدر لك عرض وظيفي، يرجى الاطلاع والرد عبر البوابة قبل {date}» — **no salary** | offer expiry |
| `OfferExpiryReminder` (F5) | `OFFER_REMINDER` | reminder + deadline | offer expiry |
| `StatusChanged` → `Rejected` | `APP_REJECTED` | «لم يتم قبول طلبك»; the appeal deadline only when appealable (use the session 1 projection) | +appeal window |
| `StatusChanged` → `ReserveListed` | `APP_RESERVE` | reserve-until date | reserve date |
| `OfferAccepted` | `OFFER_ACCEPTED` | receipt and next steps | +30 days |
| `StatusChanged` → `Hired` | `APP_HIRED` | «تم استكمال إجراءات التعيين، ستتواصل معك الجهة بشأن المباشرة» | +30 days |
| `AppealDecided` | `APPEAL_DECIDED` | the outcome label only | +30 days |
| `StatusChanged` → `Withdrawn` | `APP_WITHDRAWN` | confirmation | +7 days |
| others | — | no message | — |

**F5. 48-hour reminder.** In `RecruitmentOfferExpirer` (resolve the marker), or as a pass of the dispatcher (choose one and report): for `Issued` offers whose `ExpiryDate` is within `Recruitment:OfferReminderHours` (48) and that have no reminder event yet, insert an `OfferExpiryReminder` event, so there is one per offer.

**F6. Templates and PII guard**

- Arabic templates in the HR.Core localization resources, or one template class if the repo has no resource convention for this (report which). Links use `Recruitment:PublicPortalBaseUrl`; session 3 switches them to `/careers/my`.
- **Hard rules:** never the national ID, salary, birth date, scores, rank, committee data or internal notes. SMS stays one short message.
- `Hcm030NotificationPiiGuardTests` renders every template with sample data containing those values and asserts that none appear.

**F7. HR visibility.** On `RecruitmentApplications/Details`, a «الإشعارات» card lists event, channel, status, masked recipient and time. The interview-outcome marker (`RecruitmentInterviewService.CompleteAsync`) becomes a comment: the outcome is internal, and the candidate is notified at the decision points.

---

## Workstream G — Automated retention (part of G-20)

**G1. Pair-aware eligibility.** `CandidateRetentionService` (Infrastructure). An application **pair** — the `RecruitmentApplication` and its linked citizen or internal channel row — is due when:

- the recruitment application is not hired (`HiredEmployeeId` null);
- neither row has `LegalHold`;
- there is no `Pending` appeal;
- it is not already anonymized;
- the trigger date + `Recruitment:Retention:Months` (default 36, matching GOV-001) is in the past.
  - Trigger date = requisition `ClosedDate` ?? `CancelledAt` ?? the application's `AppliedAt`.
  - Open requisitions never trigger.

Legacy unlinked citizen rows keep today's rule. Replace the constant with the option. Keep `CanPurge` signatures for existing callers, or adapt them and list each change.

**G2. Anonymization scope.** Extend `TryAnonymize` (both overloads) or wrap them to also clear:

- the session 1 profile fields (`BirthDate`, `Specialization`, `GraduationYear`, `YearsOfExperience`, `EducationLevelCode`);
- `RejectionNote`, `EligibilityOverrideReason`;
- the contact and consent fields.

Release every `CandidateDocument` file through `IFileReferenceOutbox.EnqueueRelease`, then delete the document rows. Neutralize `CandidateNotification.Body`/`Subject`/`RecipientMasked`. Leave `// PLAN-30 S3: OTP challenges`. Everything happens in **one** transaction per pair.

**G3. `CandidateRetentionJob`**

- Weekly, leader election, batched (100 pairs per batch).
- Options:
  - `Recruitment:Jobs:CandidateRetentionEnabled` (true)
  - `Recruitment:Retention:Apply` — default **false**, so it is a dry run that only records counts. Set it to `true` in the acceptance profile. Production stays false until the data protection officer approves; document this in the runbook.
- **Run log** `CandidateRetentionRun` (in the retention migration `HCM030_CandidateRetention`):

  | Column | |
  |--------|---|
  | `Id`, `TenantId?` | |
  | `StartedAt`, `FinishedAt` | |
  | `DryRun` | |
  | `DueCount`, `AnonymizedCount`, `SkippedLegalHold`, `SkippedAppeal` | |
  | `TriggeredBy` | `Job` or the user id |
  | `Error?` | |

**G4. Admin screen** `CandidateLifecycle/Index` (`HrCoreDelete`):

- the paged due list (reference, requisition, trigger date, reason it is due — no PII beyond the reference);
- «تشغيل تجريبي» (dry run now);
- «تنفيذ الإخفاء» — only when `Retention:Apply` is true, with an in-page confirmation (no native `confirm()`);
- the run history.

Keep the existing JSON endpoints for services. Fix `RetentionCandidates` to page and to use the pair rule.

---

## Tests

**Unit**

| Class | Covers |
|-------|--------|
| `Hcm030CandidateDocumentTests` | count/size/type limits; required types → flag; documents and outbox register rows saved in one transaction; the apply fails cleanly when the upload fails; HR card hides non-Clean downloads |
| `Hcm030NotificationTests` | planner map rows; no consent → nothing; `None` provider → stays Pending then Expired; retry/backoff → Failed; idempotency (re-planning the same event or re-running the dispatcher → one row/message — **AC-HCM-30e negative**); reminder once per offer; Draft/PendingApproval offers produce no offer message |
| `Hcm030NotificationPiiGuardTests` | see F6 |
| `Hcm030RetentionJobTests` | pair due/not due; LegalHold on either row untouched (**AC-HCM-30i negative**); pending appeal untouched; open requisition untouched; dry run changes nothing and logs a run; apply clears every listed field, releases files and neutralizes notifications |
| `Fs030ServiceUploadPolicyTests` | the File.Storage policy (allowed client and scope vs. others); constraint enforced server-side |

**Integration (PostgreSQL)**

- The unique `(TenantId, IdempotencyKey)` holds under two concurrent dispatchers.
- Retention runs on a real database (batched).
- Use `Skip.If` when Docker is unavailable.

**Regression** — green or matching the handoff:
- `Hcm030`, `Hcm02`, `PublicCareers`, `Com001`, `FileStorage`/`Ac28`-related unit tests
- `CandidateLifecycle`, `Dsar`

List every modified test with a justification.

## Commit plan

6. `feat(fs030): service-to-service candidate document upload policy in File.Storage`
7. `feat(hcm030): candidate documents with required types and HR document card`
8. `feat(com030): candidate notification channels (SMTP, HTTP SMS, FileDrop), consent and templates`
9. `feat(hcm030): candidate notification planner/dispatcher job and 48h offer reminder`
10. `feat(hcm030): pair-aware candidate retention job, run log and admin screen`
11. `docs(hcm030): session 2 handoff`

Every commit builds. Tests are green at 7–11.

## Definition of done (session 2)

- Candidates attach CV, certificates and ID copy within the limits, through File.Storage with scanning; infected files never reach HR.
- Every decision point produces an audited notification for consenting citizen candidates. Messages carry no national ID or salary, duplicates are impossible, and the lab adapter lets tests read the messages.
- Retention is automated, pair-aware, dry-run by default, respects legal hold and pending appeals, and logs its runs.
- Build has 0 errors; `has-pending-model-changes` reports none for HR.Core and File.Storage; tests pass.

## Handoff (mandatory last step)

Write and commit `reports/plan30-session2-handoff.md` with:

1. Commits.
2. Migrations (HR.Core, File.Storage) and columns.
3. **The File.Storage service-upload contract** (route, scope, client, constraint) and the Keycloak provisioning steps (re-import needed?).
4. **`INotificationChannelSender` / `OutboundNotification`** and how session 3 sends an **immediate** OTP message through them (synchronously, bypassing the queue, recording the row with the code redacted).
5. The FileDrop path and file format, and how acceptance tests (running on the host against the docker stack) can read it: a bind mount in `docker-compose.acceptance.yml` or an acceptance-only read endpoint — choose one, implement it, and document it.
6. Templates, the event→message map, and options per environment.
7. The retention rule, options and run log.
8. Test results; modified tests.
9. Deviations, known issues, markers (file:line), and what session 3 must add (OTP challenges in anonymization, `/careers/my` links in templates, candidate uploads of missing documents).

Then reply with a short summary.
