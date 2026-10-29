# PLAN-30 — Final report (sessions 1–3)

**Date:** 2026-10-29
**Branch:** `feature/plan-30-candidate-experience` (local only, not pushed), from `main` @ `55fbcdd`.
**Plan:** [`docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md`](../docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md) —
Status **Implemented (acceptance evidence pending)**. Execution guide:
[`docs/acceptance-plans/execution/PLAN-30-EXECUTION.md`](../docs/acceptance-plans/execution/PLAN-30-EXECUTION.md).
Earlier handoffs: [`reports/plan30-session1-handoff.md`](plan30-session1-handoff.md), [`reports/plan30-session2-handoff.md`](plan30-session2-handoff.md).

Commits 16 (K), 17 (L) and 18 (M) are 76fe409, a1b9ae1 and 7af0c79; the orchestrator committed them after this report was written,
and they include the review fixes listed in section 6.

## 1. Summary per workstream

### Session 1 (verified against `reports/plan30-session1-handoff.md` and the code)

- **A — Unified intake.** `UnifiedIntakeService` creates a `RecruitmentApplication` at `Submitted` for every
  requisition-linked public (`PublicCareersController.Apply`, one save) and internal (`EssTalentController.Apply`, one
  transaction, two saves) application, links it through the existing `PromotedRecruitmentApplicationId` (now unique,
  filtered) and stamps `IntakeChannel` (`HrEntered`/`Public`/`Internal`/`Legacy`). Cross-channel duplicates are refused;
  linked channel rows are managed only from the unified path («يُدار هذا الطلب من مسار التوظيف الموحد.»).
- **B — Events and public status.** `RecruitmentApplicationEvent` (append-only, no PII, same save) and the pure
  `CandidatePublicStatusProjection` («قيد المراجعة», «مدعو للمقابلة», «قيد التقييم النهائي», «عرض بانتظار ردك حتى …»,
  «مقبول — بانتظار استكمال التعيين», «في قائمة الاحتياط حتى …», «العرض منتهٍ/مرفوض», «غير مقبول», «منسحب»); no hire step is
  shown before the hire.
- **C — Backfill.** `scripts/hcm030-backfill-unified-intake.ps1` + `scripts/sql/hcm030-backfill-unified-intake.sql`
  (dry run default, idempotent, no events); C# mapping `UnifiedIntakeStatusMapping` pinned to the SQL on PostgreSQL.
- **D — Profile and eligibility.** Profile columns on both rows, requisition criteria, `RecruitmentEligibilityRules`
  flags that never reject; shortlisting a flagged application needs a reason and writes `EligibilityOverride`.

### Session 2 (verified against `reports/plan30-session2-handoff.md` and the code)

- **E — Documents (FS-030 + HCM-030).** File.Storage `FilesServiceUploadController` (`api/files/service-upload`,
  `metadata`, Clean-only `content`) under the new S2S policy (`file-storage.service-upload`, client `svc-hr-core` only);
  `CandidateDocumentUploadClient` calls it directly with the service token and caller proof; `CandidateDocument` entity,
  required document types, the «المستندات» card and the HR download proxy. Any upload failure fails the whole apply with
  «تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.».
- **F — Notifications (COM-030).** `INotificationChannelSender` (SMTP via MailKit 4.18.1, generic HTTP SMS, lab FileDrop)
  beside the unchanged `INotificationService`; consent «أوافق على تلقي إشعارات حول طلبي عبر القناة المختارة»;
  `CandidateNotification` rows; `CandidateNotificationDispatcher`/`CandidateNotificationJob` (lock 8675315) with the 48 h
  offer reminder; Arabic PII-free templates; «الإشعارات» card.
- **G — Retention.** Pair rule, `CandidateRetentionService`/`CandidateRetentionJob` (lock 8675316, weekly, dry run by
  default), `CandidateRetentionRun` log per tenant, admin screen «الاحتفاظ ببيانات المرشحين» (`CandidateLifecycle/Index`).

### Session 3

- **H — OTP and session (commit `429e5b0`).** `CandidateOtpChallenge` (HMAC only, keyed IP hash), `CandidateOtpService`
  (neutral answer «إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.», silent refusals, single active
  challenge, immediate send through the channel senders with a redacted terminal `CandidateNotification`, constant-time
  verify, 5 wrong codes → 30-minute lock audited `CandidatePortalLocked`), `CandidateOtpKeyRing` + Production startup
  validator, `CareersClientIpResolver`, chained `careers-otp` limiter with an Arabic 429 page, `CandidatePortalSession`
  (`careers_session`), `/careers/my/login`, code entry and logout. Migration `HCM030_CandidatePortal`.
- **H5 — Gateway (commit `f910a77`).** `GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader` forwards the exact
  cookie `careers_session` only on route `careers-public`; the transform also stops attaching a staff access token on
  that route. Other routes unchanged; unit tests for both routes, look-alike names and antiforgery forwarding.
- **I — `/careers/my` (commit `d3080c4`).** `CandidatePortalController` + `CandidatePortalService` («طلبي»: summary,
  timeline, interviews, documents with upload of missing ones, offer card with terms, «تنزيل خطاب العرض», accept with
  «أقرّ بأنني اطلعت على شروط العرض وأقبلها» + typed full name, decline with the HCM-027 next-offer behaviour, withdraw with
  an in-page «تأكيد الانسحاب» panel, appeal and information answers). `OfferLetter.AcceptedBy`/`AcceptanceNote`/
  `AcceptanceEvidenceFileId`, `AcceptAsync` channel overload, `RecruitmentSelectionService.WithdrawByCandidateAsync`,
  `RecruitmentOfferService.DeclineAndDraftNextAsync`. Notification links → `/careers/my`; `/careers/track` shows only the
  status label. Migrations `HCM030_OfferAcceptance`, `HCM030_OfferDeclineEvidence`.
- **J — HR on-behalf acceptance (commit `da1e61b`).** `OfferOnBehalfService`: «قبول نيابةً عن المرشح» requires a reason
  ≥ 20 characters and a signed evidence file (relayed server-side, scanned), audited `AcceptOfferOnBehalf`; optional decline
  evidence; Clean-only `OfferLetters/Evidence` download; `data-hr-confirm` instead of native `confirm()`; `HrCoreWrite` kept.
- **K — Privacy and accessibility (commit 16, 76fe409).** Retention deletes OTP challenges, clears the portal lock and last
  request time, releases on-behalf offer evidence and neutralizes the candidate free text of offers and appeals
  (`AcceptanceNote`, `DeclineReason`, `RevokeReason`, appeal `Reason`/`AdditionalInfo` → «—»); pairs with such residue
  become due again. DSAR candidate export gains
  profile, channel, flags, document metadata, masked message log, offers without salary, and OTP counts only.
  `docs/PRV_001_DPIA_RECRUITMENT.md` and three ROPA activities (`pa-hcm-candidate-otp`, `pa-hcm-candidate-documents`,
  `pa-hcm-candidate-notifications`). `hcm006-a11y-scan.ps1` scans 10 views; all violations fixed (error summaries,
  `aria-describedby`/`aria-invalid`, OTP `one-time-code`/`numeric`, no inline styles/handlers, icons `aria-hidden`).
- **L — Acceptance (commit 17, a1b9ae1).** `AcHcm30CandidateExperienceTests` (8 tests for AC-HCM-30a..j),
  `AcceptanceCandidateExperiencePatch` fixtures, `-DockerContainer` on the backfill script, acceptance OTP limits raised,
  `prepare-keycloak-import.ps1` Windows PowerShell 5.1 fix. Live run: 7/8 pass; 30c skips its File.Storage steps when
  File.Storage is absent (§7).
- **M — Script and docs (commit 18, 7af0c79).** `scripts/hcm030-validate.ps1`; `PLAN-30-EXECUTION.md`; plan status and
  "Implementation notes / deviations"; `HCM_001_RECRUITMENT.md` (PLAN-30 section, G-16/G-17/G-18 and the PLAN-30 part of
  G-20 addressed — also in `HCM_001_GAP_STUDY.md`), `COM_001_NOTIFICATIONS.md` (OTP and providers per environment),
  `GOV_001_RETENTION_SCHEDULE.md` + `GOV_001_RETENTION_RUNBOOK.md` (OTP challenges and evidence files in the pair rule),
  `HIRE_ONBOARDING_GUIDE.md` (HR side), `FILE_SERVICE.md` + `SEC_003_FILE_AUTHORIZATION.md` (service upload and HR proxies),
  `MESSAGE_EVENT_MATRIX.md` (§5a internal application events → notifications), `DATABASE_SCHEMA_INVENTORY.md/.json`
  (hand-edited: Python is not installed, so `scripts/extract_database_inventory.py` was not run).

## 2. Commits of all three sessions

| # | SHA | Message |
|---|-----|---------|
| 1 | `0d58720` | feat(hcm030): unified intake — public and internal applications enter the recruitment path at Submitted |
| 2 | `f219fed` | feat(hcm030): application event log and candidate public status projection |
| 3 | `9f73536` | feat(hcm030): backfill of unlinked channel applications (script + SQL + tests) |
| 4 | `acf9e7d` | feat(hcm030): application profile, requisition criteria and eligibility flags |
| 5 | `4977aac` | docs(hcm030): session 1 handoff |
| extra | `2264e0e` | test(hcm030): parse eligibility age-bound dates culture-invariantly |
| 6 | `c79f626` | feat(fs030): service-to-service candidate document upload policy in File.Storage |
| 7 | `2283eb3` | feat(hcm030): candidate documents with required types and HR document card |
| 8 | `cafdee4` | feat(com030): candidate notification channels (SMTP, HTTP SMS, FileDrop), consent and templates |
| 9 | `e25e81b` | feat(hcm030): candidate notification planner/dispatcher job and 48h offer reminder |
| 10 | `f02ce47` | feat(hcm030): pair-aware candidate retention job, run log and admin screen |
| 11 | `7af0bf5` | docs(hcm030): session 2 handoff |
| 12 | `429e5b0` | feat(hcm030): candidate OTP challenge, careers session and per-client OTP rate limit |
| 13 | `f910a77` | feat(gw030): forward the careers session cookie on the careers route only |
| 14 | `d3080c4` | feat(hcm030): /careers/my — status, timeline, interviews, documents, offer PDF, accept/decline, withdraw, appeal |
| 15 | `da1e61b` | feat(hcm030): HR on-behalf offer acceptance requires reason and evidence |
| 16 | 76fe409 | feat(hcm030): privacy (DSAR, ROPA, OTP anonymization) and accessibility scan for candidate pages |
| 17 | a1b9ae1 | test(hcm030): integration + acceptance AC-HCM-30a..j |
| 18 | 7af0c79 | docs(hcm030): plan status, execution guide, notifications/retention/file docs, validate script |

## 3. Migrations per service and data steps

**HR.Core** (all additive; `has-pending-model-changes` → "No changes have been made to the model since the last migration."):

| Migration | Change |
|-----------|--------|
| `20261022231545_HCM030_UnifiedIntake` | `RecruitmentApplications.IntakeChannel` (default `HrEntered`; Postgres-only fill of the new column); unique filtered `IX_*_PromotedRecruitmentApplicationId` on both channel tables (a `DO` block raises `HCM030: duplicate …` and repairs nothing); new `RecruitmentApplicationEvents` |
| `20261022234315_HCM030_ApplicationProfile` | profile columns on `CitizenApplications`/`RecruitmentApplications`; `EligibilityFlags`, `EligibilityEvaluatedAt`, `EligibilityOverrideReason`; requisition criteria |
| `20261028225233_HCM030_CandidateDocuments` | new `CandidateDocuments`; `RecruitmentRequisitions.RequiredDocumentTypes` |
| `20261028233040_HCM030_CandidateNotifications` | new `CandidateNotifications`; `CitizenApplications.NotificationChannel`, `NotificationConsentAt` |
| `20261029002527_HCM030_CandidateRetention` | new `CandidateRetentionRuns` |
| `20261029013756_HCM030_CandidatePortal` | new `CandidateOtpChallenges`; `CitizenApplications.PortalLockedUntil`, `PortalOtpRequestedAt` |
| `20261029022556_HCM030_OfferAcceptance` | `OfferLetters.AcceptedBy`, `AcceptanceNote`, `AcceptanceEvidenceFileId` |
| `20261029024505_HCM030_OfferDeclineEvidence` | `OfferLetters.DeclineEvidenceFileId` |

**File.Storage:** none (no entity change; `has-pending-model-changes` → no changes). **Gateway/other services:** none.

**Data step — backfill** (after the migrations, on each environment, dry run first):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/hcm030-backfill-unified-intake.ps1                                   # dry run (ROLLBACK)
powershell -ExecutionPolicy Bypass -File scripts/hcm030-backfill-unified-intake.ps1 -Apply -ConnectionString "Host=…"  # host psql
powershell -ExecutionPolicy Bypass -File scripts/hcm030-backfill-unified-intake.ps1 -Apply -DockerContainer hrm-acceptance-postgresql-1   # psql in the container (-Database hr_core_db, -DbUser hrm_ci)
```

CSV output in `artifacts/hcm030-backfill/`; a second `-Apply` links 0; no events and no messages for historic rows.

## 4. Configuration keys and defaults per environment

| Key | Default | Development | Acceptance (compose) | Staging | Production |
|-----|---------|-------------|----------------------|---------|------------|
| `Recruitment:Eligibility:QualificationRanks` | `DIP=1, BACH=2, MASTER=3, PHD=4` | | | | |
| `Recruitment:Eligibility:AgeBoundsByRegime` | empty | | | | empty (no legal values) |
| `Recruitment:CandidateDocuments:MaxFiles` / `MaxBytes` / `MaxTotalBytes` | 5 / 5,242,880 / 9,437,184 | | | | |
| `Integration:FileStorageBaseUrl` | empty (uploads fail closed) | empty | `http://file-storage-web:8080` (also local compose) | — | not set |
| HR.Core `ServiceIdentity:Scope` | `file-storage.references file-storage.service-upload` | | + `payroll.grades.read` (compose) | | production compose: `file-storage.references` only |
| `Notifications:Candidate:Provider` | `None` | `FileDrop` | `FileDrop` | `None` | `None` (D-8) |
| `Notifications:Candidate:FileDrop:Path` | — | `artifacts/candidate-notifications` | `/var/hrm/candidate-notifications` (bind mount) | — | refused |
| `Notifications:Candidate:MaxAttempts` | 5 | | | | |
| `Notifications:Candidate:Smtp:*`, `Sms:*` (secrets `Smtp:Password`, `Sms:AuthToken` env only) | — | | | | at D-8 |
| `Recruitment:PublicPortalBaseUrl` | empty | `http://localhost:5000` | `http://localhost:6200` | empty | empty |
| `Recruitment:Portal:PhoneCountryCode` | `963` | | | | |
| `Recruitment:Jobs:CandidateNotificationsEnabled` / `…IntervalSeconds` | true / 60 | | | | |
| `Recruitment:OfferReminderHours` | 48 | | | | |
| `Recruitment:Retention:Months` / `Apply` / `BatchSize` | 36 / false / 100 | | `Apply=true` | `Apply=false` | `Apply=false` |
| `Recruitment:Jobs:CandidateRetentionEnabled` / `…IntervalMinutes` | true / 10080 | | | | |
| `Recruitment:Portal:OtpHmacKey` (env `Recruitment__Portal__OtpHmacKey`, ≥ 32 chars) | random per-process key + warning | | (random) | | **required** — `HR_CORE_OTP_HMAC_KEY` in `docker-compose.production.yml`, k8s secret `/hrmanagement/production/hr-core/otp-hmac`; `.env.example`/`.env.ci` entries |
| `Recruitment:Portal:OtpResendSeconds` / `OtpValidityMinutes` | 60 / 10 | | | | |
| `Recruitment:Portal:OtpMaxAttempts` / `LockMinutes` / `SessionMinutes` | 5 / 30 / 30 | | | | |
| `Recruitment:Portal:OtpRequestsPerIpPer10Min` / `OtpVerifyPerIpPer10Min` | 5 / 20 | | 60 / 120 | | |

Integration test hosts set `Recruitment:Jobs:CandidateNotificationsEnabled=false` and `CandidateRetentionEnabled=false`.

## 5. Security notes

- **OTP design.** 6-digit code from a CSPRNG; stored only as `HMACSHA256(key, "{challengeId:D}:{code}")` (hex, 64 chars);
  the key never sits in the database (`Recruitment:Portal:OtpHmacKey`, Production fails closed at startup). Verification
  always computes the HMAC and compares with `CryptographicOperations.FixedTimeEquals`. One active challenge per citizen
  row; 10-minute validity; success consumes it. The request IP is stored only as a keyed hash (`HMAC(key, "ip:" + ip)`).
  `CodeHmac`/`RequestIp` are `[AuditRedact]`. The FileDrop copy holds the plain code (lab only); the `CandidateNotification`
  row stores «******» with a terminal status, so the dispatcher never re-sends it.
- **Enumeration resistance.** Every request answers «إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.»
  whether the reference/national id match, the portal is locked, no contact exists, the resend window (60 s) applies, or an
  exception occurred (logged without PII). Lookup by reference (globally unique) + `NationalIdHash`, through the
  `PublicCareersPortal` bypass with the tenant taken from the row.
- **Lockout.** 5 wrong codes within `LockMinutes` → `PortalLockedUntil = now + 30 min`, audited `CandidatePortalLocked`
  (no PII); attempts are counted atomically (`Hcm030CandidateOtpConcurrencyTests` on PostgreSQL). While locked even the right
  code is refused («تم إيقاف الدخول مؤقتاً بسبب تكرار إدخال رمز خاطئ. يرجى المحاولة لاحقاً.»).
- **Rate limits.** One chained global limiter: the shared `public-careers` window (30/min, all visitors) for `Apply`,
  `Track` and the OTP endpoints, plus a per-client 10-minute window on `careers-otp` (5 requests / 20 checks). The client
  address comes from `CareersClientIpResolver` (right-most `X-Forwarded-For` only from a trusted proxy; `UseForwardedHeaders`
  is not enabled globally). Arabic 429 page without script or inline style.
- **Cookie flags.** `careers_session`: HttpOnly, SameSite=Strict, path = request path base (`/careers`), Secure always in
  Production/Staging (same-as-request elsewhere, because acceptance runs on http://localhost:6200), Data Protection purpose
  `HR.Core.CareersSession.v1`, absolute 30-minute expiry inside the payload (not sliding); anything that does not unprotect,
  has another version or is expired is no session. «خروج» deletes it. No TempData on the portal.
- **Gateway allow-list.** Only the exact name `careers_session`, only on route `careers-public` (`/careers/{**catch-all}`),
  next to the antiforgery cookies; every other route drops it. On that route the gateway also sends no staff access token
  (and clears a client `Authorization` header), so an HR user's `tenant_id` claim cannot override the session tenant.
  YARP passes `Set-Cookie` back unchanged (verified live by the acceptance run).
- **IDOR.** Every portal action resolves the citizen row, the recruitment application, the active offer and the pending
  appeal from the session ids (checked against the database), never from a route, query or form id; existing services run
  with the session tenant as the ambient tenant (`TenantIdFlow`, restored afterwards), so a client `X-Tenant-ID` cannot
  steer them. Antiforgery on every POST. No PII in URLs; the code form carries the reference, not the national id.
- **HR on-behalf acceptance.** `HrCoreWrite` kept (narrower than `HrCoreApprove`); mandatory reason ≥ 20 and evidence file;
  audit `AcceptOfferOnBehalf` with reason and evidence id; evidence scanned and served Clean-only through HR.Core.
- **Data Protection key ring (known issue).** Production compose sets no `DataProtection__KeyRingPath` for hr-core-web, so
  sessions do not survive a restart or reach another replica there (not changed, see §9).

## 6. Test results vs. baseline; modified tests

**Session 1** (handoff §8): baseline filter 551 pass / 1 fail → 626 pass / 1 fail (same `Syr001…law50`); regression 354/354;
full unit 3222/3230 (8 pre-existing failures); integration 47 pass / 1 fail (pre-existing `EstablishmentHire…`).

**Session 2** (handoff §8): full unit (PowerShell, Bypass) 3539 total: 3528 pass / 11 fail, all environment-only on the
ar-SA machine; integration filter 101: 90 pass / 3 fail (pre-existing or environment-only) / 8 skipped (MinIO/ClamAV).

**Session 3:**

- K (privacy/a11y) report: unit filter `Hcm030|Dsar|Prv00|Hcm006|Gov001` 496/497 — the only failure is
  `Prv003BreachTests.Prv003_validate_script_passes` (the test runs `powershell -File` without `-ExecutionPolicy Bypass`;
  environment-only); targeted retention + DSAR 28/28; integration `Hcm030CandidateRetention*` 2/2 on PostgreSQL;
  `prv001-ropa-validate.ps1` «PRV-001 OK: ROPA covers 14 services / 19 activities.»; `hcm006-a11y-scan.ps1` PASS (10 views);
  `hcm001-validate.ps1` PASS; `ux003-no-native-confirm.ps1` exit 1 only for pre-existing back-office `confirm()` calls.
- L (acceptance) report: `AcceptanceSeedTests` 6/6; `Acceptance|Hcm030Backfill|Hcm030Retention` 87/87; static acceptance
  tests 11/11; live results in §7.
- M (this run): see "hcm030-validate run" below.

**hcm030-validate run** (`powershell -ExecutionPolicy Bypass -File scripts/hcm030-validate.ps1`, full, on this working tree):

- EF model check: HR.Core and File.Storage → "No changes have been made to the model since the last migration."
- Unit filter `Hcm030|CandidatePublicStatusProjection|RecruitmentEligibility|Fs030|GatewayUpstreamCookieForwarding`:
  **Passed 558, Failed 0, Skipped 0** (Total 558).
- `hcm006-a11y-scan.ps1`: «PASS: HCM-006/HCM-030 public careers and candidate portal a11y scan (10 views)».
- `prv001-ropa-validate.ps1`: «PRV-001 OK: ROPA covers 14 services / 19 activities.».
- Static checks (files, 8 HCM030 migrations additive, no File.Storage migration, options per environment, gateway/OTP
  contracts, no `PLAN-30 S2/S3` markers): pass. Result: **`hcm030-validate: PASS`**, exit 0.
- Also re-run after the doc edits: `hcm001-validate.ps1` PASS, `com001-validate.ps1` PASS, `sec003-validate.ps1` OK.

**Full unit suite** (M run, PowerShell with `PSExecutionPolicyPreference=Bypass`): 3684 total: 3673 passed / 11 failed
(ten environment-only + the `Hcm029CandidateAcceptedV4Tests.External_hire_publishes_v4_with_the_offer_terms` full-run flake).

**Final run after the review fixes** (retention neutralizes offer/appeal free text; AC-30b deadline, AC-30c skip, AC-30j
precondition; `hcm030-validate.ps1` File.Storage migration match anchored to `^\d{14}_(FS|HCM)030`):

- Builds: `HR.Core.Web` 0 errors (1 existing CS9113 warning), `HRManagement.UnitTests` 0/0, `HRManagement.IntegrationTests`
  0 errors (existing xUnit2013/NU1903 warnings), `HRManagement.AcceptanceTests` 0/0.
- EF `has-pending-model-changes`: HR.Core and File.Storage → "No changes have been made to the model since the last
  migration." (no new migration: the fix only writes existing columns).
- **Full unit suite** (PowerShell, `PSExecutionPolicyPreference=Bypass`): **3685 total: 3675 passed / 10 failed / 0 skipped**.
  All ten are the DECISIONS environment-only list: `Str005ProductionBinding` ×3, `Str005GovernancePack.Exceptions…`,
  `LearningCompletion.Certificate_Number…`, `Syr001Offer…law50`, `Syr001ProductionBinding.AddHrLegislationRegister…`,
  `Ten002…IgnoreQueryFilters`, `San001…Validate_script`, `BolaArchitecture`. `Prv001RopaTests` and `Prv003…validate_script`
  pass; the Hcm029 flake did not occur. Session 2: 3528/3539 → +146 tests, no new failures.
- `Hcm030RetentionJobTests` + `Hcm030Dsar*`: 29/29 (new
  `Apply_neutralizes_offer_and_appeal_free_text_and_a_pair_with_text_left_is_due_again`).
- **Integration** (Testcontainers PostgreSQL) filter
  `Hcm030|Fs030|Hcm02|PublicCareers|EssTalent|Recruitment|Offer`: **53 total: 51 passed / 2 failed / 0 skipped**. Both
  failures are environment-only (DECISIONS): `EstablishmentHireIntegrationTests.HirePartyCompensate_VacatesPosition_…` and
  `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes_…` (Hijri). Passing classes include `PublicCareersApplyTests` (2),
  `PublicCareersRequisitionGateTests` (3), `EssTalentApplyScopeTests` (2), `Fs030ServiceUploadHttpTests` (3),
  `Hcm026*` (4), `Hcm027PayGradeLookupIntegrationTests` (9), `Hcm029HireDuplicatePersonIntegrationTests`, `Hcm030BackfillTests`,
  `Hcm030CandidateNotificationConcurrencyTests` (2), `Hcm030CandidateOtpConcurrencyTests` (3),
  `Hcm030CandidateRetentionIntegrationTests` (2, the new residual predicates translate on Npgsql), `RecruitmentHireClosure*`
  (7) and the `RecruitmentRequisition*` classes. Against session 2 (90 pass / 3 fail / 8 skipped on its 101-test filter): no
  new failure; the MinIO/ClamAV-skipped File.Storage classes are outside this filter.
- Scripts (`powershell -ExecutionPolicy Bypass`): `hcm030-validate.ps1` **PASS** (EF clean, unit filter 559/559, a11y PASS,
  ROPA OK); `prv001-ropa-validate.ps1` «PRV-001 OK: ROPA covers 14 services / 19 activities.»; `hcm006-a11y-scan.ps1`
  «PASS: … (10 views)»; `com001-validate.ps1` PASS; `hcm028-validate.ps1` PASS (125 + 209 unit); `hcm029-validate.ps1` PASS
  (95 + 317 unit).
- Live acceptance fix run: see §7.

**Environment-only failures on this machine** (ar-SA locale with the Um Al-Qura calendar; no `pwsh`): unit
`Str005ProductionBinding` ×3, `Str005GovernancePack.Exceptions…`, `LearningCompletion.Certificate_Number…`,
`Syr001Offer…law50`, `Syr001ProductionBinding.AddHrLegislationRegister…`, `Ten002…IgnoreQueryFilters` (`BenefitPlanSeeder`),
`San001…Validate_script`, `BolaArchitecture` (HRContracts/Payroll), `Prv003…validate_script` (only when the execution policy
is not bypassed for child scripts); integration
`EstablishmentHire HirePartyCompensate/Verify`, `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes` (Hijri),
`ContractSeparationChain.ActivateContract…`. `Prv001RopaTests` now passes (fixed by K3).

**Modified existing tests (all three sessions) with justification**

| Test file | Session | Why |
|-----------|---------|-----|
| `IntegrationTests/InternalChannelIntegrationTests.cs` | 1 | promote now maps to `UnderReview` + `IntakeChannel.Internal` (no jump to Shortlisted) |
| `IntegrationTests/PublicCareersApplyTests.cs`, `PublicCareersRequisitionGateTests.cs` | 1, 2 | `PublicCareersController` ctor changes; phone `"099"` → `"0999123456"` (phone now validated) |
| `IntegrationTests/RecruitmentTestControllers.cs`, `UnitTests/Hcm029TestSupport.cs` | 1, 2 | `RecruitmentApplicationsController` ctor gained `UnifiedIntakeService`, `CandidateDocumentService` |
| `UnitTests/RecruitmentEligibilityRulesTests.cs` | 2 | culture-invariant date parsing (ar-SA); flag count 6 → 7 (`MissingRequiredDocuments`) |
| `UnitTests/Hcm030TestSupport.cs` | 2 | fake document client helpers |
| `IntegrationTests/HRCoreJwtBearerIntegrationTests.cs`, `CrossTenantApiPenetrationTests.cs`, `Est004EstablishmentPlanningIntegrationTests.cs`, `TenantQuarantineIntegrationTests.cs` | 2 | disable the notification and retention background jobs in those hosts |
| `UnitTests/CandidatePublicStatusProjectionTests.cs` | 3 (I) | `OfferAccepted` no longer withdrawable on the portal (DECISIONS I) |
| `UnitTests/Hcm030NotificationPiiGuardTests.cs` | 3 (I) | links point to `/careers/my` (`CandidatePortalUrl()`) |
| `UnitTests/Hcm030CandidateDocumentTests.cs` | 3 (I) | `PLAN-30 S3` comments replaced (no behaviour change) |
| `UnitTests/GatewayRoutingConfigurationTests.cs`, `GatewayUpstreamCookieForwardingTests.cs` | 3 (H5) | new cases for the careers route allow-list; existing antiforgery cases unchanged |
| `AcceptanceTests/AcHcm27OfferMeritTests.cs` (×2), `AcHcm28CommitteeAppealTests.cs` (×1), `AcHcm29HireContractTests.cs` (×2) | 3 (J) | `AcceptOffer` now requires a reason and an evidence PDF (`OfferOnBehalfEvidence` helper) |
| `UnitTests/Hcm030RetentionJobTests.cs` | 3 (K) | OTP rows, portal lock, offer evidence and offer/appeal free text in the pair/legacy paths; audit fields |
| `AcceptanceTests/AcHcm06CandidateDsarTests.cs` | 3 (K) | negative assertions only: no `codeHmac`/`requestIp` in the package |
| `UnitTests/AcceptanceSeedTests.cs` | 3 (L) | new patch test for the AC-HCM-30 fixtures |

**New test classes (session 3):** `Hcm030CandidateOtpTests` (22 methods), `Hcm030CareersSessionTests` (12),
`Hcm030CandidatePortalTests` (31), `Hcm030OnBehalfAcceptanceTests` (21), `Hcm030DsarCandidateExportTests` (4),
`Hcm030PrivacyA11yScanTests` (3), gateway cookie cases in `GatewayUpstreamCookieForwardingTests` (13 methods in the class),
integration `Hcm030CandidateOtpConcurrencyTests` (3), acceptance `AcHcm30CandidateExperienceTests` (8).

## 7. Acceptance

**Run, partially.** Stack `hrm-acceptance` (gateway http://localhost:6200) without `minio` and `file-storage-web`:
`minio/minio:RELEASE.2025-04-22T22-12-26Z` (pinned in `docker-compose.yml`) can no longer be pulled (Docker Hub «pull
access denied … repository does not exist», quay.io 401) and there is no local copy. No substitute image was used.

| AC | Result |
|----|--------|
| AC-HCM-30a | **Pass** — public apply → `Submitted`, `IntakeChannel=Public`, linked, listed for `central-a` |
| AC-HCM-30b | **Pass** — «غير مقبول», «يمكنك الاستئناف» on `/careers/my` and «غير مقبول» on `/careers/track`; negative: shortlisted + interview shows «مدعو للمقابلة» and no «التعيين» step |
| AC-HCM-30c | **Skipped (environment)** — the `.exe` and > 5 MB refusals run first and pass (no row saved); the clean-documents apply then shows «تعذّر رفع المستندات حالياً…» (no File.Storage: «Name or service not known (file-storage-web:8080)») and the test calls `Skip.If` with that reason; clean scan, download and EICAR quarantine unverified |
| AC-HCM-30d | **Pass** — `BelowMinQualification`/`BelowMinExperience`, «دون المؤهل المطلوب», shortlisting needs a reason (`EligibilityOverride`) |
| AC-HCM-30e | **Pass** — one `INTERVIEW_SCHEDULED` FileDrop message without the national id; forced replan → still one |
| AC-HCM-30f | **Pass** — OTP sign-in, offer PDF, accept → `Accepted`, `AcceptedBy=Candidate`, `OfferAccepted`; negatives: 5 wrong codes lock; another person's reference gets the neutral message and no session |
| AC-HCM-30g | **Pass** — decline → `Declined`, `DeclinedBy=Candidate`, `OfferDeclined`; «المرشح التالي بالجدارة» (auto-draft off) |
| AC-HCM-30h | **Pass** — `FiledBy=Candidate`, `Pending`, listed for HR; negative: «انتهت مدة الاستئناف», no button, POST refused |
| AC-HCM-30i | **Pass** — admin apply by `a-directory-checker`; due pair anonymized on both rows, document deleted, `Release` queued; held pair untouched (`SkippedLegalHold ≥ 1`) |
| AC-HCM-30j | **Pass** — `-DockerContainer` dry run writes nothing; apply links all with mapped statuses; second apply links 0 |

Test methods: 7 of 8 pass (30a/b/h, 30d/e, 30f/g, 30f negative, 30h negative, 30i, 30j); `Ac30_c` failed in the L run and
now skips (fix run below). Fix run on the same stack (`Ac30_a_b_h|Ac30_c|Ac30_j`): `Ac30_a_b_h` **Pass** (now also asserts
the appeal deadline «… حتى yyyy/MM/dd» = `RejectedAt` + 14 days ± 1), `Ac30_c` **Skipped** (refusals passed, File.Storage
absent), `Ac30_j` **Skipped** — its fixtures were linked by the L run; the test now refuses to pass vacuously and asks for
`scripts/acceptance-reset.ps1` (it passed on the fresh fixtures in the L run). Regression
`AcHcm27|28|29|06` on the same stack: 8 pass / 4 fail — the 4 are exactly the tests that POST `AcceptOffer` with an evidence
PDF, which needs File.Storage (offer stays `Issued`). `AcHcm06` DSAR passes with the K assertions.

Evidence: `acceptance-evidence/AC-HCM-30/` (`steps-<scenario>.md`, `db-checks-<scenario>.json`, `http/`, `before.json`,
`after.json`, `audit.json`, `timing.json`, `metadata.json`; `result.json` holds only the last scenario's status) and seven
AC-HCM-30 Pass lines in `acceptance-evidence/summary.md`. The stack was left running (18 containers); stop and wipe it with
`powershell -ExecutionPolicy Bypass -File scripts/acceptance-reset.ps1`.

## 8. Deviations and judgement calls

All deviations are listed in the plan's "Implementation notes / deviations"; the main judgement calls:

1. **On-behalf acceptance policy:** `HrCoreWrite` kept, because `HrCoreApprove` (Admin, HrCore, HrManagementApprover,
   PlatformAdmin) is wider; the restriction is the mandatory reason (≥ 20), mandatory evidence and the
   `AcceptOfferOnBehalf` audit. The evidence is relayed server-side through the candidate-document service upload instead of
   the browser `_FileUploadWidget` (needs `ModuleFileStorage`, which HR users lack). Decline evidence added as optional
   (extra migration). The legacy `AcceptAsync(offerId)` overload records `HrOnBehalf`.
2. **`/careers/track` decision:** kept as a read-only lookup without OTP showing only the public status label (no name,
   timeline, interview place or offer terms); everything else needs OTP on `/careers/my`.
3. **MailKit vs. BCL:** MailKit 4.18.1 (MIT) rather than `System.Net.Mail.SmtpClient` (which Microsoft does not
   recommend for new development); MailKit gives STARTTLS-required and implicit-TLS (465) modes and a deterministic
   `Message-ID` from the idempotency key. Referenced from ServiceDefaults; `System.Security.Cryptography.Pkcs` pinned to 10.0.13 because MimeKit's
   10.0.0 dependency has advisories; 4.18.2 was too new (6 days) for the version-age rule.
4. **Upload failure behaviour:** any File.Storage error or transport failure fails the whole apply (or the portal document
   upload) with «تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.» and saves nothing — never a 500, never a half-saved
   application. Uploads happen before staging; a partial failure can leave an unreferenced `Pending` file (GC issue, §9).
5. **Channel-sender abstraction beside `INotificationService`** (COM-001 decision 4 / D-8 wording); preferred channel only;
   templates in one C# class; **salary banned in every message**.
6. **Link columns reused** instead of `SourceCitizenApplicationId`; **`OfferExtended` at draft** handled by keying on
   `OfferLetter.Status = Issued`.
7. **Withdraw only before `OfferAccepted`** (no workflow exit for an accepted offer); next-candidate predicates extended to
   Withdrawn applications whose offer the withdrawal settled.
8. **OTP key fallback:** a random per-process key outside Production (Data Protection cannot derive a stable key);
   **chained limiter** instead of stacking two endpoint policies; **client IP resolver** instead of global
   `UseForwardedHeaders`.
9. **Gateway:** besides the cookie allow-list, no staff token on the careers route (tenant precedence).
10. **Retention:** dry run by default (Production/Staging `Apply=false` until DPO approval); one run row per tenant;
    `PortalLockedUntil`/`PortalOtpRequestedAt` cleared in `TryAnonymize` (so DSAR erasure too); the candidate free text of
    offers and appeals is replaced with «—» (the appeal decision, statuses, dates and channel stay as the record of the
    administrative decision). The DSAR/lifecycle single-row erasures do not touch offers or appeals.
11. **Acceptance:** fixtures as a separate start-up patch; manual cookie header instead of `CookieContainer`; acceptance
    OTP limits raised to 60/120; backfill `-DockerContainer`; `prepare-keycloak-import.ps1` 5.1 fix.
12. **`hcm030-validate.ps1`** also runs the `GatewayUpstreamCookieForwarding` tests (H5) in addition to the prompt's
    filter, and asserts that no `PLAN-30 S2:`/`S3:` marker remains.

## 9. Deferred follow-ups and `// PLAN-3x:` markers

**Deferred follow-ups**

- File-reference outbox dispatcher tenant filter (`FileReferenceOutboxDispatcher.ProcessBatchAsync`, no `X-Tenant-ID`):
  Register/Release rows of candidate documents, offer evidence and retention releases probably never reach File.Storage.
- File.Storage GC tenant filter (`FilePlatformService.CollectGarbageAsync`): orphan `Pending` files are never collected.
- Production Data Protection key ring for hr-core (`DataProtection__KeyRingPath` absent in `docker-compose.production.yml`).
- Audit-log PII on anonymization (`AuditSaveChangesInterceptor` old values of non-redacted columns, which now include the
  offer and appeal free text that retention neutralizes).
- Production RLS for cross-tenant jobs (`hr_core_app` NOBYPASSRLS without `app.current_tenant`).
- Production compose lacks `file-storage.service-upload` in hr-core `ServiceIdentity__Scope`, `Integration__FileStorageBaseUrl`
  and `Recruitment__PublicPortalBaseUrl`; the production realm needs the scope on `svc-hr-core`.
- D-8 provider (SMTP/SMS and processor contract): until then no candidate message and no OTP in Production/Staging.
- Internal-candidate notifications (ESS) — out of scope.
- `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes…` culture-formatted date (Hijri on ar-SA).
- AC-HCM-30c live run and the File.Storage paths of AcHcm27/28/29 (vetted MinIO image or registry credentials).
- `acceptance-up.ps1` on Windows PowerShell 5.1 (`*> $null` under `Stop`, BOM when piping JSON to docker, `acceptance-verify`
  needs PS7) and `docker/build-manifest` not regenerated by `acceptance-up.ps1`.
- `Prv003BreachTests` and the other `*_validate_script_passes` tests should pass `-ExecutionPolicy Bypass`.
- `ux003-no-native-confirm.ps1` still fails on pre-existing back-office `confirm()` calls.
- The a11y scan is static; the axe E2E gate (UX-002) does not cover `/careers`.

**Remaining `// PLAN-30:` markers** (no `PLAN-30 S2`/`S3` markers remain; `hcm030-validate.ps1` asserts this):

- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs:324`, `:447`, `:476` — `// PLAN-30: notify candidate` (covered by the recorded offer/status events and the dispatcher; comment left)
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/ReserveListService.cs:138` — `// PLAN-30: notify candidate` (covered by `StatusChanged → ReserveListed` / `APP_RESERVE`)
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionCascade.cs:81` — `// PLAN-30: notify affected candidates … of the cancellation` (rejections via `StatusChanged → Rejected`; no dedicated cancellation template)
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealWorkflowHandler.cs:87` — `// PLAN-30: alert compliance` (`UpheldNoRemedy` compliance alert, not implemented)
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/CandidateNotificationDispatcher.cs:283` — `// PLAN-30: internal (employee) candidates get no email/SMS` (documented scope decision)

Other `PLAN-30 (FS-030/F5/K1/K2/K4/I)` comments are descriptive, not follow-ups. Earlier plans' markers kept:
`OfferLettersController` «PLAN-29 follow-up: archive in File.Storage», `RecruitmentAppealService` «PLAN-25: always start
workflow».

## 10. Manual test script (Arabic UI paths)

Full steps: `docs/acceptance-plans/execution/PLAN-30-EXECUTION.md`. Short form (acceptance stack, gateway
http://localhost:6200):

1. `/careers` → `REQ-2026-A-CX01` → تقديم with «السيرة الذاتية (مطلوب)» and the certificate, channel and consent
   «أوافق على تلقي إشعارات حول طلبي عبر القناة المختارة» → reference number.
2. `acc-central-a` → `RecruitmentApplications?RequisitionId=…`: channel «بوابة عامة», eligibility badge
   «دون المؤهل المطلوب», filter «مع ملاحظات أهلية»; Details → «المستندات» («سليم» / «قيد الفحص» / «محجور — غير متاح»).
3. Move to `Shortlisted` (reason required for a flagged application) → «المقابلات» → schedule → within a minute one file in
   `artifacts/acceptance/candidate-notifications/` (`INTERVIEW_SCHEDULED`) and «الإشعارات» shows it.
4. `/careers/my` → «الدخول إلى طلباتي» → «الرقم المرجعي» + «الرقم الوطني» → «إرسال الرمز» → read «رمز التحقق لبوابة التوظيف: …»
   from the newest FileDrop file → «إدخال رمز التحقق» → «دخول» → «طلبي».
5. With an issued offer: «عرض العمل» → «تنزيل خطاب العرض» (PDF + verification code) → «قبول العرض» («أقرّ بأنني اطلعت على
   شروط العرض وأقبلها» + «الاسم الكامل») or «الاعتذار عن العرض» («سبب الاعتذار»). HR: `OfferLetters/Details` shows
   «المرشح عبر بوابة التوظيف» or, for «قبول نيابةً عن المرشح», «HR نيابة عن المرشح» with the reason and evidence link.
6. «الانسحاب من الطلب» → «تأكيد الانسحاب» («سبب الانسحاب», «أؤكد رغبتي في الانسحاب من هذا الطلب»); after acceptance:
   «تم قبول العرض؛ للانسحاب يرجى التواصل مع جهة التوظيف.».
7. Rejected application: «الاستئناف» → «سبب الاستئناف» → «تقديم الاستئناف»; after 14 days «انتهت مدة الاستئناف في …» and no button.
8. Five wrong codes → «تم إيقاف الدخول مؤقتاً بسبب تكرار إدخال رمز خاطئ. يرجى المحاولة لاحقاً.».
9. `acc-a-directory-checker` → «الاحتفاظ ببيانات المرشحين» → «تشغيل تجريبي» → «تنفيذ الإخفاء» (acceptance only):
   `AC30-RET-0001` anonymized, `AC30-RET-0002` (legal hold) untouched.
10. `scripts/hcm030-backfill-unified-intake.ps1 -DockerContainer hrm-acceptance-postgresql-1` (dry run) → `-Apply` →
    `AC30-BF-0001` `Submitted`, `AC30-BF-0002` `UnderReview` → second `-Apply` links 0.

## 11. `git log --oneline` of the branch (from `55fbcdd`)

```
da1e61b feat(hcm030): HR on-behalf offer acceptance requires reason and evidence
d3080c4 feat(hcm030): /careers/my — status, timeline, interviews, documents, offer PDF, accept/decline, withdraw, appeal
f910a77 feat(gw030): forward the careers session cookie on the careers route only
429e5b0 feat(hcm030): candidate OTP challenge, careers session and per-client OTP rate limit
7af0bf5 docs(hcm030): session 2 handoff
f02ce47 feat(hcm030): pair-aware candidate retention job, run log and admin screen
e25e81b feat(hcm030): candidate notification planner/dispatcher job and 48h offer reminder
cafdee4 feat(com030): candidate notification channels (SMTP, HTTP SMS, FileDrop), consent and templates
2283eb3 feat(hcm030): candidate documents with required types and HR document card
c79f626 feat(fs030): service-to-service candidate document upload policy in File.Storage
2264e0e test(hcm030): parse eligibility age-bound dates culture-invariantly
4977aac docs(hcm030): session 1 handoff
acf9e7d feat(hcm030): application profile, requisition criteria and eligibility flags
9f73536 feat(hcm030): backfill of unlinked channel applications (script + SQL + tests)
f219fed feat(hcm030): application event log and candidate public status projection
0d58720 feat(hcm030): unified intake — public and internal applications enter the recruitment path at Submitted
```

(Plus this commit, which records the hashes of commits 16–18.)
