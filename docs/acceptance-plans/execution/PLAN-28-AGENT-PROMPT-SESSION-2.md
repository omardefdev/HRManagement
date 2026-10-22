# Task (Session 2 of 2): PLAN-28 Part 2 — Single, governed appeal path; acceptance tests; docs

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 2 of 2 for PLAN-28.**

- **Session 1** (branch `feature/plan-28-committee-appeal-governance`) implemented the committee half:
  - votes, decisions and per-candidate COI
  - locking
  - server-side quorum and SoD
  - the ranking gate with override
  - minutes PDF
  - acceptance seed data
- **This session implements:**
  - **F** — compliance access slice
  - **G** — rejection tracking and the appeal window
  - **H** — single appeal path with a correctly identified independent reviewer and explicit outcomes
  - **I** — hire block on pending appeals
  - **J** — acceptance tests for all of PLAN-28 (committee + appeal) and remaining tests
  - **K** — script and docs
- Do not redo session 1 work. Fix it only if a test proves a defect, and report it.

## Read first (mandatory)

1. **`reports/plan28-session1-handoff.md`** — the source of truth for session 1.
2. `docs/acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md` (Arabic), including the «تعديلات قبل التنفيذ» note. This prompt refines the plan. **Where they differ, this prompt wins.**
3. `docs/HCM_001_GAP_STUDY.md` — gaps **G-05** and **G-09**.
4. Read these fully before changing them:
   - Domain: `Entities/{RecruitmentAppeal,RecruitmentApplication}.cs`, `Enums/RecruitmentAppealStatus.cs`, `Services/RecruitmentAppealWorkflowRules.cs`
   - Infrastructure:
     - `Services/Recruitment/{RecruitmentAppealService,RecruitmentAppealWorkflowHandler,RecruitmentSelectionService,RecruitmentRequisitionCascade,ReserveListService,RecruitmentOfferService,RecruitmentHireClosureService,RecruitmentOptions}.cs`
     - `Data/WorkflowPackSeeder.cs` (`RECRUITMENT-APPEAL-REVIEW`: 1 step, role `compliance`)
     - `Services/Workflow/WorkflowInstanceActionService.cs` (read only, except the entity hook if needed)
   - Web:
     - `Controllers/{RecruitmentApplicationsController,WorkInboxController}.cs` — the appeal actions `FileAppeal`, `ApproveAppealWorkflow`, `RejectAppealWorkflow`, `DecideAppeal`, `ReopenAppeal`
     - `Services/{RecruitmentAppealWorkflowUiService,WorkflowInboxEligibilityService,HrCoreWorkInboxService,WorkflowPanelBuilder}.cs`
     - `Views/RecruitmentApplications/{Details,_AppealWorkflowPanel}.cshtml`, `Views/WorkInbox/Index.cshtml`, the shared HR.Core layout/menu
   - Authorization:
     - `src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs` (`ModuleHrCore`, `HrCoreReadOnly`, `HrCoreBranchOperation`)
     - `HrManagementRoles.cs` (`Compliance = "compliance"`)
     - `HrModuleAuthorizationFilter` (the controller exemption list)
   - Gateway: `src/Gateway/HRManagement.Gateway/appsettings.json` route `ui-hr-core` — confirm no route-level policy blocks a compliance-only user.
   - Tests: `testdata/acceptance/personas.json` (`a-compliance` has role `compliance` and an `employee_id` attribute), `scripts/prepare-keycloak-import.ps1`, the PLAN-27 acceptance tests (persona and Skip patterns).

## Context and scope boundaries

- **PLAN-25 is deferred** (no server). This session implements **only** the compliance-access slice needed for appeals (F). Do not change `WorkflowStageGuard`/`WorkflowEngine`/generic gates. Appeal-specific checks live in the appeal service/controller.
- **Out of scope:**
  - candidate self-service appeal filing on `/careers` and notifications (PLAN-30 — leave markers; the service must already support filing without a reviewer)
  - hire/contract (PLAN-29)
  - competition result objections (PLAN-31)
- Keep diffs minimal. Follow the existing style; Arabic user-facing messages; nonce CSP (no inline handlers or styles).
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

## Verified repository facts

**Today:**

- The **only** role allowed by `ModuleHrCore`/`HrCoreReadOnly`/`HrCoreBranchOperation` that can act on appeals is not `compliance`. A compliance-only user cannot open HR.Core at all.
- The reviewer is typed as a free-text GUID (`Details.cshtml` ~line 157).
- `FileAppealAsync` compares it with committee **EmployeeIds**, while `CanDecideAppeal` compares it with the actor's Keycloak **user id** (`sub`).
- Workflow approval always means "upheld".
- A legacy path, `RecruitmentSelectionService.ReopenAppealAsync` plus the `ReopenAppeal` action and form, moves a rejected application back to `UnderReview` with only `HrCoreWrite`.
- The appeal window uses `ReviewedAt`, which other operations overwrite.

**Places that set `Status = Rejected` today** — each must record the rejection (G):

| Code path | Rejection kind |
|-----------|----------------|
| `RecruitmentSelectionService.TransitionStatusAsync` (manual) | `SelectionDecision` |
| `RecruitmentRequisitionCascade.RejectOpenApplicationsAsync` (cancel and close) | `RequisitionCancelled` / `RequisitionClosed` (the caller must pass which) |
| `ReserveListService` (non-ranked siblings on hire, ~line 103) | `NotSelectedOnHire` |
| `ReserveListService` (reserve expiry, ~line 130) | `ReserveExpired` |
| `RecruitmentOfferService.RevokeAsync` without revert (~line 309) | `OfferRevoked` |
| `RecruitmentAppealService.DecideAppealAsync` / `RecruitmentAppealWorkflowHandler` (dismissed) | keep the original rejection |

**Other facts:**

- `HrOrgScopeVisibility.GetEmployeeId(User)` reads the `employee_id` claim; acceptance personas carry it.
- `RecruitmentAppealStatus` is stored as string.
- **Migrations:**
  - Command: `dotnet ef migrations add HCM028_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` (after session 1's migration).
  - Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
  - `has-pending-model-changes` must report none.

## Stop and ask if

- a migration would rewrite or delete business data (the legacy-appeal data step in H2 only **inserts** rows);
- the gateway or Keycloak setup blocks compliance users in a way that cannot be fixed in code;
- a public message contract in `src/Shared/HRManagement.MessageContracts` must change;
- the branch state does not match the handoff.

---

## Step 0 — Verify session 1 state

1. Check out `feature/plan-28-committee-appeal-governance`. It must be clean (apart from local noise), with HEAD at the handoff SHA or a descendant.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm028|Hcm027|Hcm026|Recruitment|Appeal|Committee"
   ```

   It must match the handoff. Otherwise stop and report.

---

## Workstream F — Compliance access slice (G-05)

**F1. Policies** (`HrManagementPolicies` + `HrManagementAuthorizationExtensions`):

- `HrCoreAppealReview` = Admin | HrCore | Compliance
- `HrCoreWorkInbox` = the `HrCoreReadOnly` roles + Compliance

**F2. `RecruitmentAppealsController`** — class-level `[Authorize(Policy = HrCoreAppealReview)]`. Add `"RecruitmentAppeals"` to the controller exemption list in `HrModuleAuthorizationFilter`, so the module policy does not block compliance users.

| Action | Behaviour |
|--------|-----------|
| `Index` | Pending appeals. A compliance-only user sees only appeals assigned to their `employee_id`; HR users see all appeals in their scope |
| `Details(id)` | Appeal data; the application summary **without the national ID** (name, requisition, rejection kind/date/note, assessment score summary, committee decision summary if any); the assignment and workflow panels |
| `AssignReviewer` (POST, `HrCoreWrite`) | See H2 |
| `Decide` (POST) | See H3 |
| `RequestInfo` (POST) | See H3 |
| `Resubmit` (POST, `HrCoreWrite`) | See H3 |

**F3. Move appeal handling out of `RecruitmentApplicationsController`:**

- Remove `ApproveAppealWorkflow`, `RejectAppealWorkflow`, `DecideAppeal` and `ReopenAppeal`.
- `FileAppeal` stays there (HR files on behalf of the candidate) but drops the reviewer parameter (see H1).
- `RecruitmentApplications/Details` shows the appeal status with a link to `RecruitmentAppeals/Details`.

**F4. Work inbox**

- `WorkInboxController` uses `HrCoreWorkInbox`.
- For a compliance-only user, show only `RecruitmentAppeal` items assigned to them, linking to `RecruitmentAppeals/Details`.
- Update the `RecruitmentAppeal` branch of `WorkflowInboxEligibilityService` and `RecruitmentAppealWorkflowUiService` to the new reviewer rule (H2).

**F5. Navigation and gateway**

- Add a menu entry «استئنافات التوظيف» visible to `compliance` and HR roles.
- Verify that the gateway `ui-hr-core` route lets a compliance-only user through (report what you found; change gateway config only if strictly necessary).

**F6. Persona**

- Add `a-compliance-2` (role `compliance`, tenant A, a different `employeeOrdinal`) to `testdata/acceptance/personas.json`, following existing entries: a new `passwordEnv` name, **no secret values**.
- Ensure `scripts/prepare-keycloak-import.ps1` provisions it, and document the env var.

---

## Workstream G — Rejection tracking and appeal window

**G1. New fields on `RecruitmentApplication`** (migration `HCM028_AppealGovernance`):

- `RejectedAt` (DateTime?)
- `RejectionKind` — string enum `RecruitmentRejectionKind { SelectionDecision, RequisitionCancelled, RequisitionClosed, NotSelectedOnHire, ReserveExpired, OfferRevoked }`
- `RejectionNote` (1000)

**G2. One helper for every rejection**

- Add a domain helper `ApplicationRejection.Apply(app, kind, note, nowUtc)` that sets `Status`, `ReviewedAt`, `RejectedAt`, `RejectionKind` and `RejectionNote`.
- Use it in **every** code path in the table above. Grep again for `RecruitmentApplicationStatus.Rejected` assignments, so none is missed.

**G3. Appeal window and eligibility**

- `RecruitmentAppealWorkflowRules.CanAppeal` uses `RejectedAt` (fallback: `ReviewedAt` for legacy rows) and `RecruitmentOptions.AppealWindowDays` (default 14).
- Only kinds in `RecruitmentOptions.AppealableRejectionKinds` (default: `SelectionDecision`, `NotSelectedOnHire`, `OfferRevoked`) are appealable.
- Legacy rows with a null `RejectionKind` are treated as `SelectionDecision`.
- A rejection whose kind is not appealable → «لا يقبل هذا النوع من الرفض الاستئناف.»

---

## Workstream H — Single appeal path, reviewer identity, explicit outcomes (G-09)

**H1. Filing** — `RecruitmentAppealService.FileAppealAsync(applicationId, reason, filedBy, actorUserId)`:

- `reason` ≥ 20 chars; `filedBy` is `HrOnBehalf` now (`Candidate` is reserved for PLAN-30).
- **No reviewer at filing.**
- Effects: appeal `Pending`, application `AppealStatus = Pending`.
- **No workflow yet.** The workflow starts at assignment.
- Leave `// PLAN-30: candidate files via /careers`.

**H2. Assigning the reviewer** — `AssignReviewerAsync(appealId, reviewerEmployeeId, actorUserId)`.

New appeal columns:

| Column | Type | Notes |
|--------|------|-------|
| `IndependentReviewerEmployeeId` | Guid? | — |
| `AssignedAt` | DateTime? | — |
| `AssignedBy` | Guid? | — |
| `Outcome` | string enum `RecruitmentAppealOutcome { Upheld, UpheldRankCorrection, Dismissed, UpheldNoRemedy, LegacyReopen }` | — |
| `CorrectedRank` | int? | — |
| `FiledBy` | string 20 | — |
| `InfoRequest` | string 2000 | — |
| `InfoRequestedAt` | DateTime? | — |
| `AdditionalInfo` | string 4000 | — |

- Keep the old `IndependentReviewerId` column as **legacy user id**.

Independence — reject the reviewer if the employee is:

- the applicant (internal applicant's employee)
- a member of any committee session of the requisition (use session 1's membership query)
- an interview panel member on any of the application's slots
- an assessor of the application (`RecruitmentAssessmentScore.AssessorEmployeeId`)
- the employee of the user who filed or assigned the appeal

Message: «يجب أن يكون المراجع مستقلاً عن اللجنة والمقابلة والتقييم.»

Then:

- Start `RECRUITMENT-APPEAL-REVIEW`, keeping today's Off/lab vs policy branches; leave `// PLAN-25: always start workflow`.
- Reassignment is allowed while `Pending` (audited). It cancels the in-progress workflow instance (`WorkflowStatus.Cancelled`) and starts a new one.
- UI: an employee picker (lookup API) on `RecruitmentAppeals/Details`, replacing the free-text GUID input.

**H3. Decision by the reviewer** — `DecideAsync(appealId, outcome, rationale, correctedRank, actor, actorEmployeeId)`.

Authorized only when **all** of these hold:

- the actor has the `compliance` role, or is Admin
- either `actorEmployeeId == IndependentReviewerEmployeeId`, or — legacy rows with no employee id — `actor.UserId == IndependentReviewerId`
- the appeal is `Pending` with an in-progress workflow

Otherwise «غير مصرح: يجب أن تكون المراجع المستقل المعيّن.»

Validation:

- rationale ≥ 30 chars
- `UpheldRankCorrection` requires `correctedRank` ≥ 1, and is allowed only if the application had a rank before rejection (store `RankBeforeRejection` in G2 if needed) **and** the requisition is still `Published`
- if the requisition is `Closed` or `Cancelled`, only `Dismissed` or `UpheldNoRemedy` are allowed

Then:

- store `Outcome`/`Decision`/`CorrectedRank`
- call `IWorkflowInstanceActionService.ApproveAsync`
- `RecruitmentAppealWorkflowHandler` (approved) applies the effect **from the stored `Outcome`**:

| Outcome | Effect |
|---------|--------|
| `Upheld` | application → `UnderReview`; clear `RejectedAt`/`Kind`; keep scores (assessors may resubmit) |
| `UpheldRankCorrection` | application → `Assessed`, then `AssignRankAsync(correctedRank, rankOverrideReason: rationale)` |
| `Dismissed` | stays `Rejected` |
| `UpheldNoRemedy` | stays `Rejected`; `EntityAuditLog` entry `AppealUpheldNoRemedy`; leave `// PLAN-30: alert compliance` |

In all cases: `AppealStatus = Decided`, `DecidedAt`, and an audit entry with the real actor (not the reviewer id).

**Request information** — `RequestInfoAsync(appealId, text, actor, actorEmployeeId)` (same authorization):

- rejects the workflow step with the text
- the handler (rejected branch) keeps the appeal `Pending`, sets `InfoRequest`/`InfoRequestedAt`, and clears `WorkflowInstanceId`

**Resubmit** — `ResubmitAsync(appealId, additionalInfo, actorUserId)` (`HrCoreWrite`): stores `AdditionalInfo` and starts a new workflow for the same reviewer.

**H4. Remove the legacy path, keep its history**

- Delete `ReopenAppealAsync`, the action and the form.
- In migration `HCM028_AppealGovernance` add a Postgres-guarded **insert-only** data step: for applications with `AppealNotes` not null and no `RecruitmentAppeal` row, insert a `RecruitmentAppeal` with
  - `Status = Decided`, `Outcome = LegacyReopen`, `Reason = AppealNotes`
  - `Decision = 'مسار قديم — إعادة فتح مباشرة'`, `DecidedAt = ReviewedAt`
  - the same `TenantId`
- Do not modify the applications.

**H5. Remove the direct decision path**

- `DecideAppealAsync` (no workflow) is replaced by `DecideAsync`.
- For appeals without a workflow (policy Off and not lab), `AssignReviewerAsync` must still create a workflow instance.
- If that is impossible in a configuration, decide directly with the same authorization and record it. Explain the behaviour in the report.

---

## Workstream I — Hire block on pending appeals

- `RecruitmentOptions.BlockHireOnPendingAppeal` (default `true`).
- `RecruitmentHireClosureService` and the `Hire` GET/POST in `RecruitmentApplicationsController` refuse to hire while any application on the same requisition has `AppealStatus == Pending`: «يوجد استئناف معلق على هذا الشاغر — يجب البت فيه قبل التعيين.»
- Offers remain allowed (PLAN-27 already excludes appellants from merit availability).

---

## Workstream J — Tests

### Unit (SQLite)

**`Hcm028RejectionTrackingTests`**
- every rejection path sets `RejectedAt`/`RejectionKind` (one test per path)
- window computed from `RejectedAt`
- non-appealable kinds rejected

**`Hcm028AppealLifecycleTests`**
- filing without a reviewer
- assignment independence (each rule)
- reassignment cancels the old workflow
- decide by a non-assigned compliance user rejected
- decide by a non-compliance user rejected
- each outcome's effect
- `UpheldRankCorrection` only when allowed
- closed requisition limits outcomes
- request info → resubmit
- legacy reviewer id path still works

**`Hcm028HireBlockTests`**
- hire refused with a pending appeal
- hire allowed after the decision
- option off → allowed

**`Hcm028AuthorizationTests`**
- policy `HrCoreAppealReview` accepts compliance and rejects other roles
- `RecruitmentAppeals` is exempt from the module filter while other HR.Core controllers are not (reflection or filter unit test)

**Migration data step:** a test on Postgres (integration, Testcontainers) that legacy `AppealNotes` become `LegacyReopen` appeal rows without changing applications. Use `Skip.If` without Docker.

### Acceptance — whole PLAN-28 (`src/Tests/HRManagement.AcceptanceTests/AcHcm28CommitteeAppealTests.cs`)

- Cover **AC-HCM-28a..i** from the plan, adapted to this prompt (reviewer assigned after filing; the committee gate on the seeded committee requisition), with `EvidenceRecorder("AC-HCM-28", correlationId)`.
- Personas:

| Persona | Used for |
|---------|----------|
| `central-a` | HR maker |
| `br1-mgr` | committee steps 1–2 |
| `a-management-approver` | step 3 |
| `a-compliance` | assigned reviewer |
| `a-compliance-2` | non-assigned reviewer (negative) |

- Use session 1's seeded committee requisition.
- Use `Skip.If` — **never** `WriteResult("Skip"); return;`.
- Update the existing `AcHcm22` committee/appeal scenarios (22c/22d) if they conflict with the new flow, and justify it.
- If the local stack is available, run it and produce `acceptance-evidence/AC-HCM-28/`; otherwise report that it was not run.

### Regression

Keep green, or matching the baseline: `Hcm026`, `Hcm027`, `Hcm028`, `Hcm021`, `Hcm022`, `Wf023`, `RecruitmentHireClosure`, `Fun001`, `Syr001`. List every modified test with a justification.

---

## Workstream K — Script and docs (cover both sessions)

- `scripts/hcm028-validate.ps1`, modelled on `scripts/hcm027-validate.ps1`.
- `docs/acceptance-plans/execution/PLAN-28-EXECUTION.md`: prerequisites (new persona env var), commands, personas, Arabic manual steps (committee and appeal), evidence folder.
- `PLAN-28-hcm-committee-appeal-governance.md`:
  - Status → `Implemented (acceptance evidence pending)` or `Implemented`
  - "Implementation notes / deviations" for both sessions — notably the compliance-access slice from PLAN-25, the employee-based reviewer identity, the requisition-level committee switch, and minutes generated on demand
- `docs/HCM_001_RECRUITMENT.md`: committee model and gate, appeal lifecycle and outcomes, rejection kinds. Mark G-09/G-10 (and the appeal part of G-05) as addressed.
- `docs/HIRE_ONBOARDING_GUIDE.md`: committee and appeal steps; remove any mention of the legacy reopen path.
- `docs/HIRE_RBAC_MATRIX.md`: compliance reviewer, committee chair/member/secretary, `HrCoreAppealReview`.
- `docs/SOD_MATRIX.md`: committee creator/member vs. step-3 approver; reviewer independence.
- `docs/DATABASE_SCHEMA_INVENTORY.*` (generator if one exists).

---

## Commit plan

7. `feat(hcm028): compliance access slice — appeal review policy, RecruitmentAppealsController, work inbox`
8. `feat(hcm028): rejection tracking (kind/date) and appeal window`
9. `feat(hcm028): single appeal path — reviewer assignment, explicit outcomes, info requests; legacy reopen removed`
10. `feat(hcm028): block hire on pending appeal`
11. `test(hcm028): integration + acceptance AC-HCM-28a..i`
12. `docs(hcm028): plan status, execution, guides, RBAC/SoD, validate script`

Every commit builds; tests green at 8, 9, 10 and 12.

## Definition of done (whole PLAN-28)

- The committee is an audited decision body whose approval gates ranking on committee-gated requisitions (session 1).
- A rejected candidate can be reopened **only** through an appeal decided by an independent, correctly identified compliance reviewer. The outcome is explicit, its effect is controlled, and every step is audited.
- Rejections record their kind and date, and the appeal window uses them.
- No hire happens while an appeal on the requisition is pending (configurable).
- Build has 0 errors; `has-pending-model-changes` reports none; unit/integration tests pass; `scripts/hcm028-validate.ps1` passes.
- Acceptance tests are written, and run with evidence or explicitly reported as not run.

## Final report (English; quote Arabic UI strings as-is; cover both sessions)

1. Summary per workstream (A–E from the handoff, verified; F–K).
2. Commits of both sessions.
3. Migrations, data steps, new persona/env var.
4. Test results vs. baseline; modified tests with justification.
5. Acceptance: run or not (why), evidence path.
6. Deviations and judgement calls (gateway finding, H5 behaviour).
7. Open items and `// PLAN-2x:` markers (file:line).
8. Manual test script (Arabic UI paths):
   - committee: members → votes → decisions → submit → 3 approvals → minutes → apply ranking
   - appeal: reject → file → assign reviewer → compliance decides each outcome → request info → resubmit
   - hire blocked while an appeal is pending
9. `git log --oneline` of the branch.
