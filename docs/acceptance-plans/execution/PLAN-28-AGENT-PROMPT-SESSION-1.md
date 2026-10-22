# Task (Session 1 of 2): PLAN-28 Part 1 — Selection committee as an audited decision body

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 1 of 2 for PLAN-28.**

- **This session implements** the selection-committee half:
  - Step 0 (baseline)
  - **A** — committee model
  - **B** — server-side approval checks
  - **C** — ranking gate
  - **D** — minutes PDF
  - **E** — UI
  - unit tests
- **Session 2 will implement:** the appeal half (compliance access, single appeal path, reviewer identity, explicit outcomes, rejection tracking), the acceptance tests for all of PLAN-28, and docs. **Do not implement those here.**
- At the end, write a handoff file for session 2.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md` (Arabic) — including the «تعديلات قبل التنفيذ» note at the top. This prompt refines the plan. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — gap **G-10** and decision **D-5**.
3. `reports/plan26-session1-handoff.md`, `reports/plan27-session1-handoff.md`, and the "Implementation notes / deviations" sections of PLAN-26 and PLAN-27. They describe conventions you must reuse: `RecruitmentOptions`, scoring, `OfferEligibilityRules`, `RecruitmentRequisitionCascade`, jobs, personas.
4. Read these fully before changing them:
   - Domain: `Entities/{SelectionCommitteeSession,SelectionCommitteeMember,RecruitmentRequisition,RecruitmentApplication,RecruitmentAssessmentScore}.cs`, `Services/{RecruitmentAppealWorkflowRules,SelectionWorkflowRules,OfferEligibilityRules}.cs`
   - Infrastructure: `Services/Recruitment/{SelectionCommitteeService,SelectionCommitteeLifecycleService,SelectionCommitteeWorkflowHandler,RecruitmentSelectionService,RecruitmentScoringService,RecruitmentRequisitionLifecycleService,RecruitmentOptions}.cs`, `Services/Workflow/WorkflowInstanceActionService.cs` (`ValidateSelfApprovalAsync` only), `Data/{HRCoreDbContext,WorkflowPackSeeder}.cs`, `Seeding/AcceptanceDataSeeder.cs`
   - Web: `Controllers/{SelectionCommitteesController,RecruitmentRequisitionsController,RecruitmentApplicationsController,ReportsController,LookupApiController}.cs`, `Services/{SelectionCommitteeWorkflowUiService,WorkflowInboxEligibilityService,WorkflowPanelBuilder}.cs`, `Views/SelectionCommittees/*`, `Views/RecruitmentRequisitions/{Create,Edit,Details}.cshtml`
   - `src/Shared/HRManagement.Documents.QuestPdf/*` (`HrOfficialDocumentLayout`, `HrArabicDocumentExtensions`) and how `ReportsController` produces a PDF

## Context and scope boundaries

- **PLAN-25 is deferred** (no server yet).
  - Do not change `WorkflowStageGuard`/`WorkflowEngine`/generic approval gates.
  - Committee-specific checks go into **committee services** and the entity hook `WorkflowInstanceActionService.ValidateSelfApprovalAsync` (as PLAN-27 did for offers). Nothing else in that service.
- **Out of scope:** appeals (session 2), notifications (PLAN-30 — markers only), hire/contract (PLAN-29), competitions (PLAN-31).
- Keep diffs minimal. Follow the existing style:
  - primary constructors
  - `(bool Success, string? Error)` tuples
  - `TempData` + redirect
  - Arabic user-facing messages
  - nonce CSP: no inline handlers or styles in new markup
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

## Verified repository facts

- **Enum storage is not uniform:**
  - `RecruitmentApplication.Status` is stored as int.
  - `RecruitmentRequisition.Status` and `AppealStatus` are stored as string.
  - For any new enum, choose a mapping consistent with its neighbours and state it in the handoff. **Append members at the end only.**
- **Migrations:**
  - Command: `dotnet ef migrations add HCM028_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` (after the last `HCM027_*`).
  - Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
  - `has-pending-model-changes` must report none.
- **Committee workflow** `RECRUITMENT-SELECTION-COMMITTEE` has 3 steps: (1) «تشكيل اللجنة» `hr-core`, (2) «نصاب اللجنة» `hr-core`, (3) «اعتماد مدير HR» `hr-management-approver`.
  - The quorum rule `RecruitmentAppealWorkflowRules.HasQuorum` (≥ 3 non-recused) is today enforced **only in the UI** (`SelectionCommitteeWorkflowUiService`, `WorkflowInboxEligibilityService`).
- **Committee service today:**
  - `SelectionCommitteeService.AddMemberAsync` accepts members after submit/approval, allows duplicates, and takes a raw employee GUID.
  - `SelectionCommitteeMember.Vote` is an unused string.
  - `ApprovedAt` gates nothing.
  - `RecruitmentScoringService` already rejects an assessor who is a committee member with `HasConflictOfInterest` — keep that working.
- **Current employee of the signed-in user:** `HrOrgScopeVisibility.GetEmployeeId(User)` (claim `employee_id`; acceptance personas have it). Employee lookup API: `GET /api/lookup/employees?q=&page=&pageSize=`.
- **Ranking:** `RecruitmentSelectionService.AssignRankAsync` currently allows setting a rank in any status, and only transitions `Assessed → Ranked`.
- **Tests:** unit tests use SQLite in-memory `HRCoreDbContext`.

## Stop and ask if

- a migration would rewrite or delete existing business data (duplicate committee members: see A3);
- a public message contract in `src/Shared/HRManagement.MessageContracts` must change;
- the code differs materially from what is described here.

---

## Step 0 — Baseline

1. Start from the **tip of `feature/plan-27-offer-merit-reserve`** (nothing is merged to `main`). Create branch `feature/plan-28-committee-appeal-governance`. It must be clean apart from local noise.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm02|Recruitment|Offer|Selection|Committee|Appeal|Fun001|Syr001"
   ```

   Record the results in `reports/plan28-baseline.md`. Do not fix unrelated failures.

---

## Workstream A — Committee model (G-10)

**A1. Requisition-level switch** (replaces a global `RequireCommitteeApproval`)

- New column `RecruitmentRequisition.RequiresCommitteeApproval` (bool, default false for existing rows).
- New option `RecruitmentOptions.CommitteeApprovalDefault` (default `false`; add `true` to `appsettings.Production.json` and `appsettings.Staging.json` under `Recruitment`). It sets the initial value on create.
- Editable on the requisition Create/Edit forms and on Details until the first rank is assigned on that requisition. Changing it later → «لا يمكن تغيير إلزامية اللجنة بعد بدء الترتيب.»

**A2. Session fields**

| Field | Type | Notes |
|-------|------|-------|
| `Purpose` | enum `CommitteePurpose { Interview, Ranking, Final }` | Arabic display names |
| `SubmittedAt` | DateTime? | — |
| `LockedAt` | DateTime? | — |
| `ChairEmployeeId` | Guid? | — |

**A3. Members**

- Unique index `(SessionId, EmployeeId)`. In the migration, add a Postgres-guarded pre-check that **raises** a clear exception if duplicates exist (do not delete them). Add a read-only `scripts/hcm028-find-duplicate-committee-members.ps1`, following `scripts/hcm026-find-duplicate-requisitions.ps1`.
- Rules:
  - the member must be an existing employee
  - exactly one `Chair` before submit
  - the existing `HasConflictOfInterest` ⇒ `Recused` stays

**A4. New entities** (`AuditableEntity`, tenant-filtered like their neighbours):

| Entity | Fields | Uniqueness |
|--------|--------|------------|
| `SelectionCommitteeMemberCoi` | `MemberId`, `ApplicationId`, `HasConflict` (bool), `DeclaredAt`, `RecordedBy` | `(MemberId, ApplicationId)` |
| `SelectionCommitteeVote` | `SessionId`, `MemberId`, `ApplicationId`, `Vote` (enum `Agree/Disagree/Abstain`), `Comment` (1000), `RecordedBy`, `RecordedAt` | `(MemberId, ApplicationId)` |
| `SelectionCommitteeDecision` | `SessionId`, `ApplicationId`, `Recommendation` (enum `Recommend/Reserve/Reject`), `ProposedRank` (int?), `Rationale` (2000) | `(SessionId, ApplicationId)` |

- Keep the old `SelectionCommitteeMember.Vote` string column but stop using it. Mark it `// legacy — PLAN-28`.

**A5. Domain rules `SelectionCommitteeRules`** (pure, unit-testable):

- **Eligible voters for an application** = non-recused members without a per-candidate conflict for that application.
- **A decision is supported** when Agree votes > half of the eligible voters (strict majority); abstentions count as not-Agree.
- **`ValidateForSubmit(session, members, decisions, votes, candidateApplicationIds) → string?`:**
  - quorum ≥ 3 non-recused members
  - one chair
  - a decision for **every** candidate application (status `Assessed` or `Ranked` on the requisition at submit time)
  - every decision supported
  - `ProposedRank` is required, positive and unique for `Recommend`/`Reserve`, and null for `Reject`
- **Suggested ranking:** order candidates by `AssessmentScore` descending, ties broken by earlier `AppliedAt`. It is used to pre-fill `ProposedRank` in the UI.

**A6. Services**

- `SelectionCommitteeService`:
  - `AddMemberAsync` rejects when the session is locked («تم تقديم الجلسة للاعتماد — العضوية مقفلة.»), on duplicates, and when the employee does not exist
  - new `RemoveMemberAsync` (unlocked only)
  - `SetChairAsync`
  - `RecordMemberCoiAsync`
  - `RecordVoteAsync` — records on behalf of the member, sets `RecordedBy` = the current user; rejects votes from members who are recused or conflicted for that candidate
  - `UpsertDecisionAsync`
  - all of them reject when the session is locked
- `SelectionCommitteeLifecycleService.SubmitForApprovalAsync`:
  - runs `ValidateForSubmit`
  - sets `SubmittedAt` and `LockedAt`
  - starts the workflow exactly as today (keep the existing Off/lab vs policy branches)
  - leaves `// PLAN-25: always start workflow` where the lab-only branch is
- `SelectionCommitteeWorkflowHandler`:
  - **approved** → `ApprovedAt`
  - **rejected** → `WorkflowInstanceId = null`, `SubmittedAt = null`, `LockedAt = null` (unlocked for rework), plus an audit entry

---

## Workstream B — Server-side approval checks (committee only)

- New `SelectionCommitteeLifecycleService.ApproveStepAsync(sessionId, WorkflowActionActor actor, Guid? actorEmployeeId, string? comments)` and `RejectStepAsync(...)`. They validate, then call `IWorkflowInstanceActionService`.
- Validations before approve:
  - **Step ≥ 2:** `HasQuorum` must hold (computed from the DB, not from the UI).
  - **Step 3:** the actor must not be the session creator (`CreatedBy`), and `actorEmployeeId` must not be a member of the session — «لا يجوز لعضو في اللجنة أو منشئها اعتماد الجلسة.»
- `SelectionCommitteesController.ApproveWorkflow`/`RejectWorkflow` must call these lifecycle methods (keep the org-scope check already there).
- Also add the creator check to `WorkflowInstanceActionService.ValidateSelfApprovalAsync` for entity type `SelectionCommitteeSession`, so that any other caller is covered too. That method only knows the user id, so the member check stays in the lifecycle service.
- Update `SelectionCommitteeWorkflowUiService` and the committee branch of `WorkflowInboxEligibilityService` to call the same domain/service checks, so the UI and server agree.

---

## Workstream C — Ranking gate

`RecruitmentSelectionService.AssignRankAsync(applicationId, rank, rankOverrideReason = null, actorUserId = null)`:

**Always:**
- the application status must be `Assessed` or `Ranked`, otherwise «يمكن الترتيب للطلبات المُقيَّمة فقط.»
- the existing appeal and duplicate checks stay

**When `requisition.RequiresCommitteeApproval` is true:**
- An approved session (`ApprovedAt != null`) with `Purpose` `Ranking` or `Final` must exist for the requisition. Use the latest approved one. Otherwise «يتطلب الترتيب اعتماد لجنة الاختيار أولاً.»
- The application must have a decision `Recommend` or `Reserve` in that session. A `Reject` decision → «أوصت اللجنة برفض هذا المرشح.»
- `rank` must equal `ProposedRank`, unless `rankOverrideReason` (≥ 20 chars) is given. In that case store `RankOverrideReason`, `RankOverriddenBy` and `RankOverriddenAt` on the application (new columns) and write an `EntityAuditLog` entry.

**Bulk action:**
- New `ApplyCommitteeRankingAsync(requisitionId, actorUserId)` applies `ProposedRank` to all `Recommend`/`Reserve` decisions of the latest approved session in one transaction (it uses the same validation).
- Button «تطبيق ترتيب اللجنة» on `RecruitmentRequisitions/Details` (`HrCoreWrite`).

**Compatibility:**
- Make sure PLAN-27's `OfferEligibilityRules`/best-available-rank logic and the existing tests still work. Adjust only the test setups that relied on ranking a non-Assessed application.

---

## Workstream D — Minutes PDF

- `SelectionCommitteeMinutesDocument` built with `HrOfficialDocumentLayout`/Arabic helpers, following `ReportsController`'s PDF pattern. Contents:
  - requisition number and title, session date, purpose
  - members (role, recused yes/no)
  - for each candidate: computed score, vote counts, recommendation, proposed rank, rationale
  - the approval trail from the workflow `StepHistoryJson` (step, actor name, date, comment)
- `GET SelectionCommittees/Minutes/{id}` (`HrCoreReadOnly`) streams the PDF. Available only after `SubmittedAt`.
- The PDF is generated from locked data. No file storage in this plan; leave `// PLAN-29: archive minutes via File.Storage`.

---

## Workstream E — UI (`SelectionCommittees/Details`)

Tabs or sections:

1. **Members** — employee picker (lookup API), role, chair selector, remove (unlocked only), recused badge.
2. **Candidates and votes** — a matrix of candidates × members:
   - per-candidate COI toggle and vote select, recorded by the HR secretary
   - Agree/Disagree/Abstain totals per candidate
   - candidate score shown read-only
3. **Decisions** — recommendation, proposed rank (pre-filled from the suggested ranking) and rationale.
4. **Submit panel** — the submit button and the list of unmet requirements from `ValidateForSubmit` (the server validates again).
5. **Lock banner** after submit, a «تحميل المحضر» link, and the workflow panel.

Also:

- `RecruitmentRequisitions/Details`: the `RequiresCommitteeApproval` badge/toggle, the latest committee session status, and the «تطبيق ترتيب اللجنة» button.
- On `RecruitmentApplications/Details`, when the gate is on: show the committee decision; the rank form shows `ProposedRank` and requires an override reason when different.

---

## Tests (unit, SQLite)

**`Hcm028CommitteeRulesTests`** (pure)
- quorum
- chair required
- majority with abstentions
- a conflicted member is not counted
- missing decision for a candidate
- duplicate/invalid `ProposedRank`
- `Reject` with a rank is invalid
- suggested ranking order and ties

**`Hcm028CommitteeServiceTests`**
- duplicate member rejected
- add/remove/vote/decision after lock rejected
- vote by a recused or conflicted member rejected
- workflow rejection unlocks the session

**`Hcm028CommitteeApprovalTests`**
- step-2 approval without quorum rejected **server-side**
- step-3 approval by the creator rejected
- step-3 approval by a member (employee id) rejected
- a valid path sets `ApprovedAt`

**`Hcm028RankingGateTests`**
- gate off → current behaviour (status Assessed/Ranked only)
- gate on, no approved session → rejected
- `Reject` decision → rejected
- rank ≠ `ProposedRank` without a reason → rejected; with a reason → stored and audited
- bulk apply ranks all recommended candidates
- ranking a non-Assessed application → rejected

**Minutes:** a smoke test that the PDF builds (non-empty bytes) for a seeded session.

**Regression** — green, or matching the baseline: `Hcm026`, `Hcm027`, `Hcm021`, `Hcm022`, `Wf023`, `RecruitmentScoring`, `OfferEligibility`, `Fun001`, `Syr001`. List every modified existing test with a one-line justification.

---

## Seeder

- In `AcceptanceDataSeeder` (deterministic, idempotent), add one tenant-A requisition with `RequiresCommitteeApproval = true`:
  - ≥ 3 assessed candidates
  - a committee of 3 members + chair drawn from seeded employees, **not** linked to `a-management-approver`, `br1-mgr` or `central-a`
  - left **unsubmitted**, so acceptance can drive submit → approve → rank
- Record the ids in `testdata/acceptance/recruitment-states.json` if that file exists; otherwise document them in the handoff.

---

## Commit plan

1. `feat(hcm028): committee model — purpose, chair, per-candidate COI, votes, decisions, requisition switch`
2. `feat(hcm028): committee submit/lock rules and server-side approval checks`
3. `feat(hcm028): ranking gate on committee approval + bulk apply`
4. `feat(hcm028): committee minutes PDF + UI`
5. `chore(hcm028): acceptance seed for committee-gated requisition`
6. `docs(hcm028): session 1 handoff`

Every commit builds; unit tests green at 2, 3, 4 and 5.

## Definition of done (session 1)

- A committee records per-candidate COI, votes and decisions. Once submitted it is locked, approved in 3 steps with server-side quorum and SoD checks, and produces a PDF minutes document.
- On committee-gated requisitions, ranking follows the approved committee decisions (or is documented and audited as an override).
- Build has 0 errors; `has-pending-model-changes` reports none; new and regression unit tests pass.

## Handoff (mandatory last step)

Write and commit `reports/plan28-session1-handoff.md` with:

1. Branch and commits.
2. Migration(s), columns, indexes, pre-checks.
3. New and changed types with paths, and enum storage choices.
4. `RecruitmentOptions` additions and appsettings changes.
5. Public signatures session 2 will use:
   - committee membership queries (session 2's appeal reviewer must be independent of committee members)
   - `AssignRankAsync` with override (used for appeal rank correction)
   - the lifecycle approve/reject methods
6. Seeded ids.
7. Test results vs. baseline; modified tests.
8. Deviations, known issues, `// PLAN-2x:` markers (file:line).
9. Notes for session 2.

Then reply with a short summary.
