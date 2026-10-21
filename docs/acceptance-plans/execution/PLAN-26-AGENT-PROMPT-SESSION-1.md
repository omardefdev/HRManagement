# Task (Session 1 of 2): PLAN-26 Part 1 — Baseline, assessment criteria, structured scoring, interview lifecycle

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer working in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop available). Work autonomously and follow the rules below strictly.

**This is session 1 of 2.**

- **This session implements:** Step 0 (baseline) and Workstreams **A** (assessment criteria), **B** (structured scoring), **C** (interview lifecycle) and **F1** (seeders for criteria/scores).
- **Session 2 will implement:** D (requisition integrity), E (closing date + intake job), F2, integration and acceptance tests, and docs. **Do not implement any of those here.**
- At the end you must write a handoff file for session 2 (see "Handoff").

## Read first (mandatory, before writing any code)

1. `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md` — the plan (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — gaps **G-03** and **G-08**.
3. `docs/acceptance-plans/PLAN-22-hcm-recruitment-workflows-ui.md` — plan style.
4. Read these files fully before changing them:
   - `src/Services/HRCore/HR.Core.Domain/Entities/{RecruitmentRequisition,RecruitmentApplication,RecruitmentAssessmentCriteria,RecruitmentInterviewSlot,SelectionCommitteeMember}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Enums/{RecruitmentApplicationStatus,InterviewSlotStatus}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Services/SelectionWorkflowRules.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/{RecruitmentSelectionService,RecruitmentInterviewService,RecruitmentRequisitionLifecycleService}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs` — the recruitment entity configurations
   - `src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs` and `src/Services/HRCore/HR.Core.Infrastructure/Data/DemoDataSeeder.cs` (the recruitment parts)
   - `src/Services/HRCore/HR.Core.Web/Controllers/{RecruitmentApplicationsController,RecruitmentRequisitionsController,LookupApiController,PublicCareersController}.cs`
   - `Views/RecruitmentApplications/Details.cshtml`, `Views/RecruitmentRequisitions/Details.cshtml`, `Views/PublicCareers/TrackResult.cshtml`
   - `src/Services/HRCore/HR.Core.Web/Program.cs`
   - `src/Tests/HRManagement.UnitTests/RecruitmentSelectionServiceTests.cs` (test pattern)

## Context and scope boundaries

- **PLAN-25 is deferred** (no server environment yet). Do **not** change `WorkflowStageGuard`/`WorkflowEngine`/`WorkflowInstanceActionService`, and make no authorization changes on `PublicJobPostingsController`/`TalentMarketplaceController`. Development config (`NationalScope:IsLabScale=true`) already starts approval workflows locally.
- **Out of scope:** PLAN-27..31 (offer merit/reserve/expiry, committee votes and appeals, hire/contract, notifications and portal self-service, competitions). Where you touch those areas, leave a `// PLAN-2x:` comment only.
- Keep diffs minimal. Do not reformat or refactor unrelated code. Follow the existing style:
  - primary constructors
  - `(bool Success, string? Error)` result tuples
  - `TempData["Success"/"Error"]` + redirect in controllers
  - Arabic user-facing messages in the tone of existing ones

## Verified repository facts you must respect

- Solution `HRManagement.slnx`, .NET 10, nullable enabled. Warnings are not errors.
- Entities derive from `AuditableEntity`. `TenantId`/`CreatedBy`/`UpdatedBy` are set by `AuditSaveChangesInterceptor`. Global tenant query filters are active.
- **Enum storage is not uniform:**
  - `RecruitmentRequisition.Status` is stored as a **string** (`HRCoreDbContext.cs` ~line 845).
  - `RecruitmentApplication.Status` and `RecruitmentInterviewSlot.Status` are stored as int.
  - Check each mapping before writing SQL. **Append new enum members at the end only.**
- **Migrations:**
  - Command: `dotnet ef migrations add HCM026_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`
  - Guard Postgres-only raw SQL with `if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")`.
  - At the end, `dotnet ef migrations has-pending-model-changes` (same -p/-s) must report no changes.
- **Unit tests** use SQLite in-memory `HRCoreDbContext` (see `RecruitmentSelectionServiceTests`).
- **Authorization:** policies are `HrManagementPolicies.HrCoreReadOnly`, `HrCoreWrite`, `HrCoreApprove` (Admin | HrCore | HrManagementApprover | PlatformAdmin) and `HrCoreDelete`. For a conditional check inside an action, use `IAuthorizationService.AuthorizeAsync(User, policy)`.
- **UI:** MVC + Razor, AdminLTE 3, RTL Arabic.
  - CSP is nonce-based. `<script>`/`<style>` tags receive the nonce automatically through `CspNonceTagHelper`.
  - **No inline event handlers (`onclick=` …) and no inline `style=""` attributes** in new markup. Use `wwwroot/js/*.js` or nonce'd `<script>` blocks and CSS classes.
- **Identity helpers:**
  - Employee lookup API: `GET /api/lookup/employees?q=&page=&pageSize=`.
  - Current user's employee id: `HrOrgScopeVisibility.GetEmployeeId(User)` (nullable).
  - Current user id: `sub`/NameIdentifier claim parsed as Guid (see `ParseUserId` in the controllers).
- **Configuration:**
  - Create a `RecruitmentOptions` class bound to the `Recruitment` section (`IOptions<RecruitmentOptions>`), registered in HR.Core. **Design it so session 2 can extend it** (e.g. a nested `Jobs` object).
  - Existing reads of `Recruitment:LegacyHireWithoutOffer` and `Recruitment:RejectSiblingApplicationsOnHire` via `IConfiguration` must keep working.

## Stop and ask (do not guess) if

- a migration would delete or rewrite existing business data;
- a public message contract in `src/Shared/HRManagement.MessageContracts` would need to change;
- something secret-looking (`.env`, `.acceptance/credentials.json`, keys, connection strings) would be committed;
- the code differs materially from what is described here, in a way that changes the data model.

---

## Step 0 — Local baseline (no server needed)

1. `git status`, then create branch `feature/plan-26-assessment-interview-requisition`.
2. Much recruitment code is currently uncommitted. If the working tree is dirty, first commit it **unchanged** as one commit `chore: snapshot working tree before PLAN-26`. Respect `.gitignore`. Never stage `TestResults/`, `tmp/`, `*.log` or secrets.
3. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Recruitment|Hcm|SelectionWorkflow|RequisitionWorkflow|OfferWorkflow|Est0|Wf02"
   ```

   Record pass/fail counts and the names of failing tests in `reports/plan26-baseline.md`. Do not fix unrelated pre-existing failures; only record them.
4. Verify the "الوضع الحالي" table of the plan against the code (rows on criteria, score and interview). Note any difference.

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

**A2. Persistence** (migration `HCM026_CriteriaGovernance`)

- New columns on `RecruitmentAssessmentCriteria`:
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
  - **If the previous active version is locked:** require `changeReason` (≥ 10 chars) and `actorCanApprove == true`, write an `EntityAuditLog`, and leave existing scores on their own version.
  - Versions are **immutable**. There is no update method.
- `GetActiveAsync(requisitionId)`, `GetVersionsAsync(requisitionId)`, `LockAsync(criteriaId)`.

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
  - items with `Stage == Interview` are submitted without a completed interview with outcome `Attended` (Workstream C).
- On success it:
  - upserts the scores
  - locks the criteria version on the first score
  - sets `application.CriteriaVersionId` to the active version
  - recomputes the result
- `ComputeAsync(applicationId)`:
  - Per assessor: weighted total = Σ(score / MaxScore × Weight). An assessor is **complete** when all `IsMandatory` items are scored.
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
  - `NoShow` does not change the application status
- `HasAttendedInterviewAsync(applicationId)`.
- **Interview gate:** if the active criteria contain any item with `Stage == Interview`, reaching `Assessed` requires `HasAttendedInterviewAsync == true`.

**C3. UI on `RecruitmentApplications/Details`**

- Schedule form: datetime, mode, location/link, panel multi-select of employees (reuse the existing employee lookup or select-list pattern), notes.
- Per-slot actions: complete (outcome + notes), reschedule (new time + reason), cancel (reason). Show status, outcome and the reschedule chain.
- `/careers` `TrackResult.cshtml` must list only slots with status `Scheduled`.

---

## Workstream F1 — Seeders (criteria and scores only)

- In `AcceptanceDataSeeder.SeedSelectionPipelineAsync` and the recruitment part of `DemoDataSeeder`:
  - write criteria in the **new JSON format**, marked `IsActive = true` (otherwise newly seeded criteria are inactive on a fresh DB, because the A2 data step runs before seeding)
  - seed `RecruitmentAssessmentScore` rows where applications are pre-set to `Assessed`/`Ranked`, so their `AssessmentScore` is consistent with the formula
- Keep seeding deterministic (`DeterministicGuid` pattern) and idempotent.
- **Do not** add closing dates here (session 2).

---

## Tests for this session (unit, `src/Tests/HRManagement.UnitTests`, SQLite pattern)

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

**Regression** — keep green, or matching the recorded baseline: `Hcm021`, `Hcm022`, `Wf023`, `Est0`, `RecruitmentHireClosure`, `RecruitmentRequisition`, `PublicCareers`, `Fun001`, `RecruitmentSelectionService`.

When you change an existing test because behaviour changed intentionally, list it with one line of justification.

---

## Commit plan for this session

0. `chore: snapshot working tree before PLAN-26` (only if needed)
1. `feat(hcm026): assessment criteria domain + rules + tests`
2. `feat(hcm026): criteria persistence, service, publish gate, UI`
3. `feat(hcm026): structured multi-assessor scoring`
4. `feat(hcm026): interview lifecycle`
5. `chore(hcm026): seeders for criteria and scores`
6. `docs(hcm026): session 1 handoff`

Every commit must build. Unit tests must be green at commits 3, 4 and 5.

## Definition of done (session 1)

- Using only the UI and services (no seed data), a published requisition's applicant can go from shortlist to `Assessed` and be ranked:
  - criteria version created (before publishing)
  - interview scheduled, then completed as `Attended`
  - assessors submit scores
  - auto-`Assessed`
  - `AssignRank`
- No path sets `Assessed` manually, and interviews never regress an application's status.
- `dotnet build` passes with 0 errors; `has-pending-model-changes` reports none.
- New unit tests pass; regression filters are green or have only baseline failures.

## Handoff (mandatory last step)

Write `reports/plan26-session1-handoff.md`, commit it, and include in it:

1. Branch name and last commit SHA; list of commits (sha + message).
2. Migrations created (names) and what each changes, including data steps.
3. New and changed types with file paths: entities, enums, services, controllers, partial views, JS files.
4. `RecruitmentOptions`: full class shape, config keys and defaults, where it is registered — session 2 will extend it.
5. Public method signatures session 2 will reuse or touch, especially in `RecruitmentRequisitionLifecycleService`, `RecruitmentInterviewService` and `RecruitmentScoringService`.
6. Test results: baseline vs. now (counts), new test classes, existing tests modified (with justification).
7. Deviations from PLAN-26 or this prompt, with reasons; known issues; `// PLAN-2x:` markers left (file:line).
8. Anything session 2 must know before starting.

Then reply with a short summary of the handoff file contents.
