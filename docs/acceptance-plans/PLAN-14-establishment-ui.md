# PLAN-14 — Establishment UI (PLAN-EST-A)

**Scope:** FR-ORG-07 / CAP-ORG-03 — Phase 1 operational UI  
**Status:** Implemented  
**Related card:** EST-001

## Goal

Close the operational gap: create and manage `JobCatalogEntry` and `FundedPosition` from HR.Core screens (not seed-only), while preserving `EstablishmentService` rules and existing HCM consumption.

## In scope

- `EstablishmentScopeService` (branch scope + materialized path filter)
- `JobCatalogController` + Index/Create/Edit/Details
- `FundedPositionsController` + Index/Create/Details + Freeze/Unfreeze
- `GET /api/lookup/job-catalog` + `initJobCatalogPicker`
- Sidebar **الملاك والمناصب (EST-001)**
- RBAC: central write (unrestricted org scope); branch manager read-only on funded positions within branch; branch manager forbid on job catalog

## Out of scope (EST-B/C/D)

- Workflow approval for establishment changes
- Temporal/as-of establishment views
- Establishment gap reports
- External finance integration

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-01 | Central creates catalog + vacant position → appears in requisition create | Frozen position excluded from vacant-positions API |
| AC-EST-02 | Branch manager sees branch positions in Index | Cannot see sibling branch; no Create |
| AC-EST-03 | Freeze vacant position with reason → Frozen | Freeze occupied position rejected |

## Verification

```powershell
powershell -File scripts/est001-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est001
```

Manual: login as `acc-central-a` → create catalog entry + funded vacant position (2026) → `RecruitmentRequisitions/Create` → select branch → new position appears.
