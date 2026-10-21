# Task (Session 2 of 2): PLAN-26 Part 2 — Requisition integrity, application window, intake job, tests, docs

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer working in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop available). Work autonomously and follow the rules below strictly.

**This is session 2 of 2.**

- **Session 1** already implemented, on branch `feature/plan-26-assessment-interview-requisition`:
  - the baseline (Step 0)
  - Workstream A (assessment criteria)
  - Workstream B (structured multi-assessor scoring)
  - Workstream C (interview lifecycle)
  - F1 (seeders for criteria/scores)
- **This session implements:**
  - **D** — requisition integrity
  - **E** — application window and automatic intake close
  - **F2** — closing dates in seeders
  - **G** — remaining unit tests, integration tests and acceptance tests for the whole of PLAN-26
  - **H** — script and docs
- Do not redo or restructure session 1 work. Fix it only if a test proves a defect, and report it.

## Read first (mandatory, before writing any code)

1. **`reports/plan26-session1-handoff.md`** — what session 1 built: types, signatures, `RecruitmentOptions`, migrations, deviations. Treat it as the source of truth for session 1.
2. `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md` — the plan (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
3. `docs/HCM_001_GAP_STUDY.md` — gaps **G-11** and **G-20**.
4. `docs/acceptance-plans/execution/PLAN-22-EXECUTION.md` and `docs/acceptance-plans/README.md` — execution-doc style and the acceptance-evidence contract.
5. Read these files fully before changing them:
   - `src/Services/HRCore/HR.Core.Domain/Services/{RequisitionWorkflowRules,RecruitmentApplicationIntakeRules,RecruitmentRequisitionService,EstablishmentService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/{RecruitmentRequisitionLifecycleService,RecruitmentApplicationIntakeService,RecruitmentOfferService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/{EstablishmentChangeApplier,EstablishmentChangeLifecycleService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/LeaveScheduleReconcilerJob.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs` — the `RecruitmentRequisition` configuration
   - `src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs`, `src/Services/HRCore/HR.Core.Infrastructure/Data/DemoDataSeeder.cs`
   - `src/Services/HRCore/HR.Core.Web/Controllers/{RecruitmentRequisitionsController,RecruitmentApplicationsController,PublicJobPostingsController,FundedPositionsController,PublicCareersController}.cs` and their Views
   - `src/Services/HRCore/HR.Core.Web/Program.cs`
   - `src/Tests/HRManagement.AcceptanceTests/AcHcm22RecruitmentWorkflowsUiTests.cs` (acceptance pattern — but see the Skip rule below)
   - `scripts/hcm022-validate.ps1`

## Context and scope boundaries

- **PLAN-25 is deferred** (no server environment yet).
  - Do **not** change `WorkflowStageGuard`/`WorkflowEngine`/`WorkflowInstanceActionService` gates.
  - Make no authorization changes on `PublicJobPostingsController`/`TalentMarketplaceController`.
  - Create no prod-like compose file.

  Development config (`NationalScope:IsLabScale=true`) already starts approval workflows locally.
- **Out of scope:** PLAN-27..31. Leave a `// PLAN-2x:` comment only where you touch those areas.
- Keep diffs minimal. Do not reformat or refactor unrelated code. Follow the existing style:
  - primary constructors
  - `(bool Success, string? Error)` result tuples
  - `TempData["Success"/"Error"]` + redirect
  - Arabic user-facing messages

## Verified repository facts you must respect

- Entities derive from `AuditableEntity`. Global tenant query filters are active.
- Inside background jobs, read across tenants with `.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)`.
- **`RecruitmentRequisition.Status` is stored as a string** (`HRCoreDbContext.cs` ~line 845, `HasConversion<string>()`), so SQL filters must use string literals. **Append new enum members at the end only.**
- **Migrations:**
  - Command: `dotnet ef migrations add HCM026_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`
  - Guard Postgres-only raw SQL with `if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")`.
  - `dotnet ef migrations has-pending-model-changes` must report none at the end.
- **Tests:**
  - Unit tests use SQLite in-memory `HRCoreDbContext`.
  - Integration tests have `Testcontainers.PostgreSql`.
  - Acceptance tests need the local Docker stack (`pwsh scripts/acceptance-up.ps1`) and `RUN_ACCEPTANCE=true`.
- **Authorization:** policies are `HrCoreReadOnly`, `HrCoreWrite`, `HrCoreApprove` (Admin | HrCore | HrManagementApprover | PlatformAdmin) and `HrCoreDelete`. For a conditional check, use `IAuthorizationService.AuthorizeAsync(User, policy)`.
- **UI:** MVC + Razor, AdminLTE 3, RTL Arabic. CSP is nonce-based. **No inline event handlers and no inline `style=""` attributes** in new markup.
- **Background job pattern:** copy `LeaveScheduleReconcilerJob`:
  - `BackgroundService` + `AdvisoryLeaderElection.TryRunAsLeaderAsync` + a scoped worker
  - registered in `HR.Core.Web/Program.cs` next to it
  - choose a new unique advisory-lock key (grep for collisions)
- **Configuration:** extend the `RecruitmentOptions` class created in session 1 (see the handoff). Do not create a second options class.

## Stop and ask (do not guess) if

- a migration would delete or rewrite existing business data;
- the duplicate-requisition check (D3) finds duplicates in a database you are applying migrations to;
- a public message contract in `src/Shared/HRManagement.MessageContracts` would need to change;
- something secret-looking would be committed;
- the branch state does not match the handoff file.

---

## Step 0 — Verify session 1 state

1. Check out `feature/plan-26-assessment-interview-requisition`. `git status` must be clean, and the last commit must match the handoff SHA (or be a descendant of it).
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm026|Recruitment|Hcm|SelectionWorkflow|RequisitionWorkflow|OfferWorkflow|Est0|Wf02"
   ```

   Results must match the handoff. **If anything is red that the handoff says was green, stop and report** before writing new code.
3. Confirm these session 1 deliverables exist:
   - `RecruitmentAssessmentCriteriaService`
   - `RecruitmentScoringService`
   - the interview lifecycle methods
   - `RecruitmentOptions`
   - migrations `HCM026_CriteriaGovernance`, `HCM026_AssessmentScores`, `HCM026_InterviewLifecycle`

---

## Workstream D — Requisition integrity (G-11)

**D1. Blocking statuses**

- Add `RequisitionWorkflowRules.BlockingStatuses = [Draft, PendingApproval, Approved, Published]`.
- Use it in `RecruitmentRequisitionsController.HasActiveRequisitionOnPositionAsync` and `FundedPositionsController` (~line 154).
- Grep for every other use of `ActiveStatuses`/`IsActiveStatus` and decide per call site. Report your decisions.

**D2. DB guard** (migration `HCM026_RequisitionIntegrity`)

- Filtered unique index `IX_RecruitmentRequisitions_Tenant_Position_Blocking` on `(TenantId, FundedPositionId)` with filter `"Status" IN ('Draft','PendingApproval','Approved','Published')`.
- In the same migration, Postgres-guarded, run a pre-check before creating the index: `DO $$ … RAISE EXCEPTION 'HCM026: duplicate active requisitions per funded position — run scripts/hcm026-find-duplicate-requisitions.ps1' … $$`.
- New requisition columns:
  - `CancellationReason` (500)
  - `CancelledAt`
  - `CancelledBy` (Guid?)
  - `ApplicationClosingDate` (DateOnly?)
  - `IntakeClosedAt` (DateTime?)

**D3. `scripts/hcm026-find-duplicate-requisitions.ps1`**

- Lists duplicate blocking requisitions per tenant/position using `docker exec` + `psql`, following existing scripts' connection conventions.
- Read-only.

**D4. Cancel after approval/publish**

- `RequisitionWorkflowRules.CanCancel` allows Draft, PendingApproval, Approved, Published.
- `RecruitmentRequisitionLifecycleService.CancelAsync(requisitionId, reason, actorUserId, actorCanApprove)`:
  - **Draft/PendingApproval:** `HrCoreWrite` is enough. If a `WorkflowInstanceId` exists and that instance is `InProgress`, set it to `WorkflowStatus.Cancelled` with `CompletedAt`.
  - **Approved/Published:** require `actorCanApprove` (`HrCoreApprove`) and a reason ≥ 10 chars. Reject if any application is `Hired`/`OfferAccepted`, or any offer is `Accepted`.
  - **Cascade in one transaction** (put it in an internal method reusable by D6):
    - posting `IsPublished = false`; internal vacancy `IsOpen = false`
    - offers in Draft/PendingApproval/Issued → `Revoked`, cancelling their in-progress workflow instances
    - non-terminal applications (not Hired/Rejected/Withdrawn) → `Rejected`, `ReviewedAt = now`, notes appended «إلغاء الشاغر: {reason}»
    - interview slots still `Scheduled` for those applications → `Cancelled` with the same reason
    - requisition `Cancelled`, `ClosedDate`/`CancelledAt`/`CancelledBy`/`CancellationReason` set
    - one `EntityAuditLog` entry
  - Leave `// PLAN-30: notify affected candidates` at the cascade.
- UI: a cancel form with reason on `RecruitmentRequisitions/Details`, shown according to status and policy.

**D5. Close**

- `CloseAsync` rejects when any non-converted active offer exists (Draft/PendingApproval/Issued/Accepted). Message: revoke or complete them first.
- Otherwise it moves non-terminal applications to `Rejected` with note «إغلاق الشاغر» and cancels their `Scheduled` interviews.
- Leave `// PLAN-27: reserve list` at that point.
- Do not change `RecruitmentHireClosureService`.

**D6. Establishment cascade**

- Domain `EstablishmentRecruitmentCascadeRules` + change to `EstablishmentChangeApplier.ApplyFreezeFundedPositionAsync` (~line 454). Before freezing:
  - if the position has a blocking requisition with an `Issued` or (`Accepted` and not converted) offer → fail with «لا يمكن تجميد منصب عليه عرض توظيف صادر أو مقبول — ألغِ العرض أولاً.»
  - otherwise cancel the requisition through the D4 internal cascade (system actor, reason «تجميد المنصب: {freezeReason}») in the same transaction
- If `EstablishmentChangeLifecycleService` validates change requests at submit time, add the same offer check there.
- Grep for any other code that makes a funded position non-Vacant (other than hire) and report it.

---

## Workstream E — Application window and automatic intake close (part of G-20)

**E1. Closing date at publish**

- Add `ApplicationClosingDate` to the requisition Create/Edit view models and views. It is optional in Draft and **required at first publish** (public or internal), with `>= today(UTC) + RecruitmentOptions.MinPostingDays` (add the option, default 7). Arabic error message.
- `RecruitmentRequisitionService.MapToPublicJobPosting`/`MapToInternalVacancy` copy it into `ClosingDate` instead of `null`.

**E2. Intake validation**

- `RecruitmentApplicationIntakeRules.ValidatePublishedRequisition`:
  - reject when `requisition.IntakeClosedAt != null` («التقديم على هذا الشاغر مغلق.»)
  - use `postingClosingDate ?? requisition.ApplicationClosingDate`
- `GetOpenPublishedRequisitionsAsync` excludes requisitions with `IntakeClosedAt != null`.
- The HR manual create path (`RecruitmentApplicationsController.Create`) is therefore also blocked after closing.

**E3. Extend the closing date**

- New action `RecruitmentRequisitionsController.ExtendClosingDate(id, newDate, reason)` (`HrCoreWrite`):
  - only a later date, ≥ today
  - reason ≥ 10 chars
  - updates the requisition, posting and vacancy
  - if `IntakeClosedAt` is set and the requisition is still Published with a Vacant position: clear it, set posting `IsPublished = true` and vacancy `IsOpen = true`
  - writes an `EntityAuditLog` entry
- `PublicJobPostings/Edit`: for requisition-linked postings (`RequisitionId != null`), `ClosingDate` becomes read-only and the posted value is ignored, with an info message pointing to the requisition. Legacy postings keep current behaviour. Do not change that controller's authorization.

**E4. Job**

- Worker `RecruitmentIntakeCloser.CloseExpiredAsync(DateOnly todayUtc, CancellationToken)`:
  - for Published requisitions with `ApplicationClosingDate < todayUtc` and `IntakeClosedAt == null`: posting `IsPublished = false`, vacancy `IsOpen = false`, `IntakeClosedAt = UtcNow`
  - **the requisition stays Published** (selection continues)
  - idempotent
- `RecruitmentIntakeCloseJob : BackgroundService`, following the `LeaveScheduleReconcilerJob` pattern:
  - interval `RecruitmentOptions.Jobs.IntakeCloseIntervalMinutes` (default 60)
  - enabled by `RecruitmentOptions.Jobs.IntakeCloseEnabled` (default true)
  - disable it in any test host / `WebApplicationFactory` configuration that starts HR.Core.
- UI: show an «التقديم مغلق» badge on requisition lists/Details and on `/careers` when intake is closed.

---

## Workstream F2 — Seeders (closing dates)

- In `AcceptanceDataSeeder` and `DemoDataSeeder`, published requisitions get `ApplicationClosingDate = reference date + 30 days`, and their postings/vacancies get the same `ClosingDate`.
- Keep seeding deterministic and idempotent. Do not touch session 1's criteria/score seeding unless broken.

---

## Workstream G — Tests

### Unit (SQLite pattern)

**`Hcm026RequisitionIntegrityTests`**
- second requisition on a position with a Published requisition rejected
- cancel Published → full cascade (posting, vacancy, offers, applications, interviews, audit)
- cancel blocked with a Hired application
- cancel of a Published requisition without the approve flag rejected
- close blocked with an Issued offer
- close rejects open applications

**`Hcm026ClosingDateTests`**
- publish without a closing date rejected
- closing date < today + MinPostingDays rejected
- posting/vacancy get the date
- intake rejected after the date and when `IntakeClosedAt` is set
- extension rules (earlier date rejected; reason required; reopens intake)

**`Hcm026IntakeCloserTests`**
- closes expired
- ignores future
- idempotent
- requisition stays Published

### Integration (`src/Tests/HRManagement.IntegrationTests`, Postgres Testcontainers)

- `Hcm026RequisitionIndexIntegrationTests`: the filtered unique index rejects a duplicate blocking requisition on real Postgres, and allows a new one after cancellation. Use `Skip.If` when Docker is unavailable.
- `Hcm026EstablishmentCascadeIntegrationTests`: freeze without an offer cancels the requisition; freeze with an Issued offer fails.

### Acceptance — whole of PLAN-26 (`src/Tests/HRManagement.AcceptanceTests/AcHcm26AssessmentInterviewTests.cs`)

Cover **AC-HCM-26a..i** from the plan:
- **a, b, d** — criteria (session 1)
- **c** — scoring (session 1)
- **e** — interviews (session 1)
- **f, g, h** — integrity (this session)
- **i** — intake close (this session)

Rules:
- Use `EvidenceRecorder("AC-HCM-26", correlationId)`.
- Personas: `central-a` and `br1-mgr` (hr-core, used as the two assessors), `a-management-approver`.
- When preconditions are missing use `Skip.If(condition, reason)`. **Never** use the pattern `recorder.WriteResult("Skip", …); return;` (it makes a test pass without asserting).
- If the local Docker stack is available, run with `RUN_ACCEPTANCE=true` and produce `acceptance-evidence/AC-HCM-26/`; otherwise state clearly that they were not run.

### Regression and fixes to existing tests

- Keep green, or matching the baseline: everything from session 1 plus `Hcm021`, `Hcm022`, `Wf023`, `Est0`, `Est024`, `RecruitmentHireClosure`, `RecruitmentRequisition`, `PublicCareers`, `Fun001`, `EstablishmentHire`.
- When you change an existing test because behaviour changed intentionally, list it with one line of justification.

---

## Workstream H — Script and docs (cover both sessions)

- `scripts/hcm026-validate.ps1`, modelled on `scripts/hcm022-validate.ps1`: checks that all PLAN-26 files and the four `HCM026_*` migrations exist, then runs the unit tests filtered by `Hcm026` and the `Hcm021|Hcm022` regression.
- `docs/acceptance-plans/execution/PLAN-26-EXECUTION.md`, modelled on `PLAN-22-EXECUTION.md`: prerequisites, verification commands, personas, Arabic manual steps for all of PLAN-26, evidence folder.
- `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md`:
  - set Status to `Implemented (acceptance evidence pending)` or `Implemented` if acceptance ran green
  - add an "Implementation notes / deviations" section covering **both** sessions (use the handoff for session 1)
- `docs/HCM_001_RECRUITMENT.md`: add sections for criteria versions, scoring formula, interview lifecycle, requisition integrity (cancel/close/freeze cascade) and the intake window. Mark G-03/G-08/G-11 as addressed by PLAN-26.
- `docs/HIRE_ONBOARDING_GUIDE.md`: update the selection steps (criteria before publish, scoring sheet, completing the interview, closing date and extension, cancelling a requisition). Fix only the sections you touch.
- `docs/DATABASE_SCHEMA_INVENTORY.*`: if a generator script exists under `scripts/`, run it; otherwise update the HR.Core recruitment tables manually.

---

## Commit plan for this session

7. `feat(hcm026): requisition integrity, cancel/close cascade, establishment freeze cascade`
8. `feat(hcm026): application closing date, extension, intake close job`
9. `chore(hcm026): seeders closing dates`
10. `test(hcm026): integration + acceptance`
11. `docs(hcm026): plan status, execution, guides, validate script`

Every commit must build. Tests must be green at commits 7, 8 and 11.

## Definition of done (whole PLAN-26)

- A real tenant can reach `Ranked` **without any seed data**: create criteria → publish with a closing date → apply → shortlist → schedule, complete the interview → assessors score → auto-`Assessed` → rank.
- A second blocking requisition on the same funded position is impossible (service check + DB index).
- Cancel/close/freeze cascades and the intake window behave as specified.
- `dotnet build` passes with 0 errors; `dotnet ef migrations has-pending-model-changes` reports none.
- All new unit and integration tests pass; the regression filters are green or have only baseline failures.
- `scripts/hcm026-validate.ps1` passes.
- Acceptance tests are written, and either run (evidence produced) or explicitly reported as not run.

## Final report (reply with this, in English, with Arabic UI strings quoted as-is; cover both sessions)

1. Summary per workstream A–H (A–C and F1 from the handoff, verified).
2. Commits of both sessions (sha + message).
3. All four migrations and what each changes; any data steps.
4. Test results: baseline vs. final (counts), new test classes, existing tests modified (one line of justification each).
5. Acceptance: run or not run (why); evidence folder path.
6. Deviations from PLAN-26 and from the prompts, each with a reason.
7. Decisions taken at D1/D6 call sites and other judgement calls.
8. Open items and TODOs for PLAN-27/28/30 (file:line of each `// PLAN-2x:` marker).
9. Manual test script for a human tester: Arabic UI paths, step by step, from creating criteria to ranking, plus cancel/freeze/closing-date checks.
