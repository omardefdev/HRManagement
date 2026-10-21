# PLAN-18-EXEC — إغلاق EST-E (MDM consumer + COD JobCode)

تنفيذ [PLAN-18 — establishment-mdm-consumer](../PLAN-18-establishment-mdm-consumer.md) لإثبات **AC-EST-13** (pickers + MDM validation) و**AC-EST-14** (COD JobCode). يعتمد على PLAN-14..16.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- بذرة MDM الوطنية (`NATIONAL-MDM-SY-v1`) تحتوي `GR-05`, `JC-ADM`
- Keycloak persona `central-a`

## أوامر التحقق

```powershell
powershell -File scripts/est005-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est005
dotnet test src/Tests/HRManagement.IntegrationTests --filter Est005
powershell -File scripts/acceptance-establishment-mdm-consumer.ps1
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-EST-13 | `central-a` | lookup MDM + مسار draft |
| AC-EST-14 | `central-a` | preview COD JobCatalogEntry |

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-EST-13 | `acceptance-evidence/AC-EST-13/` | lookup Grade يعيد GR-*؛ INVALID مرفوض في unit/integration |
| AC-EST-14 | `acceptance-evidence/AC-EST-14/` | preview JobCatalogEntry يعيد رمز غير فارغ |

## تعريف الإنجاز

- [x] لا نص حر لـ Grade/JobClassification في نماذج EST
- [x] Apply يرفض أكواد MDM غير صالحة
- [x] JobCode يُولَّد عند Apply (Create)
- [x] `est005-validate.ps1` + AC-EST-13/14 Pass
