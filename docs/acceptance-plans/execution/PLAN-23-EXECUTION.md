# PLAN-23-EXEC — صندوق عمل موحد (WF-002 UI)

تنفيذ [PLAN-23](../PLAN-23-wf-work-inbox-ui.md) لإثبات **AC-WF-23**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- Keycloak personas: `central-a`, `br1-mgr`, `a-management-approver`, `a-compliance`
- PLAN-21 + PLAN-22 مُنفَّذان (workflow Details panels)

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| 0 | Query + mapping | `WorkflowInboxQueryService`, `WorkflowInboxEligibilityService`, `WorkflowDeepLinkResolver`, `WorkInboxItemMapper` |
| 1 | Service + DI | `HrCoreWorkInboxService`, `Program.cs` |
| 2 | UI | `WorkInboxController`, `WorkInbox/Index`, `_Layout` |
| 3 | Stats | `StatsApiController`, Gateway badge (count field unchanged) |
| 4 | Tests + docs | `Wf023*`, `AcWf23*`, `wf023-validate.ps1`, `HIRE_ONBOARDING_GUIDE` |

## أوامر التحقق

```powershell
powershell -File scripts/wf023-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Wf023
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcWf23
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm021|Hcm022"
```

## Manual smoke

1. Seed pending workflows via PLAN-21/22 paths
2. Login `acc-br1-mgr` → `WorkInbox/Index` → verify items + «فتح» links
3. Click deep link → approve on Details → item disappears after refresh
4. Gateway dashboard badge reflects unified `count`

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-WF-23a | `br1-mgr` | inbox lists requisition + offer |
| AC-WF-23b | `a-management-approver` | step-2 items only |
| AC-WF-23c | `central-a` | SoD — no self-approval items |
| AC-WF-23d | `a-compliance` | appeal review item |
| AC-WF-23e | any authenticated | stats count shape |
| AC-WF-23f | `br1-mgr` | legacy approval row |

## Result

Implemented — virtual projection inbox without EF persistence; write paths on `IWorkInboxService` throw `NotSupportedException` by design.
