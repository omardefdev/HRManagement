# Task (Session 3 of 3): PLAN-30 Part 3 — Candidate self-service on /careers with OTP, HR on-behalf acceptance, privacy and accessibility, acceptance AC-HCM-30a..j, docs

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) / YARP engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 3 of 3 for PLAN-30.**

- **Session 1** delivered:
  - unified intake
  - `RecruitmentApplicationEvent`
  - `CandidatePublicStatusProjection`
  - the backfill
  - profile fields and eligibility flags
- **Session 2** delivered:
  - candidate documents via a File.Storage service upload
  - candidate notifications (channel senders, consent, the `CandidateNotification` table, the planner/dispatcher job, the 48-hour reminder, the PII guard)
  - pair-aware retention
- **This session implements:**
  - **H** — OTP access and the `careers_session` cookie (D-4), including the gateway cookie allow-list and per-client rate limiting
  - **I** — the `/careers/my` pages and candidate actions
  - **J** — restricted, documented HR on-behalf acceptance
  - **K** — privacy and accessibility
  - **L** — acceptance AC-HCM-30a..j
  - **M** — the validate script and docs covering all three sessions

Do not redo earlier work. Fix it only if a test proves a defect, and report it.

## Read first (mandatory)

1. **`reports/plan30-session1-handoff.md`** and **`reports/plan30-session2-handoff.md`** — the sources of truth. Session 2's handoff explains how to send an **immediate** OTP through the channel senders and how acceptance reads FileDrop messages.
2. `docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md` (Arabic), including the «تعديلات قبل التنفيذ» note. This prompt refines the plan. **Where they differ, this prompt wins.**
3. `docs/HCM_001_GAP_STUDY.md` — **G-18**, and decision **D-4**.
4. HR.Core:
   - `Web/Controllers/{PublicCareersController,OfferLettersController,RecruitmentApplicationsController}.cs`
   - `Infrastructure/Middleware/PublicCareersRateLimitExtensions.cs`
   - `Infrastructure/Services/Recruitment/{RecruitmentOfferService,RecruitmentAppealService,OfferLetterDocumentService,RecruitmentSelectionService}.cs`
   - `Domain/Entities/OfferLetter.cs` (`OfferDeclinedBy.Candidate` exists)
   - `Domain/Entities/RecruitmentAppeal.cs` (`RecruitmentAppealFiledBy.Candidate` exists)
   - `Domain/Services/{SelectionWorkflowRules,OfferWorkflowRules,RecruitmentAppealWorkflowRules}.cs`
   - `Views/PublicCareers/*`
   - `Views/Shared/_PublicLayout.cshtml`
   - `Program.cs` — middleware order: `UseRouting` → `UsePublicCareersRateLimiting` → … → `UseAuthentication`.
5. Gateway:
   - `src/Shared/HRManagement.ServiceDefaults/{GatewayAccessTokenTransform,GatewayUpstreamCookieForwarding,ForwardedProxyTrust}.cs`
   - `src/Gateway/HRManagement.Gateway/appsettings*.json` — route `careers-public`, `/careers/{**catch-all}`, `AuthorizationPolicy: Anonymous`
   - the gateway tests (grep `GatewayUpstreamCookieForwarding`)
6. Privacy and accessibility:
   - `scripts/hcm006-a11y-scan.ps1`
   - `docs/prv-ropa/processing-activities.json` and `scripts/prv001-ropa-validate.ps1`
   - `HR.Core.Web/Controllers/DsarInternalController.cs` (candidate export)
   - `docs/UX_A11Y_INVENTORY.md`
   - `docs/ADR-UX-002-a11y.md`
7. Acceptance:
   - `src/Tests/HRManagement.AcceptanceTests/{AcHcm28CommitteeAppealTests,AcHcm29HireContractTests,AcHcm06CandidateDsarTests,Ac28FileScanTests,EvidenceRecorder}.cs`
   - `testdata/acceptance/personas.json`
   - `scripts/acceptance-*.ps1`

## Context and scope boundaries

- **PLAN-25 is deferred.** No generic gate changes.
- **Out of scope:**
  - the mobile app (MOB-001)
  - live civil-registry identity checks (EXT-002; use the existing mock only if something needs it)
  - appeal reviewer and decision changes (PLAN-28)
  - internal-candidate notifications
- Keep diffs minimal.
- **The public portal must be secure by construction:**
  - every portal action loads the application **only** from the session, never from a route or form id;
  - anti-forgery on every POST;
  - no PII in URLs, logs or error messages.
- Arabic UI; nonce CSP; no native `confirm()`; accessible forms (labels, `aria-describedby` for errors, focus on the error summary).
- **Never commit:**
  - `global.json`
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets, OTP codes or acceptance credentials
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

## Verified repository facts

**The gateway strips cookies.** `GatewayAccessTokenTransformProvider` removes the upstream `Cookie` header and re-adds only antiforgery cookies (`GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader`). A `careers_session` cookie set by HR.Core will **not** reach HR.Core through the gateway unless you allow it explicitly:
- for the careers route only;
- by exact cookie name;
- with unit tests;
- leaving every other route unchanged.

YARP passes `Set-Cookie` back by default — verify this. The careers route keeps its path (no `/api/` or `/ui/` prefix removal). Check how `/careers/...` maps to `PublicCareersController` (`[Route("")]`) and scope the cookie path accordingly.

**The rate limiter is one shared window.** `AddPublicCareersRateLimiting` registers a single, non-partitioned fixed window (`Recruitment:PublicCareersRateLimitPerMinute`, 30) shared by **all** visitors. OTP endpoints need a **partitioned** limiter keyed by client IP (use the trusted forwarded-for logic in `ForwardedProxyTrust`) **plus** database counters per application.

**Offer acceptance today**
- `RecruitmentOfferService.AcceptAsync(Guid offerId, CancellationToken)` has no actor and no channel.
- `OfferLettersController.AcceptOffer` is `HrCoreWrite` and needs no reason.
- `DeclineAsync(offerId, reason, declinedBy, actorUserId)` already supports `OfferDeclinedBy.Candidate`.
- `HrManagementPolicies.HrCoreApprove` is **not narrower** than `HrCoreWrite`: it allows `Admin`, `HrCore`, `HrManagementApprover` and `PlatformAdmin`. Switching to it as the plan suggests would **widen** access. Keep `HrCoreWrite` and enforce the restriction through a mandatory reason, mandatory evidence and the audit. Record this deviation.

**Appeals**
- `RecruitmentAppealService.FileAppealAsync(applicationId, reason, filedBy, actorUserId, actorEmployeeId)` supports `RecruitmentAppealFiledBy.Candidate`, and `GetFilingError` gives the window check.
- `ResubmitAsync` answers an info request, which a candidate could do from the portal.

**Withdrawal**
- `SelectionWorkflowRules` allows `Withdrawn` from every open status, including `OfferExtended` and `OfferAccepted`.
- No service settles the active offer on withdrawal.

**Offer PDF.** `OfferLetterDocumentService` renders it and allows download only for `Issued`/`Accepted`. It has a verification code. PLAN-29 left `// PLAN-30: candidate download from /careers` in `OfferLettersController`.

## Stop and ask if

- allowing the careers cookie through the gateway needs more than a careers-route-scoped, name-scoped change;
- the session 2 OTP send path is missing or would store the code in clear;
- any migration would rewrite or delete business data;
- the branch state does not match the session 2 handoff.

---

## Step 0 — Verify session 2 state

1. `git checkout -- testdata`. Check out `feature/plan-30-candidate-experience` at the session 2 handoff SHA or a descendant.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm030|CandidatePublicStatusProjection|RecruitmentEligibility|Fs030|PublicCareers|Gateway"
   ```

   It must match the handoff.

---

## Workstream H — OTP access and the careers session (D-4)

**H1. Entity `CandidateOtpChallenge`** (migration `HCM030_CandidatePortal`), tenant-scoped:

| Column | Notes |
|--------|-------|
| `Id`, `TenantId`, `CitizenApplicationId` | |
| `CodeHmac` | HMAC-SHA256 of the code. Key from configuration or Data Protection, **never** stored next to it. The plain code is never stored. |
| `Channel` | |
| `CreatedAt`, `ExpiresAt` | +10 minutes |
| `Attempts` | |
| `ConsumedAt` | |
| `RequestIp` | hashed |

Also add `CitizenApplication.PortalLockedUntil` (DateTime?).

**H2. `CandidateOtpService`**

- **`RequestAsync(referenceNumber, nationalId, ip)`:**
  - Look up by reference plus `NationalIdHash`, using the `PublicCareersPortal` bypass and an explicit tenant from the row.
  - **Always** return the same neutral message «إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.»
  - Refuse silently when locked, when no contact exists, or when a resend is attempted within `Recruitment:Portal:OtpResendSeconds` (60).
  - At most one active challenge: a new one invalidates the previous.
  - Send immediately through session 2's channel sender, using the preferred channel or the other one if missing. The FileDrop copy contains the code (lab only). Any `CandidateNotification` row records the code **redacted**.
- **`VerifyAsync(challengeId or reference, code, ip)`:**
  - 6 digits, constant-time comparison.
  - Wrong code → `Attempts++`. At `Recruitment:Portal:OtpMaxAttempts` (5) set `PortalLockedUntil = now + Recruitment:Portal:LockMinutes` (30), and audit it (`CandidatePortalLocked`, no PII).
  - Success consumes the challenge.
- Neither OTP request nor verify touches the application status.

**H3. Rate limiting.** New partitioned policy `careers-otp` (per client IP; options `Recruitment:Portal:OtpRequestsPerIpPer10Min` (5) and `…VerifyPerIpPer10Min` (20)) on the OTP endpoints, in addition to the existing global policy. Return Arabic 429 pages.

**H4. Session**

- Cookie `careers_session`: `HttpOnly`, `Secure` (SameAsRequest in dev), `SameSite=Strict`, path scoped to the portal.
- The value is a Data-Protection-protected payload `{ CitizenApplicationId, TenantId, IssuedAt, ExpiresAt }`:
  - absolute 30 minutes (`Recruitment:Portal:SessionMinutes`);
  - versioned purpose string;
  - the key ring shared across HR.Core replicas — verify `AddHrDataProtection` is used.
- Logout «خروج» deletes it.
- A small `CandidatePortalSession` helper/filter resolves the session or redirects to `/careers/my/login`.
- **IDOR rule:** all portal actions resolve the application and offer from the session.

**H5. Gateway allow-list**

- Extend `GatewayUpstreamCookieForwarding`/the transform so cookies named `careers_session` are forwarded **only** for the careers route (by route id or path prefix `/careers`).
- Unit tests:
  - careers route forwards it;
  - other routes drop it;
  - antiforgery behaviour is unchanged.
- Verify the round trip through the local gateway.

---

## Workstream I — `/careers/my` self-service

**Pages** (HR.Core, anonymous controller `CandidatePortalController` under the careers routes, `_PublicLayout`):

| Route | Content |
|-------|---------|
| `login` (GET/POST) | reference number + national ID → OTP request → code entry |
| `index` («طلبي») | public status and deadline from `CandidatePublicStatusProjection`; timeline from events; interviews (date, time, place or «عن بُعد»; no panel names); documents (list; upload missing ones while intake is open, via session 2's upload path and limits); the offer card; the appeal card |
| offer card (when the active offer is `Issued`, or `Accepted`) | terms (position, grade, salary, start date, expiry — salary is shown here, after OTP); «تنزيل خطاب العرض» (PDF via `OfferLetterDocumentService` + verification code); accept; decline |

**Actions** (POST, anti-forgery). Each one is audited with actor `Candidate` (no user id). Each one goes through the existing services, so session 1 records events and session 2 sends the notifications.

**Accept offer**
- Requires a typed acknowledgement: checkbox «أقرّ بأنني اطلعت على شروط العرض وأقبلها» plus the full name typed to match.
- Extend `AcceptAsync(Guid offerId, string acceptedBy, string? note, Guid? evidenceFileId, Guid? actorUserId, CancellationToken)`, keeping an overload for existing callers.
- New `OfferLetter` fields: `AcceptedBy` («Candidate» / «HrOnBehalf»), `AcceptanceNote`, `AcceptanceEvidenceFileId`.
- Refuse expired offers (existing rule).

**Decline offer**
- With a reason: `DeclineAsync(..., OfferDeclinedBy.Candidate, actorUserId: null)`.
- Keep the PLAN-27 auto-draft/suggestion behaviour exactly as for HR declines (`AutoDraftNextOfferAsync` equivalent in a service, not in the controller).

**Withdraw**
- Allowed while the projection's `CanWithdraw` is true: any open status **before `Hired`**.
- New `RecruitmentSelectionService.WithdrawByCandidateAsync(applicationId, reason)` settles the active offer first:
  - `Issued` → `Declined`, `DeclinedBy = Candidate`, reason «انسحاب المرشح: …»;
  - `Draft`/`PendingApproval` → `Revoked`, with the workflow instance cancelled (reuse the revoke internals without moving the application to Rejected/Ranked);
  - then the application → `Withdrawn`.
- Run the same next-candidate suggestion as a decline when an offer was settled.
- Requires a confirmation step (an in-page confirm panel).

**Appeal**
- Shown only while `AppealOpenUntil` is in the future and no appeal exists: `FileAppealAsync(appId, reason, RecruitmentAppealFiledBy.Candidate, actorUserId: null)`.
- After the window: no button, plus «انتهت مدة الاستئناف في {date}».
- When the pending appeal is awaiting information (`IsAwaitingInfo`), show the request and a form that calls `ResubmitAsync(appealId, info, actorUserId: null, actorUserName: "Candidate")`.

**Links and markers**
- Switch session 2's template links to `/careers/my` (`Recruitment:PublicPortalBaseUrl`).
- Keep `/careers/track` as a read-only lookup without OTP. It shows only the public status label, never offer terms or timeline details. Decide whether it remains, and report.
- Resolve `// PLAN-30` markers: the `OfferLettersController` download, `DeclineOffer`, and `RecruitmentAppealService.FileAppealAsync`.

---

## Workstream J — HR on-behalf acceptance (restricted and documented)

- `OfferLetters/Details` «قبول نيابةً عن المرشح» requires:
  - a reason (≥ 20 chars);
  - an evidence file (signed paper acceptance), uploaded with the existing HR file-upload component and registered via `IFileReferenceOutbox` with holder `OfferLetter:AcceptanceEvidence`.
- Calls `AcceptAsync(..., acceptedBy: HrOnBehalf, note, evidenceFileId, actorUserId)`. Audit `Action = "AcceptOfferOnBehalf"`.
- **Policy:** keep `HrCoreWrite` (see Verified facts). Document why `HrCoreApprove` was not used.
- `Details` shows who accepted (candidate via the portal, or HR on behalf with the reason and an evidence link).
- `DeclineOffer` keeps HR-on-behalf for paper declines. Add the same evidence field as optional.

---

## Workstream K — Privacy and accessibility

1. **Retention.** Session 2's anonymization also deletes the pair's `CandidateOtpChallenge` rows and clears `PortalLockedUntil`. Resolve the S2 marker and extend the retention tests.
2. **DSAR.** The candidate export in `DsarInternalController` includes:
   - session 1 profile fields
   - `IntakeChannel`
   - eligibility flags
   - document metadata (no content)
   - notification log (masked recipient, template, status, time)
   - offer acceptance channel

   It excludes OTP data except counts. Update `AcHcm06CandidateDsarTests` expectations if needed.
3. **ROPA.** Add processing activities for candidate OTP verification, candidate documents and candidate notifications to `docs/prv-ropa/processing-activities.json`:
   - purpose, legal basis, data categories, retention, recipients (SMTP/SMS processor «يُحدَّد عند D-8»);
   - make `scripts/prv001-ropa-validate.ps1` pass.
4. **Accessibility.**
   - Extend `scripts/hcm006-a11y-scan.ps1` to the new views: login, OTP, my, the offer, withdraw and appeal forms, and the apply form with uploads.
   - Add checks for label/`for` pairs, `aria-describedby` on errors, `autocomplete="one-time-code"` and `inputmode="numeric"` on the OTP input, and a visible focus target after errors.
   - Fix every violation.

---

## Workstream L — Acceptance (whole PLAN-30)

Write `src/Tests/HRManagement.AcceptanceTests/AcHcm30CandidateExperienceTests.cs`:
- `EvidenceRecorder("AC-HCM-30", correlationId)`;
- the citizen side through the gateway `/careers` (anonymous HTTP client with a cookie container);
- HR steps with existing HR core/approver personas from `personas.json`;
- FileDrop messages read as described in the session 2 handoff;
- use `Skip.If` for missing preconditions — **never** `WriteResult("Skip"); return;`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-30a | citizen applies → `RecruitmentApplication` `Submitted` immediately, `IntakeChannel=Public`, listed under the requisition in HR | — |
| AC-HCM-30b | HR rejects → `/careers/my` (and `/track`) shows «غير مقبول» plus the appeal deadline | a shortlisted/interviewed application never shows a hire step |
| AC-HCM-30c | apply with CV + certificate → visible on HR Details as Clean | `.exe` or > 5 MB refused; EICAR → quarantined, not downloadable (`Ac28` pattern) |
| AC-HCM-30d | qualification below the requisition minimum → flag visible, application screenable; shortlisting needs a reason | — |
| AC-HCM-30e | interview scheduled → one FileDrop message without the national ID | the same event replanned / dispatcher re-run → still one message |
| AC-HCM-30f | candidate logs in with OTP (code read from FileDrop) → accepts → `OfferAccepted`, `AcceptedBy=Candidate` | 5 wrong codes → locked; another person's reference + national ID → no access (neutral message, no session) |
| AC-HCM-30g | candidate declines with a reason → `OfferDeclined`, `DeclinedBy=Candidate`, next-candidate suggestion/draft per PLAN-27 config | — |
| AC-HCM-30h | rejected candidate files an appeal within the window → `FiledBy=Candidate`, appears in `RecruitmentAppeals` | after the window → no button, and POST refused |
| AC-HCM-30i | retention job (acceptance `Apply=true`, short months option) anonymizes a due pair, releases files | a pair with `LegalHold` untouched |
| AC-HCM-30j | backfill: seeded unlinked citizen and internal rows → linked recruitment applications with mapped statuses | second run inserts nothing |

Seed what the scenarios need in `AcceptanceDataSeeder`: a requisition with criteria and required documents; legacy unlinked citizen/internal rows for 30j; a due retention pair with a held twin for 30i. It must be deterministic and idempotent. Reset with `scripts/acceptance-reset.ps1`.

If the local stack is available, run it with `RUN_ACCEPTANCE=true` and produce `acceptance-evidence/AC-HCM-30/` (re-import Keycloak if session 2 added the scope). Otherwise report that it was not run and why.

**Regression** — green or matching the baseline:
- `Hcm026`–`Hcm030`, `AcHcm06`, `PublicCareers`, `EssTalent`, `Fun001`, `Com001`
- gateway unit tests
- File.Storage tests

List every modified test with a justification.

---

## Workstream M — Script and docs (cover all three sessions)

- **`scripts/hcm030-validate.ps1`**, modelled on `scripts/hcm029-validate.ps1`:
  - checks the migrations (`HCM030_*`, the File.Storage changes), the key files and the options per environment (`Notifications:Candidate:Provider=None` in Production/Staging; `Recruitment:Retention:Apply=false` in Production);
  - runs `Hcm030|CandidatePublicStatusProjection|RecruitmentEligibility|Fs030`, the a11y scan and the ROPA validation.
- **`docs/acceptance-plans/execution/PLAN-30-EXECUTION.md`:**
  - prerequisites: Keycloak re-import for the new scope; FileDrop path or mount; acceptance options;
  - commands and personas;
  - Arabic manual steps: apply with documents → HR list and flags → interview → message in FileDrop → OTP login → offer PDF, accept/decline → withdraw → appeal → retention dry run/apply → backfill dry run/apply;
  - the evidence folder.
- **`PLAN-30-hcm-candidate-experience.md`:**
  - Status → `Implemented (acceptance evidence pending)` or `Implemented`;
  - "Implementation notes / deviations" for all three sessions, including:
    - reuse of the existing link columns instead of `SourceCitizenApplicationId`;
    - `OfferExtended` at draft;
    - the channel-sender abstraction beside `INotificationService`;
    - no salary in any message;
    - `HrCoreWrite` kept for on-behalf acceptance;
    - the gateway cookie allow-list;
    - the partitioned OTP limiter;
    - retention dry-run by default;
    - the D-8 provider pending;
  - list the deferred follow-ups explicitly.
- **`docs/HCM_001_RECRUITMENT.md`:** unified intake, public status, documents, notifications, portal, retention. Mark G-16/G-17/G-18 and the PLAN-30 part of G-20 as addressed.
- **`docs/COM_001_NOTIFICATIONS.md`:** the real channels, providers per environment, consent, idempotency, PII rules, the FileDrop lab adapter.
- **`docs/GOV_001_RETENTION_SCHEDULE.md` and `docs/GOV_001_RETENTION_RUNBOOK.md`:** the pair rule, months option, dry run/apply and the screen.
- **`docs/HIRE_ONBOARDING_GUIDE.md`:** the HR side — one screening list, eligibility flags, documents card, notifications card, on-behalf acceptance with evidence.
- **`docs/FILE_SERVICE.md` / `docs/SEC_003_FILE_AUTHORIZATION.md`:** the service-upload endpoint and policy.
- **`docs/MESSAGE_EVENT_MATRIX.md`:** the internal application events (not bus messages) and the notifications they trigger.
- **`docs/DATABASE_SCHEMA_INVENTORY.*`** (use the generator if one exists).

## Commit plan

12. `feat(hcm030): candidate OTP challenge, careers session and per-client OTP rate limit`
13. `feat(gw030): forward the careers session cookie on the careers route only`
14. `feat(hcm030): /careers/my — status, timeline, interviews, documents, offer PDF, accept/decline, withdraw, appeal`
15. `feat(hcm030): HR on-behalf offer acceptance requires reason and evidence`
16. `feat(hcm030): privacy (DSAR, ROPA, OTP anonymization) and accessibility scan for candidate pages`
17. `test(hcm030): integration + acceptance AC-HCM-30a..j`
18. `docs(hcm030): plan status, execution guide, notifications/retention/file docs, validate script`

Every commit builds. Tests are green at 12–16 and 18.

## Definition of done (whole PLAN-30)

- One screening list per requisition, and no misleading candidate timeline.
- Every decision point produces an audited, PII-free notification.
- Candidates sign in with OTP and can:
  - see an accurate status;
  - download the offer letter;
  - accept or decline;
  - withdraw;
  - appeal within the window;
  - complete documents.
- HR on-behalf acceptance needs a reason and evidence.
- Retention is automated and respects legal hold.
- The a11y scan has no critical findings.
- Build has 0 errors; `has-pending-model-changes` reports none for every changed service; tests pass; `scripts/hcm030-validate.ps1` passes.
- Acceptance is written, and either run with evidence or explicitly reported as not run.

## Final report (English; quote Arabic UI strings as-is; cover all three sessions)

1. Summary per workstream (A–G from the handoffs, verified; H–M).
2. Commits of all three sessions.
3. Migrations per service and the data steps (backfill script usage).
4. Configuration keys added and their defaults per environment.
5. Security notes: the OTP design, lockout, rate limits, cookie flags, the gateway allow-list, IDOR protection.
6. Test results vs. baseline; modified tests with justification.
7. Acceptance: run or not (and why), evidence path.
8. Deviations and judgement calls (the policy for on-behalf acceptance, the `/careers/track` decision, the MailKit vs. BCL choice, the upload failure behaviour).
9. Deferred follow-ups and `// PLAN-3x:` markers (file:line).
10. Manual test script (Arabic UI paths).
11. `git log --oneline` of the branch.
