# PLAN-19 — Establishment Job Catalog Bulk Import (PLAN-EST-F)

**Scope:** EST-001 / ONB-001 / MIG-003 — استيراد جماعي محكوم  
**Status:** Implemented  
**Depends on:** PLAN-18 (EST-E) + PLAN-15 (EST-B)  
**Related cards:** EST-001, MDM-001, COD-001, ONB-001, MIG-003 (LegacyJobCode)

## Goal

استيراد CSV لإنشاء عدة إدخالات `JobCatalogEntry` في **دورة حوكمة واحدة** مع تحقق MDM وتوليد JobCode عند Apply.

## In scope

- `JobCatalogImportBatch` / `JobCatalogImportLine` (EST-006)
- `FileJobCatalogPort` + `JobCatalogImportService`
- `EstablishmentChangeType.BulkCreateJobCatalog` + applier atomic
- `JobCatalog/Import` + template CSV
- ملخص جماعي في `EstablishmentChanges/Details`
- AC-EST-15..17 + `est006-validate.ps1`

## Out of scope

- استيراد جماعي للمناصب الممولة
- Excel / async jobs >500 rows
- شاشة MDM

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-15 | CSV 3 صفوف صالحة → draft → apply → 3 entries | — |
| AC-EST-16 | — | GradeCode=INVALID → رفض + رقم السطر |
| AC-EST-17 | Migration + LegacyJobCode محفوظ | Standard + LegacyJobCode مرفوض |

## Verification

```powershell
powershell -File scripts/est006-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est006
dotnet test src/Tests/HRManagement.IntegrationTests --filter Est006
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcEst15|AcEst16|AcEst17"
```
