# PLAN-25 — Progress (checkpoint)

**Branch:** `feature/plan-25-production-approvals` (from `main` @ `3893a5d`, PLAN-33 merged). Local only — never pushed.
**Prompt:** `docs/acceptance-plans/execution/PLAN-25-AGENT-PROMPT.md`. Baseline: `reports/plan25-baseline.md`.

## Phase status

| Phase | Status | Commit |
|-------|--------|--------|
| P0 baseline, inventory, notes | Done | e05632d |
| P1 approval start coordinator | Done | 8360b5e |
| P2 repair endpoint + script | Done | (P2 commit) |
| P3 step roles + gates | Pending | |
| P4 retire RECRUITMENT-HIRE | Pending | |
| P5 write policies + org scope | Pending | |
| P6 prod-like journey, AC-HCM-25, CI | Pending | |
| P7 live verification | Pending | |
| P8 docs, review, final report | Pending | |

## Decisions (prompt decisions 1–10 adopted as written; refinements below)

| # | Decision | Reason |
|---|----------|--------|
| R1 | `Workflow:AdminBypass` = **false in every environment** (base `appsettings.json`), not only Production/Staging | Inventory: no unit/integration/acceptance/E2E test approves a workflow step as `hr-admin`; the only admin uses are reviewer-eligibility and legacy `ApprovalStep` checks |
| R2 | `Workflow:EnforceStepRoleEntityTypes` = RecruitmentRequisition, OfferLetter, SelectionCommitteeSession, RecruitmentAppeal, RecruitmentCompetition, CompetitionResultVersion, EstablishmentChangeRequest | Decision 4. **AssetAssignment / AssetLossReport stay out**: their step-1 role depends on which seeder ran first (`WorkflowPackSeeder` hr-core vs `AssetAssignmentWorkflowSeeder` admin-logistics), and AdminLogistics starts them over HTTP; enforcing would need a seed/persona decision — follow-up. Transfers, leave, card requests approve outside `WorkflowInstanceActionService` (no instance to guard) |
| R3 | Step-1 role: the seed says **`hr-core`** for every recruitment/establishment workflow; `branch-manager` alone does **not** approve step 1. Only `acc-negative-invalid-scope` has branch-manager without hr-core. `HIRE_RBAC_MATRIX.md` already says hr-core; no doc mismatch to fix beyond stating the server enforces it | Seed defines the rule |
| R4 | Coordinator is called **before** any entity mutation; the entity status and `WorkflowInstanceId` are set together after a non-null instance, inside one relational transaction when the provider is relational | `WorkflowStarter` and `PolicyOrchestrator` call `SaveChangesAsync` themselves, so earlier mutations (e.g. the committee's `SubmittedAt`/`LockedAt`) would otherwise be saved even when the start fails |
| R5 | Appeals (`RecruitmentAppealService.StartReviewWorkflowAsync`) and competitions/results already always start — **not** routed through the coordinator (they have their own deliberate semantics: H5 "no definition → reviewer decides directly", competition start error). Verified only | Prompt decision 3 ("only if trivial") |
| R6 | `FailIfDefinitionMissing` controls whether the coordinator reports a missing definition as an error (true) or as `(null, null)` for callers with their own report (repair dry run). The submit paths never change status without an instance regardless of the flag | Decision 2 wins over the flag |
| R7 | The prod-like compose keeps `ASPNETCORE_ENVIRONMENT=Acceptance` (personas, seed), so `appsettings.Production.json` is not loaded there; it therefore **mirrors** decision 10's Production values explicitly (`PolicyEngine__Core.Recruitment=Shadow`, `Recruitment__Approvals__AlwaysStartWorkflow=true`, `Workflow__AdminBypass=false`) | Otherwise the prod-like run would test "Off", not the Production value |

## Inventory (P0)

### Approval starts / PendingApproval setters (src, non-test)

| Path | Before PLAN-25 |
|------|----------------|
| `RecruitmentRequisitionLifecycleService.SubmitForApprovalAsync` | Off: PendingApproval always, workflow only if `IsLabScale`; Shadow/Enforce: `policyResult.WorkflowInstanceId` (null in Shadow) |
| `RecruitmentOfferService.SubmitForApprovalAsync` (normal + override code) | same pattern |
| `SelectionCommitteeLifecycleService.SubmitForApprovalAsync` | `SubmittedAt`/`LockedAt` set unconditionally, workflow only if `IsLabScale` (Off) or from policy |
| `EstablishmentChangeLifecycleService.SubmitForApprovalAsync` | Off+lab: PendingApproval + workflow; **Off+non-lab: applied immediately without approval**; Shadow/Enforce: policy id (null in Shadow) |
| `RecruitmentAppealService.StartReviewWorkflowAsync` | policy, then `??=` start — always starts (PLAN-28) |
| `CompetitionLifecycleService.SubmitForApprovalAsync`, `CompetitionResultsService.SubmitForApprovalAsync` | unconditional start, error when no definition (PLAN-31) |
| `PolicyOrchestrator.EnforceAsync` | starts only in Enforce + Allowed + TriggerWorkflow |
| `WorkflowInstancesController.Start` (API) | AdminLogistics asset assignment / loss (HTTP) |

### Remaining `IsLabScale` uses (non-test)

The four lifecycle services above (gates), three `*WorkflowUiService` warning texts, `NationalScopeOptions`, `NationalScopeGuard` (startup log). After P1 only the options/guard remain.

### Actors and callers

`CreateWorkflowActor` in Competitions, EstablishmentChanges, OfferLetters, RecruitmentRequisitions, SelectionCommittees and `CreateActor` in WorkflowInstances take the **first** role claim; RecruitmentAppealsController hard-codes `Compliance`. Callers of `ApproveAsync`/`RejectAsync`: those controllers, `SelectionCommitteeLifecycleService.Approve/RejectStepAsync`, `RecruitmentAppealService.DecideAsync/RequestInfoAsync`. Test constructions: SoDRequisitionApprovalTests, Est024, Hcm021, Hcm027 (×2), Hcm028 (support + lifecycle + committee), Hcm031 (support, lifecycle, results, review-fix).

### Workflow definitions without an outcome handler

ServiceTransaction (HIRE-WIZARD), SeparationRecord, CardRequest, TransferRequest, **RecruitmentApplication (RECRUITMENT-HIRE)**, DisciplinaryAction, Ticket. Only RECRUITMENT-HIRE is in scope (G-14); the others go on the architecture-test allow-list with a reason.

### POST actions without an explicit policy (HR.Core.Web)

51 `[HttpPost]` actions: ApprovedCVs 4, CareerPaths 4, Contacts 3, Documents 3, EmployeeProfiles 1, ExportJobs 1 (`[Authorize]` only), KeyPositions 3, HrStatisticReports 1, DailyAttendanceRecords 5, FieldInspectionVisits 1, DiwanCorrespondences 1, Qualifications 3, Rosters 4, Shifts 3, Signatures 1, SuccessionPlans 4, **PublicJobPostings 5, TalentMarketplace 4 (recruitment)**. All PUT/DELETE/PATCH are in policy-protected classes. The MVC module filter applies `Module.HrCore` (includes branch-manager and hr-management-approver) to every method; the GET/POST split exists only at the gateway.

### Recruitment screens without org scope (D10)

RecruitmentRequisitions Index/Details/Submit/Publish/PublishInternal/Delete/ExtendClosingDate/Close/Cancel/…; RecruitmentApplications everything; OfferLetters Index/Details/Submit/Accept/Decline/Revoke/Document/Evidence; SelectionCommittees Index/Details and every POST except Approve/Reject; Competitions everything (central-only rule from PLAN-32 not applied). Already scoped: approve/reject POSTs of requisitions/offers/committees, requisition Create/Edit, VacantPositions, RecruitmentAppeals, RecruitmentReports.

### Stuck rows on the acceptance database (read-only SQL, 2026-10-29)

| Query | Rows |
|-------|------|
| Requisitions `PendingApproval`, `WorkflowInstanceId IS NULL` | **1** — `3ceea897-5cc4-46f2-b211-7fe3abb4ecf4` (`-2026-00020`, created 17:11 by PLAN-33's prod-like journey run) |
| Offers `PendingApproval` without instance | 0 |
| Committee sessions submitted, not approved, without instance | 0 |
| Establishment change requests without instance | 0 (table empty) |
| Appeals `Pending` without instance | 1 — `7b02d6d0-…` with **no reviewer assigned** (correct: the review starts at assignment; not a repair target) |
| `WorkflowInstances` of `RecruitmentApplication` (RECRUITMENT-HIRE) | 0 |

## Test and live-run results

| When | Run | Result |
|------|-----|--------|
| P0 | unit baseline filter | 1534/1535 (known `BolaArchitectureTests`) |
| P0 | integration baseline filter | 68/73, 3 pre-existing failures, 2 opt-in skips |
| P1 | unit filter + Hcm0 + Recruitment + Syr001 | 1722/1724 — the 2 failures are baseline (`BolaArchitectureTests`, `Syr001ProductionBindingTests` DI — both in the PLAN-32 known list) |
| P1 | full unit suite (before the Syr001 fixes) | 3969/3986 — the same 17 environment/baseline failures as PLAN-32 |
| P1 | integration filter + Hcm0 | 73/78 after fixes: 3 baseline failures, 2 opt-in skips; `Hcm025ProductionApprovalIntegrationTests` 4/4 on PostgreSQL (Testcontainers), `Hcm033LabScaleBehaviourTests` 7/7 |
| P1 | `Hcm025ApprovalCoordinatorTests` | 24/24 |

## Files changed

- P0: `reports/plan25-baseline.md`, `reports/plan25-progress.md`, `docs/acceptance-plans/PLAN-25-hcm-production-workflow-binding.md`, `docs/acceptance-plans/execution/PLAN-25-AGENT-PROMPT.md`

## P1 notes

- `ApprovalStartCoordinator` (HR.Core.Infrastructure/Services/Workflow) + `RecruitmentApprovalOptions` (`Recruitment:Approvals`). The lifecycle services build it from their existing dependencies (no constructor change, so the ~40 manual constructions in tests compile unchanged); it is also registered in DI for the repair endpoint.
- Refactored: requisition, offer (normal + override code), committee (lock + audit now inside the apply step), establishment change (fields + status inside the apply step; the Off+non-lab auto-apply is gone). Each also has `RestartApprovalAsync` (audited `ApprovalWorkflowRestarted`).
- Removed helpers: `IsLabScale()` from all four services; `ResolveMode()` from offer, committee and establishment (requisition keeps it for Publish/PublishInternal). Remaining `IsLabScale` in src: `NationalScopeOptions` (definition) and `NationalScopeGuard` (start-up log) only.
- UI: `_WorkflowRestartPanel` partial (warning + «إعادة بدء مسار الاعتماد» for Admin/hr-core) replaces the «فعّل PolicyEngine… أو IsLabScale» warning on requisition, offer, establishment panels and adds it to the committee panel (which showed nothing before); `RestartApproval` POST (`HrCoreWrite`) on the four controllers.
- Settings: Production/Staging get `PolicyEngine:Core.Recruitment=Shadow` and `Recruitment:Approvals` (`AlwaysStartWorkflow=true`, `FailIfDefinitionMissing=true`).
- Tests modified (behaviour change, not weakening): `Hcm033LabScaleBehaviourTests` (3 non-lab tests flipped to "now starts", restart test added), `Syr001RecruitmentLegalSourceTests` and `Syr001OfferLegalSourceTests` (real starter + seeded definitions; the offer test expected `Issued` on submit — stale, now PendingApproval with an instance; it was a known baseline failure and now passes), `Est002`/`Est005`/`Est006` integration tests (asserted the defect "Off mode applies on submit"; now submit → PendingApproval → approved through the real handler → Applied, via `EstablishmentApprovalTestHelper`), AC-HCM-21d/22e step notes. `IntegrationTestAuth` gains an `X-Test-Employee-Id` header.

## P2 notes

- `ApprovalRepairService` (Infrastructure/Services/Workflow) + `RecruitmentAdminController` `POST api/recruitment/admin/repair-approvals?dryRun=true` (`HrCoreDelete`, `[ApiController]`). Tenant = caller (context filter). Each repair goes through the entity's `RestartApprovalAsync` (same coordinator as a submit, audited `ApprovalWorkflowRestarted`); appeals through the new `RecruitmentAppealService.RestartReviewWorkflowAsync` (only Pending + assigned reviewer + not awaiting info + no instance — PLAN-28 rules); orphan `RecruitmentApplication` (RECRUITMENT-HIRE) instances → `Cancelled` with a history entry and `OrphanHireWorkflowCancelled` audit. A failed row (e.g. missing definition) is reported `Failed` with its message, never thrown.
- `scripts/hcm025-repair-stuck-approvals.ps1`: password grant on `hr-gateway-client` (the acceptance harness pattern), gateway `/api/hr-core/api/recruitment/admin/repair-approvals`, dry run by default, `-Apply`, CSV + JSON under `artifacts/hcm025-repair/`. Credentials from parameters or `HCM025_ADMIN_USERNAME`/`HCM025_ADMIN_PASSWORD`; on a local acceptance stack the password may come from the untracked `.acceptance/credentials.json` and the client secret from `.env`; nothing is written or logged. Default user `acc-a-directory-checker` (the only hr-admin persona).
- Tests: `Hcm025ApprovalRepairTests` 3/3 (dry run changes nothing; apply repairs exactly the 6 listed rows, 5 restart audits + 1 orphan audit, idempotent; missing definition reported), `Hcm025ProductionApprovalIntegrationTests.Repair_endpoint_…` (hr-core 403, admin dry run lists, apply starts, idempotent).

## Open issues

- `-warnaserror` fails on the pre-existing NU1903 SSH.NET advisory (IntegrationTests); not introduced here.

## Next step

P3: `WorkflowActionActor` with all roles + employee id, `WorkflowStageGuard` role check (approve + reject), `IWorkflowApprovalGate` classes, panel builder parity.
