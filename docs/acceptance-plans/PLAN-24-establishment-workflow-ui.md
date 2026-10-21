# PLAN-24 — اعتماد تغييرات الملاك + توسيع صندوق العمل (PLAN-EST-A / EST-002 UI)

**Scope:** EST-002 — workflow approve UI لـ `EstablishmentChangeRequest` + توسيع WorkInbox (PLAN-23)  
**Status:** Implemented  
**Depends on:** [PLAN-15](PLAN-15-establishment-governance.md) + [PLAN-23](PLAN-23-wf-work-inbox-ui.md) (Implemented)  
**Related cards:** EST-002, WF-001, WF-002, CAP-ORG-03  
**Reference:** [EST_001_ESTABLISHMENT.md](../EST_001_ESTABLISHMENT.md)

## Goal

إكمال مسار **حوكمة الملاك** من الواجهة: بعد Submit يظهر **لوحة اعتماد workflow** (خطوتان) مع SoD، وتظهر الطلبات في **صندوق العمل** مع deep link.

## Deliverables

| Artifact | Path |
|----------|------|
| Lab-scale submit | `EstablishmentChangeLifecycleService` — Off+IsLabScale → start WF |
| UI service | `EstablishmentChangeWorkflowUiService` |
| Controller | `ApproveWorkflow` / `RejectWorkflow` on `EstablishmentChangesController` |
| Partial | `_EstablishmentChangeWorkflowPanel.cshtml` |
| Inbox | `WorkflowInboxQueryService` + `WorkflowDeepLinkResolver` |
| Tests | `Est024EstablishmentWorkflowUiTests`, `AcEst24EstablishmentWorkflowUiTests` |
| Script | `scripts/est024-validate.ps1` |

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-24a | central submit → lab WF → br1-mgr step 1 | — |
| AC-EST-24b | mgmt-approver step 2 → Applied | — |
| AC-EST-24c | — | central-a SoD on own request |
| AC-EST-24d | Reject → Draft | — |
| AC-EST-24e | WorkInbox deep link | — |
| AC-EST-24f (stretch) | Enforce submit → PendingApproval + WF | Branch denied |

## Verification

```powershell
powershell -File scripts/est024-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est024
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcEst24
dotnet test src/Tests/HRManagement.UnitTests --filter "Wf023|Hcm021|Hcm022"
```

## Definition of done

- [x] Details shows workflow panel (not raw GUID)
- [x] Lab-scale submit starts workflow without auto-apply
- [x] EstablishmentChangeRequest in WorkInbox
- [x] Est024 + Wf023/Hcm021/Hcm022 regression PASS
