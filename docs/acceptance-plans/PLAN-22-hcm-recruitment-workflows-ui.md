# PLAN-22 — اعتماد مسارات التوظيف المتبقية (PLAN-HCM-B)

**Scope:** FR-HCM-04/06 — إغلاق فجوات workflow UI بعد PLAN-21  
**Status:** Implemented  
**Depends on:** [PLAN-21](PLAN-21-hcm-requisition-approval-ui.md) (Implemented) + PLAN-02/03  
**Related cards:** HCM-004, HCM-006, WF-001, CAP-HCM-01  
**Out of scope (PLAN-23..24):** صندوق عمل موحد (WF-002 UI)، `EstablishmentChanges` UI، تفعيل Enforce الكامل في acceptance، تحسين pickers اللجنة (employee GUID)

## Goal

إكمال مسار التوظيف من **الاختيار → خطاب العرض → (اختياري) لجنة → استئناف** باعتماد workflow من الواجهة، بإعادة استخدام البنية التي أنشأها PLAN-21 (`WorkflowInstanceActionService`, `WorkflowStarter`, partials).

## الوضع الحالي (فجوات)

| المسار | Backend | Workflow pack | Handler | UI اعتماد |
|--------|---------|---------------|---------|------------|
| **OfferLetter** | `RecruitmentOfferService.SubmitForApprovalAsync` + `OfferLetterWorkflowHandler` | `RECRUITMENT-OFFER-APPROVAL` (2 خطوات) | نعم في `WorkflowInstanceActionService` | **لا** — `OfferLetters/Details` |
| **SelectionCommittee** | `SelectionCommitteeService` — إنشاء/أعضاء فقط | `RECRUITMENT-SELECTION-COMMITTEE` (3 خطوات) | **لا** | **لا** — `SelectionCommittees/Index` فقط |
| **RecruitmentAppeal** | `RecruitmentAppealService` — `FileAppeal`/`DecideAppeal` مباشر | `RECRUITMENT-APPEAL-REVIEW` (1 خطوة `compliance`) | **لا** | جزئي — `RecruitmentApplications/Details` بدون workflow |

**فجوات إضافية:**

- `PolicyEngine:Off` لخطاب العرض يُصدر **`Issued` مباشرة** (`RecruitmentOfferService` سطر 96–101) — يتعارض مع اختبار UI (PLAN-21 أبقى `PendingApproval` + workflow في lab-scale).
- لا قواعد policy لـ committee/appeal في `PolicyEngineDomainSeeds` (يوجد فقط `R-OFF-ISSUE` → `RECRUITMENT-OFFER-APPROVAL`).
- SoD لـ `OfferLetter` **غير مُطبّق** في `WorkflowInstanceActionService` (يوجد فقط لـ requisition/establishment).
- `_WorkflowActionPanel` مربوط hard-coded بـ `RecruitmentRequisitionsController`.
- `SelectionCommitteeSession.WorkflowInstanceId` موجود في النموذج لكن غير مستخدم.

## In scope

### 0. Refactor مشترك (Prerequisite)

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `WorkflowPanelViewModel` | `ViewModels/WorkflowPanelViewModels.cs` | تعميم `RequisitionWorkflowPanelViewModel` |
| `WorkflowPanelBuilder` | `HR.Core.Web/Services/WorkflowPanelBuilder.cs` | بناء Steps/CanApprove/BlockReason (DRY) |
| `_WorkflowActionPanel.cshtml` | `Views/Shared/` | قبول `ViewData`: Controller, ApproveAction, RejectAction, EntityId, ConfirmReject |

تحديث PLAN-21 requisition لاستخدام البنية العامة (regression عبر `Hcm021`).

### 1. OfferLetter — اعتماد خطاب العرض (HCM-004)

**Backend:**

- `RecruitmentOfferService.SubmitForApprovalAsync`: عند `PolicyEngine:Off` + `NationalScope:IsLabScale=true` — بدء `RECRUITMENT-OFFER-APPROVAL` عبر `IWorkflowStarter` + `PendingApproval` (مثل PLAN-21). إزالة auto-issue في lab-scale.
- `WorkflowInstanceActionService`: SoD — `OfferLetter.CreatedBy != actor` (رسالة عربية مطابقة).

**UI:**

- `OfferLetterWorkflowUiService` + `OfferLetterDetailsViewModel`.
- `OfferLettersController`: `ApproveWorkflow` / `RejectWorkflow` POST (`HrCoreBranchOperation`).
- `OfferLetters/Details.cshtml`: partial `_OfferWorkflowPanel` + banner عند `PendingApproval` بلا workflow.

**Policy:** rule موجود `R-OFF-ISSUE` → `RECRUITMENT-OFFER-APPROVAL`.

### 2. SelectionCommittee — لجنة الاختيار (HCM-006)

**Backend:**

- `SelectionCommitteeWorkflowHandler` — عند اكتمال workflow: تعيين `ApprovedAt` على الجلسة.
- `SelectionCommitteeLifecycleService`:
  - `SubmitForApprovalAsync(sessionId)` — يتحقق من ≥1 عضو، يبدأ `RECRUITMENT-SELECTION-COMMITTEE` (policy Enforce أو lab Off + IsLabScale).
  - يربط `WorkflowInstanceId`.
- `PolicyEngineDomainSeeds`: rule `R-COMMITTEE-SUBMIT` → `RECRUITMENT-SELECTION-COMMITTEE`.
- `WorkflowInstanceActionService`: handler جديد.
- **Step 2 gate:** خطوة «نصاب اللجنة» لا تُفعّل `CanApprove` إلا إذا `RecruitmentAppealWorkflowRules.HasQuorum` (≥3 غير منسحبين).

**UI:**

- `SelectionCommitteesController`: `Details(sessionId)` + `Submit` + `ApproveWorkflow`/`RejectWorkflow`.
- `Index.cshtml`: رابط Details + زر «تقديم للاعتماد».
- Workflow panel على Details.

**Workflow steps (seeded):**

1. تشكيل اللجنة — `hr-core`
2. نصاب اللجنة — `hr-core` (+ quorum UI gate)
3. اعتماد مدير HR — `hr-management-approver`

**Migration:** `ApprovedAt` nullable على `SelectionCommitteeSession` (اختياري بسيط).

### 3. RecruitmentAppeal — مراجعة الاستئناف (HCM-006)

**Backend:**

- `RecruitmentAppealWorkflowHandler`:
  - عند **approve**: استدعاء منطق `DecideAppealAsync` (upheld=true افتراضياً أو قرار من comments/ContextJson).
  - عند **reject**: مسار رفض الاستئناف + إعادة حالة الطلب.
- `RecruitmentAppealService.FileAppealAsync`: بعد إنشاء appeal — بدء `RECRUITMENT-APPEAL-REVIEW` (Enforce أو lab Off + IsLabScale) + ربط `WorkflowInstanceId`.
- **تعطيل** `DecideAppeal` المباشر عند وجود workflow نشط.
- `PolicyEngineDomainSeeds`: rule `R-APPEAL-FILE` → `RECRUITMENT-APPEAL-REVIEW`.
- `WorkflowInstanceActionService`: handler + gate `CanDecideAppeal` (مراجع مستقل).

**UI:**

- `RecruitmentAppealWorkflowUiService` — panel على `RecruitmentApplications/Details`.
- إخفاء/استبدال نموذج `DecideAppeal` المباشر عند وجود workflow؛ إظهار panel لـ `acc-a-compliance` (`compliance` role + `IndependentReviewerId`).

## Out of scope

- صندوق عمل موحد (PLAN-23)
- ربط المنصة + acceptance Enforce + E2E (PLAN-24)
- `EstablishmentChanges` workflow UI
- تحسين UX اختيار موظفين للجنة (GUID manual)

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-22a | Ranked app → CreateOffer → Submit → br1-mgr → mgmt-approver → `Issued` → Accept | — |
| AC-HCM-22b | — | منشئ العرض لا يرى اعتماد على عرضه (SoD) |
| AC-HCM-22c | Committee 3+ members → Submit → 3 خطوات UI → `ApprovedAt` | Submit بلا أعضاء → مرفوض |
| AC-HCM-22d | Rejected app → FileAppeal → `acc-a-compliance` يعتمد → حالة الطلب تتحدث | مراجع ≠ `IndependentReviewerId` → ممنوع |
| AC-HCM-22e | Offer Submit lab-scale → `PendingApproval` + `WorkflowInstanceId` | Off + non-lab → تحذير (لا auto-issue في lab) |

**Personas:** `acc-central-a`, `acc-br1-mgr`, `acc-a-management-approver`, `acc-a-compliance` (`testdata/acceptance/personas.json`).

## Verification

```powershell
powershell -File scripts/hcm022-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm022
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm22
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm021
```

**Manual (مسار كامل):**

1. طلب شاغر معتمد (PLAN-21) → طلبات → Rank → CreateOffer
2. SubmitOffer → اعتماد خطوتين → Issued → Accept
3. (اختياري) SelectionCommittees → أعضاء → Submit → اعتماد 3 خطوات
4. (اختياري) Reject candidate → FileAppeal → compliance approve

## Definition of done

- اعتماد **OfferLetter** + **SelectionCommittee** + **Appeal** من UI
- lab-scale يبدأ workflow ولا يتخطى الاعتماد (offer لا auto-issue)
- Handlers الثلاثة مربوطة بـ `WorkflowInstanceActionService`
- AC-HCM-22a..e = Pass + `hcm022-validate.ps1` PASS + `Hcm021` regression PASS

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan doc | `docs/acceptance-plans/PLAN-22-hcm-recruitment-workflows-ui.md` |
| Execution | `docs/acceptance-plans/execution/PLAN-22-EXECUTION.md` |
| Script | `scripts/hcm022-validate.ps1` |
| Unit | `Hcm022RecruitmentWorkflowsUiTests.cs` |
| Acceptance | `AcHcm22RecruitmentWorkflowsUiTests.cs` |
| Guide | تحديث `docs/HIRE_ONBOARDING_GUIDE.md` — خطوات 6–8 |

## Effort estimate

~1.5 sprint (أكبر من PLAN-21): refactor مشترك + 3 handlers + 3 UI surfaces + policy seeds + 5 AC.
