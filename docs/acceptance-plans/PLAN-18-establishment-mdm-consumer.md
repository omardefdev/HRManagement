# PLAN-18 — Establishment MDM Consumer (PLAN-EST-E)

**Scope:** FR-ORG-07 / CAP-ORG-03 — Phase 4 consumer alignment  
**Status:** Implemented  
**Depends on:** PLAN-14 (EST-A) + PLAN-15 (EST-B) + PLAN-16 (EST-C)  
**Related cards:** EST-001, MDM-001, COD-001 (partial)

## Goal

ربط نماذج دليل الوظائف والمناصب الممولة بـ MDM-001 (درجة + تصنيف وظيفي)، توليد `JobCode` عبر COD-001 عند Apply، وتحسين UX لمسار المسودة → `EstablishmentChanges`.

## In scope

- Lookup API: `/api/lookup/reference-data?kind=Grade|JobClassification`
- TomSelect pickers في `_JobCatalogFormFields` و `FundedPositions/Create`
- `EstablishmentReferenceValidator` + `ReferenceDataProvider`
- `CodeEntityKind.JobCatalogEntry` + `JobCatalogCodeService`
- UX: alert مسودة، `_EstablishmentPayloadSummary`، JobCode readonly على Edit

## Out of scope (PLAN-19)

- استيراد CSV جماعي
- migration `OccupationalGroup` → `JobClassificationCode`
- شاشة MVC لإدارة MDM

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-13 | lookup Grade/JobClassification + draft→apply بأكواد MDM صالحة | `GradeCode=INVALID` مرفوض |
| AC-EST-14 | Apply يولّد `JobCode` فريداً عبر COD | preview/generate بدون policy → fallback آمن |

## Verification

```powershell
powershell -File scripts/est005-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est005
dotnet test src/Tests/HRManagement.IntegrationTests --filter Est005
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcEst13|AcEst14"
```

Manual: `acc-central-a` → JobCatalog/Create → pickers → «حفظ مسودة» → EstablishmentChanges/Details → submit → JobCode مولّد.
