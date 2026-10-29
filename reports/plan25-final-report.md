# PLAN-25 — Final report

**Date:** 2026-10-29
**Branch:** `feature/plan-25-production-approvals` (local only, **not pushed**), from `main` @ `3893a5d` (PLAN-26..33 merged).
**Plan:** [`docs/acceptance-plans/PLAN-25-hcm-production-workflow-binding.md`](../docs/acceptance-plans/PLAN-25-hcm-production-workflow-binding.md)
— Status **Implemented (CI not executed)**. Execution prompt:
[`docs/acceptance-plans/execution/PLAN-25-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-25-AGENT-PROMPT.md); execution guide:
[`PLAN-25-EXECUTION.md`](../docs/acceptance-plans/execution/PLAN-25-EXECUTION.md). Progress log with decisions R1–R7 and per-phase
notes: [`reports/plan25-progress.md`](plan25-progress.md); baseline: [`reports/plan25-baseline.md`](plan25-baseline.md).

## 1. Summary per phase

| Phase | Scope |
|-------|-------|
| P0 | Baseline (build 0 errors; `-warnaserror` fails on the pre-existing NU1903 SSH.NET advisory), inventory (approval starts, actors, callers, 51 unprotected POSTs, unscoped recruitment screens, stuck rows on the acceptance DB), decisions 1–10 in the plan |
| P1 | `ApprovalStartCoordinator` + `RecruitmentApprovalOptions`; requisition, offer (normal/override), committee and establishment-change submits always start their workflow and change state only with the instance; restart paths; UI restart button; Production/Staging settings |
| P2 | `ApprovalRepairService` + `POST api/recruitment/admin/repair-approvals` (`HrCoreDelete`) + `scripts/hcm025-repair-stuck-approvals.ps1` |
| P3 | `WorkflowActionActor` with all roles, employee id, scope claim; `WorkflowStageGuard` step-role check on approve **and** reject; seven `IWorkflowApprovalGate`s; `WorkflowApprovalEvaluator` shared by the server and the detail panels; `ValidateSelfApprovalAsync` deleted |
| P4 | RECRUITMENT-HIRE retired (rule, definition, static fallback, binding); `WorkflowDefinitionsHaveHandlersTests` |
| P5 | Explicit write policies on public postings / internal vacancies + closing-date audit; `HrCorePostActionsHaveExplicitPolicyTests` ratchet; org scope on the recruitment screens (D10); competitions central-only |
| P6 | Prod-like compose mirrors decision 10; prod-like journey with a server-side refusal; CI journey filter; `AcHcm25ProductionApprovalTests` (25a–h) |
| P7 | Live runs on the prod-like and standard profiles (§8) |
| P8 | Docs, `scripts/hcm025-validate.ps1`, independent review + refutation, this report |

## 2. Commits

```
(P8)    docs(hcm025): plan status, RBAC/onboarding/gap updates, execution guide, validate script, final report
f962bdc test(hcm025): live evidence on the prod-like and standard profiles
4f15785 test(hcm025): add the central-a2 persona to the catalog (missing from 5290bd5)
76470fa fix(hcm025): review fixes — no adoption of acted-on instances, linked-instance outcomes, scope on documents and postings, inbox override rule, cancel and restart
5290bd5 test(hcm025): prod-like journey green and acceptance AC-HCM-25a..h
d62120f feat(hcm025): explicit write policies on postings/vacancies and org scope on recruitment screens
b0f911e fix(hcm025): retire the orphan RECRUITMENT-HIRE workflow and its trigger rule
d9a7976 feat(wf025): server-side step-role check and per-entity approval gates on approve and reject
9db6397 feat(hcm025): repair endpoint and script for stuck approvals and orphan hire instances
8360b5e feat(hcm025): approval workflows always start (policy may deny or redirect, never skip)
e05632d docs(hcm025): baseline, inventory and pre-implementation notes
```

Author identity: the repository's existing `omardefdev`. `4f15785` restores the persona entry that a blanket
`git checkout -- testdata` (meant only for the test-rewritten `testdata/*/out` files) reverted before `5290bd5` was made.

## 3. Settings per environment

| Key | Lab / acceptance (`appsettings.json`) | Staging / Production | Prod-like compose |
|-----|---------------------------------------|----------------------|-------------------|
| `NationalScope:IsLabScale` | `true` | `false` | `false` |
| `PolicyEngine:Core.Recruitment` | unset (Off) | `Shadow` | `Shadow` |
| `Recruitment:Approvals:AlwaysStartWorkflow` | `true` (default) | `true` | `true` |
| `Recruitment:Approvals:FailIfDefinitionMissing` | `true` (default) | `true` | `true` |
| `Workflow:AdminBypass` | `false` | `false` | `false` |
| `Workflow:EnforceStepRoleEntityTypes` | RecruitmentRequisition, OfferLetter, SelectionCommitteeSession, RecruitmentAppeal, RecruitmentCompetition, CompetitionResultVersion, EstablishmentChangeRequest | same | same (from `appsettings.json`) |

These are configuration values, not legal values. The prod-like compose keeps `ASPNETCORE_ENVIRONMENT=Acceptance` (personas,
seed, FileDrop), so it repeats the Production values explicitly (decision R7).

## 4. Approval-start map, before → after

| Path | Before PLAN-25 | After |
|------|----------------|-------|
| Requisition submit | Off: PendingApproval always, workflow only with `IsLabScale`; Shadow: PendingApproval with **null** instance; Enforce: instance only if a rule triggered | Coordinator: policy (Shadow/Enforce) may deny or start another workflow; otherwise RECRUITMENT-REQUISITION-APPROVAL starts; PendingApproval **with** the instance in one transaction; no definition → refusal, state unchanged |
| Offer submit (normal / exception) | same pattern | same, with RECRUITMENT-OFFER-APPROVAL / RECRUITMENT-OFFER-OVERRIDE-APPROVAL |
| Committee submit | session locked and submitted, workflow only with `IsLabScale` (silent stall otherwise) | lock, `SubmittedAt` and audit inside the apply step: only together with the instance |
| Establishment change submit | Off + non-lab: **applied immediately without approval**; Shadow: PendingApproval with null instance | always ESTABLISHMENT-CHANGE-APPROVAL; applied only by the outcome handler after both approvals |
| Appeal review (assignment) | always started (PLAN-28, `??=`) | unchanged (verified); restart path added for the repair |
| Competition / results submit | always started, error without definition (PLAN-31) | unchanged (verified) |
| Hire (Shadow/Enforce) | Enforce + R-REC-HIRE (or the static fallback) started an orphan RECRUITMENT-HIRE instance | no workflow (governed by the offer approval) |
| Stuck rows from earlier builds | no exit | Details «إعادة بدء مسار الاعتماد» (`HrCoreWrite`) and the repair endpoint/script (`HrCoreDelete`) |

## 5. Gates and the rules moved

Evaluator order: structure (in progress, no skip, step exists) → step role → entity gate → initiator rule (approve only).

| Rule | Before | Now | Approve / reject | Test |
|------|--------|-----|------------------|------|
| Step role (`RoleRequired` ∈ all actor roles) | UI and inbox only | `WorkflowStageGuard.ValidateStepRole` | both | `WorkflowStageGuardRoleTests`, `Hcm025ApprovalGatesTests.Server_refuses_step_two_…`, AC-HCM-25c |
| Admin bypass | UI only, always on | `Workflow:AdminBypass` (false) | both | `WorkflowStageGuardRoleTests.Admin_passes_only_with_admin_bypass` |
| Requisition creator SoD | `ValidateSelfApprovalAsync` (approve) | `RequisitionApprovalGate` | both | `Requisition_creator_cannot_approve_or_reject`, `Server_reject_by_the_creator_…` |
| Offer creator SoD | `ValidateSelfApprovalAsync` | `OfferApprovalGate` | both | `Offer_creator_cannot_approve_or_reject` |
| Override offer: distinct approver per step | `ValidateSelfApprovalAsync` | `OfferApprovalGate` | approve | `Override_offer_needs_a_distinct_approver_…` |
| Committee creator | `ValidateSelfApprovalAsync` | `CommitteeApprovalGate` | both | `Committee_creator_cannot_approve_or_reject` |
| Committee quorum (≥ 3 active from the quorum step) | `SelectionCommitteeLifecycleService.ApproveStepAsync` | `CommitteeApprovalGate` | approve | `Committee_quorum_step_…`, AC-HCM-25d |
| Committee member on the final step | lifecycle service | `CommitteeApprovalGate` | both | `Committee_member_cannot_approve_or_reject_the_final_step` |
| Appeal explicit outcome | `ValidateSelfApprovalAsync` | `AppealApprovalGate` | approve | `Appeal_only_the_assigned_reviewer_…` |
| Appeal assigned reviewer | appeal service only | `AppealApprovalGate` (+ service) | both | same, AC-HCM-25e |
| Competition creator | `ValidateSelfApprovalAsync` | `CompetitionApprovalGate` | both | `Competition_creator_cannot_…` |
| Results: computer/creator, submitter, tie resolver | `ValidateSelfApprovalAsync` | `ResultsApprovalGate` | both | `Results_computer_submitter_and_tie_resolver_…` |
| Results: distinct approver per step | `ValidateSelfApprovalAsync` | `ResultsApprovalGate` | approve | same |
| Establishment creator | `ValidateSelfApprovalAsync` | `EstablishmentChangeApprovalGate` | both | `Establishment_creator_cannot_…` |
| Branch scope (requisition / offer / committee) | controller POSTs only | gates (`WorkflowGateOrgScope`) + `RecruitmentOrgScopeFilter` | both | `Requisition_branch_actor_…`, `Offer_branch_actor_…`, `Hcm025OrgScopeTests` |
| Central only (competition, results, establishment) | establishment panel only | gates | both | `Competition_…`, `Establishment_…` |
| Initiator never approves later stages | guard | guard (after the gate) | approve | `WorkflowStageGuardRoleTests.Role_is_checked_before_the_initiator_rule_…` |
| UI = server | — | `WorkflowPanelBuilder.BuildAsync` asks the evaluator | both | `Hcm025PanelServerParityTests` (6 states × 7 users × 2 = 84 cases) |

## 6. Step-role enforcement per domain

| Domain (entity type) | Enforced | Note |
|----------------------|----------|------|
| RecruitmentRequisition, OfferLetter, SelectionCommitteeSession, RecruitmentAppeal, RecruitmentCompetition, CompetitionResultVersion, EstablishmentChangeRequest | **on** | decision 4 |
| AssetAssignment, AssetLossReport | off | approved through `WorkflowInstancesController` (AdminLogistics over HTTP); the step-1 role of ASSET-ASSIGN-APPROVAL depends on which seeder ran first (hr-core vs admin-logistics) — needs a seed/persona decision (follow-up) |
| TransferRequest, leave, card requests | n/a | approved locally in their own services, not through `WorkflowInstanceActionService` |
| ServiceTransaction, SeparationRecord, DisciplinaryAction, Ticket | off | outcome owned by other services (allow-list in `WorkflowDefinitionsHaveHandlersTests`) |

## 7. Test results vs. baseline

| Run | Baseline (P0) | Final | Note |
|-----|---------------|-------|------|
| Build | 0 errors, 4 NU1903 warnings; `-warnaserror` fails at restore on NU1903 (SSH.NET, IntegrationTests) | same | no warning introduced by PLAN-25 |
| Unit, P0 filter | 1534/1535 | all pass except the known `BolaArchitectureTests` | — |
| Unit, full suite | 17 known environment/baseline failures (PLAN-32 list) | the 16 known failures only | `Syr001OfferLegalSourceTests` (a baseline failure) now passes |
| Unit, new PLAN-25 tests | — | `Hcm025ApprovalCoordinatorTests` 24, `Hcm025ApprovalRepairTests` 3, `Hcm025ApprovalGatesTests` 16, `Hcm025PanelServerParityTests` 84, `Hcm025RecruitmentHireRetiredTests` 4, `Hcm025ReviewFixTests` 7, `WorkflowStageGuardRoleTests` 21, `WorkflowDefinitionsHaveHandlersTests` 3, `HrCorePostActionsHaveExplicitPolicyTests` 3 — all pass | — |
| Integration, P0 filter | 68/73 (3 pre-existing failures, 2 opt-in skips) | the same 3 pre-existing failures; on the widened filter (+Hcm0/Scope/Recruitment/Est0/Inbox/Approval) only pre-existing failures remain | hire-closure, payroll and contract-chain failures also fail on unmodified `main` (re-run in a temporary worktree) |
| Integration, new | — | `Hcm025ProductionApprovalIntegrationTests` 5 (PostgreSQL, Production values), `Hcm025OrgScopeTests` 6, `Hcm025PostingPoliciesTests` 3 — all pass | — |
| `scripts/hcm025-validate.ps1` | — | **PASS** (tests 182/183; the one is the known `BolaArchitectureTests` baseline) | — |

Tests modified (behaviour change, not weakening): `Hcm033LabScaleBehaviourTests` (non-lab now starts; restart test),
`Syr001RecruitmentLegalSourceTests`/`Syr001OfferLegalSourceTests` (real starter + definitions; stale "Issued on submit"),
`Est002`/`Est005`/`Est006` (asserted the "apply on submit" defect), `Hcm027OfferOutcomeTests`/`Hcm027OverrideWorkflowTests` and
`SoDRequisitionApprovalTests` (actors hold the step roles), `WorkflowStageGuardTests` (Arabic messages),
`Hcm029OfferDocumentTests`/`RecruitmentRequisitionWorkflowIntegrationTests`/`Est002` reject (instances linked to the entity,
review finding 2), five UI-service constructions, AC-HCM-21d/22e notes, AC-HCM-31 and AC-HCM-26h (step 1 by `central-a2`),
the requisition-to-hire journey (server-side refusal added). `IntegrationTestAuth` gained an employee-id header.

## 8. Live results per profile

Final runs on 2026-10-29: every image rebuilt from this branch (`76470fa` + `4f15785`), clean reset (`acceptance-reset.ps1`,
Keycloak realm regenerated with 61 personas), `ACCEPTANCE_REQUIRE_ALL=true`.

**Prod-like profile** (`docker-compose.acceptance-prodlike.yml`: IsLabScale=false, Core.Recruitment=Shadow, AlwaysStartWorkflow,
AdminBypass=false), filter `AcHcm25|AcHcm21|AcHcm22|AcWf23|AcHcm26..33`: **51/53**, then AC-HCM-26h re-run after its fix: **Pass**.

| Scenario | Result | Evidence |
|----------|--------|----------|
| AC-HCM-25a requisition submit → instance → two approvals → Approved | Pass | `acceptance-evidence/AC-HCM-25/` |
| AC-HCM-25b offer → two approvals → Issued | Pass | 〃 |
| AC-HCM-25c hr-core POSTs approve and reject of step 2 → refused, state and history unchanged | Pass | 〃 |
| AC-HCM-25d committee: 2 active members → «النصاب غير مكتمل…», still step 2; 4 active → step 3 → approved | Pass | 〃 |
| AC-HCM-25e assigned a-compliance decides; a-compliance-2 refused (gate); hr-core shown the role reason and refused | Pass | 〃 |
| AC-HCM-25f a-management-approver POST PublicJobPostings/Edit → 403, unchanged | Pass | 〃 |
| AC-HCM-25g R-REC-HIRE absent/neutralised, RECRUITMENT-HIRE unpublished, no in-progress hire instance | Pass | 〃 |
| AC-HCM-25h repair script: dry run lists the seeded stuck requisition and orphan hire instance; apply repairs both, audited; second dry run clean | Pass | 〃 + `repair/*.csv` |
| AC-HCM-21a–d, 22a–e, WF-23a–f, 27, 28, 29, 31, 32, 33 | Pass | their folders |
| AC-HCM-26a–g, i | Pass | `acceptance-evidence/AC-HCM-26/` |
| AC-HCM-26h freeze cascade | **Pass on re-run** — first live Pass since PLAN-26 (the first run still sent step 1 as `br1-mgr`, refused on central-only EstablishmentChanges; fixed to `central-a2`) | 〃 |
| AC-HCM-30 (except i) | Pass | `acceptance-evidence/AC-HCM-30/` |
| AC-HCM-30i retention apply | Fail on prod-like — **expected**: the prod-like file carries the Production value `Recruitment:Retention:Apply=false`, so no apply run is logged; **Pass on the lab profile** | 〃 |
| Prod-like journey (`Category=JourneyProdLike`) | **Pass** — requisition → approvals (with a server-side wrong-role refusal) → publication → citizen application with PDF → screening/interview/assessment/rank → offer approvals → OTP acceptance → hire → HR.Contracts appointment and contract | `acceptance-evidence/AC-HCM-33/prodlike/` |

An earlier prod-like run (before the review fixes and the `central-a2` persona) gave AC-HCM-25 8/8 and 49/53 overall; its two
AC-HCM-31 failures were the branch user on central-only competitions (seed fix D-h).

**Standard (lab) profile**, suites touched by enforcement `AcEst24|Ac32Logistics|Ac33AssetLoss|AcWf23|Ac11Policy|Ac12PayrollSod`
+ `Ac30_i`: **12 Pass, 2 Blocked, 0 Fail**. Blocked (not `[RequiredAcceptance]`, preconditions unrelated to PLAN-25):
`Ac12PayrollSodTests` (the payroll run could not be submitted for review) and
`AcEst24…WorkInbox_may_list_establishment_change_deep_link` (no pending establishment change in the inbox). AC-HCM-30i Pass.
`Category=Journey`: the recruitment journey **Pass** (`acceptance-evidence/AC-HCM-33/`); the other 46 journeys fail with
`Executable doesn't exist … ms-playwright` — Playwright browsers are not installed on this machine (environment, as in PLAN-33).

`acceptance-evidence/summary.md` regenerated.

## 9. Repair on the acceptance database

| When | Rows | Result |
|------|------|--------|
| P0 (read-only SQL, PLAN-33's stack) | 1 requisition `PendingApproval` without an instance (`3ceea897…`, left by PLAN-33's prod-like run); 0 offers, 0 sessions, 0 establishment changes; 1 pending appeal without an instance but **no reviewer** (correctly not a repair target); 0 RECRUITMENT-HIRE instances | the stack was reset for the final runs, which removed that row |
| AC-HCM-25h (live) | seeded: 1 stuck requisition + 1 orphan RECRUITMENT-HIRE instance | dry run listed both; apply: the requisition got RECRUITMENT-REQUISITION-APPROVAL, the orphan was Cancelled, audits written (`acceptance-evidence/AC-HCM-25/repair/`) |
| After all final runs | `scripts/hcm025-repair-stuck-approvals.ps1` dry run (hr-admin persona) | found 0 — nothing left without a workflow by the new code |

## 10. Decisions and deviations

Prompt decisions 1–10 were adopted as written. Refinements and deviations (details in `reports/plan25-progress.md`):

| # | Decision / deviation | Reason |
|---|----------------------|--------|
| R1 | `Workflow:AdminBypass=false` in **every** environment | no test or scenario approves a step as `hr-admin` |
| R2 | Enforcement on the six recruitment entity types + establishment; assets/transfer/others not enforced | ASSET-ASSIGN-APPROVAL's step-1 role depends on seeder order; other domains approve outside the action service |
| R3 | Step 1 is `hr-core` everywhere (seed = rule); `branch-manager` alone never approves step 1 | the docs already said so; only `acc-negative-invalid-scope` lacks hr-core |
| R4 | Coordinator called before any mutation; status + instance in one transaction | the starter and the policy ledger save the context themselves |
| R5 | Appeals and competitions not routed through the coordinator | they already always started; appeals keep H5 |
| R6 | `FailIfDefinitionMissing` only controls how the coordinator reports; submits never change state without an instance | decision 2 wins over the flag |
| R7 | Prod-like compose repeats the Production values explicitly | `ASPNETCORE_ENVIRONMENT` stays Acceptance, so the Production file is not loaded |
| D-a | Coordinator name is generic (`ApprovalStartCoordinator`, not "Recruitment…") | it also serves establishment changes |
| D-b | Gate context carries the pinned definition (prompt shape + definition) | the override rule needs the workflow code |
| D-c | Evaluator order: structure → role → gate → initiator; guard messages in Arabic | each refusal names the most specific rule |
| D-d | Duplicated committee checks removed from `SelectionCommitteeLifecycleService` | the gate runs on every approve (single source of truth) |
| D-e | Work inbox keeps its bulk evaluation but uses the server's role rule and (after review) the override rule | avoids per-row queries in the list |
| D-f | Appeal panel buttons follow the evaluator's reject-side check | the decision form itself supplies the outcome the approve side needs |
| D-g | AC-HCM-25g live = rule/definition/instance facts; Enforce-mode hire proven by `Hcm025RecruitmentHireRetiredTests` | Enforce is not the deployed mode (Production = Shadow) |
| D-h | Seed fix (decision 8): persona `central-a2` for AC-HCM-31's step-1 approvals (was the branch user `br1-mgr` on central-only competitions); catalog 60 → 61 | the rule stays; also unblocks AC-HCM-26h |
| D-i | `SelectionCommitteeRules.QuorumMessage` reworded to start «النصاب غير مكتمل» | the plan's wording; no test compared the literal |
| D-j | YAML validated structurally only | no YAML parser on this machine (as PLAN-33 K5) |
| D-k | Playwright not used for the journey (HTTP through the gateway, as PLAN-33) | browsers not installed on this machine |

Independent review: 10 findings, each attacked by a refuter (6 confirmed, 4 plausible, 0 refuted); 8 fixed with a regression
test each, 2 low pre-existing ones recorded as follow-ups — table in `reports/plan25-progress.md` («P8 review»), commit `76470fa`.

## 11. Follow-ups

- Step-role enforcement for AssetAssignment/AssetLossReport (decide ASSET-ASSIGN-APPROVAL's step-1 role: hr-core vs admin-logistics) and other domains.
- Give the 42 frozen non-recruitment POST actions an explicit policy (`tests/architecture/hrcore-post-policy-allowlist.txt`).
- Branch scope on `PromoteFromCitizen`/`PromoteFromInternal` (citizen/internal application ids), internal vacancies (`TalentMarketplace`), and the create-application requisition dropdown.
- Review finding 7: the inbox's `CreatedBy ?? InitiatedBy` fallback (hides, never grants).
- Review finding 8: a unique partial index on in-progress instances per entity (needs a migration) against concurrent double submits.
- Retired-rule mechanism: `PolicyRule` has no `IsActive`; a column would make deactivation explicit.
- HR.Contracts' `IHrCoreActiveRegimeClient` DI gap in the integration test host (pre-existing failures), payroll/hire-closure integration failures that also fail on `main`.
- Remote CI never executed (§12).

## 12. Push and CI instructions for the user

Nothing was pushed. `main` is 128 commits ahead of `origin/main` before this plan; this branch adds the PLAN-25 commits.
After merging into local `main` (done at the end of this plan, as for PLAN-26..33):

```bash
git push origin main
```

Or, to review first through a pull request:

```bash
git push -u origin feature/plan-25-production-approvals
gh pr create --base main --head feature/plan-25-production-approvals --title "PLAN-25: production approval binding and server-side enforcement" --body-file reports/plan25-final-report.md
```

What runs:

| Workflow | Trigger | What to check |
|----------|---------|---------------|
| `ci.yml` | push / PR to `main` | `build-and-test` (build, unit, integration with Testcontainers — expect the pre-existing failures listed in §7 unless the CI environment differs); `acceptance` job with `ACCEPTANCE_REQUIRE_ALL` on the lab profile (AC-HCM-25a/25b run there too and record «lab» as their profile; the Production-value proof is the prod-like journey in `e2e-journeys.yml` and the PostgreSQL integration test); `codeql`; `gitleaks` |
| `codeql.yml` | push / PR to `main`, weekly | no new alerts in the changed C# (coordinator, gates, filter, repair endpoint) |
| `gitleaks.yml` | push / PR to `main` | no secret in the repair script (credentials come from parameters/env/local untracked files only) |
| `e2e-journeys.yml` | **schedule / manual only** — run it: `gh workflow run e2e-journeys.yml --ref main` (or the branch) | `recruitment-journey` job: lab journey, then «Switch … to the prod-like profile» and «Run requisition-to-hire journey (prod-like)» must pass, and both zero-skip steps must report `Passed: 1, skipped: 0` |

Follow a run with `gh run list --workflow e2e-journeys.yml --limit 1` and `gh run watch`.
