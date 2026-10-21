# PLAN-24-EXEC — اعتماد تغييرات الملاك (EST-002 UI)

تنفيذ [PLAN-24](../PLAN-24-establishment-workflow-ui.md) لإثبات **AC-EST-24**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- Personas: `central-a`, `br1-mgr`, `a-management-approver`
- PLAN-23 مُنفَّذ (WorkInbox)

## مراحل التنفيذ

| # | المرحلة | الملفات |
|---|---------|---------|
| 0 | Lab-scale submit | `EstablishmentChangeLifecycleService` |
| 1 | Workflow UI | `EstablishmentChangeWorkflowUiService`, controller, partial |
| 2 | WorkInbox | `WorkflowInboxQueryService`, `WorkflowDeepLinkResolver` |
| 3 | Tests + docs | `Est024*`, `AcEst24*`, `est024-validate.ps1` |
| 4 | Enforce stretch | `Enforce_submit_sets_pending_without_auto_apply` unit + AcEst24f |

## أوامر التحقق

```powershell
powershell -File scripts/est024-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est024
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcEst24
dotnet test src/Tests/HRManagement.UnitTests --filter "Wf023|Hcm021|Hcm022"
```

## Manual smoke

1. `acc-central-a` → JobCatalog/Create → draft → EstablishmentChanges/Details → submit
2. `acc-br1-mgr` → WorkInbox → فتح → اعتماد step 1
3. `acc-a-management-approver` → اعتماد step 2 → `Applied`
4. Refresh WorkInbox — item removed

## Result

Implemented — establishment workflow UI aligned with PLAN-21 pattern; WorkInbox aggregates `EstablishmentChangeRequest`.
