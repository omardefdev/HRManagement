# PLAN-46 baseline

Taken 2026-11-07 on `origin/main` = `c8252dc6`.

## 0.2 Dependencies

PLAN-40 (`OutboxPublishOrderingArchitectureTests`), PLAN-43 (`ExternalApprovalService`, `WorkflowApprovalStartRequest`, `ActingUserScope`) and PLAN-45 (`WorkflowApprovalsController`, `WorkInboxAudience`, `ExternalApprovalGate`) are on `main`.

## 0.3 Build

`dotnet build HRManagement.slnx -warnaserror`: 0 errors, 0 warnings, 80 s.

## 0.5 Data

### Demo server (read-only, tag `9560b03e51a3`)

| Item | Count |
|------|-------|
| `CardRequests` status 0 (`Pending`) | 45 |
| `CardRequests` status 1 (`Processed`) | 450 |
| `IssuedCards` | 457 |
| `DisciplinaryActions` | 20, all in the national tenant |
| Instances of `CardRequest`, `DisciplinaryAction`, `SeparationRecord`, `ServiceTransaction`, `TransferRequest`, `Ticket` | 0 |

Definitions on the server, all version 1, published, national tenant: `DISCIPLINARY-APPROVAL`, `HIRE-WIZARD`, `ID-CARD-ISSUANCE`, `IT-TICKET`, `SEPARATION-WIZARD`, `TRANSFER-APPROVAL`.

Row security on the server: `CardRequests`, `IssuedCards` (owner `idcard_migrator`) and `DisciplinaryActions` (owner `compliance_migrator`) have row security enabled and forced. `AuditLogs` in both databases has neither.

The acceptance figures are added when the stack next starts (P5).

## 0.6 PLAN-60 alignment

PLAN-60 §0.1 keeps the names and order of PLAN-46 §1. Differences 3 to 5 are already fixed in PLAN-60's text:

- line 95: `Workflow:EnforceStepRoleEntityTypes` is named only as abolished;
- line 106: owner-side reconciliation is required of it;
- lines 160 and 232: `AlreadyTerminal` applies `FinalOutcome`.

PLAN-60's document needs no correction.

## Inventories

### Publishers and consumers

- `IssuedCards.Add`: `IssuedCardsController` Create and Renew, `CardRequestsController.ProcessRequest`, and `DemoIDCardSeeder` (seed data).
- `ICardIssued` is published only by `IssuedCardsController` Create and Renew; `ProcessRequest` publishes nothing (G-IDA-03, wave 2). Consumers: `CardIssuedHireChecklistConsumer` (HR.Contracts), `CardIssuedAnalyticsConsumer` (Dev.Analytics).
- `IDisciplinaryActionTaken`: sole publisher `DisciplinaryActionsController.Create`; sole consumer Payroll's `DisciplinaryActionTakenConsumer`. Payroll dedupes on MessageId only, never on `ActionId`, so a publish with a new message id would write a second adjustment.

### Tenant sources

`AuditLogs` is outside row security in IDCard and Compliance (excluded by `TenantRlsMigrationHelper` and `ten002c-harden-tenant-rls.sql`), and the audit interceptor writes a row in the same save as each record. Neither service has `IX_AuditLogs_EntityType_TenantId` yet.

### Wiring

`Integration__HrCoreBaseUrl` and `hr-core.workflow` already reach `svc-idcard`, `svc-compliance` and `svc-hr-contracts`. None of them calls start, cancel or status yet. IDCard has no lookup client and no `hr-core.lookup` scope.

### Policies

- `WorkflowStepAction` holds neither `idcard` nor `compliance`; `HrCoreWorkInbox` holds `compliance` but not `idcard`.
- The gateway rule for `/ui/hr-core/WorkflowApprovals` and the module-filter exemption already exist from PLAN-45.
- `PolicyEngine:IdCard` in the plan is not a valid key: the domain is `IDCard.Lifecycle`.
- `PolicyWorkflowTriggerAllowlist` is empty, so `svc-idcard` starts nothing through a policy rule, whatever the definition's state.

### Acceptance classes

- Change needed: `AcInt37ProductionWiringTests.Ac37c` (posts `IssuedCards/Renew` and reads a new serial) and `AcOps38VpsDemoDefectChecks` step 4 (posts `IssuedCards/Create` with no request). `AcWf43VpsChecks` (exact definition counts, and `ID-CARD-ISSUANCE` in the invalid list).
- Regression only: `AcWf23WorkInboxUiTests` (check `Wf23_d`'s `a-compliance` rows), `AcHcm28CommitteeAppealTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcWf45*`, `AcS2s42TrustBoundaryTests`, `Ac11PolicyWorkflowTests`.
- No acceptance class posts `CardRequests/*` or `DisciplinaryActions/*`.

### Demo users

14 users, all in the national tenant `00000000-0000-4000-8000-000000000001`. No `idcard` user, one `compliance` user (`e2e-compliance`). `e2e-checker-mgmt` holds `hr-management-approver` and `hr-core`.

## 0.4 Baseline tests

Run with the additive P1 shared types in the tree; no existing code path changed.

- Unit, filter `IdCard|Compliance|Disciplinary|WorkflowDefinitionsHaveHandlers|Wf043|Wf045`: 320 passed, 0 failed.
- Integration, filter `IdCard|Compliance|Wf043|Wf045`: 11 passed, 16 skipped, 0 failed, 27 in all.
