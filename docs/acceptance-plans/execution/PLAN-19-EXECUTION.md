# PLAN-19-EXEC — إغلاق EST-F (استيراد CSV دليل الوظائف)

تنفيذ [PLAN-19](../PLAN-19-establishment-job-catalog-import.md) لإثبات **AC-EST-15..17**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- بذرة MDM (`GR-05`, `JC-ADM`, …)
- Keycloak persona `central-a`

## أوامر التحقق

```powershell
powershell -File scripts/est006-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est006
dotnet test src/Tests/HRManagement.IntegrationTests --filter Est006
powershell -File scripts/acceptance-establishment-job-catalog-import.ps1
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-EST-15 | `central-a` | import sample CSV → apply |
| AC-EST-16 | `central-a` | invalid grade CSV rejected |
| AC-EST-17 | `central-a` | migration legacy code |

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-EST-15 | `acceptance-evidence/AC-EST-15/` | 3 JobCatalog entries after apply |
| AC-EST-16 | `acceptance-evidence/AC-EST-16/` | import rejected with line number |
| AC-EST-17 | `acceptance-evidence/AC-EST-17/` | LegacyJobCode preserved |

## Manual

1. `acc-central-a` → JobCatalog/Import
2. رفع `testdata/acceptance/establishment/job-catalog-sample.csv`
3. EstablishmentChanges/Details → submit + legal → applied
4. JobCatalog/Index → 3 سجلات جديدة
