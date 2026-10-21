# PLAN-15 — Establishment Governance (PLAN-EST-B)

**Scope:** FR-ORG-07 / CAP-ORG-03 — Phase 2 governance  
**Status:** Implemented  
**Depends on:** PLAN-14 (PLAN-EST-A)

## Goal

Govern establishment mutations through approved change requests with legal source, workflow approval, and append-only change logs — without direct UI writes to live `JobCatalogEntry` / `FundedPosition` tables.

## In scope

- `EstablishmentChangeRequest` + `EstablishmentChangeLog` (migration EST002)
- `EstablishmentChangeApplier` + payload DTOs
- `EstablishmentChangeLifecycleService` (draft / submit / apply / cancel)
- Policy domain `Core.Establishment` + workflow `ESTABLISHMENT-CHANGE-APPROVAL`
- `EstablishmentChangeWorkflowHandler` wired in `WorkflowInstancesController`
- `EstablishmentChangesController` + Index/Details + Submit/Cancel
- EST-A POST refactor → change request drafts + change log tabs on Details
- Nav: **طلبات تغيير الملاك**
- `PolicyEnforcementMode.Off` → submit applies immediately (acceptance/dev)

## Out of scope (EST-C/D)

- Temporal/as-of establishment views
- Gap reports / `WorkforcePlan`
- External finance integration

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-04 | Central: create position request + legal → approve → appears in vacant-positions | PendingApproval position not in vacant-positions |
| AC-EST-05 | Reject restores Draft; no position created | — |
| AC-EST-06 | Details shows ChangeLog with LegalSource + actor | Submit without legal source rejected |

## Verification

```powershell
powershell -File scripts/est002-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est002
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj --filter Est002
```

Manual: login as `acc-central-a` → JobCatalog/Create → save draft → EstablishmentChanges/Details → submit with SYR-001 legal source → position/catalog applied (Off mode) or WF approve (Enforce mode).
