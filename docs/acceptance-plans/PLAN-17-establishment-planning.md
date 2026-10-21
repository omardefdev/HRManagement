# PLAN-17 — Establishment Planning (PLAN-EST-D)

**Scope:** FR-ORG-08 / CAP-ORG-04 — Phase 4 workforce planning & gap reports  
**Status:** Implemented  
**Depends on:** PLAN-14 (EST-A) + PLAN-15 (EST-B) + PLAN-16 (EST-C)

## Goal

Close the planning gap: structured `WorkforcePlan` targets, establishment gap reports from `FundedPosition` (with optional as-of), and file-based finance budget reconciliation — without live ERP integration.

## In scope

- `WorkforcePlanLine` + finance import batch/line entities (migration EST004)
- `EstablishmentMetricsService`, `EstablishmentGapAnalysisService`, `EstablishmentGapRules`
- `WorkforcePlanService` + Details/lines/approve UI
- Gap report UI + `EstablishmentPlanningApiController`
- `IFinanceBudgetPort` + CSV import + cost-center reconciliation UI/API

## Out of scope

- Live external finance / SAP integration (CAP-EXT-01)
- Replacing Dev.Analytics event-based headcount (ANA-001)
- Future EffectiveFrom live scheduling (EST-C stretch)

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-10 | Plan vs funded gap reflects occupied/vacant/frozen | Gap does not use AnalyticsEvents |
| AC-EST-11 | Branch manager sees branch plan gap only | Sibling branch hidden |
| AC-EST-12 | Finance CSV import + reconciliation variance | No claim of live external finance |

## Verification

```powershell
powershell -File scripts/est004-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj --filter Est004
```

Manual: WorkforcePlans/Details → add line → approve → GapReport shows funded/occupied/vacant/frozen; EstablishmentFinance/Import CSV → Reconciliation shows variance.

## Execution

See [PLAN-17-EXECUTION.md](execution/PLAN-17-EXECUTION.md) for Docker acceptance (AC-EST-10..12), seed patch, and `scripts/acceptance-establishment-planning.ps1`.
