# PLAN-20-EXEC — إغلاق EST-G (واجهة MDM)

تنفيذ [PLAN-20](../PLAN-20-establishment-mdm-admin-ui.md) لإثبات **AC-EST-18**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- بذرة MDM على AGENCY-A (PLAN-18 consumer patch)
- Keycloak personas `central-a`, `br1-mgr`

## أوامر التحقق

```powershell
powershell -File scripts/est007-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est007
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcEst18
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-EST-18a | `central-a` | إنشاء `GR-16` + lookup |
| AC-EST-18b | `central-a` | إنشاء `JC-HR` + lookup |
| AC-EST-18c | `br1-mgr` | Forbid على Create |
| AC-EST-18d | `central-a` | تعطيل درجة مستخدمة في JobCatalog مرفوض |

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-EST-18 | `acceptance-evidence/AC-EST-18/` | create + lookup + forbid + deactivate impact |

## Manual

1. `acc-central-a` → الحوكمة → المرجع الوطني (MDM)
2. إضافة `GR-16` (Grade) و `JC-HR` (JobClassification)
3. JobCatalog/Create → pickers تعرض الرموز الجديدة
4. `acc-br1-mgr` → ReferenceCatalog/Create → 403
