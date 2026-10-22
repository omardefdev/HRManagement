# PLAN-29-EXEC — إغلاق التعيين وتكامل العقد والرواتب والوثائق الرسمية (HCM-G)

تنفيذ [PLAN-29](../PLAN-29-hcm-hire-contract-documents.md) لإثبات **AC-HCM-29a..i** (الجلستان 1 و2).
تسليم الجلسة الأولى: [`reports/plan29-session1-handoff.md`](../../../reports/plan29-session1-handoff.md).

## المتطلبات

- Docker stack من هذا الفرع: `pwsh scripts/acceptance-reset.ps1` ثم `pwsh scripts/acceptance-up.ps1` (يحذف بيانات مشروع
  `hrm-acceptance` فقط). تتغير الصور `hr-core-web` و`hr-contracts-web` و`payroll-web` و`hr-gateway` (ترحيلات
  `HCM029_OfferDocument` و`FUN029_ContractOfferTerms` و`FUN029_ContractTypeRegimeMap` تُطبَّق عند الإقلاع؛ قواعد مسارات
  البوابة الجديدة في `HRManagement.ServiceDefaults`).
- **ربط أنواع العقود لبيئة القبول:** `DemoContractsSeeder` (ملف القبول) يبذر لكل جهة نوعين تجريبيين موسومين
  `AGENCY-X-ACC-LAW50-PERM` «تجريبي — دائم وفق القانون 50» و`AGENCY-X-ACC-LAW50-TEMP` مع ربط `law50`/`Permanent` و`law50`/`Temporary`.
  لا ربط لـ`law50`/`Administrative` عمداً (AC-HCM-29e). ملاحظة: أنواع العقود في بيئة القبول مرمّزة بالجهة (`AGENCY-A-DEFAULT`)،
  فلا يوجد رمز `DEFAULT` صريح هناك — قبل PLAN-29 كانت تعيينات القبول تُنشأ **بلا عقد** بصمت.
- كود الترميز `DEC-APPT-YYYY-#####` (نوع COD-001 الجديد `AdministrativeDecision`) يُبذر في HR.Core إن غاب.
- **اتصال HR.Contracts → HR.Core (S2S):** `docker-compose.acceptance.yml` يضبط `Integration__HrCoreBaseUrl` وهوية `svc-hr-contracts`
  (السر المخبري ونطاقات `hr-core.lookup` و`hr-core.workflow` التي يضيفها `sync-keycloak-dev-realm.ps1`)؛ دونها يفشل توليد
  أرقام المعاملات ولا تُنشأ معاملة التعيين.
- **ملاحظة:** في بيئة القبول تفقد رموز COD-001 بادئتها (مثلاً `-2026-00001`) — خلل سابق خارج PLAN-29.
- AC-HCM-29a/d/e تحتاج اتصال HR.Core → Payroll لحدود الدرجة (كما في [PLAN-27-EXECUTION](PLAN-27-EXECUTION.md))، وإلا تُتخطى.

### الإعدادات

| الخدمة | المفتاح | الافتراضي | Production/Staging | الجلسة |
|--------|---------|-----------|--------------------|--------|
| HR.Core | `Recruitment:DefaultEmploymentType` | `Permanent` | — | 1 |
| HR.Core | `Recruitment:OfferLetterConditionsAr` | نص شروط قصير | — | 1 |
| HR.Contracts | `Contracts:AllowDefaultContractTypeFallback` | `true` | **`false`** | 2 |
| HR.Contracts | `HireChecklist:RequireAppointmentDecision` | `true` | — | 2 |
| HR.Contracts | `HireChecklist:RequireMedicalFitness` | `false` | — | 2 |
| HR.Contracts | `Recruitment:CommencementDeadlineDays` | `30` | — | 2 |
| HR.Contracts | `Recruitment:Jobs:CommencementWatchEnabled` / `…IntervalMinutes` | `true` / `1440` | — | 2 |
| Payroll | `Payroll:RequireBaseSalaryOnActivation` | `false` | — | 2 |

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| A | مطابقة الشخص وأخطاء التعيين الآمنة | `HirePersonMatchService`, `HireNationalIdRules`, `HireClosureException`, `RecruitmentHireClosureService` |
| B | نوع التوظيف من `EmploymentType` | `HireEmploymentTypeRules`, `RecruitmentHireViewModel` |
| C | `ICandidateAccepted` v4؛ لا حدث للتنقل الداخلي | `ICandidateAccepted.cs`, `RecruitmentHireClosureService.PublishHireEventsAsync` |
| D | خطاب العرض PDF وبصمته | `OfferLetterDocumentService`, `OfferLetterDocument`, `OfferLetterWorkflowHandler` |
| E | النسب الصريح وشروط العرض على العقد | `FUN029_ContractOfferTerms` (+ تعبئة من `Notes`)، `ProcessCandidateAcceptedHandler`, `PendingContractProvisioning` |
| F | نوع العقد حسب نظام الخدمة + تدخل + شاشة الإدارة | `ContractTypeRegimeMap`, `ContractTypeResolver`, `CreatePendingHireContract`, `ContractTypeRegimeMapsController` |
| G/H | `IContractActivated` بالراتب والدرجة؛ ملف الأجر | `MassTransitEventPublisher`, `HandleContractActivatedHandler` |
| I | قرار التعيين والفحص الطبي | `HireStep`, `HireChecklistDefaults`, `IssueAppointmentDecision`, `HireWizardController` |
| J | رصد عدم المباشرة | `HireCommencementWatcher`, `HireCommencementWatchJob` |
| K/L | الاختبارات والتوثيق | `Hcm029*`, `Fun029*`, `Pay029*`, `AcHcm29HireContractTests`, `hcm029-validate.ps1` |

## أوامر التحقق

```powershell
powershell -File scripts/hcm029-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm029|FullyQualifiedName~Fun029|FullyQualifiedName~Pay029"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm029|FullyQualifiedName~Fun029"   # Testcontainers
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm29
```

آخر تشغيل: **AC-HCM-29a..i = Pass** (2026-10-22). الأدلة: `acceptance-evidence/AC-HCM-29/` (`steps.md` و`steps-<سيناريو>.md`، `http-*.json`، `before.json`،
`db-checks-<سيناريو>.json`، `timing.json`، `result.json`).

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| 29a/b/c/f | `central-a` (+ `br1-mgr` و`a-management-approver` لاعتماد الطلب والعرض) | التعيين ونموذجه وخطاب العرض |
| 29d | `a-contracts-checker` | تفعيل العقد (`HrContracts.Approve`) |
| 29g | `a-contracts-maker` (قرار التعيين) + `a-contracts-checker` (الإكمال) | بوابة قرار التعيين |
| 29e | `central-a` + `a-contracts-maker` | نوع عقد غير مطابق ← تدخل ← إنشاء يدوي |
| 29i | — (رسالة على وسيط القبول) | التنقل الداخلي |

الشروط المسبقة الاختبارية (SQL، موثقة في الاختبار): الترتيب (نطاق AC-HCM-26)، الرقم الوطني للطلب، توقيع العقد وسجل
المباشرة اللازمان للتفعيل، ونقل المعاملة إلى «قيد التنفيذ» مع إكمال بنود خارج PLAN-29.

## Manual — التعيين والعقد والراتب (AC-HCM-29a..d, h)

1. `acc-central-a` → طلب مقبول العرض → **تعيين**. إن كان الرقم الوطني لموظف قائم: يفتح النموذج على «ربط موظف موجود» مع لافتة
   «يوجد موظف مسجَّل بنفس الرقم الوطني …» (b). «إنشاء جديد» بالرقم نفسه ← «الرقم الوطني مسجَّل لموظف قائم — استخدم «ربط موظف موجود».».
2. غيّر الرقم الوطني عن رقم الطلب دون مبرر ← «الرقم الوطني لا يطابق الرقم المسجَّل في طلب التوظيف — أدخل مبرر الاختلاف (10 أحرف على الأقل).» (c).
3. فئة التوظيف: دائم / إداري / متعاقد مؤقت. اختيار غير الافتراضي دون مبرر ← «اختيار فئة توظيف غير الافتراضية يتطلب مبرراً (10 أحرف على الأقل).».
4. أكّد التعيين ← `HireWizard/Step2`: رأس الصفحة يعرض العقد (النوع `…-ACC-LAW50-PERM`، الدرجة، الراتب، النظام law50، النوع) (a).
   في قاعدة العقود: `ServiceTransactions.RecruitmentApplicationId/RequisitionId/OfferLetterId/ExpectedStartDate` مملوءة (h).
5. `acc-a-contracts-checker` → العقد → توقيع + إثبات مباشرة → **تفعيل العقد** ← `ui/payroll/EmployeePayProfiles`: الراتب = راتب العرض
   والدرجة = درجة العرض في السلّم الساري (d). راتب خارج حدود الدرجة ← حجز صرف «PAY029-RANGE: الراتب خارج حدود الدرجة».

## Manual — قرار التعيين والفحص الطبي (AC-HCM-29g)

1. `HireWizard/Step2`: بند «إصدار قرار التعيين (D-6)» معلق (المالك hr-contracts، 48 ساعة).
2. **إكمال المعاملة** (الخطوة 4) مرفوض ما دام البند معلقاً («قائمة التحقق غير مكتملة …»).
3. **إصدار قرار التعيين** ← «تم إصدار قرار التعيين رقم DEC-APPT-…»؛ نقرة ثانية ← «قرار التعيين صادر مسبقاً برقم …».
4. لتفعيل الفحص الطبي: `HireChecklist__RequireMedicalFitness=true` في `hr-contracts-web` ثم تعيين جديد ← بند «الفحص الطبي قبل التعيين»؛
   أدخل رقم تقرير اللياقة ← ✓. المعاملات السابقة لا تتغير.

## Manual — نوع عقد غير مطابق (AC-HCM-29e)

1. عيّن مرشحاً بفئة «إداري» (مع مبرر) — لا ربط لـ`law50`/`Administrative`.
2. `HireWizard/Step2`: شريط أحمر «لا يوجد نوع عقد مطابق لنظام الخدمة law50 / Administrative — يتطلب تدخلاً»؛ لا عقد.
3. شاشة التدخلات (`ProcessInterventions`، نوع Hire): تدخل مفتوح بالسبب نفسه والطرف `HR.Contracts`.
4. أضف الربط من `ContractTypeRegimeMaps` للتعيينات اللاحقة، أو اختر نوع عقد ← **إنشاء العقد المعلّق** ← يختفي الشريط.

## Manual — خطاب العرض (AC-HCM-29f)

`OfferLetters/Details` لعرض صادر ← **خطاب العرض الرسمي (PDF)**؛ «رمز التحقق» في التفاصيل وعلى الخطاب. عرض مسودة أو قيد الاعتماد ←
«خطاب العرض الرسمي متاح فقط للعروض الصادرة أو المقبولة.».

## Manual — عدم المباشرة والتنقل الداخلي (J, AC-HCM-29i)

1. معاملة تعيين بتاريخ مباشرة متوقع أقدم من 30 يوماً ودون إثبات مباشرة: بعد دورة `HireCommencementWatchJob` (يومية، أول دورة بعد دقيقة
   من الإقلاع) ← شارة «متأخر عن المباشرة» في `HireWizard` وتدخل «تأخر المباشرة عن المهلة». إلغاء التعيين مؤجل.
2. تعيين من شاغر داخلي (موظف قائم) ← لا معاملة Appointment في العقود؛ HR.Movements ينشئ طلب نقل من `IInternalPlacementCompleted`.
