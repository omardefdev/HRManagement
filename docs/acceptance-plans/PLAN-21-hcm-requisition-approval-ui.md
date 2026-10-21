# PLAN-21 — اعتماد طلب الشاغر من الواجهة (PLAN-HCM-A)

**Scope:** FR-HCM-02 / إغلاق فجوة `RecruitmentRequisitions/Details`  
**Status:** Implemented  
**Depends on:** PLAN-02 + PLAN-03  
**Related cards:** HCM-001, WF-001, CAP-HCM-01

## Goal

تمكين مسار **تقديم → اعتماد (خطوتان) → نشر** من الواجهة مع SoD (المنشئ لا يعتمد طلبه)، وبدء workflow تلقائياً في بيئة المختبر عند `PolicyEngine:Off`.

## In scope

- `WorkflowInstanceActionService` + `WorkflowStarter` (DRY مع API)
- `RecruitmentRequisitionWorkflowUiService` + ViewModels
- `RecruitmentRequisitions/Details` — لوحة مسار + اعتماد/رفض
- Partials: `_RequisitionWorkflowPanel`, `_WorkflowProgressPanel`, `_WorkflowActionPanel`
- Lab-scale: بدء `RECRUITMENT-REQUISITION-APPROVAL` عند Submit + `IsLabScale=true`
- AC-HCM-21 + `hcm021-validate.ps1` + Hcm021 / AcHcm21 tests

## Out of scope (PLAN-22..24)

- خطاب العرض، لجنة الاختيار، الاستئناف
- صندوق عمل موحد
- `EstablishmentChanges` UI
- تفعيل Enforce الكامل في acceptance

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-21a | central-a يقدّم → br1-mgr خطوة 1 → mgmt-approver خطوة 2 → Approved + نشر | — |
| AC-HCM-21b | — | central-a لا يرى اعتماد على طلبه |
| AC-HCM-21c | رفض → Draft | — |
| AC-HCM-21d | Submit lab-scale ينشئ WorkflowInstanceId | Off + non-lab → تحذير |

## Verification

```powershell
powershell -File scripts/hcm021-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm021
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm21
```

Manual: `acc-central-a` → Create → Submit → `acc-br1-mgr` → Details → اعتماد → `acc-a-management-approver` → اعتماد → نشر.
