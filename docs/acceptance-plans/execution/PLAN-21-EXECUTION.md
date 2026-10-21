# PLAN-21-EXEC — إغلاق HCM-A (اعتماد طلب الشاغر من UI)

تنفيذ [PLAN-21](../PLAN-21-hcm-requisition-approval-ui.md) لإثبات **AC-HCM-21**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- Keycloak personas: `central-a`, `br1-mgr`, `a-management-approver`
- `NationalScope:IsLabScale=true` (افتراضي في appsettings)

## أوامر التحقق

```powershell
powershell -File scripts/hcm021-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm021
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm21
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-HCM-21a | `central-a` → `br1-mgr` → `a-management-approver` | تقديم + اعتماد خطوتين + نشر |
| AC-HCM-21b | `central-a` | SoD — لا اعتماد ذاتي |
| AC-HCM-21c | `br1-mgr` أو `a-management-approver` | رفض → مسودة |
| AC-HCM-21d | `central-a` | Submit ينشئ WorkflowInstanceId |

## Manual

1. `acc-central-a` → `RecruitmentRequisitions/Create` → منصب شاغر → Submit
2. خروج → `acc-br1-mgr` → Details → **اعتماد**
3. `acc-a-management-approver` → Details → **اعتماد**
4. `acc-central-a` أو HR → **نشر الإعلان** / **نشر داخلي**

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-HCM-21 | `acceptance-evidence/AC-HCM-21/` | SoD UI + workflow start + approve chain |
