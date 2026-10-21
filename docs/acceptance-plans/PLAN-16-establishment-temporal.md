# PLAN-16 — Establishment Temporal (PLAN-EST-C)

**Scope:** FR-ORG-07 / CAP-ORG-03 — Phase 3 temporal/as-of  
**Status:** Implemented  
**Depends on:** PLAN-14 (EST-A) + PLAN-15 (EST-B)

## Goal

Add dated versions for `JobCatalogEntry` and `FundedPosition` with as-of queries and timeline UI — fixing org as-of funded positions (AC-14 gap) without gap reports (EST-D).

## In scope

- `JobCatalogVersion` + `FundedPositionVersion` (migration EST003)
- `EffectiveFrom` on `EstablishmentChangeRequest`
- `JobCatalogVersionService`, `FundedPositionVersionService`, `EstablishmentAsOfService`
- Version append on governed apply + hire assign/vacate
- Fix `OrgUnitVersionService.ResolveAsOfAsync` funded positions
- `EstablishmentTemporalApiController` (as-of + history)
- As-of picker + timeline on JobCatalog/FundedPositions Details
- Backfill in acceptance/national seed

## Out of scope (EST-D)

- WorkforcePlan gap reports
- External finance integration

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-07 | as-of before/after freeze reflects correct status | as-of does not use live status |
| AC-EST-08 | as-of before hire assign = Vacant | — |
| AC-EST-09 | Details timeline shows LegalSource + OperationType | as-of before position created = NotFound |

## Verification

```powershell
powershell -File scripts/est003-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est003
```

Manual: FundedPositions/Details → pick past date → status matches historical version; org as-of API returns correct position statuses.
