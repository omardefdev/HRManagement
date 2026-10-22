# Task (Session 1 of 3): PLAN-30 Part 1 — One intake path, honest public status, application event log, backfill, application profile and eligibility

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer working in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 1 of 3 for PLAN-30.**

| Session | Scope |
|---------|-------|
| **1 (this one)** | Step 0, **A** unified intake (G-16), **B** public status projection and the application event log, **C** backfill, **D** application profile and eligibility (G-17), unit and integration tests |
| 2 | Candidate documents via File.Storage, candidate notifications (Email/SMS/FileDrop), automated retention |
| 3 | `/careers/my` self-service with OTP, HR on-behalf acceptance, accessibility, privacy register, acceptance tests, docs |

**Do not implement session 2 or 3 work here.** At the end, write a handoff file for session 2.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md` (Arabic), including the «تعديلات قبل التنفيذ» note at the top. This prompt refines the plan. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — gaps **G-16** and **G-17**, decision **D-4**.
3. The handoffs and "Implementation notes" of PLAN-26..29: `reports/plan2[6789]-session1-handoff.md` and the plan docs. Reuse their conventions:
   - `RecruitmentOptions`
   - `ApplicationRejection`
   - `RecruitmentAppealWorkflowRules.GetAppealError`
   - the reserve list
   - `OfferLetterDocumentService`
   - the background job pattern
4. Read these fully before changing them:
   - `src/Services/HRCore/HR.Core.Web/Controllers/{PublicCareersController,EssTalentController,PublicJobPostingsController,TalentMarketplaceController,RecruitmentApplicationsController}.cs`
     - `PromoteFromCitizen` is around line 555; `PromoteFromInternal` around line 616.
   - `src/Services/HRCore/HR.Core.Domain/Entities/{CitizenApplication,InternalApplication,RecruitmentApplication,RecruitmentRequisition,PublicJobPosting,OfferLetter,RecruitmentInterviewSlot,RecruitmentAppeal}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Enums/{CitizenApplicationStatus,InternalApplicationStatus,RecruitmentApplicationStatus,OfferStatus}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Services/{SelectionWorkflowRules,CitizenSelectionWorkflowRules,RecruitmentApplicationIntakeRules,RecruitmentAppealWorkflowRules,CandidateApplicationLegalLifecycleService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/{RecruitmentApplicationIntakeService,RecruitmentSelectionService,RecruitmentOfferService,RecruitmentInterviewService,RecruitmentAppealService,RecruitmentOptions}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs`
     - the `SaveChangesAsync` override is near the end;
     - see the `CitizenApplication` / `RecruitmentApplication` model configuration.
   - Views:
     - `PublicCareers/{Details,Track,TrackResult}`
     - `PublicJobPostings/Details`
     - `TalentMarketplace/Details`
     - `EssTalent/MyApplications`
     - `RecruitmentApplications/{Index,Details}`
     - `ViewModels/CitizenApplicationApplyViewModel.cs`
   - `scripts/hcm001-migrate-legacy-applications.ps1` — the pattern for the backfill script.
   - `seed-packs/national-mdm-sy-v1/catalog.json` — the `Qualification` catalog: `DIP`, `BACH`, `MASTER`, `PHD`, with no order field.
   - `docs/syr/service-regimes.json` — it contains **no** age bounds.

## Context and scope boundaries

- **PLAN-25 is deferred** (no server). Make no generic workflow-gate or policy-catalog changes. Where this prompt touches an existing endpoint, keep its current authorization.
- **Design decision (from the plan):** `RecruitmentApplication` becomes the **only status record**. `CitizenApplication` and `InternalApplication` stay as **channel records** (submitted data, external identity, reference number).
- **Out of scope for this session:**
  - documents/attachments, notifications, retention job (session 2);
  - OTP portal and candidate actions (session 3).
  - Leave `// PLAN-30 S2:` / `// PLAN-30 S3:` markers where those will plug in.
- Keep diffs minimal. Follow the existing style: Arabic user-facing messages, nonce CSP (no inline handlers or styles), no native `confirm()` (`scripts/ux003-no-native-confirm.ps1`).
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` — restore them with `git checkout -- testdata` before committing

## Verified repository facts

**Two status paths today**
- `PublicCareersController.Apply` creates only a `CitizenApplication`, with status `Received`.
- HR screens it in `PublicJobPostings/Details` (`ChangeApplicationStatus`, `DeclareCitizenCoi`). `PromoteFromCitizen` then creates a `RecruitmentApplication` directly at `Shortlisted`.
- `EssTalentController.Apply` and `PromoteFromInternal` work the same way for employees.
- A link already exists in both channel records: `CitizenApplication.PromotedRecruitmentApplicationId` / `InternalApplication.PromotedRecruitmentApplicationId` (FK, `SetNull`). `RecruitmentApplication.SourceInternalApplicationId` exists too.
- `RecruitmentApplication` has **no** `SourceCitizenApplicationId`. **Reuse the existing links. Do not add a duplicate column.**

**Misleading tracking**
- `PublicCareersController.BuildTimeline` (around line 191) marks «المقابلة» and «التعيين» as reached whenever `PromotedRecruitmentApplicationId` has a value.
- It reads `CitizenApplicationStatus` only, so a rejection or hire in the main path is never shown.

**Offer nuance — critical for the projection**
- `RecruitmentOfferService.CreateDraftAsync` sets `RecruitmentApplication.Status = OfferExtended` when the **draft** is created (around line 128), before approval and issue.
- An offer is visible to the candidate only when `OfferLetter.Status` is `Issued` (or `Accepted`). The projection must therefore read the **active offer's status**, not only the application status.

**Status storage**
- `RecruitmentApplication.Status`, `CitizenApplication.Status` and `InternalApplication.Status` are stored as **int**.
- `AppealStatus` and `RejectionKind` are stored as strings.

**Tenancy on the public portal**
- The portal runs anonymously and reads with `IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)`.
- The citizen row is created with `TenantId = posting.TenantId`. Every new row created in that request must set `TenantId` **explicitly** to the same value.

**Intake period**
- PLAN-26 already closes intake (`RecruitmentRequisition.ApplicationClosingDate`, `IntakeClosedAt`, `RecruitmentIntakeCloseJob`) and validates in `RecruitmentApplicationIntakeRules.ValidatePublishedRequisition`.

**Retention**
- `CandidateApplicationLegalLifecycleService.CanPurge(CitizenApplication)` returns false whenever `PromotedRecruitmentApplicationId` has a value. After unification every new citizen row is linked.
- Session 2 handles this. Do **not** change retention here, but list it in the handoff.

**Migrations and tests**
- Command: `dotnet ef migrations add HCM030_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`. The latest migration is `HCM029_OfferDocument`.
- Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
- `has-pending-model-changes` must report none.
- Unit tests use a SQLite in-memory `HRCoreDbContext`. Integration tests use Testcontainers PostgreSQL.

## Stop and ask if

- a migration would rewrite or delete business data (the backfill only **inserts** recruitment applications and sets the existing link columns);
- an existing consumer, report or test depends on `CitizenApplication.Status` / `InternalApplication.Status` changing over time for requisition-linked rows in a way you cannot keep;
- the code differs materially from what is described here.

---

## Step 0 — Baseline

1. Run `git checkout -- testdata`. From `main` (which contains PLAN-26..29), create branch `feature/plan-30-candidate-experience`. It must be clean apart from local noise.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm02|Recruitment|Citizen|PublicCareers|Ess|Candidate|Offer|Appeal"
   ```

   Record the results in `reports/plan30-baseline.md`. Restore `testdata` again before committing.

---

## Workstream A — One intake path (G-16)

**A1. `UnifiedIntakeService`** (HR.Core.Infrastructure/Services/Recruitment), scoped.

- `RecruitmentApplication CreateFromCitizen(CitizenApplication citizen, RecruitmentRequisition requisition)`
- `RecruitmentApplication CreateFromInternal(InternalApplication internalApp, Employee employee, RecruitmentRequisition requisition)`

Both stage (do not save) a `RecruitmentApplication` with:

| Field | Value |
|-------|-------|
| `Status` | `Submitted` |
| `TenantId` | the source row's |
| `ApplicantFullName` | from the source |
| `NationalId` / `NationalIdHash` | from the citizen row or the employee (the employee's `NationalId` is decrypted on materialization) |
| `RequisitionId` | the requisition's |
| `AppliedPosition` | `RecruitmentApplicationIntakeRules.DenormalizeJobTitle` |
| `AppliedAt` | the source submission time (not "now") |
| `IntakeChannel` | see A5 |
| `Notes` | «تقديم عبر البوابة العامة {ReferenceNumber}» / «ترشّح داخلي» |
| `SourceInternalApplicationId` | for internal applications |

They also set the source's `PromotedRecruitmentApplication`.

Also add `Task<RecruitmentApplication?> EnsureLinkedAsync(CitizenApplication | InternalApplication, CancellationToken)`. It creates the missing link for a requisition-linked row that has none, using the **status mapping of Workstream C**. The legacy promote endpoints use it (A3).

**A2. Callers**

- `PublicCareersController.Apply` — for a requisition-linked posting, create the citizen row **and** the recruitment application in **one** `SaveChangesAsync`. Legacy postings (`RequisitionId == null`) are unchanged.
- **Duplicate check:** besides the existing citizen check, refuse when a non-terminal `RecruitmentApplication` with the same `NationalIdHash` already exists on the requisition (for example, an HR-entered or internal application). Use the same Arabic message.
- `EssTalentController.Apply` does the same for internal candidates.
- **COI:** the conflict-of-interest declaration lives on the `RecruitmentApplication` only (`RecruitmentSelectionService.DeclareCoiAsync`). Nothing is copied at intake. The backfill copies the citizen row's COI fields.

**A3. Manual promotion**

- Remove the «تحويل» buttons from `PublicJobPostings/Details` and `TalentMarketplace/Details`.
- `PromoteFromCitizen` / `PromoteFromInternal` stay only for requisition-linked rows that still have no link (rows from before the backfill). They delegate to `EnsureLinkedAsync`, which never jumps to `Shortlisted`. Otherwise they redirect to the linked application with an info message.

**A4. Screening in one place**

- In `PublicJobPostings/Details` and `TalentMarketplace/Details`, for requisition-linked rows:
  - show the projected status (B2) and a link to `RecruitmentApplications/Details`;
  - remove the status `<select>` and the COI form;
  - add a header button «فرز الطلبات» → `RecruitmentApplications?RequisitionId=…` (the filter already exists).
- `ChangeApplicationStatus` / `DeclareCitizenCoi` (public and internal) refuse requisition-linked rows with «يُدار هذا الطلب من مسار التوظيف الموحد.». Legacy unlinked rows keep today's behaviour.

**A5. `RecruitmentApplication.IntakeChannel`**

- Type: string(20), values `Public`, `Internal`, `HrEntered`, `Legacy`.
- Set it at every creation path, including `RecruitmentApplicationsController.Create` (`HrEntered`).
- Show it as a badge in `RecruitmentApplications/Index` and `Details` («بوابة عامة» / «ترشّح داخلي» / «إدخال يدوي» / «قديم»).
- PLAN-32 analytics will use it.

**A6. Migration `HCM030_UnifiedIntake`**

- `IntakeChannel`, with a Postgres-guarded backfill of the column for existing rows:
  - linked from `CitizenApplications` → `Public`
  - `SourceInternalApplicationId` set → `Internal`
  - `IsLegacyUnlinked` → `Legacy`
  - otherwise `HrEntered`
- Unique filtered indexes on `CitizenApplications.PromotedRecruitmentApplicationId` and `InternalApplications.PromotedRecruitmentApplicationId` (`IS NOT NULL`). **Before adding them, check for duplicates with a query and report the result.**
- The event table from B1.

---

## Workstream B — Honest public status and the application event log

**B1. `RecruitmentApplicationEvent`** — the persisted form of the plan's internal `RecruitmentApplicationStatusChanged` event.

Append-only, tenant-scoped. **No PII.**

| Column | Notes |
|--------|-------|
| `Id` | |
| `TenantId` | |
| `RecruitmentApplicationId` | |
| `EventType` | string(40) |
| `FromStatus` | int? |
| `ToStatus` | int? |
| `RelatedEntityId` | Guid? — slot, offer or appeal |
| `OccurredAt` | |
| `NotificationsPlannedAt` | DateTime?, null — used by session 2 |

Index `(TenantId, RecruitmentApplicationId, OccurredAt)` and a filtered index on `NotificationsPlannedAt IS NULL`.

**Recorder.** Called from `HRCoreDbContext.SaveChangesAsync` **and** `SaveChanges`, so unit tests and every service path (jobs, cascades, hire closure, reserve list, appeals) are covered. It reads the `ChangeTracker` only (no queries) and adds rows in the same save:

| Event type | Trigger |
|------------|---------|
| `Received` | `RecruitmentApplication` Added |
| `StatusChanged` | `Status` modified (from/to) |
| `InterviewScheduled` | slot Added — a rescheduled slot (`RescheduledFromSlotId` set) records `InterviewRescheduled` instead |
| `InterviewCancelled` | slot status → `Cancelled` |
| `OfferIssued` / `OfferAccepted` / `OfferDeclined` / `OfferExpired` / `OfferRevoked` | `OfferLetter.Status` modified to that value |
| `AppealFiled` / `AppealDecided` | `RecruitmentAppeal` Added / status leaves `Pending` |

- Take `TenantId` from the entity.
- Add a way to suppress recording for bulk data fixes, for example a scoped flag. Document it.
- **Session 2's dispatcher consumes these rows. Do not send anything here.**

**B2. `CandidatePublicStatusProjection`** (HR.Core.Domain/Services, pure, no EF).

- **Inputs:**
  - application status, `RejectedAt`, `RejectionKind`, `AppealStatus`, `ReserveUntil`
  - the active offer's status and `ExpiryDate`
  - interviews: time, mode, location, status
  - the appeal-window result (pass in `RecruitmentAppealWorkflowRules.GetAppealError(...)` or its deadline)
  - the application's events
- **Output:** `CandidatePublicView`, containing:
  - `Code`, `LabelAr`, `DetailAr`
  - `DeadlineUtc?`
  - flags `CanRespondToOffer`, `AppealOpenUntil`, `CanWithdraw` (session 3 uses them)
  - `Timeline` — ordered public steps with dates, from the events. Rows without events get a timeline synthesized from the current status only. **Never** mark a step reached unless it happened.

Mapping (from the plan, with the offer nuance):

| Internal | Public (Arabic) |
|----------|-----------------|
| Submitted / UnderReview | «قيد المراجعة» |
| Shortlisted / InterviewScheduled | «مدعو للمقابلة» + the next scheduled interview (if any) |
| Assessed / Ranked | «قيد التقييم النهائي» |
| OfferExtended with the active offer `Draft`/`PendingApproval` | «قيد التقييم النهائي» (**not** an offer yet) |
| OfferExtended with the active offer `Issued` | «عرض بانتظار ردك حتى {date}» |
| OfferAccepted / Hired | «مقبول — بانتظار استكمال التعيين» |
| ReserveListed | «في قائمة الاحتياط حتى {date}» |
| OfferDeclined | «العرض منتهٍ/مرفوض» |
| Rejected | «غير مقبول» + «يمكنك الاستئناف حتى {date}» only when the rejection is appealable and the window is open; «استئنافك قيد المراجعة» while an appeal is pending |
| Withdrawn | «منسحب» |

Never expose rank, scores, committee data, panel names or internal notes.

**B3. Use the projection**

- `/careers/track` → `TrackResult`: replace `BuildTimeline` with the projection for linked rows. Legacy unlinked rows keep the old citizen-status display.
- ESS `EssTalent/MyApplications`: show the projected status instead of the raw internal enum.
- For linked rows, `CitizenApplication.Status` / `InternalApplication.Status` are **no longer authoritative** and no longer written. Keep the columns for legacy rows and document this in the handoff. Grep every reader of these two properties and fix each one, or explain why it is unaffected.

---

## Workstream C — Backfill (AC-HCM-30j)

**C1. Script and SQL**

- `scripts/sql/hcm030-backfill-unified-intake.sql` — one transactional, idempotent PostgreSQL script.
- `scripts/hcm030-backfill-unified-intake.ps1` — on the pattern of `hcm001-migrate-legacy-applications.ps1`:
  - `-DryRun` (the default) reports counts per mapping;
  - `-Apply` executes;
  - a CSV report goes under `artifacts/hcm030-backfill/`.
- The SQL handles requisition-linked `CitizenApplications` **and** `InternalApplications` that have no `PromotedRecruitmentApplicationId`:
  - inserts the `RecruitmentApplication`, setting `IntakeChannel`, `AppliedAt` = source submission time, and `TenantId`;
  - sets the link;
  - copies citizen COI fields.

**C2. Status mapping** (citizen and internal enums have the same names)

| Source | Requisition `Published` | Requisition cancelled / closed |
|--------|-------------------------|--------------------------------|
| Received / Submitted | `Submitted` | `Rejected` (`RequisitionCancelled` / `RequisitionClosed`) |
| UnderReview | `UnderReview` (`ScreenedAt` = source `UpdatedAt`) | same as above |
| Shortlisted / Accepted | `Shortlisted` (Accepted adds a note) | same as above |
| Rejected | `Rejected` (`SelectionDecision`) | `Rejected` (`SelectionDecision`) |

- `RejectedAt` = the source's last update time (or the requisition close/cancel time), **never now**, so the backfill never reopens an appeal window.
- Running the script twice changes nothing.
- No events are recorded (raw SQL), so session 2 sends no notifications for historic rows. State this in the script header.

**C3. Reuse the mapping.** `EnsureLinkedAsync` (A1) uses the same mapping in C#. One unit test asserts that C# and SQL agree on each row of the table.

---

## Workstream D — Application profile and eligibility (G-17, without documents)

**D1. Profile fields** (migration `HCM030_ApplicationProfile`), on `CitizenApplication`, with a **snapshot** on `RecruitmentApplication`:

| Field | Type / source |
|-------|---------------|
| `EducationLevelCode` | string(20) — MDM `CatalogKind.Qualification` code |
| `Specialization` | string(200) |
| `GraduationYear` | int? |
| `YearsOfExperience` | decimal(4,1)? |
| `BirthDate` | DateOnly? |

- The apply form gets these fields: labels, validation, a `<select>` of active qualification catalog entries, and a reasonable range for year and experience.
- **Internal candidates:** snapshot `BirthDate` from `Employee.BirthDate`. Take the highest-ranked `Qualification.QualificationCode` of the employee (by D3 ranks) when present; otherwise leave it null.
- `BirthDate` is PII:
  - do not log it;
  - show it only to HR on Details;
  - session 2 adds it to anonymization (marker).

**D2. Requisition criteria**

- Nullable fields on `RecruitmentRequisition`: `MinQualificationCode`, `MinYearsOfExperience`, `MinAge`, `MaxAge`.
- Editable only while the requisition is editable (follow the existing requisition edit rules).
- Shown on the public posting as «شروط التقديم».
- Null = not checked.

**D3. `RecruitmentEligibilityRules`** (Domain, pure). It returns flags. **It never rejects.**

| Flag | Arabic |
|------|--------|
| `BelowMinQualification` | «دون المؤهل المطلوب» |
| `QualificationUnranked` | «تعذّر تقييم المؤهل» |
| `BelowMinExperience` | «خبرة أقل من المطلوب» |
| `AgeBelowMin` | «دون الحد الأدنى للعمر» |
| `AgeAboveMax` | «فوق الحد الأعلى للعمر» |
| `MissingProfileData` | «بيانات ناقصة» |

- Qualification order comes from `Recruitment:Eligibility:QualificationRanks` (code → int, default `DIP=1, BACH=2, MASTER=3, PHD=4`). Do not change the national MDM seed pack, whose digest is pinned.
- Age is computed on the application date, against the requisition's `MinAge`/`MaxAge`.
- Optional per-regime defaults may go in `Recruitment:Eligibility:AgeBoundsByRegime`, with **no Production values**. The repository has no legal age data. Do not invent any.

**D4. Store and use the flags**

- Add `EligibilityFlags` (string, comma-separated codes) and `EligibilityEvaluatedAt` on `RecruitmentApplication`, evaluated at intake (A1). Also backfill-evaluate them for applications created by C, or leave them null and document that.
- Show Arabic badges in `RecruitmentApplications/Index` (plus a filter «مع ملاحظات أهلية») and `Details`.
- Moving a flagged application to `Shortlisted` requires `EligibilityOverrideReason` (≥ 10 chars), stored on the application and audited (`EntityAuditLog`, `Action = "EligibilityOverride"`, flags and reason only). Enforce it in `RecruitmentSelectionService.TransitionStatusAsync`, which every UI path uses (check bulk paths). Add the textarea to the status form when flags exist.

**D5. Intake period visibility**

- `PublicCareers/Details` must show «آخر موعد للتقديم» from the effective closing date (requisition `ApplicationClosingDate`, or the posting `ClosingDate`) and the existing closed notice.
- Add it only if it is missing. PLAN-26 owns the logic.

---

## Tests

**Unit**

| Class | Covers |
|-------|--------|
| `Hcm030UnifiedIntakeTests` | public apply creates both rows in one save (Submitted, channel `Public`, link, explicit `TenantId`); internal apply the same (`Internal`); legacy posting unchanged; duplicate across channels refused; promote endpoints on linked rows redirect; `EnsureLinkedAsync` maps statuses; `ChangeApplicationStatus` refuses linked rows |
| `CandidatePublicStatusProjectionTests` | every row of the B2 table; Draft/PendingApproval offer is not shown as an offer; appeal line only when appealable and open; a promoted application no longer shows «التعيين ✓» (AC-HCM-30b negative); no internal data in the output |
| `Hcm030ApplicationEventLogTests` | each B1 trigger records exactly one row with the right from/to; no PII columns; suppression works |
| `RecruitmentEligibilityRulesTests` | each flag; unranked code; null criteria; age edges; override reason required and audited when moving to Shortlisted |

**Integration (PostgreSQL)**

- `Hcm030BackfillTests` runs `scripts/sql/hcm030-backfill-unified-intake.sql` on seeded rows (every mapping row, cancelled/closed requisitions, already-linked rows). Assert:
  - the counts;
  - idempotency (second run inserts 0);
  - `RejectedAt` is not now;
  - the unique link indexes hold.
- Skip with `Skip.If` when Docker is unavailable.

**Regression** — green or matching the baseline:
- `Hcm026`, `Hcm027`, `Hcm028`, `Hcm029`
- `PublicCareers`, `EssTalent`, `RecruitmentHire`, `Fun001`

List every modified existing test with a one-line justification.

## Commit plan

1. `feat(hcm030): unified intake — public and internal applications enter the recruitment path at Submitted`
2. `feat(hcm030): application event log and candidate public status projection`
3. `feat(hcm030): backfill of unlinked channel applications (script + SQL + tests)`
4. `feat(hcm030): application profile, requisition criteria and eligibility flags`
5. `docs(hcm030): session 1 handoff`

Every commit builds. Tests are green at 2, 3, 4 and 5.

## Definition of done (session 1)

- Every public or internal application on a requisition is a `RecruitmentApplication` from the moment it is submitted.
- There is only one screening place per requisition.
- `/careers/track` and ESS show an accurate public status with no false steps.
- Every status, interview, offer and appeal change leaves one event row.
- The backfill is idempotent and tested.
- Profile fields and eligibility flags are visible to HR, and overriding them needs a reason.
- Build has 0 errors; `has-pending-model-changes` reports none; tests pass.

## Handoff (mandatory last step)

Write and commit `reports/plan30-session1-handoff.md` with:

1. Branch and commits.
2. Migrations and columns, plus the result of the duplicate-link check.
3. `UnifiedIntakeService` API and the status mapping (C# = SQL).
4. **The exact event types and trigger rules of B1**, the suppression mechanism, and the event row shape. This is session 2's contract.
5. `CandidatePublicView` shape and flags. This is session 3's contract.
6. Readers of `CitizenApplication.Status` / `InternalApplication.Status` and what you did with each.
7. Eligibility flags and options.
8. Test results vs. baseline; modified tests.
9. Deviations, known issues, `// PLAN-30 S2:` / `// PLAN-30 S3:` markers (file:line), and the retention issue (`CanPurge(CitizenApplication)` vs. linked rows).

Then reply with a short summary.
