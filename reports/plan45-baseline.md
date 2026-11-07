# PLAN-45 baseline

Taken 2026-11-07 on `feature/plan-45-wf-asset-custody-approval-loop` (cut from `origin/main` aeb6d716). Raw logs: `artifacts/plan45/` (not committed).

## Dependencies (0.2)

- PLAN-40: `OutboxPublishOrderingArchitectureTests` on `main`, custody outcomes staged by `StageAssetOutcomeAsync` inside `OutboxUnitOfWork`.
- PLAN-43: `ExternalApprovalService`, `ExternalApprovalTypes`, custody events `SchemaVersion = 2`, `EnsureVersionAsync`.
- PLAN-44: `WorkflowInstanceInsert`, unique indexes, `WorkflowOutcomeReconciliationJob`.

## Build and filters (0.3, 0.4)

| Run | Result |
|---|---|
| `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors, 57 s |
| Unit filter `Fun005\|Asset\|Wf043` | 238 passed, 1 failed |
| Integration filter `Ac33\|Asset\|Wf043` | 16 passed, 9 skipped (no Docker tests in this run), 0 failed |

The one failure, `ArabicPdfGenerationTests.AssetHandoverPdfService_renders_arabic_labels`, is QuestPDF's licence notice: the filtered run does not reach the fixture that sets the licence. It is not a regression, and it passes in the full suite.

## Data (0.5, server, read-only)

The server runs `973d68488f09` (previous `3bd9ee92bcf5`). Status integers are the enum order.

| Table | Result |
|---|---|
| `Assets` | 200, none privileged |
| `AssetAssignments` | 300, all `Active`, none with an instance |
| `AssetLossReports` | 0 |
| `PendingApproval` with no instance | 0 |
| `WorkflowInstances` | 4 `RecruitmentRequisition`, Completed, v1. None of a custody type |
| Tenants | 1 |

The acceptance data has no pending request either: `AcceptanceLogisticsSeeder` seeds one `Active` assignment and no loss report. The figures from a running stack are added in P5.

## Definitions (0.6)

| Code | Server | Seed |
|---|---|---|
| `ASSET-ASSIGN-APPROVAL` | v1 Published, distinct approvers, steps `hr-core` then `hr-management-approver` | same, id `f0050001-...-0001` |
| `ASSET-LOSS-APPROVAL` | v1 Published, no distinct approvers, one step `admin-logistics` | same, id `f0050002-...-0002` |

`EnsureMultiStepAssetAssignmentAsync` (moved into `WorkflowPackSeeder` by PLAN-44) publishes a version only if no published version has two steps. `NationalSeedOrchestrator` calls it at lines 47 and 87.

## Tenant source for a job (P-4)

- AdminLogistics has no tenant table, and no service read returns the tenant list. Each `<svc>_app` role connects to its own database only.
- **The source: `AuditLogs` in `admin_db`.** It has no row security on the server (`relrowsecurity = f`, read in P0), it has no EF filter, and `admin_app` can read it. The audit interceptor writes one row in the same save as every entity change, so a tenant with a request has a row with `EntityType` `AssetAssignment` or `AssetLossReport`. No new trust between services. On the server: 300 rows for `AssetAssignment`, one tenant.
- The table has no index beyond its key, so the migration adds one on `(EntityType, TenantId)`.
- `IgnoreTenantFilterForSystem` removes the EF filter only, and finds nothing under forced row security.

**Runtime roles:** `admin_app` in production and in `hrm-leastpriv`. The demo server and the main acceptance stack connect as the administrator, so forced row security binds there only for `*_migrator`. The test of P-4 therefore uses a real non-superuser role.

## The previous tag and unknown status values (P-3)

Storage is a plain integer with no check constraint, and EF casts an unknown value without error. The tag shows the raw number as the label. No page crashes, and there is no `switch` on either enum.

| Row on the previous tag | Effect |
|---|---|
| Assignment `ApprovalStarting` (4), `ReturnedAt` null | Open: blocks a new assignment and asset cancellation. No card, no action, the return is refused. Separation, transfer or a loss clears it. A scoped user gets 403 |
| Assignment `Withdrawn` (5), `ReturnedAt` set | Closed everywhere, the label shows `5` |
| Loss `ApprovalStarting` (4) | Not seen as pending, so the form shows; the new index refuses a second report, which the tag shows as an error |
| Loss `Withdrawn` (5) | Closed, the label shows `5` |

The consumers return without effect on either value. Rollback is safe for the data: nothing crashes, and a row in `ApprovalStarting` stays held until the new tag runs again.

**The definitions stay on the chain after a rollback.** The previous tag starts custody on v2, whose step 1 is `admin-logistics`, and it has no `admin-logistics` in `WorkflowStepAction`, so a new custody request cannot pass step 1 and has no withdrawal. Nothing crashes and no data is lost: such a request waits for the roll-forward.

## Callers

- `WorkflowTriggerService`: two production callers, both in `AssetsController` (`AssignToEmployee`, `MarkLost`).
- Writers of `PendingApproval`: `AssetsController` (both actions) and the default of `AssetLossReport.Status`. Migrations and seeders never write it.
- `policyResult.WorkflowInstanceId` is always null today (the allow-list is empty).
- Places that must learn the new status: `AssetOrgScopeGuard` (the anchor), `Details.cshtml`, `MarkLost`'s pending check, the filtered unique index on loss reports (`"Status" = 0`).
- `PartyAck` compensation reopens every row with `ReturnedAt` for the employee, whatever its status: it must leave withdrawn and rejected rows closed.

## Gateway and policies

| Path | Today |
|---|---|
| `/ui/hr-core/WorkInbox` GET | `HrCoreWorkInbox` |
| `/ui/hr-core/WorkflowApprovals` GET | would be `HrCoreReadOnly` |
| POST with `/ApproveWorkflow` or `/RejectWorkflow` | `HrCoreBranchOperation`, so the new rule must come before the mutating block |

`admin-logistics` is in neither `WorkflowStepAction` nor `HrCoreWorkInbox`. `WorkflowInstancesController` keeps `HrCoreReadOnly` on the class, so `admin-logistics` acts through the generic page only.

The inbox renders `/ui/hr-core` + the deep link, so the link for external types is `/WorkflowApprovals/Details/{id}`. The HR.Core layout shows the full menu to everyone but the marker and the compliance-only user, so a logistics-only user needs a short menu.

## Acceptance classes that touch this (P-12)

- **Prod-like:** `AcWf43DefinitionGovernanceTests` (43e and 43f pin `hr-core` on step 1: rewritten), `AcMsg40AtomicPublishingTests` (an instance pinned to v1), `AcS2s42TrustBoundaryTests`, `AcHcm25ProductionApprovalTests`, `AcInt37ProductionWiringTests`, `AcOps39KeycloakDataProtectionTests`, `AcWf44InstanceIntegrityTests`.
- **Lab:** `Ac11PolicyWorkflowTests`, `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests` (all `required: false` today), `AcWf23WorkInboxUiTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcHcm28CommitteeAppealTests`.
- The seeded privileged asset `ACCEPT-ASSET-BR1` already has a holder, so tests insert their own assets, as `AcS2s42TrustBoundaryTests` does.

## Demo users (P-13)

- The manifest has 12 demo users, none with `admin-logistics`. On the server: 13 realm users, none with `admin-logistics`, and `e2e-checker-mgmt` is the only `hr-management-approver`.
- Keycloak imports the realm only when it is new, and `reconcile.sh` never touches users. Adding to the manifest alone creates nothing on the live realm, so P7 also creates the two users with `kcadm` in the Keycloak container, where the demo password is already in the environment.
- A logistics user needs the role and `tenant_id` only: scope binds `branch-manager` alone.
- Seeded demo assets are not privileged and their values run from 50,000 to 5,000,000: an assignment needs approval (value above 10,000) and a loss does not. A loss on the demo needs an asset created through the UI, which the shim classifies as privileged.
