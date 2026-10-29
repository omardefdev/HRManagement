# Task: PLAN-25 — Make recruitment approvals work with Production settings and enforce approvals on the server: always-start approval workflows, step-role and entity gates on approve and reject, orphan RECRUITMENT-HIRE removed, posting/vacancy write policies, org scope on recruitment screens (one autonomous session)

## How to run this task — read before anything else

This is a **single prompt for one autonomous session**. Run it as phases **P0–P8** in order.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx`, 0 errors.
2. Run the phase's tests plus its regression filter.
3. Commit, using the phase's commit message.
4. Update **`reports/plan25-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test and live-run results;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan25-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase.

**Context discipline.** Use subagents for broad read-only searches. On the mounted repo, use `git grep`: a recursive grep over `bin`/`obj` is very slow.

**Orchestration rules** (ultracode / workflows may be on):
- Parallel agents are allowed for:
  - read-only exploration;
  - writing **separate new test files**;
  - independent review.
- Never let two agents edit the same file concurrently.
- These edits are made **serially by you, the lead**:
  - `WorkflowStageGuard`, `WorkflowInstanceActionService`, `WorkflowPanelBuilder`
  - `HrManagementAuthorizationExtensions`
  - `HrModuleAuthorizationFilter`
  - `HRCoreDbContext`/migrations
  - `Program.cs`
  - `appsettings*.json`
  - seeders (`WorkflowPackSeeder`, `PolicyEngineDomainSeeds`)
  - `docker-compose*.yml`
  - `.github/workflows/*`
- Live acceptance runs are serial.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write a `BLOCKED:` entry in the progress file, with the question and the options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

**This plan changes shared security code.** Every behaviour change to approvals must be proved by a test on the server path, not only on the UI. Every existing workflow domain must be re-verified.

---

## Role

You are a senior .NET 10 / ASP.NET Core / EF Core (PostgreSQL) / Keycloak / xUnit / Playwright engineer, responsible for workflow and authorization correctness, in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop, machine culture ar-SA).

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-25-hcm-production-workflow-binding.md` (Arabic). This prompt refines it heavily, because PLAN-26..33 changed a lot since it was written. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md`:
   - **G-01, G-02, G-04, G-05, G-14, G-15**;
   - §5 decision **D-1**;
   - §7 completion criteria.
3. `reports/plan33-final-report.md` and `reports/plan33-progress.md` — the prod-like compose, the prod-like journey and **its recorded stop point**, the acceptance gate, `[RequiredAcceptance]`, and the inventory.
4. `reports/plan32-final-report.md` — finding **D10**: the recruitment list screens do not apply org scope.
5. The PLAN-27/28/31 final reports and progress logs: approval rules already added on the server (offer-override distinct approvers, committee quorum/member/creator checks, appeal reviewer, competition and results SoD).
6. Read fully before changing:
   - `src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowStageGuard.cs`
   - `HR.Core.Infrastructure/Services/Workflow/{WorkflowInstanceActionService,WorkflowStarter,WorkflowEngine*,WorkflowDefinitionMapper}.cs`
   - `HR.Core.Web/Services/WorkflowPanelBuilder.cs` and every `*WorkflowUiService`
   - every controller with a `CreateWorkflowActor` (Competitions, EstablishmentChanges, OfferLetters, RecruitmentAppeals, RecruitmentRequisitions, SelectionCommittees, WorkflowInstances) and every caller of `IWorkflowInstanceActionService`
   - `HR.Core.Infrastructure/Services/Recruitment/{RecruitmentRequisitionLifecycleService,RecruitmentOfferService,SelectionCommitteeLifecycleService,RecruitmentAppealService}.cs`
   - `HR.Core.Infrastructure/Services/Recruitment/Competitions/{CompetitionLifecycleService,CompetitionResultsService}.cs`
   - `HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs`
   - `HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs` (what Shadow/Enforce return, and when a `TriggerWorkflow` effect starts an instance)
   - `HR.Core.Infrastructure/Data/{WorkflowPackSeeder,PolicyEngineDomainSeeds}.cs`
   - `HR.Core.Web/Controllers/{RecruitmentApplicationsController (Hire),PublicJobPostingsController,TalentMarketplaceController,RecruitmentRequisitionsController,OfferLettersController,SelectionCommitteesController,CompetitionsController}.cs`
   - `src/Shared/HRManagement.ServiceDefaults/{HrManagementAuthorizationExtensions,HrModuleAuthorizationFilter,HrOrgScopeVisibility,KeycloakRoleMapping}.cs`, `HR.Core.Infrastructure/OrgScopeService.cs`
   - `appsettings.json` / `appsettings.Production.json` / `appsettings.Staging.json` of HR.Core.Web
   - `docker-compose.acceptance.yml` and `docker-compose.acceptance-prodlike.yml` (PLAN-33)
   - `testdata/acceptance/personas.json` (roles; `br1-mgr` has `branch-manager` + `hr-core` and a `scopeOrgUnitId`)
   - `src/Tests/HRManagement.E2E/*` (the prod-like journey from PLAN-33)
   - `.github/workflows/{ci,e2e-journeys}.yml`

## Decisions for this implementation (record them in P0)

| # | Decision |
|---|----------|
| 1 | **D-1 adopted:** an approval workflow is **mandatory**. The policy engine may only deny, or choose a different workflow code; it never removes the workflow. `NationalScope:IsLabScale` no longer decides whether an approval starts. |
| 2 | **Status only with an instance:** an entity moves to `PendingApproval` (or the committee to "submitted") **only** together with a non-null `WorkflowInstanceId`, in the same save. A missing definition gives a clear Arabic error and no state change. |
| 3 | **Scope of the start fix:** requisition submit, offer submit (normal and override), and committee submit (the three paths still gated on `IsLabScale`). Verify that appeals (`instanceId ??= Start…`, PLAN-28) and competitions/results (PLAN-31) already always start, and route them through the same coordinator only if that is trivial. **`EstablishmentChangeLifecycleService` has the identical defect:** fix it with the same coordinator, because it blocks production the same way. |
| 4 | **Step role is enforced on the server** for approve **and** reject. The actor carries **all** roles. Admin bypass is off in Production/Staging (`Workflow:AdminBypass`). Enforcement is on for the recruitment entity types and `EstablishmentChangeRequest` (`Workflow:EnforceStepRoleEntityTypes`). Other domains (assets, transfer, …) are listed as a follow-up once their suites are re-verified — unless they pass with enforcement on, in which case include them and record it. |
| 5 | **One source of truth:** entity rules (SoD, quorum, membership, assigned reviewer, distinct approvers, org scope) live in per-entity `IWorkflowApprovalGate` classes used by `WorkflowInstanceActionService` **and** by `WorkflowPanelBuilder`. `ValidateSelfApprovalAsync` and the committee member/quorum checks now in `SelectionCommitteeLifecycleService.ApproveStepAsync` move into gates, with no behaviour lost. |
| 6 | **Reject** uses the same role check and the same gates, **except** the quorum rule: a committee may be rejected without quorum. Record any other deliberate difference. |
| 7 | **G-14:** the hire is governed by the offer approval. `R-REC-HIRE` is deactivated, the `RECRUITMENT-HIRE` definition is unpublished, and orphan instances are cancelled by the repair step. |
| 8 | **G-15 and D10 together:** explicit write policies on posting/vacancy actions, plus **org scope on the recruitment screens** for scoped users (`OrgAccessScope.MustFilter`): lists filtered, details of out-of-scope items 404, POSTs refused, approval gates check scope. Competitions stay central-only, as in PLAN-32. If an existing acceptance scenario relied on a cross-unit action, fix its **seed** to be in scope — not the rule — and record it. |
| 9 | **G-01 / CI:** all code is already committed locally, but `main` is ~115 commits ahead of `origin` and CI has not run on PLAN-26..33. **Do not push.** The final report gives the exact push/PR/CI commands for the user. Stage 0's `-warnaserror` build is run and recorded; fix only warnings this plan introduces. |
| 10 | **Production settings:** `PolicyEngine:Core.Recruitment = "Shadow"` and `Recruitment:Approvals:AlwaysStartWorkflow = true` in `appsettings.Production.json` and `appsettings.Staging.json`. These are configuration, not legal values. |

## Scope boundaries

- **Out of scope:**
  - the content of appeal decisions (PLAN-28);
  - criteria management (PLAN-26);
  - full Enforce mode outside recruitment;
  - HR.Contracts' own approval flows;
  - new product features;
  - pushing.
- Keep diffs focused. Arabic UI messages; nonce CSP; no native `confirm()`.
- **Never commit:**
  - `global.json`
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets or credentials
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

  Evidence folders under `acceptance-evidence/` are committed.

## Repository facts (verified on 2026-10-29 while PLAN-33 was running — re-verify after it merges)

**G-02 is open**
- `RecruitmentRequisitionLifecycleService.SubmitForApprovalAsync` (around lines 44–70), `RecruitmentOfferService.SubmitForApprovalAsync` (around lines 155–181) and `SelectionCommitteeLifecycleService` (around line 74) set `PendingApproval` in mode `Off` and start the workflow **only if** `IsLabScale()`.
- In Shadow/Enforce mode they store `policyResult.WorkflowInstanceId`, which **can be null**.
- `EstablishmentChangeLifecycleService` (around lines 99–112) has the same pattern.
- `RecruitmentAppealService` starts with `instanceId ??= workflowStarter.StartByWorkflowCodeAsync(...)`, and `CompetitionLifecycleService`/`CompetitionResultsService` start unconditionally.
- `appsettings.json`: `IsLabScale = true`. Production/Staging: `IsLabScale = false`, no `PolicyEngine` default mode.

**G-04 is open**
- `WorkflowStageGuard.ValidateApprove` checks only status, step skip, existence and initiator-on-later-steps — **no role check**.
- `WorkflowActionActor(UserId, UserName, Role)` has **one** role; each controller's `CreateWorkflowActor` takes the first `ClaimTypes.Role`/`role` claim.
- `WorkflowInstanceActionService.ApproveAsync` calls `ValidateSelfApprovalAsync`, which holds the creator SoD for requisition/offer/competition/establishment, the results SoD, the override distinct approvers, the committee creator check and the appeal explicit outcome. **`RejectAsync` has no guard at all.**
- `WorkflowPanelBuilder.UserHasStepRole` checks the role **in the UI only**, with admin bypass.
- Committee member and quorum checks live in `SelectionCommitteeLifecycleService.ApproveStepAsync`.

**G-05 is closed by PLAN-28:** the `HrCoreAppealReview` policy, `RecruitmentAppealsController` (actor role Compliance), the module-filter exception, and the inbox. Verify only.

**G-14 is open**
- `WorkflowPackSeeder` still defines `RECRUITMENT-HIRE` (EntityType `RecruitmentApplication`, step `hr-core`).
- `PolicyEngineDomainSeeds` rule `R-REC-HIRE` has the effect `TriggerWorkflow RECRUITMENT-HIRE`. `RecruitmentApplicationsController.Hire` calls `policyOrchestrator.EnforceAsync` in Shadow/Enforce.
- No handler exists for `RecruitmentApplication` in `WorkflowInstanceActionService`.
- `WorkflowDefinition` has `IsPublished`/`ValidFrom`/`ValidTo` (no `IsActive`).

**G-15 is open**
- `PublicJobPostingsController` and `TalentMarketplaceController` have no `[Authorize(Policy=…)]` on `Edit`, `ChangeApplicationStatus`, `DeclareCitizenCoi` or `Delete` (only the global module filter).

**D10 (PLAN-32):** `RecruitmentApplicationsController` and `RecruitmentRequisitionsController` do not apply `OrgScopeService`; `RecruitmentAppealsController` and `ReportsController` do.

**Step roles in the seeds:**

| Workflow | Steps |
|----------|-------|
| requisition / offer | `hr-core` → `hr-management-approver` |
| override | `hr-core` → `hr-management-approver` → `hr-management-approver` |
| committee | `hr-core` → `hr-core` → `hr-management-approver` |
| appeal | `compliance` |
| competition / results | `hr-core` → `hr-management-approver` |
| establishment | `hr-core` → `hr-management-approver` |

The RBAC matrix also lists `branch-manager` for step 1 — check whether `branch-manager` users without `hr-core` approve step 1 anywhere (the `br1-mgr` persona has both). If the docs and the seeds disagree, the **seed defines the rule**. Record the mismatch and fix the docs.

## Stop (record `BLOCKED:` and end the turn) if

- **PLAN-33 is not finished and merged into `main`.** It runs in the same working tree and owns the prod-like compose and journey. Do not run in parallel.
- Role enforcement breaks a non-recruitment domain in a way that would require changing its personas or business rules.
- A gate cannot be expressed without data the actor does not carry, and adding it needs a Keycloak/realm change beyond adding claims already present.
- The repair would need to rewrite business data beyond starting the missing workflow instances and cancelling orphan `RECRUITMENT-HIRE` instances.

---

## P0 — Preconditions, baseline, inventory, notes

1. `git checkout -- testdata`.
   - Confirm `feature/plan-33-verification-docs` is merged into `main` and `reports/plan33-final-report.md` exists; otherwise **stop**.
   - From `main`, create `feature/plan-25-production-approvals`.
2. **Baseline:**
   - `dotnet build HRManagement.slnx` and `dotnet build HRManagement.slnx -warnaserror` (record warnings, decision 9);
   - unit tests `Hcm02|Hcm03|Wf0|Workflow|SoD|Est0|Policy|Architecture`;
   - integration tests with the same filter;
   - record everything in `reports/plan25-baseline.md`.
3. **Inventory** (subagents) into the progress file:
   - every place that sets `PendingApproval`/submitted or starts a workflow;
   - every `CreateWorkflowActor`;
   - every caller of `ApproveAsync`/`RejectAsync`;
   - every `[HttpPost]` in HR.Core.Web without an explicit `[Authorize(Policy=…)]` or `[AllowAnonymous]`;
   - every recruitment list/details/POST without org scope;
   - stuck rows on the acceptance database (read-only SQL): entities `PendingApproval` with a null `WorkflowInstanceId`, and `RECRUITMENT-HIRE` instances `InProgress`.
4. Add a «تعديلات قبل التنفيذ (2026-10-29)» block (Arabic) to the top of the PLAN-25 doc with decisions 1–10, and note which items PLAN-26..33 already closed (G-05, committee `SubmittedAt`, appeal start). Set `Depends on` to "PLAN-26..33 منفذة".
5. Create `reports/plan25-progress.md`. If `docs/acceptance-plans/execution/PLAN-25-AGENT-PROMPT.md` (this prompt) is untracked, include it in the commit.

Commit: `docs(hcm025): baseline, inventory and pre-implementation notes`.

## P1 — Approval start coordinator (G-02)

- **`ApprovalStartCoordinator`** (HR.Core.Infrastructure/Services/Workflow; generic, so recruitment and establishment share it). One method:
  - input: entity type, id, tenant, workflow code (default and override), policy request (optional), actor, context JSON;
  - output: `(Guid? InstanceId, string? Error)`.

  Logic:
  1. if the policy mode ≠ Off → `EnforceAsync`; denied → return the error;
  2. if the policy returned an instance id → use it;
  3. otherwise, with `AlwaysStartWorkflow` (default true) → `StartByWorkflowCodeAsync`;
  4. no published definition → the Arabic error «لا يوجد تعريف مسار اعتماد منشور لهذا الإجراء.» (`FailIfDefinitionMissing`, default true).

  It never starts twice: if the orchestrator already started an instance for the same entity in this call, reuse it.
- **`RecruitmentApprovalOptions`** (`Recruitment:Approvals`): `AlwaysStartWorkflow`, `FailIfDefinitionMissing`.
- **Refactor** the requisition, offer (normal and override code) and committee submits, plus the establishment change, to call the coordinator. Set the status **only** when an instance id is returned (decision 2). Remove the duplicated `IsLabScale()`/`ResolveMode()` helpers where they become unused (keep `IsLabScale` for its other legitimate uses). Grep every remaining caller and justify each one.
- **UI:**
  - replace the «فعّل PolicyEngine… أو IsLabScale» warning with «إعادة بدء مسار الاعتماد» (`HrCoreWrite`), shown only for `PendingApproval` without an instance — it calls the coordinator;
  - remove the lab-scale wording from views.
- **Settings:** decision 10 in Production/Staging. Development and acceptance keep working with the new code path.
- **Tests:**
  - `Hcm025ApprovalCoordinatorTests` — Off/Shadow/Enforce × lab/non-lab; denial; policy-provided id; missing definition; never two instances; status unchanged on error;
  - an integration test `Hcm025ProductionApprovalIntegrationTests` on PostgreSQL with Production-like configuration: requisition submit → instance → two approvals → Approved; offer → Issued; committee; establishment change.
- **Update existing tests** that asserted the lab/non-lab difference — including the PLAN-33 integration test that proved the "non-lab warning" and the AC-21d/22e evidence notes — to the new behaviour. List each one.

Commit: `feat(hcm025): approval workflows always start (policy may deny or redirect, never skip)`.

## P2 — Repair stuck approvals and orphan instances

- **Admin endpoint** `POST /api/recruitment/admin/repair-approvals?dryRun=true` (`HrCoreDelete`; idempotent; tenant-aware). It lists, and when `dryRun=false` repairs:
  - requisitions and offers `PendingApproval` with no instance → start through the coordinator (actor = the caller, audited);
  - committee sessions submitted without an instance → the same;
  - appeals `Pending` with an assigned reviewer and no instance → start the review workflow (PLAN-28 rules);
  - establishment change requests `PendingApproval` with no instance → the same;
  - `RECRUITMENT-HIRE` instances `InProgress` → `Cancelled` with a reason (G-14).

  It returns counts and ids, and writes an `EntityAuditLog` row per repaired entity.
- **`scripts/hcm025-repair-stuck-approvals.ps1`:** calls the endpoint through the gateway with a token obtained for an admin user whose credentials are passed as parameters or read from env — **never stored**. Dry run by default, `-Apply` to execute, CSV report under `artifacts/hcm025-repair/`. If a token flow is impractical, choose the closest existing pattern and record it.
- **Tests:** dry run changes nothing; apply repairs exactly the listed rows; idempotent; a missing definition is reported, not thrown.

Commit: `feat(hcm025): repair endpoint and script for stuck approvals and orphan hire instances`.

## P3 — Server-side step roles and entity gates (G-04)

- **`WorkflowActionActor`**: becomes `(Guid? UserId, string? UserName, IReadOnlySet<string> Roles, Guid? EmployeeId)`. Keep a compatibility path only if it is needed to compile tests (prefer updating callers).
  - **One shared helper** builds it from the `ClaimsPrincipal`: all `ClaimTypes.Role` **and** `role` claims after Keycloak mapping, plus the `employee_id` claim.
  - Replace every `CreateWorkflowActor`. `RecruitmentAppealsController` stops hard-coding `Compliance`.
- **`WorkflowStageGuard.ValidateApprove` / new `ValidateReject`:**
  - `step.RoleRequired` must be in `actor.Roles` (case-insensitive);
  - admin passes only when `Workflow:AdminBypass` = true — false in Production/Staging; decide for Development/Acceptance from the inventory, preferring false;
  - applied when the instance's `EntityType` is in `Workflow:EnforceStepRoleEntityTypes` (decision 4);
  - Arabic message «هذه الخطوة تتطلب دور «{role}».».
- **`IWorkflowApprovalGate`** (HR.Core.Infrastructure/Services/Workflow/Gates):
  - shape: `string EntityType`, `Task<string?> GetBlockReasonAsync(WorkflowInstance, WorkflowStepSnapshot, WorkflowActionActor, WorkflowActionKind Approve|Reject, CancellationToken)`;
  - gates: `RequisitionApprovalGate`, `OfferApprovalGate` (creator SoD + override distinct approvers + org scope), `CommitteeApprovalGate` (creator; members cannot approve step 3; quorum ≥ 3 active members on the quorum step, approve only), `AppealApprovalGate` (the assigned independent reviewer only; explicit outcome on approve), `CompetitionApprovalGate`, `ResultsApprovalGate` (computer, submitter and tie-resolvers excluded; distinct approvers), `EstablishmentChangeApprovalGate`;
  - org-scope checks for scoped users on requisition/offer/committee gates (decision 8).
- **`WorkflowInstanceActionService`:** `ApproveAsync` **and** `RejectAsync` run the guard, then the gate for the entity type. Delete `ValidateSelfApprovalAsync` after moving every rule; a test proves each old rule still holds. Remove the duplicated checks from `SelectionCommitteeLifecycleService.ApproveStepAsync` once the gate covers them, or keep them as defence in depth and record which.
- **`WorkflowPanelBuilder`:** `CanApprove`/`CanReject` and `BlockReason` come from the same guard and gates (async), so the UI never shows an action the server refuses, and the reverse.
- **Tests:**
  - `WorkflowStageGuardRoleTests` — role present, absent, multiple roles, admin bypass on/off, a non-enforced entity type;
  - `Hcm025ApprovalGatesTests` — each gate × approve/reject, positive and negative, including every rule moved from `ValidateSelfApprovalAsync` (one test per old rule);
  - a **parity test**: for a matrix of actors × instances, the panel's `CanApprove` equals the server's approve outcome;
  - **regression** on all workflow domains: run the unit and integration tests of assets, transfer, establishment, inbox, committee, appeals, competitions and results.

Commit: `feat(wf025): server-side step-role check and per-entity approval gates on approve and reject`.

## P4 — Orphan RECRUITMENT-HIRE (G-14)

- `PolicyEngineDomainSeeds`: add `DeactivateRuleIfPresentAsync` and deactivate `R-REC-HIRE` (`AddRuleIfMissingAsync` never updates). Keep `R-REC-ORG` and `R-REC-HIRE-OFFER`.
- `WorkflowPackSeeder`: unpublish `RECRUITMENT-HIRE` (`IsPublished = false`, `ValidTo` set) **for existing databases too**: an idempotent patch on startup, following the existing seeder patch pattern.
- **Architecture test** `WorkflowDefinitionsHaveHandlersTests`: every published definition's `EntityType` is handled by `WorkflowInstanceActionService.PublishWorkflowOutcomeAsync` (or is on an explicit allow-list with a reason).
- **Tests:** in Enforce mode, a hire creates no `RECRUITMENT-HIRE` instance (**AC-HCM-25g**), and the hire still completes.

Commit: `fix(hcm025): retire the orphan RECRUITMENT-HIRE workflow and its trigger rule`.

## P5 — Write policies and org scope on recruitment screens (G-15, D10)

**Posting and vacancy write policies**
- `PublicJobPostingsController` and `TalentMarketplaceController`:
  - `Edit`/`ChangeApplicationStatus`/`DeclareCitizenCoi` → `[Authorize(Policy = HrCoreWrite)]`;
  - `Delete` → `HrCoreDelete`;
  - legacy-posting `ClosingDate` changes are audited, and shortening below today with applications present is refused (requisition-linked postings are already managed on the requisition — PLAN-26).
- **Architecture test** `HrCorePostActionsHaveExplicitPolicyTests` (UnitTests/Architecture): every `[HttpPost]` action in HR.Core.Web has an explicit `[Authorize(Policy=…)]` (on the action or the class) or a justified `[AllowAnonymous]`.
  - It is a **ratchet**: today's non-recruitment offenders are frozen in `tests/architecture/hrcore-post-policy-allowlist.txt` with a reason each.
  - Recruitment controllers may not be on the list.
  - New offenders fail.

**Org scope** (scoped users, `MustFilter`):
- `RecruitmentRequisitions`, `RecruitmentApplications`, `OfferLetters`, `SelectionCommittees` — the list filters by the funded position's org unit; out-of-scope Details → 404; POST actions refuse out-of-scope ids with a 404/Arabic error.
- Competitions: list/details for unrestricted users only (PLAN-32 rule).
- Use `OrgScopeService` the way `RecruitmentAppealsController`/`ReportsController` do.

**Tests:**
- `Hcm025OrgScopeTests` — branch scope sees own unit only, foreign details 404, foreign POST refused;
- `Hcm025PostingPoliciesTests` — `hr-management-approver` POST `PublicJobPostings/Edit` → 403 (**AC-HCM-25f**).

Commit: `feat(hcm025): explicit write policies on postings/vacancies and org scope on recruitment screens`.

## P6 — Prod-like profile, acceptance AC-HCM-25, CI

- **Use PLAN-33's `docker-compose.acceptance-prodlike.yml`.** Make sure it matches the plan: `NationalScope__IsLabScale=false`, no `PolicyEngine__*` override, so the Production values from decision 10 apply.
- Make PLAN-33's **prod-like journey** (`Category=JourneyProdLike`) **green**, and add it to the CI journey filter in `e2e-journeys.yml` (zero-skip). Validate the YAML locally.
- **`src/Tests/HRManagement.AcceptanceTests/AcHcm25ProductionApprovalTests.cs`:** `[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-HCM-25", …)`, P2/PLAN-33 seeded ids. Run on the prod-like profile.

| ID | Scenario |
|----|----------|
| 25a | requisition submit → `WorkflowInstanceId` ≠ null → two approvals → `Approved` |
| 25b | offer → two approvals → `Issued` |
| 25c | a user without `hr-management-approver` POSTs approval of step 2 → refused with the role message, state unchanged |
| 25d | committee with 3 members → quorum step approved (positive); with 2 active members → refused «النصاب غير مكتمل» (negative) |
| 25e | the assigned `a-compliance` approves (positive); another compliance user → gate refusal, and `hr-core` → role refusal (negative) |
| 25f | `a-management-approver` POST `PublicJobPostings/Edit` → 403 |
| 25g | Enforce mode: hire → no `RECRUITMENT-HIRE` instance |
| 25h | repair script dry run lists the seeded stuck rows (seed a few deliberately); apply starts their workflows |

Commit: `test(hcm025): prod-like journey green and acceptance AC-HCM-25a..h`.

## P7 — Live verification

1. **Prod-like profile** (rebuild images from this branch; re-import Keycloak only if claims or roles changed): run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`:

   ```powershell
   dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcHcm25|AcHcm21|AcHcm22|AcWf23|AcHcm26|AcHcm27|AcHcm28|AcHcm29|AcHcm30|AcHcm31|AcHcm32|AcHcm33"
   ```

   Then run the prod-like journey. **The goal: the whole recruitment path passes with Production settings.**
2. **Standard acceptance profile:** run the suites touched by role enforcement — `AcEst24`, `Ac32` logistics, `Ac33` asset loss, `AcWf23`, `Ac11` policy workflow, `Ac12` payroll SoD if relevant — plus the `Category=Journey` E2E.
3. For each failure: a test defect → fix the test; a product defect inside this plan's scope → fix it with a test; anything else → record it as `Fail`/`Blocked` with evidence. Never hide a failure.
4. Regenerate `acceptance-evidence/summary.md`, check that the AC-HCM-25 folder has the eight items, and record the per-scenario table in the progress file.

Commit: `test(hcm025): live evidence on the prod-like and standard profiles`.

## P8 — Docs, validate script, independent review, final report

- **`scripts/hcm025-validate.ps1`:**
  - no recruitment or establishment approval start remains gated on `IsLabScale` (grep);
  - the Production/Staging settings from decision 10 are present;
  - the architecture tests pass (post policies, definitions-have-handlers);
  - `WorkflowStageGuard` has the role check;
  - `R-REC-HIRE` is deactivated;
  - runs `Hcm025|WorkflowStageGuard|Architecture`.
- **Docs:**
  - `PLAN-25-hcm-production-workflow-binding.md` — status and implementation notes/deviations, including what PLAN-26..33 had already closed;
  - `docs/HIRE_RBAC_MATRIX.md` — step roles enforced on the server, gates, org scope, a corrected step-1 role list;
  - `docs/HIRE_ONBOARDING_GUIDE.md` — approvals always start, the restart button, the repair script; remove the G-02 note left by PLAN-33;
  - `docs/HCM_001_RECRUITMENT.md` and `docs/HCM_001_GAP_STUDY.md` — G-02/G-04/G-14/G-15 closed with evidence, G-01 "local commits; CI pending push", the §7 checklist updated (criterion 2 now proven on prod-like);
  - the WF-001 / workflow docs (the guard's role check, `EnforceStepRoleEntityTypes`, `AdminBypass`);
  - the establishment docs (approval start fix);
  - `docs/acceptance-plans/execution/PLAN-25-EXECUTION.md` — commands, the prod-like profile, the repair script, personas, Arabic manual steps.
- **Independent review:** a fresh subagent reads this prompt, the plan and `git diff main...HEAD`. It hunts for:
  - any path that sets `PendingApproval` without an instance;
  - any approve/reject path that bypasses the guard or a gate;
  - UI/server disagreement;
  - scope leaks;
  - rules lost in the move from `ValidateSelfApprovalAsync`;
  - non-recruitment domains broken by enforcement.

  Each finding is attacked by a refuter agent. Fix what survives, with a regression test each.
- **`reports/plan25-final-report.md`** (English; quote Arabic as-is):
  1. Summary per phase.
  2. Commits.
  3. Settings per environment.
  4. The approval-start map before/after.
  5. Gates and the rules moved, with their tests.
  6. Domains with step-role enforcement on/off.
  7. Test results vs. baseline; modified tests.
  8. Live results per profile, with evidence paths.
  9. Repair results on the acceptance database.
  10. Decisions and deviations.
  11. Follow-ups.
  12. **Push and CI instructions for the user**: the exact `git push`/PR/`gh` commands; what `ci.yml`, `codeql.yml`, `gitleaks.yml` and `e2e-journeys.yml` will run; what to check.

Commit: `docs(hcm025): plan status, RBAC/onboarding/gap updates, execution guide, validate script, final report`.

## Definition of done

- No recruitment or establishment entity reaches `PendingApproval` without a workflow instance, with Production settings.
- The prod-like journey is green and runs in CI's journey filter.
- Step roles and entity gates are enforced on the server for approve and reject, and the UI shows exactly what the server allows.
- `RECRUITMENT-HIRE` is retired, and orphans are repaired.
- Posting/vacancy writes carry explicit policies, and a ratchet test prevents new unprotected POSTs.
- Scoped users see and act only within their units on the recruitment screens.
- AC-HCM-25a..h pass live with evidence; the recruitment suites pass on the prod-like profile; the affected non-recruitment suites pass on the standard profile.
- Build has 0 errors; tests pass; `scripts/hcm025-validate.ps1` passes; `reports/plan25-final-report.md` exists with push/CI instructions.

Then reply with a short summary and the path of the final report.
