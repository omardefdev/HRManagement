# Task: Implement PLAN-26 — Assessment criteria, interview lifecycle and requisition integrity (HR.Core recruitment)

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer working in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop available). Implement PLAN-26 end-to-end: domain, persistence, services, UI, one background job, tests, scripts and docs. Work autonomously and follow the rules below strictly.

## Read first (mandatory, before writing any code)

1. `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md` — the plan (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — gaps **G-03, G-08, G-11, G-20**, which this work closes.
3. `docs/acceptance-plans/PLAN-22-hcm-recruitment-workflows-ui.md`, `docs/acceptance-plans/execution/PLAN-22-EXECUTION.md` and `docs/acceptance-plans/README.md` — plan style and the acceptance-evidence contract.
4. Read these files fully before changing them:
   - `src/Services/HRCore/HR.Core.Domain/Entities/{RecruitmentRequisition,RecruitmentApplication,RecruitmentAssessmentCriteria,RecruitmentInterviewSlot,SelectionCommitteeMember,FundedPosition}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Enums/{RequisitionStatus,RecruitmentApplicationStatus,InterviewSlotStatus}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Services/{SelectionWorkflowRules,RequisitionWorkflowRules,RecruitmentApplicationIntakeRules,RecruitmentRequisitionService,EstablishmentService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/{RecruitmentSelectionService,RecruitmentInterviewService,RecruitmentRequisitionLifecycleService,RecruitmentApplicationIntakeService,RecruitmentHireClosureService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/{EstablishmentChangeApplier,EstablishmentChangeLifecycleService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs` — the recruitment entity configurations
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/LeaveScheduleReconcilerJob.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs` and `src/Services/HRCore/HR.Core.Infrastructure/Data/DemoDataSeeder.cs` (the recruitment parts)
   - `src/Services/HRCore/HR.Core.Web/Controllers/{RecruitmentRequisitionsController,RecruitmentApplicationsController,PublicJobPostingsController,FundedPositionsController,LookupApiController}.cs` and their Views
   - `src/Services/HRCore/HR.Core.Web/Program.cs`
   - `src/Tests/HRManagement.UnitTests/RecruitmentSelectionServiceTests.cs` (test pattern), `src/Tests/HRManagement.AcceptanceTests/AcHcm22RecruitmentWorkflowsUiTests.cs` (acceptance pattern)

## Context and scope boundaries

- **PLAN-25 is deferred** (no server environment yet). Do **not** implement any PLAN-25 item:
  - no changes to `WorkflowStageGuard`/`WorkflowEngine`/`WorkflowInstanceActionService` gates
  - no approval coordinator
  - no authorization changes on `PublicJobPostingsController`/`TalentMarketplaceController`
  - no prod-like compose file

  Development config (`NationalScope:IsLabScale=true`) already starts approval workflows locally.
- **Out of scope** — do not implement:
  - offer merit order, reserve list, offer expiry (PLAN-27)
  - committee votes and appeal changes (PLAN-28)
  - hire/contract changes (PLAN-29)
  - candidate notifications and portal self-service (PLAN-30)
  - competitions and exams (PLAN-31)

  Where this work touches those areas, leave a clearly named extension point and a `// PLAN-27:` (etc.) comment — nothing more.
- Keep diffs minimal. Do not reformat or refactor unrelated code. Follow the existing style:
  - primary constructors
  - `(bool Success, string? Error)` result tuples
  - `TempData["Success"/"Error"]` + redirect in controllers
  - Arabic user-facing messages in the tone of existing ones

## Verified repository facts you must respect

- Solution `HRManagement.slnx`, .NET 10, nullable enabled. Warnings are not errors.
- Entities derive from `AuditableEntity`. `TenantId`/`CreatedBy`/`UpdatedBy` are set by `AuditSaveChangesInterceptor`. Global tenant query filters are active.
- Inside background jobs, read across tenants with `.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)`.
- **Enum storage is not uniform:**
  - `RecruitmentRequisition.Status` is stored as a **string** (`HRCoreDbContext.cs` ~line 845, `HasConversion<string>()`).
  - `RecruitmentApplication.Status` and `RecruitmentInterviewSlot.Status` have no conversion (stored as int).
  - Check each enum's mapping before writing any SQL filter. **Append new enum members at the end only.**
- **Migrations:**
  - Command: `dotnet ef migrations add HCM026_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`
  - Guard Postgres-only raw SQL with `if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")`.
  - At the end, `dotnet ef migrations has-pending-model-changes` (same -p/-s) must report no changes.
- **Tests:**
  - Unit tests use SQLite in-memory `HRCoreDbContext` (see `RecruitmentSelectionServiceTests`).
  - Integration tests have `Testcontainers.PostgreSql`.
  - Acceptance tests need the local Docker stack (`pwsh scripts/acceptance-up.ps1`) and `RUN_ACCEPTANCE=true`.
- **Authorization:** policies are `HrManagementPolicies.HrCoreReadOnly`, `HrCoreWrite`, `HrCoreApprove` (Admin | HrCore | HrManagementApprover | PlatformAdmin) and `HrCoreDelete` (`src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs`). For a conditional check inside an action, use `IAuthorizationService.AuthorizeAsync(User, policy)`.
- **UI:** MVC + Razor, AdminLTE 3, RTL Arabic.
  - CSP is nonce-based (`HrSecurityHeadersExtensions.cs`). `<script>`/`<style>` tags receive the nonce automatically through `CspNonceTagHelper`.
  - **No inline event handlers (`onclick=` …) and no inline `style=""` attributes** in new markup. Put JS in `wwwroot/js/*.js` or nonce'd `<script>` blocks and use CSS classes.
- **Background job pattern:** copy `LeaveScheduleReconcilerJob`:
  - `BackgroundService` + `AdvisoryLeaderElection.TryRunAsLeaderAsync` + a scoped worker service
  - registered in `HR.Core.Web/Program.cs` next to it
  - choose a new unique advisory-lock key (grep to avoid collisions)
- **Identity helpers:**
  - Employee lookup API: `GET /api/lookup/employees?q=&page=&pageSize=`.
  - Current user's employee id: `HrOrgScopeVisibility.GetEmployeeId(User)` (nullable).
  - Current user id: `sub`/NameIdentifier claim parsed as Guid (see `ParseUserId` in the controllers).
- **Configuration:**
  - Add a `Recruitment` section bound to a new `RecruitmentOptions` class (`IOptions<RecruitmentOptions>`), registered in HR.Core.
  - Existing reads of `Recruitment:LegacyHireWithoutOffer` and `Recruitment:RejectSiblingApplicationsOnHire` via `IConfiguration` must keep working.

## Stop and ask (do not guess) if

- a migration would delete or rewrite existing business data;
- the duplicate-requisition check (D3) finds duplicates in a database you are applying migrations to;
- a public message contract in `src/Shared/HRManagement.MessageContracts` would need to change;
- something secret-looking (`.env`, `.acceptance/credentials.json`, keys, connection strings) would be committed;
- the code differs materially from the "current state" described here, in a way that changes the data model.

---

## Step 0 — Local baseline (no server needed)

1. `git status`, then create branch `feature/plan-26-assessment-interview-requisition`.
2. Much recruitment code is currently uncommitted. If the working tree is dirty, first commit it **unchanged** as one commit `chore: snapshot working tree before PLAN-26`, so that PLAN-26 diffs are reviewable on their own. Respect `.gitignore`. Never stage `TestResults/`, `tmp/`, `*.log` or secrets.
3. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Recruitment|Hcm|SelectionWorkflow|RequisitionWorkflow|OfferWorkflow|Est0|Wf02"
   ```

   Record pass/fail counts and the names of failing tests in `reports/plan26-baseline.md`. Do not fix unrelated pre-existing failures; only record them.
4. Verify the "الوضع الحالي" table of the plan against the code. Note any difference in the final report.

---

## Workstream A — Assessment criteria management (G-03)

**Problem:** `SelectionWorkflowRules` requires `CriteriaVersionId` to reach `Assessed`, but criteria can only be created by `AcceptanceDataSeeder`. No UI or service exists.

**A1. Domain**

- `HR.Core.Domain/Recruitment/AssessmentCriteriaItem.cs`: `record AssessmentCriteriaItem(string Key, string NameAr, decimal Weight, decimal MaxScore, AssessmentStage Stage, bool IsMandatory = true, string? Description = null)`.
- `enum AssessmentStage { Screening = 0, Exam = 1, Interview = 2, Experience = 3, Other = 4 }` with Arabic `[Display]` names.
- `AssessmentCriteriaSerializer`: (de)serializes `CriteriaJson` with System.Text.Json (camelCase). It must also read the **legacy format** `[{"name":"المقابلة","weight":40}]`: Key = `item-{index+1}`, MaxScore = 100, Stage = Other, IsMandatory = true.
- `AssessmentCriteriaRules.Validate(IReadOnlyList<AssessmentCriteriaItem>) → string?` (Arabic message):
  - at least one item
  - keys unique (case-insensitive) and ≤ 64 chars
  - `NameAr` non-empty, ≤ 200 chars
  - `Weight > 0`, and the sum of weights = 100 (tolerance 0.01)
  - `MaxScore > 0`

**A2. Persistence**

- New columns on `RecruitmentAssessmentCriteria` (migration `HCM026_CriteriaGovernance`):
  - `IsActive` (bool, default false)
  - `LockedAt` (DateTime?)
  - `ChangeReason` (string? max 500)
  - `ApprovedBy` (Guid?)
- Data step: set `IsActive=true` on the highest `VersionNumber` per requisition.
- Add a filtered unique index on `(TenantId, RequisitionId)` where `"IsActive" = true`. Keep the existing `(TenantId, RequisitionId, VersionNumber)` unique index.

**A3. Service `RecruitmentAssessmentCriteriaService`** (Infrastructure/Services/Recruitment)

- `CreateVersionAsync(requisitionId, title, items, changeReason, actorUserId, actorCanApprove)`:
  - Validates items.
  - Requisition status must be Draft, PendingApproval, Approved or Published.
  - New `VersionNumber` = max + 1; it becomes active and the previous active version is deactivated.
  - **If the previous active version is locked** (any score exists): require `changeReason` (≥ 10 chars) and `actorCanApprove == true`, write an `EntityAuditLog`, and leave existing scores on their own version.
  - Versions are **immutable**. There is no update method.
- `GetActiveAsync(requisitionId)`, `GetVersionsAsync(requisitionId)`, `LockAsync(criteriaId)` (called by scoring on first score).

**A4. Publish gate**

- When `RecruitmentOptions.RequireCriteriaBeforePublish` (default `true`) is set, `RecruitmentRequisitionLifecycleService.PublishAsync` and `PublishInternalAsync` reject publication without an active version, with «يجب اعتماد معايير التقييم قبل النشر.».
- Update existing tests that publish without criteria: seed criteria, or set the option to `false` in that test.

**A5. UI**

- New controller `RecruitmentAssessmentCriteriaController`:
  - class-level `[Authorize(Policy = HrCoreReadOnly)]`
  - `POST Create` requires `HrCoreWrite`, plus a runtime `HrCoreApprove` check when the active version is locked
- Partial `_AssessmentCriteriaPanel.cshtml` on `RecruitmentRequisitions/Details`:
  - versions table with active and locked badges
  - "إصدار جديد" form with dynamic rows (name, weight, max score, stage, mandatory), a live weight total (JS file, no inline handlers) and a change-reason field when needed
- Show a warning on the Details page when the requisition is Approved and has no active criteria.

---

## Workstream B — Structured, multi-assessor scoring

**B1. Entity `RecruitmentAssessmentScore : AuditableEntity`** (migration `HCM026_AssessmentScores`)

- Fields:
  - `RecruitmentApplicationId`
  - `CriteriaVersionId`
  - `ItemKey` (64)
  - `AssessorUserId` (Guid)
  - `AssessorEmployeeId` (Guid?)
  - `Score` (decimal(7,2))
  - `Notes` (1000)
  - `NoConflictDeclared` (bool)
  - `SubmittedAt`
- Unique `(TenantId, RecruitmentApplicationId, AssessorUserId, ItemKey)`. Cascade delete from the application; restrict from criteria.

**B2. `RecruitmentScoringService`**

- `SubmitScoresAsync(applicationId, assessorUserId, assessorEmployeeId, IReadOnlyDictionary<string, decimal> scores, string? notes, bool noConflictDeclared)`. It rejects, with an Arabic message, when:
  - the application has no requisition or the requisition has no active criteria;
  - application status is not `InterviewScheduled` or `Assessed` (scores lock once `Ranked` or later);
  - a key is unknown, or a score is outside `0..MaxScore`;
  - `noConflictDeclared == false`;
  - the assessor is the applicant (`SourceInternalApplication.EmployeeId == assessorEmployeeId`);
  - the assessor employee is a `SelectionCommitteeMember` with `HasConflictOfInterest` in any session of this requisition;
  - items with `Stage == Interview` are submitted without a completed interview with outcome `Attended` (see C).
- On success it:
  - upserts the scores
  - locks the criteria version on the first score
  - sets `application.CriteriaVersionId` to the active version
  - recomputes the result
- `ComputeAsync(applicationId)`:
  - Per assessor: weighted total = Σ(score / MaxScore × Weight) over that assessor's items. An assessor is **complete** when all `IsMandatory` items are scored.
  - Final score = mean of complete assessors, rounded to 2 decimals with `MidpointRounding.AwayFromZero`, stored in `application.AssessmentScore`.
- **Auto-transition:** when status is `InterviewScheduled`, complete assessors ≥ `RecruitmentOptions.MinAssessors` (default 1), and the interview gate is satisfied → move to `Assessed` through `SelectionWorkflowRules`.

**B3. Close the manual bypass**

- `RecruitmentSelectionService.TransitionStatusAsync` rejects `to == Assessed` with «يتم الانتقال إلى «مُقيَّم» آلياً بعد اكتمال التقييمات.».
- The allowed-targets list shown in the UI must not offer `Assessed`.
- Remove the `RecordAssessment` action and its form from `RecruitmentApplicationsController`/`Details.cshtml`.
- Keep `RecordAssessmentAsync` only as an internal legacy method (XML doc: "legacy — not exposed; PLAN-26"). Update `RecruitmentSelectionServiceTests` accordingly.

**B4. UI** — partial `_AssessmentScoringPanel.cshtml` on `RecruitmentApplications/Details`:

- the active criteria items
- the current user's editable scores (when allowed) with the mandatory checkbox «أقر بعدم وجود تعارض مصالح مع هذا المرشح»
- a read-only summary: one row per assessor (total, complete yes/no) and the final score with the formula explained

---

## Workstream C — Interview lifecycle (G-08)

**C1. Model** (migration `HCM026_InterviewLifecycle`)

- `InterviewSlotStatus`: append `Rescheduled = 3`.
- New `enum InterviewMode { InPerson = 0, Remote = 1 }` and `enum InterviewOutcome { Attended = 0, NoShow = 1 }`.
- `RecruitmentInterviewSlot` new fields:
  - `Mode`
  - `CompletedAt` (DateTime?)
  - `Outcome` (InterviewOutcome?)
  - `CancelledReason` (500)
  - `RescheduledFromSlotId` (Guid?, self-FK, restrict)
  - `OutcomeNotes` (2000)
- New entity `RecruitmentInterviewPanelMember : AuditableEntity`: `SlotId`, `EmployeeId`, `Role` ("Chair" or "Member", max 20), `CoiDeclared` (bool). Unique `(SlotId, EmployeeId)`; cascade from the slot.

**C2. `RecruitmentInterviewService`**

- `ScheduleAsync(applicationId, scheduledAt, mode, location, IReadOnlyList<Guid> panelEmployeeIds, string? panelNotes, Guid? citizenApplicationId = null)`:
  - `scheduledAt > UtcNow`
  - application status is `Shortlisted` or `InterviewScheduled` **only**
  - no other slot with status `Scheduled` for the same application
  - at least one panel member; all members are existing employees; none is the applicant
  - transitions `Shortlisted → InterviewScheduled`
  - **Remove the current behaviour that moves `Assessed`/`Ranked` back to `InterviewScheduled`** (current lines ~32–38). Scheduling for those statuses must be rejected and leave the status unchanged.
- `RescheduleAsync(slotId, newTime, reason)`:
  - the original slot becomes `Rescheduled`; a new `Scheduled` slot is created with `RescheduledFromSlotId` and the same panel
  - the chain length is limited by `RecruitmentOptions.MaxInterviewReschedules` (default 1)
  - reason ≥ 5 chars
- `CancelAsync(slotId, reason)`: `Scheduled → Cancelled`. The application status does not change.
- `CompleteAsync(slotId, outcome, notes)`:
  - only `Scheduled` slots with `ScheduledAt <= UtcNow`
  - sets `Completed`, `CompletedAt`, `Outcome`, `OutcomeNotes`
  - `NoShow` does not change the application status (HR then decides to reject or reschedule)
- `HasAttendedInterviewAsync(applicationId)`.
- **Interview gate:** if the active criteria contain any item with `Stage == Interview`, reaching `Assessed` requires `HasAttendedInterviewAsync == true`.

**C3. UI on `RecruitmentApplications/Details`**

- Schedule form: datetime, mode, location/link, panel multi-select of employees (reuse the existing employee lookup or select-list pattern), notes.
- Per-slot actions: complete (outcome + notes), reschedule (new time + reason), cancel (reason). Show status, outcome and the reschedule chain.
- `/careers` `TrackResult.cshtml` must list only slots with status `Scheduled` (never cancelled or superseded ones).

---

## Workstream D — Requisition integrity (G-11)

**D1. Blocking statuses**

- Add `RequisitionWorkflowRules.BlockingStatuses = [Draft, PendingApproval, Approved, Published]`.
- Use it in `RecruitmentRequisitionsController.HasActiveRequisitionOnPositionAsync` and `FundedPositionsController` (~line 154).
- Grep for every other use of `ActiveStatuses`/`IsActiveStatus` and decide per call site. Report your decisions.

**D2. DB guard** (migration `HCM026_RequisitionIntegrity`)

- Filtered unique index `IX_RecruitmentRequisitions_Tenant_Position_Blocking` on `(TenantId, FundedPositionId)` with filter `"Status" IN ('Draft','PendingApproval','Approved','Published')` (string values, because Status is stored as text).
- In the same migration, Postgres-guarded, run a pre-check `DO $$ … RAISE EXCEPTION 'HCM026: duplicate active requisitions per funded position — run scripts/hcm026-find-duplicate-requisitions.ps1' … $$` before creating the index.
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
  - **Cascade in one transaction:**
    - posting `IsPublished = false`; internal vacancy `IsOpen = false`
    - offers in Draft/PendingApproval/Issued → `Revoked`, cancelling their in-progress workflow instances
    - non-terminal applications (not Hired/Rejected/Withdrawn) → `Rejected`, `ReviewedAt = now`, notes appended «إلغاء الشاغر: {reason}»
    - requisition `Cancelled`, `ClosedDate`/`CancelledAt`/`CancelledBy`/`CancellationReason` set
    - one `EntityAuditLog` entry
  - Leave `// PLAN-30: notify affected candidates` at the cascade.
- UI: a cancel form with reason on `RecruitmentRequisitions/Details`, shown according to status and policy.

**D5. Close**

- `CloseAsync` rejects when any non-converted active offer exists (Draft/PendingApproval/Issued/Accepted). Message: revoke or complete them first.
- Otherwise it moves non-terminal applications to `Rejected` with note «إغلاق الشاغر».
- Leave `// PLAN-27: reserve list` at that point.
- Do not change `RecruitmentHireClosureService`.

**D6. Establishment cascade**

- Domain `EstablishmentRecruitmentCascadeRules` + change to `EstablishmentChangeApplier.ApplyFreezeFundedPositionAsync` (~line 454). Before freezing:
  - if the position has a blocking requisition with an `Issued` or (`Accepted` and not converted) offer → fail with «لا يمكن تجميد منصب عليه عرض توظيف صادر أو مقبول — ألغِ العرض أولاً.»
  - otherwise cancel the requisition through the same internal cascade (system actor, reason «تجميد المنصب: {freezeReason}») in the same transaction
- If `EstablishmentChangeLifecycleService` validates change requests at submit time, add the same offer check there.
- Grep for any other code that makes a funded position non-Vacant (other than hire) and report it.

---

## Workstream E — Application window and automatic intake close (part of G-20)

**E1. Closing date at publish**

- Add `ApplicationClosingDate` to the requisition Create/Edit view models and views. It is optional in Draft and **required at first publish** (public or internal), with `>= today(UTC) + RecruitmentOptions.MinPostingDays` (default 7). Arabic error message.
- `MapToPublicJobPosting`/`MapToInternalVacancy` copy it into `ClosingDate` instead of `null`.

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

## Workstream F — Seeders

- `AcceptanceDataSeeder.SeedSelectionPipelineAsync` and the recruitment part of `DemoDataSeeder`:
  - write criteria in the new JSON format, marked `IsActive = true`
  - give published requisitions `ApplicationClosingDate = reference date + 30 days`
  - seed scores through the new model where applications are pre-set to `Assessed`/`Ranked`
  - keep seeding deterministic (existing `DeterministicGuid` pattern) and idempotent

---

## Workstream G — Tests

### Unit (`src/Tests/HRManagement.UnitTests`, SQLite pattern)

**`Hcm026AssessmentCriteriaRulesTests`**
- valid set
- sum of weights ≠ 100
- duplicate keys
- `MaxScore` 0
- empty list
- legacy JSON parsing

**`Hcm026CriteriaServiceTests`**
- v1 created and active
- v2 deactivates v1
- new version after lock requires reason + approve flag
- publish blocked without active criteria (and allowed when the option is false)

**`Hcm026ScoringServiceTests`**
- score > max rejected
- unknown key rejected
- COI not declared rejected
- applicant as assessor rejected
- conflicted committee member rejected
- **exact weighted calculation:** items A (weight 40, max 100) and B (weight 60, max 50)
  - assessor 1: A=80, B=40 → 80.00
  - assessor 2: A=60, B=25 → 54.00
  - final = 67.00
- `MinAssessors=2`: not Assessed after one assessor, Assessed after two
- Interview-stage item without an attended interview rejected
- scoring after `Ranked` rejected
- manual `TransitionStatusAsync(..., Assessed)` rejected

**`Hcm026InterviewLifecycleTests`**
- past time rejected
- scheduling for a `Ranked` application rejected **and status unchanged**
- second concurrent Scheduled slot rejected
- reschedule chain and max
- cancel
- completing a future slot rejected
- NoShow keeps the status

**`Hcm026RequisitionIntegrityTests`**
- second requisition on a position with a Published requisition rejected
- cancel Published → full cascade
- cancel blocked with a Hired application
- cancel of a Published requisition without the approve flag rejected
- close blocked with an Issued offer
- close rejects open applications

**`Hcm026IntakeCloserTests`**
- closes expired
- ignores future
- idempotent
- requisition stays Published
- extend date reopens intake

### Integration (`src/Tests/HRManagement.IntegrationTests`, Postgres Testcontainers where available)

- `Hcm026RequisitionIndexIntegrationTests`: the filtered unique index rejects a duplicate blocking requisition on real Postgres. Use `Skip.If` when Docker is unavailable.
- `Hcm026EstablishmentCascadeIntegrationTests`: freeze without an offer cancels the requisition; freeze with an Issued offer fails.

### Acceptance (`src/Tests/HRManagement.AcceptanceTests/AcHcm26AssessmentInterviewTests.cs`)

- Cover **AC-HCM-26a..i** from the plan with `EvidenceRecorder("AC-HCM-26", correlationId)`.
- Personas: `central-a` and `br1-mgr` (hr-core, used as the two assessors), `a-management-approver`.
- When preconditions are missing use `Skip.If(condition, reason)`. **Never** use the pattern `recorder.WriteResult("Skip", …); return;` (it makes a test pass without asserting).

### Regression and fixes to existing tests

- Run and keep green: `Hcm021`, `Hcm022`, `Wf023`, `Est0`, `RecruitmentHireClosure`, `RecruitmentRequisition`, `PublicCareers`, `Fun001`.
- When you change an existing test because behaviour changed intentionally, list it in the report with one line of justification.

---

## Workstream H — Script and docs

- `scripts/hcm026-validate.ps1`, modelled on `scripts/hcm022-validate.ps1`: checks the new files and migrations exist, then runs the unit tests filtered by `Hcm026` and the `Hcm021|Hcm022` regression.
- `docs/acceptance-plans/execution/PLAN-26-EXECUTION.md`, modelled on `PLAN-22-EXECUTION.md`: prerequisites, verification commands, personas, Arabic manual steps, evidence folder.
- `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md`:
  - set Status to `Implemented (acceptance evidence pending)` or `Implemented` if acceptance ran green
  - add an "Implementation notes / deviations" section
- `docs/HCM_001_RECRUITMENT.md`: add sections for criteria versions, scoring formula, interview lifecycle, requisition integrity and the intake window. Mark G-03/G-08/G-11 as addressed by PLAN-26.
- `docs/HIRE_ONBOARDING_GUIDE.md`: update the selection steps (criteria before publish, scoring sheet, completing the interview, closing date and extension, cancelling a requisition). Fix only the sections you touch.
- `docs/DATABASE_SCHEMA_INVENTORY.*`: if a generator script exists under `scripts/`, run it; otherwise update the HR.Core recruitment tables manually.

---

## Commit plan (small, reviewable commits on the feature branch)

0. `chore: snapshot working tree before PLAN-26` (only if needed)
1. `feat(hcm026): assessment criteria domain + rules + tests`
2. `feat(hcm026): criteria persistence, service, publish gate, UI`
3. `feat(hcm026): structured multi-assessor scoring`
4. `feat(hcm026): interview lifecycle`
5. `feat(hcm026): requisition integrity, cancel/close cascade, establishment freeze cascade`
6. `feat(hcm026): application closing date, extension, intake close job`
7. `chore(hcm026): seeders`
8. `test(hcm026): integration + acceptance`
9. `docs(hcm026): plan status, execution, guides, validate script`

Every commit must build. Tests must be green at commits 3, 5, 6 and 9.

---

## Definition of done

- A real tenant can reach `Ranked` **without any seed data**: create criteria → publish with a closing date → apply → shortlist → schedule, complete the interview → assessors score → auto-`Assessed` → rank.
- No UI or service path sets `Assessed` manually. Interviews never regress an application's status.
- A second blocking requisition on the same funded position is impossible (service check + DB index).
- Cancel/close/freeze cascades behave as specified.
- `dotnet build` passes with 0 errors; `dotnet ef migrations has-pending-model-changes` reports none.
- All new unit and integration tests pass; the regression filters are green or have only failures already recorded in the baseline.
- `scripts/hcm026-validate.ps1` passes.
- Acceptance tests written. If the local Docker stack is available, run them with `RUN_ACCEPTANCE=true` and produce `acceptance-evidence/AC-HCM-26/`; if not, state clearly that they were not run.

## Final report (reply with this, in English, with Arabic UI strings quoted as-is)

1. Summary: what was implemented, per workstream A–H.
2. Commits (sha + message).
3. Migrations created and what each changes; any data steps.
4. Test results: baseline vs. final (counts), new test classes, existing tests modified (one line of justification each).
5. Acceptance: run or not run (why); evidence folder path.
6. Deviations from PLAN-26 and from this prompt, each with a reason.
7. Decisions taken at D1/D6 call sites and other judgement calls.
8. Open items and TODOs left for PLAN-27/28/30 (with file:line of each `// PLAN-2x:` marker).
9. Manual test script for a human tester: Arabic UI paths, step by step, from creating criteria to ranking, plus cancel/freeze/closing-date checks.
