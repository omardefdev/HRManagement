# PLAN-17-EXEC — إغلاق قبول EST-D (بذور + اختبارات + Docker)

تنفيذ [PLAN-17 — establishment-planning](../PLAN-17-establishment-planning.md) لإثبات **AC-EST-10** (فجوة الملاك من FundedPosition)، **AC-EST-11** (نطاق فرع RBAC)، و**AC-EST-12** (استيراد CSV مالي + تسوية). يعتمد على [PLAN-00](PLAN-00-EXECUTION.md).

## المتطلبات

- Docker stack عبر `scripts/acceptance-up.ps1` (البوابة `:5200` أو `:6200` حسب compose)
- Keycloak + personas من `testdata/acceptance/personas.json`
- بذرة القبول تشمل `AcceptancePlanningPatch` (خطة BR1 معتمدة + batch مالي)

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-EST-10 | `central-a` | gap-analysis لـ BR1 |
| AC-EST-11 | `br1-mgr` + `br2-mgr` | BR1 → 200؛ BR2 → 403 |
| AC-EST-12 | `central-a` | POST import CSV + GET reconciliation |

## أوامر التحقق

### Unit + integration (محلي)

```powershell
powershell -File scripts/est004-validate.ps1
```

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter Est004
dotnet test src/Tests/HRManagement.IntegrationTests --filter Est004
```

### Docker acceptance

```powershell
powershell -File scripts/acceptance-establishment-planning.ps1
```

أو يدوياً:

```powershell
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcEst10|AcEst11|AcEst12"
```

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-EST-10 | `acceptance-evidence/AC-EST-10/` | `occupied+vacant+frozen==funded`؛ `deficitCount>0`؛ `source=FundedPosition` |
| AC-EST-11 | `acceptance-evidence/AC-EST-11/` | BR1 200؛ BR2 403 |
| AC-EST-12 | `acceptance-evidence/AC-EST-12/` | import 200 + `batchId`؛ reconciliation variance ≠ 0 |

## ملفات البذرة والاختبار

| الملف | الدور |
|-------|-------|
| `AcceptanceSeedIds.cs` | IDs ثابتة للخطة والـ batch |
| `AcceptancePlanningPatch.cs` | خطة BR1 + FinanceBudgetImportBatch |
| `testdata/acceptance/establishment/br1-budget-sample.csv` | CSV مرجعي للاستيراد |
| `Est004EstablishmentPlanningTests.cs` | as-of freeze، CSV invalid، analytics isolation |
| `Est004EstablishmentPlanningIntegrationTests.cs` | RBAC API |
| `AcEst10/11/12*.cs` | قبول حي + EvidenceRecorder |

## حالات سلبية

- AC-EST-10: لا يقرأ `AnalyticsEvents` (مصدر HR.Core = FundedPosition فقط)
- AC-EST-11: مدير BR1 لا يرى gap-analysis لـ BR2 (403)
- AC-EST-12: لا يُزعم ERP حي — `note: Stub file import` في الاستجابة

## تعريف الإنجاز

- [x] `AcceptancePlanningPatch` بعد `acceptance-up` + seed
- [x] `est004-validate.ps1` يمر (unit + integration)
- [x] `RUN_ACCEPTANCE=true` + filter `AcEst10|AcEst11|AcEst12` → Pass + مجلدات الأدلة
- [x] PLAN-17 يربط بقسم Execution هذا
