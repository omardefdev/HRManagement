# Task: PLAN-31 — Multi-vacancy competitions, anonymous double-marked exams, results with an objection window, allocation by merit and preference, reserve recall, bulk offers (one long autonomous session)

## How to run this task — read before anything else

This is a **single prompt for a long autonomous session**. Run it as phases **P0–P8** in order.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx`, 0 errors.
2. Run that phase's tests, plus the regression filter of the phase.
3. Commit. Use the commit messages of the phase.
4. Update **`reports/plan31-progress.md`** — your checkpoint. It contains:
   - the phase status table;
   - the decisions taken (with reasons);
   - files created or changed;
   - migrations;
   - test results;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan31-progress.md` and `git log --oneline -30` **before doing anything else**. Continue from the recorded next step. Never redo a committed phase.

**Context discipline.** Use subagents (Explore or general-purpose) for broad, read-only searches across the repository, and keep your own context for design and edits. Read a file fully only before you change it.

**Orchestration rules** (this session may run with ultracode / dynamic workflows):
- Parallel agents are allowed for:
  - read-only exploration;
  - writing **separate new test files**;
  - independent review of a finished phase.
- **Never** let two agents edit the same file concurrently.
- These edits are made **serially by you, the lead**:
  - `HRCoreDbContext`
  - EF migrations and `HRCoreDbContextModelSnapshot.cs`
  - `Program.cs`
  - `RecruitmentOptions`
  - seeders (`WorkflowPackSeeder`, `CodeGenerationSeeder`, `AcceptanceDataSeeder`)
  - `personas.json` / Keycloak scripts
  - shared enums
- Create **one migration at a time**, only after the previous one is committed.
- If a workflow agent worked in an isolated copy, merge its result serially, rebuild and rerun the tests before committing.

**Blocking decisions.** When a "Stop" condition below is hit:
1. Write a `BLOCKED:` entry in the progress file, with the exact question and the options you see.
2. Commit what is complete.
3. End the turn. **Do not guess** legal values or business rules.

**Keep it simple.** Prefer the simplest design that satisfies the acceptance scenarios and the rules below. Prove each behaviour with a test, not with prose.

---

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) / MassTransit / QuestPDF engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop).

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md` (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md`:
   - **G-19**;
   - §5 decisions **D-2** (competitions — the owner has now asked for implementation) and **D-7** (reserve validity).
3. The plan docs ("Implementation notes" sections) and handoffs of **PLAN-26..30**:
   - `reports/plan2[6789]-session1-handoff.md`
   - `reports/plan30-session1-handoff.md`, `reports/plan30-session2-handoff.md`
   - the PLAN-30 final report / implementation notes

   PLAN-30 introduced:
   - unified intake (`RecruitmentApplication` is the only status record; `IntakeChannel`);
   - an application event log;
   - `CandidatePublicStatusProjection`;
   - candidate documents;
   - candidate notifications (planner/dispatcher, templates, PII guard);
   - retention;
   - `/careers/my` with OTP.

   **Use the names that actually exist** — the handoffs are the source of truth, not this prompt.
4. Read fully before changing:
   - `HR.Core.Domain/Entities/{RecruitmentRequisition,RecruitmentApplication,OfferLetter,RecruitmentAppeal,RecruitmentAssessmentCriteria,RecruitmentAssessmentScore,FundedPosition,JobCatalogEntry,SelectionCommittee*}.cs`
   - `HR.Core.Domain/Enums/{RecruitmentApplicationStatus,RequisitionStatus,AssessmentStage,RecruitmentRejectionKind,RecruitmentAppealOutcome}.cs`
   - `HR.Core.Domain/Services/{RequisitionWorkflowRules,SelectionWorkflowRules,RecruitmentAppealWorkflowRules,AssessmentScoringRules,OfferWorkflowRules,ApplicationRejection}.cs`
   - `HR.Core.Infrastructure/Services/Recruitment/*`, especially:
     - `RecruitmentRequisitionLifecycleService`, `RecruitmentScoringService`, `RecruitmentSelectionService`, `CommitteeRankingGate`
     - `RecruitmentOfferService` (`CreateDraftAsync`, `SubmitForApprovalAsync`, `TryAutoDraftNextOfferAsync`, `AvailableForMerit`)
     - `ReserveListService`, `RecruitmentReserveExpiryJob`
     - `RecruitmentAppealService`, `RecruitmentAppealWorkflowHandler`
     - `SelectionCommitteeMinutesService`, `OfferLetterDocumentService`
     - `RecruitmentIntakeCloseJob`
     - the PLAN-30 intake, notification and portal services
   - `HR.Core.Infrastructure/Data/{HRCoreDbContext,WorkflowPackSeeder,CodeGenerationSeeder}.cs`, `HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs`
   - `HR.Core.Web/Controllers/{RecruitmentRequisitionsController,RecruitmentApplicationsController,OfferLettersController,PublicCareersController}.cs` and the PLAN-30 portal controller
   - Shared:
     - `HrManagementRoles`, `HrManagementAuthorizationExtensions`, `HrModuleAuthorizationFilter`, `GatewayPathAuthorizationMiddleware`
     - `src/Shared/HRManagement.Documents.QuestPdf/*`
     - `AdvisoryLeaderElection`
   - `testdata/acceptance/personas.json`, `scripts/prepare-keycloak-import.ps1`, `scripts/sync-keycloak-dev-realm.ps1`
   - `docs/syr/legal-source-binding.md` (create it if missing), `docs/HIRE_RBAC_MATRIX.md`
   - `scripts/hcm030-validate.ps1` — the pattern for the validate script
   - the `perf/` folder

## Decisions for this implementation (record them in P0)

| # | Decision |
|---|----------|
| 1 | **D-2:** the owner asked to implement PLAN-31 (2026-10-29). The legal basis and **every legal parameter** are **inputs per competition**, taken from the approved legal reference. The parameters are: objection window, reserve validity, pass mark, criteria weights, double-marking threshold, reconciliation rule, tie-break order, max lines per candidate. **Never invent legal values.** Configuration defaults exist only in `appsettings.Development.json` / the acceptance profile. Production has none, so creating a competition there requires explicit values. |
| 2 | **A requisition stays one funded position.** Keep `VacancyCount` and its DB check constraint `CK_RecruitmentRequisitions_VacancyCount` (0..1). A **competition line** groups several approved requisitions; `RequiredCount` = the number of attached requisitions. Do not replace `ValidateVacancyCount`. Record this deviation from the plan. |
| 3 | **One `RecruitmentApplication` per person per competition**, with `CompetitionId`, ordered **line preferences**, and `RequisitionId = null` until allocation. Allocation sets `RequisitionId`, so the existing offer → hire → contract flow works unchanged afterwards. Unique `(TenantId, CompetitionId, NationalIdHash)`. |
| 4 | **Notifications carry no score, rank or national ID** (the PLAN-30 PII guard). The individual result message only says that results are published and links to `/careers/my`, where score, rank and outcome are shown after OTP. Record this deviation from «إشعار فردي بالدرجة والرتبة». |
| 5 | **Objections reuse `RecruitmentAppeal`** with a kind (`ResultObjection`) and the result version, so PLAN-28's independent reviewer and review workflow apply. If PLAN-28's rejection outcomes do not fit, add objection-specific outcomes (`Upheld`/`Dismissed`) rather than bending the rejection ones. |
| 6 | **Anonymous marking.** A dedicated marker role sees exam numbers only, never identities. |
| 7 | **Bulk offers** create drafts and submit each one into the **existing** per-offer approval. A batch approval workflow (`RECRUITMENT-OFFER-BATCH-APPROVAL`) is **optional**: implement it only after everything else passes; otherwise document it as a follow-up. |
| 8 | **Reserve recall** happens on decline, expiry, revocation, or the candidate's withdrawal of an allocated application, within the line's reserve validity. Recall on non-commencement depends on PLAN-29's deferred "cancel hire" follow-up: leave a `// PLAN-31 follow-up:` marker. |
| 9 | **PLAN-25 stays deferred.** Do not change generic workflow gates or `WorkflowStageGuard`. New workflow definitions follow the PLAN-27/28 seeding and handler pattern. |

## Scope boundaries

- **Out of scope:**
  - question banks or online exams
  - remote proctoring
  - live civil-registry checks
  - payroll or contracts changes beyond what the existing hire flow already does
- Single-requisition recruitment must behave **exactly as before**. It is the implicit one-line case: no competition rows; existing screens and tests unchanged.
- Arabic UI; nonce CSP (no inline handlers or styles); no native `confirm()` (`scripts/ux003-no-native-confirm.ps1`); accessible forms. Public pages must pass `scripts/hcm006-a11y-scan.ps1` after you add them to it.
- **Never commit:**
  - `global.json`
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets or credentials (`.env`, `.acceptance/credentials.json`)
  - test-rewritten `testdata/*/out/*.json` — run `git checkout -- testdata` before each commit
- Do not push to origin.

## Repository facts (verified on the PLAN-29 code, 2026-10-22 — re-verify; PLAN-30 changes come from its handoffs)

**Requisitions**
- `RecruitmentRequisition` has one `FundedPositionId`, `VacancyCount = 1` (enforced by a DB check constraint), and `LegalSourceId`.
- Statuses: Draft → … → Approved → Published → Closed/Cancelled.
- It also has `ApplicationClosingDate`, `IntakeClosedAt` (PLAN-26 intake-close job), `PublishingChannel`, `PublicJobPostingId`, `InternalVacancyId`, and `RequiresCommitteeApproval` (PLAN-28).
- `FundedPosition` has `GradeCode`.

**Offers**
- `RecruitmentOfferService.CreateDraftAsync` needs the application's requisition: funded position, `LegalSourceId` and terms. It validates the salary against Payroll grade bounds through `IPayrollGradeLookup`, and sets the application to `OfferExtended` **at draft**.
- `TryAutoDraftNextOfferAsync` and `ReserveListService` work **per requisition**:
  - `MoveSiblingsToReserveAsync`, `GetNextCandidateSuggestionAsync`, `ExpireReserveAsync`;
  - `RecruitmentApplication.ReserveUntil`;
  - option `Recruitment:ReserveValidityMonths`.

**Appeals**
- `RecruitmentAppeal` is tied to a rejected application.
- `RecruitmentAppealWorkflowRules.GetAppealError` measures the window from `RejectedAt` with `Recruitment:AppealWindowDays` and the appealable rejection kinds.
- A pending appeal blocks hire (`Recruitment:BlockHireOnPendingAppeal`).

**Ranking and committee.** A unique filtered index on `RecruitmentApplications (TenantId, RequisitionId, Rank)` applies where `RequisitionId` is not null. The PLAN-28 committee gate (`CommitteeRankingGate`, `RequiresCommitteeApproval`) can block ranking and offers on a requisition. Competition results approval must satisfy or replace it for attached requisitions — decide, implement, test, report.

**Documents.** PDFs are generated on demand with QuestPDF (`HrOfficialDocumentLayout`), with a SHA-256 of canonical JSON stored at issue and a 12-hex verification code. Patterns: `OfferLetterDocumentService` (PLAN-29) and the committee minutes (PLAN-28).

**Background jobs and codes**
- Jobs: `BackgroundService` + `AdvisoryLeaderElection.TryRunAsLeaderAsync` + `IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)`. Rows written by jobs set `TenantId` explicitly. Pick unused advisory lock keys (grep).
- Reference numbers: `ICodeGenerator` + `CodeGenerationRequests.*` + `CodeGenerationSeeder` (COD-001).
- Exam numbers must **not** come from a sequence: random, unique per competition, non-guessable.

**Personas and roles.** Adding a role or persona follows the PLAN-27/28 precedent: `personas.json` with `passwordEnv` names only, `prepare-keycloak-import.ps1`, and a Keycloak re-import. The persona catalog size is asserted in four places: `AcceptancePersonaCatalog`, `prepare-keycloak-import.ps1`, `AcceptanceV2CatalogTests`, `AcceptanceIdentityPopulationTests`.

**Migrations**
- Command: `dotnet ef migrations add HCM031_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`.
- Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
- `has-pending-model-changes` must report none.

**Tests.** Unit tests use SQLite in-memory; integration tests use Testcontainers PostgreSQL (`Skip.If` without Docker); acceptance tests use the local stack with `EvidenceRecorder` and `Skip.If` (never `WriteResult("Skip"); return;`).

## Stop (record `BLOCKED:` and end the turn) if

- the PLAN-30 state is not what its handoffs say, or PLAN-30 is not implemented (no session 3 report, or `scripts/hcm030-validate.ps1` fails);
- a migration would rewrite or delete business data;
- anonymity cannot be guaranteed because the HR.Core module filter or gateway rules would give markers access to application data, and fixing that needs a change to a generic gate (PLAN-25 territory);
- a legal rule is needed that the legislation register or the plan does not provide, and it cannot be modelled as a per-competition input.

---

## Target design

### Entities (HR.Core.Domain, tenant-scoped, `AuditableEntity`; enums stored as strings)

**`RecruitmentCompetition`**

| Group | Fields |
|-------|--------|
| Identity | `Number` (COD, new code type), `Title`, `LegalSourceId` (required, an existing legislation source chosen at creation) |
| Publishing | `PublishingChannel`, `PublicJobPostingId?` |
| Status | `Status` (see below) |
| Dates | `AnnouncementDate`, `ApplicationClosingDate`, `IntakeClosedAt` |
| Legal inputs | `ObjectionWindowDays`, `ReserveValidityMonths`, `MaxLinesPerCandidate`, `PassMark`, `ReconcileThreshold`, `ReconcileRule` (`Average` / `ReconcilerDecides` / `AverageOfClosestTwo`), `TieBreakOrder` (ordered list from a fixed set: `ExamScore`, `InterviewScore`, `YearsOfExperience`, `AppliedAt`) |
| Workflow | `ApprovalWorkflowInstanceId`, `CurrentResultVersionId` |
| Closing | `FinalizedAt`, `CancelledAt`, `CancellationReason` |

Statuses: `Draft → PendingApproval → Approved → Announced → IntakeClosed → Examining → ResultsDraft → ResultsPendingApproval → ResultsPublished → Finalized`. `Cancelled` is reachable before `Finalized`. **Model the objection window as the period after publication** (`PublishedAt + ObjectionWindowDays`), not as a separate status, unless a status makes the code simpler — decide and record. Transitions live in a pure `CompetitionWorkflowRules` with guards.

**`CompetitionLine`**
- `CompetitionId`, `LineNo`, `Title`, `GradeCode`, `JobCatalogEntryId?`, `OrgUnitId?`, `CriteriaVersionId` (criteria must include an exam stage — check `AssessmentStage`; add `Exam` if missing).
- `RequiredCount` (computed from attached requisitions), `LinePassMark?` (overrides the competition `PassMark`).
- `RecruitmentRequisition.CompetitionLineId?`. Attach only `Approved`, not-yet-published requisitions whose funded position grade equals the line grade (and job catalog entry, when set).
- An attached requisition **cannot** be published on its own and has no posting or vacancy of its own. Announcing the competition moves the attached requisitions to `Published`, so existing intake and offer gates keep working, and no individual postings are created.

**`CompetitionPreference`**
- `RecruitmentApplicationId`, `CompetitionLineId`, `Order` (1..`MaxLinesPerCandidate`).
- Unique `(ApplicationId, LineId)` and `(ApplicationId, Order)`.

**`ExamSession`**
- `CompetitionId`, `Type` (`Written` / `Practical` / `Oral`), `Title`, `ScheduledAt`, `DurationMinutes`, `Venue`, `Capacity`, `MaxScore`.
- `Status`: `Planned` → `Seated` → `Held` → `MarkingClosed`.

**`ExamCandidate`**
- `ExamSessionId`, `RecruitmentApplicationId`, `ExamNumber` (random, unique per competition), `SeatNumber`.
- `Attendance` (`Unknown` / `Present` / `Absent`).
- `AdmissionCardSha256?`, `AdmissionCardIssuedAt?`.
- One row per application per exam type.

**`ExamMarkerAssignment`**
- `ExamSessionId`, `MarkerUserId`, `MarkerEmployeeId?`, `Role` (`First` / `Second` / `Reconciler`).
- Distinct people per role on a session.

**`ExamScore`**
- `ExamCandidateId`, `MarkerUserId`, `Round` (`First` / `Second` / `Reconciler` / `ObjectionCorrection`), `Score`, `SubmittedAt`, `Note?`.
- Unique `(ExamCandidateId, Round)` except `ObjectionCorrection`.
- **Blind:** the second marker never sees the first score, and the reconciler sees both only after both are submitted.

**`CompetitionResultVersion`**
- `CompetitionId`, `VersionNo`.
- `Status`: `Draft` → `PendingApproval` → `Approved` → `Published` → `Superseded`.
- `ComputedAt`, `PublishedAt`, `Reason`, `WorkflowInstanceId`, `Sha256`.

**`CompetitionResult`**
- `ResultVersionId`, `RecruitmentApplicationId`, `ExamNumber` (denormalized), `ExamScore`, `FinalScore`, `OverallRank`, `Outcome` (`Passed` / `Failed` / `Absent`).
- A snapshot that is immutable once published.

**`CompetitionAllocation`** (written at finalize)
- `CompetitionId`, `RecruitmentApplicationId`, `AllocatedLineId?`, `AllocatedRequisitionId?`, `PreferenceOrderSatisfied?`.

**`CompetitionReserveEntry`**
- `CompetitionLineId`, `RecruitmentApplicationId`, `LineRank`, `ReserveUntil`.
- `Status`: `Active` / `Recalled` / `Expired` / `Removed`.

**Other additions**
- `RecruitmentApplication.CompetitionId?` and `CompetitionLineId?`.
- New `RecruitmentRejectionKind.CompetitionResult` — not appealable by default, because the objection window is the remedy.
- `RecruitmentAppeal.Kind` (`Rejection` default / `ResultObjection`) and `CompetitionResultVersionId?`.

**Migrations (one at a time):** `HCM031_Competitions`, `HCM031_Exams`, `HCM031_Results`.

### Services (HR.Core.Infrastructure/Services/Recruitment/Competitions)

| Service | Responsibility |
|---------|----------------|
| `CompetitionLifecycleService` | create, edit, attach and detach requisitions, submit for approval, announce, close intake, cancel |
| `CompetitionIntakeService` | apply with preferences, on top of the PLAN-30 unified intake |
| `ExamSeatingService` | seating |
| `ExamAdmissionCardDocumentService` | admission card PDF |
| `ExamMarkingService` | marking |
| `CompetitionResultsService` | compute, approve, publish, version |
| `CompetitionObjectionService` | objections, over `RecruitmentAppealService` |
| `CompetitionAllocationService` | allocation |
| `CompetitionReserveService` | line-level reserve and recall |
| `CompetitionOfferService` | bulk draft and submit |
| `CompetitionResultsDocumentService` | public results list PDF |

Pure rules live in Domain: `CompetitionWorkflowRules`, `ExamMarkingRules`, `CompetitionScoringRules`, `CompetitionAllocationRules`. They are unit-testable without EF.

---

## P0 — Baseline, orientation, notes (no feature code)

1. `git checkout -- testdata`. Find PLAN-30's state:
   - If `feature/plan-30-candidate-experience` is merged into `main`, create `feature/plan-31-competitions-exams` from `main`.
   - If it is not merged but complete, branch from its tip and say so in the progress file.
   - Otherwise **stop** (see Stop).
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm02|Hcm030|Recruitment|Offer|Appeal|Reserve|Committee|PublicCareers"
   ```

   Record the results in `reports/plan31-baseline.md`.
3. Use subagents to map, and write into the progress file:
   - every place that assumes a non-legacy `RecruitmentApplication` has `RequisitionId != null` (intake, index filters, projection, notifications, retention trigger date, DSAR, offers, reserve, committee gate, hire);
   - how markers could reach HR.Core pages: the module filter `AddHrModuleAuthorizeFilter(ModuleHrCore)`, gateway path rules for `/ui/hr-core`, and `HrCoreReadOnly`;
   - the legislation register entries usable as a competition legal source;
   - the PLAN-30 event/notification/portal extension points.
4. Add a «تعديلات قبل التنفيذ (2026-10-29)» block to the top of `PLAN-31-hcm-competitions-exams.md`, in Arabic, listing decisions 1–9 above. Set `Depends on` to "PLAN-26..30 منفذة، PLAN-25 مؤجلة".
5. Create `reports/plan31-progress.md`.

Commit: `docs(hcm031): baseline, pre-implementation notes and progress log`.

## P1 — Competition model, lines, approval workflow, HR screens (AC-HCM-31a)

- Migration `HCM031_Competitions`:
  - competition, lines and preferences;
  - `RecruitmentRequisition.CompetitionLineId`;
  - `RecruitmentApplication.CompetitionId`/`CompetitionLineId`;
  - indexes, including the unique `(TenantId, CompetitionId, NationalIdHash)` filtered on `CompetitionId IS NOT NULL`.
- **`CompetitionLifecycleService`:**
  - create with validation of the legal inputs. Ranges: objection window 1..60 days, reserve 0..36 months, max lines 1..lines count, pass mark and threshold within the exam max score. These are sanity ranges, not legal values.
  - attach/detach requisitions with the rules above. Reject an unapproved or already published requisition with an Arabic message (**31a negative**).
  - submit for approval: every line has ≥ 1 requisition and criteria with an exam stage whose weights sum to 100; at least one exam session is not required yet.
- **Workflow `RECRUITMENT-COMPETITION-APPROVAL`:**
  - steps `hr-core` → `hr-management-approver`, with self-approval prevented (the PLAN-27/28 pattern);
  - a handler sets `Approved`, or returns the competition to `Draft` with the reason.
- **Announce** (`Approved` → `Announced`):
  - creates one public posting for the competition (and an internal vacancy if the channel includes internal);
  - moves attached requisitions to `Published` without individual postings;
  - blocks the requisition's own publish action while it is attached.
- **HR UI** `Competitions/Index|Create|Edit|Details`, with Details tabs: «الأسطر»، «المتقدمون»، «الامتحانات»، «الدرجات»، «النتائج»، «الاعتراضات»، «التخصيص والعروض» (empty placeholders until later phases). Policies:
  - read `HrCoreReadOnly`;
  - write `HrCoreWrite`;
  - approve through the workflow inbox.

  Add the menu entry.
- **Tests:** `Hcm031CompetitionLifecycleTests` — rules, attach and detach, approval, the announce effects, single-requisition flow untouched.

Commit: `feat(hcm031): recruitment competitions — lines, requisition grouping, approval workflow, announcement`.

## P2 — Intake with line preferences

- **Public apply:** `/careers/competitions/{id}` (announcement with the lines, closing date and «شروط التقديم») with ordered preferences (1..`MaxLinesPerCandidate`). Reuse the PLAN-30 apply pipeline (profile, documents, consent, unified intake): one `RecruitmentApplication` with `CompetitionId`, `RequisitionId = null`, and its channel record.
- **Internal apply:** the same through ESS when the channel includes internal.
- Duplicate per person per competition → the Arabic message, no exception.
- **Eligibility flags** (PLAN-30 rules) are evaluated **per preferred line** against the criteria of the requisitions on that line: the union of flags, with the line shown. Still no auto-reject.
- **Intake close:** extend the PLAN-26 intake-close job, or add a competition pass, so `ApplicationClosingDate` closes competition intake (`IntakeClosed`).
- **Fix every `RequisitionId != null` assumption** listed in P0 for competition applications: the projection («قيد المراجعة» / exam / results states), the HR index (filter by competition), retention trigger (competition `FinalizedAt` / `CancelledAt`), DSAR. Add tests for each fix.
- **HR «المتقدمون» tab:** paged list with preferences, flags and channel; bulk screening actions reuse `RecruitmentSelectionService` transitions (statuses up to `Shortlisted` = admitted to the exam).
- **Tests:** `Hcm031IntakeTests` — preferences validation, duplicate, closed intake refused, projection states, the RequisitionId-null fixes.

Commit: `feat(hcm031): competition intake with ordered line preferences`.

## P3 — Exams: seating, admission cards, attendance, anonymous double marking (AC-HCM-31b, 31c)

- Migration `HCM031_Exams`.
- **`ExamSeatingService.SeatAsync(competitionId)`:**
  - assigns admitted applicants (`Shortlisted`) to sessions by capacity;
  - deterministic order (by exam number); stable seat numbers;
  - refuses when total capacity < applicants (Arabic message with the shortfall);
  - re-seating is allowed only before cards are issued.
- **Exam numbers:** cryptographically random, fixed length, unique per competition, unrelated to application order or identity.
- **Admission card PDF** (`ExamAdmissionCardDocument`, `HrOfficialDocumentLayout`, Arabic RTL):
  - competition, candidate name, exam number, session type, date/time, venue, seat, instructions (options text);
  - hash + verification code at issue (the PLAN-29 pattern);
  - **no national ID**;
  - download for HR and for the candidate in `/careers/my` (P6);
  - an event for the PLAN-30 notifications (P6).
- **Attendance:** HR marks Present/Absent per session (bulk). `Absent` → no marking, outcome `Absent` later.
- **Marker role and policy:**
  - new role `hr-exam-marker` (`HrManagementRoles`) and policy `HrCoreExamMarking`;
  - the marker gets **only** the marking screen. Verify that the module filter and gateway rules let markers reach `ExamMarking/*` and nothing else in HR.Core; adjust only what is specific to this role (see Stop);
  - personas `a-exam-marker`, `a-exam-marker-2`, `a-exam-marker-3` (`passwordEnv` only), with catalog counts updated in the four places.
  - Assigning a marker who also holds HR read roles is allowed but shows the warning «يملك هذا المصحح صلاحية الاطلاع على هوية المتقدمين» and is audited.
- **`ExamMarking/Index|Session|Paper`:**
  - lists the marker's assigned sessions and papers **by exam number only** — no name, national ID, application id or channel in HTML, JSON or URLs. Use the `ExamCandidate` id in URLs and never expose the application id;
  - First and Second rounds are blind;
  - after both: `|a − b| > ReconcileThreshold` → the paper enters the reconciliation queue for the `Reconciler`; otherwise the exam score is computed by `ReconcileRule`;
  - scores are locked when the session is `MarkingClosed`;
  - every submission is audited (marker, round, time — not the identity of the candidate).
- **Tests:**
  - `Hcm031SeatingTests` — 120 across 2 halls (**31b**), capacity shortfall, deterministic, numbers unique and random;
  - `Hcm031MarkingTests` — blindness, distinct markers, reconciliation trigger (**31c**), each rule, locking, absentees;
  - `Hcm031MarkerAccessTests` — the policy denies marker access to `RecruitmentApplications`, `Competitions` and the portal data, and the marking responses contain no identity fields (assert on rendered HTML and JSON);
  - `Hcm031AdmissionCardTests` — PDF, hash stable, no national ID.

Commits:
- `feat(hcm031): exam sessions, seating and admission cards`
- `feat(hcm031): anonymous double marking with reconciliation and marker role`

## P4 — Results, approval, publication, objections, versions (AC-HCM-31d, 31e)

- Migration `HCM031_Results` (versions, results, allocation, reserve entries, appeal kind columns).
- **`CompetitionResultsService.ComputeDraftAsync`:**
  - `FinalScore` from the line criteria weights. Reuse `RecruitmentScoringService` / `AssessmentScoringRules` for the non-exam stages; the exam stage takes the exam score normalized to the stage weight.
  - Outcome `Passed` when ≥ the pass mark, `Failed`, or `Absent`.
  - `OverallRank` by `FinalScore` desc, then `TieBreakOrder`. A tie that remains after the order gets the same rank and is flagged for HR («تعادل يتطلب قراراً») — no silent ordering.
  - Writes a `Draft` version with a canonical-JSON SHA-256.
- **Workflow `RECRUITMENT-RESULTS-APPROVAL`:** a committee step, then `hr-management-approver`.
  - For the committee step, reuse the PLAN-28 committee if a session can be linked to a competition with a small change. Otherwise use a role step with distinct approvers (SoD). Decide and record.
  - Approval satisfies the PLAN-28 committee gate for the attached requisitions (P0 analysis); test it.
- **Publish:**
  - version → `Published`, the previous one → `Superseded`;
  - public page `/careers/competitions/{id}/results`: exam number + outcome only (no names, no scores), paged, with the publication date and the objection deadline;
  - `CompetitionResultsDocument` PDF of the same list;
  - an event per applicant for the notifications (P6).
- **Objections** (`CompetitionObjectionService`):
  - filed by the candidate (portal, P6) or HR on behalf, while `now ≤ PublishedAt + ObjectionWindowDays` of the **first** published version. A later version does not reopen the window — record this;
  - after the window: refused with an Arabic message (**31e negative**);
  - one pending objection per application;
  - review by the PLAN-28 independent reviewer and workflow.
  - **Upheld:** the reviewer enters an `ObjectionCorrection` score (or orders re-marking by a new marker; choose one and record) → recompute → a **new Draft version** with `Reason`, which goes through the results approval again → publish supersedes. Keep a diff log of rank and outcome changes between versions.
  - **Dismissed:** no change.
  - **Finalization is blocked while any objection is pending.**
- **Tests:**
  - `Hcm031ResultsTests` — weights, normalization, pass mark, tie handling, hash, immutability after publish, approval/SoD, public page without names (**31d**);
  - `Hcm031ObjectionTests` — within/after the window (**31e**), upheld → new version with diff, dismissed, finalize blocked while pending.

Commits:
- `feat(hcm031): competition results — compute, approve, publish, public list`
- `feat(hcm031): result objections with re-computation and versioned results`

## P5 — Finalize, allocation, reserve, recall, bulk offers (AC-HCM-31f, 31g)

**`CompetitionAllocationService.FinalizeAsync`.** Allowed once the window has passed and no objection is pending. Idempotent; `Finalized` once.

1. Take `Passed` candidates of the current published version in rank order.
2. Each candidate takes the **highest-preference line with remaining capacity**.
3. Within a line, assign the allocated candidates to the attached requisitions in a deterministic order. Default: requisition number. If lines span org units and you add an org-unit preference, record it.
4. **Allocated:**
   - `RequisitionId` = the assigned requisition;
   - `CompetitionLineId` set;
   - status `Ranked`;
   - `Rank` = overall rank (respect the unique index);
   - an allocation row.
5. **Passed but not allocated:** a `CompetitionReserveEntry` on each preferred line they passed (`LineRank` by overall rank), `ReserveUntil = finalize date + ReserveValidityMonths`, status `ReserveListed`.
6. **Failed / Absent / passed with no preferred line left and no reserve:** `Rejected` with kind `CompetitionResult`.

**Bulk offers** (`CompetitionOfferService`):
- `CreateDraftsAsync(competitionId, lineId?)` calls `RecruitmentOfferService.CreateDraftAsync` for each allocated application without an active offer:
  - grade = line grade;
  - salary = the Payroll grade lookup by a configured rule (`Recruitment:Competition:DefaultOfferSalary` = `GradeMin` | `GradeBase`);
  - start date and validity from inputs.
- Per-item results; failures are listed, never silent.
- `SubmitDraftsAsync` submits each draft into the existing approval.
- Optional batch workflow — see decision 7.

**Reserve recall** (`CompetitionReserveService`):
- **Hook:** offer `Declined` / `Expired` / `Revoked`, or withdrawal of an allocated application, on a competition requisition.
- **Recall:** take the first `Active` entry of the **same line** with `ReserveUntil ≥ today` by `LineRank`:
  - assign the vacated requisition;
  - status `Ranked`;
  - entry `Recalled`;
  - other entries of that applicant → `Removed`.
- Then the PLAN-27 behaviour: a suggestion, or an auto-draft when `Recruitment:AutoDraftNextOffer`.
- **No valid reserve:** no recall (**31g negative**). Raise an HR-visible notice on the competition (and a message if PLAN-30 has an HR channel); the requisition stays vacant.
- **Expiry:** extend `RecruitmentReserveExpiryJob` (or add a pass) to expire competition entries → applicant `Rejected` (`ReserveExpired`) when no entry remains.
- Single-requisition reserve behaviour must not change (regression).

**HR «التخصيص والعروض» tab:** finalize (with an in-page confirm), the allocation table per line, reserve lists, bulk draft/submit with per-item results, the recall log.

**Tests:**
- `Hcm031AllocationTests`:
  - lines 3 + 2 with preferences → 5 `Ranked`, distributed by preference, and reserve (**31f**);
  - ties;
  - capacity exhaustion;
  - idempotency;
  - blocked while an objection is pending.
- `Hcm031ReserveRecallTests`:
  - decline → first reserve of the same line recalled (**31g**);
  - expired reserve → none;
  - recalled applicant removed from other lines;
  - single-requisition reserve unchanged.
- `Hcm031BulkOfferTests`: drafts, salary rule, failures listed, submit into the existing approval.

Commits:
- `feat(hcm031): finalize — allocation by merit and preference, line reserve lists`
- `feat(hcm031): bulk offer drafts and line-level reserve recall`

## P6 — Candidate experience and integration (PLAN-29/30)

**`/careers/my`** (OTP session) gains a competition section:
- preferences;
- the admission card download;
- results: score, rank and outcome, shown only after publication;
- the objection form within the window (after it: no button, plus «انتهت مدة الاعتراض في {date}»);
- the allocation or reserve status.

Extend `CandidatePublicStatusProjection` with the competition states:

| State | Arabic |
|-------|--------|
| applied | «قيد المراجعة» |
| admitted | «مقبول للاختبار — {date}» |
| results pending | «بانتظار النتائج» |
| results published | «النتائج منشورة — راجع نتيجتك» |
| objection pending | «اعتراضك قيد المراجعة» |
| allocated | «مرشح للتعيين على {line}» |
| reserve | «في قائمة الاحتياط حتى {date}» |
| not successful | «غير ناجح» |

**Notifications** (PLAN-30 planner and templates):
- `ADMISSION_CARD_READY`, `RESULTS_PUBLISHED`, `OBJECTION_DECIDED`, `COMPETITION_ALLOCATED`, `COMPETITION_RESERVE`, `COMPETITION_RESERVE_RECALLED`, `COMPETITION_NOT_SUCCESSFUL`;
- **no score, rank or national ID** in any message. Extend the PII guard test.

**Retention and DSAR** (PLAN-30):
- competition applications use the competition's `FinalizedAt` / `CancelledAt` as the trigger date;
- anonymization covers preferences, exam candidates (exam number kept, identity link cleared with the application), objections text and admission-card data;
- the DSAR export includes exam numbers, scores, results and allocation.

**Hire flow:** an allocated, accepted competition application goes through PLAN-29 hire closure and HR.Contracts unchanged. Prove it with one integration test from allocation to `ICandidateAccepted` v4.

**Tests:** `Hcm031PortalTests`, `Hcm031NotificationTests`, the retention and DSAR additions.

Commit: `feat(hcm031): candidate portal, notifications, retention and DSAR for competitions`.

## P7 — Integration, scale, acceptance

**Integration (PostgreSQL)** — `Hcm031IntegrationTests`:
- unique constraints (one application per person per competition, exam numbers, score rounds);
- concurrent marking submissions;
- finalize idempotency under two concurrent calls (row lock or optimistic concurrency);
- end-to-end allocation → offer → accept → hire event.

**Scale:**
- `perf/hcm031-competition-scale.ps1` runs a `Hcm031Perf` test category (Testcontainers): 10,000 applicants, 4 lines, 20 sessions.
- Seating, results computation and allocation together must take **< 60 s**. Write the timings to `artifacts/hcm031-perf/` and assert the limit.
- **Set-based work only:** no per-row `SaveChanges`; batches of ≤ 1,000 with change tracking off where safe; projections instead of full entity graphs; no N+1.

**Acceptance** — `src/Tests/HRManagement.AcceptanceTests/AcHcm31CompetitionTests.cs`:
- covers AC-HCM-31a..g from the plan, adapted to this prompt;
- `EvidenceRecorder("AC-HCM-31", correlationId)`;
- personas: HR core, the management approver, markers 1–3, and the PLAN-28 compliance reviewer for objections;
- the candidate side through the gateway `/careers` with OTP from the FileDrop path (PLAN-30 handoff);
- deterministic, idempotent seed in `AcceptanceDataSeeder`: approved, unpublished requisitions (3 + 2) with graded positions and criteria with an exam stage; a legislation source; acceptance-only legal input values;
- for **31b**, seed 120 applicants directly (documented test data), not through the UI;
- if the stack is available, run with `RUN_ACCEPTANCE=true` (re-import Keycloak for the new personas) and produce `acceptance-evidence/AC-HCM-31/`. Otherwise report that it was not run and why.

**Regression** — green or matching the baseline:
- `Hcm026`–`Hcm030`, `AcHcm26`–`AcHcm30` where runnable
- `Fun001`, `HireSagaRecruitmentLineage`, `PublicCareers`, `EssTalent`
- the reserve and offer tests

List every modified test with a one-line justification.

Commit: `test(hcm031): integration, scale and acceptance AC-HCM-31a..g`.

## P8 — Docs, validate script, independent review, final report

- **`scripts/hcm031-validate.ps1`** (pattern `hcm030-validate.ps1`):
  - checks the migrations and key files;
  - checks the per-environment options (no legal defaults in `appsettings.Production.json` / `Staging`);
  - runs the `Hcm031` unit tests, the a11y scan with the new public views, and the PII guard.
- **`docs/acceptance-plans/execution/PLAN-31-EXECUTION.md`:**
  - prerequisites: Keycloak re-import, the acceptance legal inputs, the FileDrop path;
  - commands and personas;
  - Arabic manual steps: create competition → attach requisitions → approve → announce → apply with preferences → seat and cards → attendance → double marking and reconciliation → results draft → approve → publish → objection → new version → finalize → bulk offers → decline → recall;
  - the perf command and the evidence folder.
- **`PLAN-31-hcm-competitions-exams.md`:** status `Implemented (acceptance evidence pending)` or `Implemented`; "Implementation notes / deviations" (decisions 1–9, plus every judgement call); the deferred follow-ups listed explicitly.
- **`docs/syr/legal-source-binding.md`:** a competition section listing which legislation source is required and every legal input «يُحدَّد من المرجع المعتمد عند اعتماد D-2». No invented articles or numbers.
- **Other docs:**
  - `docs/HCM_001_RECRUITMENT.md` — G-19 addressed;
  - `docs/HIRE_ONBOARDING_GUIDE.md` — the competition path;
  - `docs/HIRE_RBAC_MATRIX.md` — the marker role, policies, SoD;
  - `docs/HCM_001_GAP_STUDY.md` — G-19 status, D-2 note;
  - `docs/MESSAGE_EVENT_MATRIX.md` — the new application events and notifications;
  - `docs/DATABASE_SCHEMA_INVENTORY.*` — with the generator if one exists.
- **Independent review:** spawn a fresh subagent that reads only this prompt, the plan, and `git diff main...HEAD`. It reports gaps against decisions 1–9, the acceptance scenarios, anonymity, PII and the single-requisition regression — **correctness and requirements only, no style**. Fix real gaps, rerun the affected tests, and record the review and your responses in the progress file.
- **`reports/plan31-final-report.md`** (English; quote Arabic UI strings as-is):
  1. Summary per phase.
  2. Commits (`git log --oneline main..HEAD`).
  3. Migrations and data steps.
  4. Configuration keys and defaults per environment (state explicitly: no legal defaults in Production).
  5. Roles, policies, personas; the Keycloak re-import.
  6. Test results vs. baseline; modified tests with justification.
  7. Scale timings.
  8. Acceptance: run or not (and why), evidence path.
  9. Decisions and deviations.
  10. Deferred follow-ups and `// PLAN-31 follow-up:` markers (file:line).
  11. Manual test script (Arabic UI paths).

Commit: `docs(hcm031): plan status, execution guide, legal binding, RBAC, validate script, final report`.

## Definition of done

- A multi-vacancy competition runs from announcement to bulk offers.
- Marking is anonymous, blind and double, with reconciliation.
- Results are approved, published without names, objectable within the window, and versioned.
- Allocation follows merit and preference; reserves are recalled within validity, and expired reserves are not.
- No legal value is invented; every legal input is per competition, with no Production defaults.
- No message carries a score, rank or national ID.
- The single-requisition recruitment path is unchanged (regression green).
- Build has 0 errors; `has-pending-model-changes` reports none; unit and integration tests pass; scale < 60 s; `scripts/hcm031-validate.ps1` passes.
- Acceptance is written, and either run with evidence or explicitly reported as not run.
- `reports/plan31-progress.md` shows every phase done, and `reports/plan31-final-report.md` exists.

Then reply with a short summary and the path of the final report.
