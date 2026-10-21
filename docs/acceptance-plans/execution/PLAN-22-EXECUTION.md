# PLAN-22-EXEC — إغلاق HCM-B (اعتماد مسارات التوظيف المتبقية)

تنفيذ [PLAN-22](../PLAN-22-hcm-recruitment-workflows-ui.md) لإثبات **AC-HCM-22**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1`
- Keycloak personas: `central-a`, `br1-mgr`, `a-management-approver`, `a-compliance`
- `NationalScope:IsLabScale=true` (افتراضي في appsettings)
- PLAN-21 مُنفَّذ (اعتماد طلب الشاغر يعمل)

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| 0 | Refactor مشترك | `WorkflowPanelViewModels`, `WorkflowPanelBuilder`, `_WorkflowActionPanel` |
| 1 | OfferLetter UI | `RecruitmentOfferService`, `OfferLetterWorkflowUiService`, `OfferLettersController`, `Details` |
| 2 | SelectionCommittee | `SelectionCommitteeWorkflowHandler`, `SelectionCommitteeLifecycleService`, policy seed, `Details` |
| 3 | RecruitmentAppeal | `RecruitmentAppealWorkflowHandler`, `RecruitmentAppealService`, `Details` panel |
| 4 | Tests + docs | `Hcm022*`, `AcHcm22*`, `hcm022-validate.ps1`, `HIRE_ONBOARDING_GUIDE` |

## أوامر التحقق

```powershell
powershell -File scripts/hcm022-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm022
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm22
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm021
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-HCM-22a | `central-a` → `br1-mgr` → `a-management-approver` | SubmitOffer + اعتماد خطوتين → Issued |
| AC-HCM-22b | `central-a` | SoD — لا اعتماد ذاتي على العرض |
| AC-HCM-22c | `central-a` → `br1-mgr` → `a-management-approver` | لجنة 3+ أعضاء → Submit → 3 خطوات |
| AC-HCM-22d | `central-a` → `a-compliance` | FileAppeal → اعتماد مراجع مستقل |
| AC-HCM-22e | `central-a` | SubmitOffer lab-scale ينشئ WorkflowInstanceId |

## Manual — OfferLetter (AC-HCM-22a/b/e)

1. أكمل PLAN-21: طلب شاغر معتمد ومنشور
2. `acc-central-a` → `RecruitmentApplications` → Rank → CreateOffer → `OfferLetters/Details`
3. **تقديم للاعتماد** → تحقق `PendingApproval` + workflow panel
4. `acc-br1-mgr` → Details → **اعتماد** (خطوة 1)
5. `acc-a-management-approver` → Details → **اعتماد** (خطوة 2) → `Issued`
6. `acc-central-a` → **قبول العرض** (Accept)
7. SoD: منشئ العرض لا يرى أزرار اعتماد على عرضه

## Manual — SelectionCommittee (AC-HCM-22c)

1. `acc-central-a` → `SelectionCommittees` → Create session لطلب شاغر
2. Add ≥3 members (بدون تعارض أو مع Recused)
3. **تقديم للاعتماد** → Details → workflow panel
4. `acc-br1-mgr` → خطوة 1 + 2 (خطوة 2 تتطلب نصاب ≥3)
5. `acc-a-management-approver` → خطوة 3 → `ApprovedAt` مُعبّأ

## Manual — Appeal (AC-HCM-22d)

1. رفض مرشح (`RecruitmentApplications/Details` → Reject)
2. FileAppeal مع `IndependentReviewerId` = persona compliance
3. `acc-a-compliance` → Details → workflow panel → **اعتماد**
4. تحقق تحديث `AppealStatus` + حالة الطلب
5. Negative: persona آخر ≠ `IndependentReviewerId` → لا اعتماد

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-HCM-22 | `acceptance-evidence/AC-HCM-22/` | Offer + Committee + Appeal workflow UI + SoD + lab-scale start |

## Regression

- `Hcm021` / `AcHcm21` يجب أن تبقى PASS بعد refactor `_WorkflowActionPanel`
- لا تغيير سلوك `RECRUITMENT-REQUISITION-APPROVAL` إلا عبر البنية المشتركة
