# PLAN-29 — إغلاق التعيين وتكامل العقد والرواتب والوثائق الرسمية (PLAN-HCM-G)

**Scope:** HCM-001 ↔ FUN-001 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-12, G-13, G-22، وجزء من G-18 (وثيقة العرض)  
**Status:** Proposed  
**Priority:** P1  
**Depends on:** PLAN-27 (شروط عرض متسقة)، PLAN-25 المرحلة 0  
**Decisions:** D-6 (قرار التعيين والفحص الطبي)  
**Services touched:** HR.Core، HR.Contracts، Payroll، (HR.Health اختياري)، File.Storage

## Goal

1. لا يُنشأ شخص مكرر عند التعيين، وكل خطأ متوقع يظهر برسالة عربية لا بخطأ 500.
2. تنتقل **شروط العرض المعتمدة** (الدرجة، الراتب، نظام الخدمة، فئة التوظيف، تاريخ المباشرة) آلياً إلى العقد ثم إلى ملف الأجر، وفق المسار: العرض ← `ICandidateAccepted` v4 ← العقد ← `IContractActivated` ← الرواتب.
3. يُبنى النسب (lineage) على حقول صريحة، لا على تحليل نص `Notes`.
4. تُصدر وثائق رسمية: خطاب العرض PDF، وقرار التعيين بنداً في قائمة التحقق.

## الوضع الحالي

| البند | المرجع |
|-------|--------|
| لا فحص مسبق لوجود موظف بالرقم الوطني؛ الفهرس الفريد يرمي `DbUpdateException` غير ملتقط ← 500 | `RecruitmentHireClosureService.cs:162-166`، `HRCoreDbContext.cs:128`، `RecruitmentApplicationsController.cs:473` |
| لا مقارنة بين الرقم المُدخل ورقم الطلب | `RecruitmentApplicationsController.Hire` POST |
| `EmploymentType.Permanent` مثبت | `RecruitmentHireClosureService.cs:309, 394` |
| فئات النموذج PERMANENT/TEMPORARY/CONTRACT/INTERNSHIP ≠ `EmploymentType` (Permanent/Administrative/Temporary) | `RecruitmentHireViewModel.cs:18-24` |
| `ICandidateAccepted` v3 بلا شروط مالية | `MessageContracts/Events/ICandidateAccepted.cs` |
| عقد `DEFAULT` فقط، وتجاهل صامت إن غاب | `HR.Contracts.Application/.../InboundEventCommands.cs:52-66` |
| النسب في `Notes` | `InboundEventCommands.cs:97-121` |
| `IContractActivated.BaseSalary/PayGradeCode` تُنشر `null` | `HR.Contracts.Infrastructure/Messaging/MassTransitEventPublisher.cs:43-45` |
| لا بند لقرار التعيين أو الفحص الطبي | `HireChecklistDefaults.cs` |

---

## In scope

### 0. تحقق مسبق (نصف يوم)

اختبار تكامل `Hcm029InternalMobilityProbeTests`: تعيين داخلي (`SourceInternalApplicationId`) ثم فحص ما تنشئه العقود. إن أُنشئت معاملة `Appointment` لموظف قائم يُفعَّل البند 3.4.

### 1. منع التكرار ومعالجة الأخطاء (G-12)

- **`HirePersonMatchService`** (HR.Core.Infrastructure): يبحث بـ `NationalIdHash` أولاً، ثم (اختيارياً عبر MDM-002) بالاسم المطبّع + تاريخ الميلاد.
  - **GET `Hire`:** إن وُجد تطابق يُعرض تبويب «ربط موظف موجود» افتراضياً مع لافتة «يوجد موظف بنفس الرقم الوطني: …».
  - **POST CreateNew مع تطابق:** خطأ تحقق «الرقم الوطني مسجل لموظف قائم — استخدم الربط».
- **الرقم الوطني:** إن كان `application.NationalId` موجوداً يجب أن يطابق المُدخل، وإلا يُطلب `NationalIdMismatchReason` ويُسجل في التدقيق.
- **الأخطاء:** `RecruitmentHireClosureService` يحوّل انتهاك الفهرس الفريد (`PostgresException 23505`) و`DbUpdateConcurrencyException` إلى `HireClosureException` بنوع ورسالة عربية، والمتحكم يلتقطها.
- **مواءمة الفئة مع النطاق:**
  - قائمة الفئات تُبنى من `EmploymentType` (دائم/إداري/متعاقد مؤقت).
  - تُشتق افتراضياً من نظام الخدمة (`LegalRegimeId` عبر SYR-003)، وتغييرها يتطلب سبباً.
  - `EmploymentRecord.EmploymentType` و`IEmployeeCreated.EmploymentType` يستخدمان القيمة المختارة.
  - إن لزم «تدريب عملي» يُضاف كقيمة `EmploymentType` جديدة بقرار، لا كنص حر.

### 2. شروط العرض إلى العقد والرواتب (G-13)

**عقد الرسالة — `ICandidateAccepted` v4** (إضافي، `SchemaVersion => 4`، مع أعضاء افتراضية للتوافق مع v3):

| الحقل الجديد | المصدر |
|--------------|--------|
| `OfferedSalary` (decimal?) | `OfferLetter.OfferedSalary` |
| `GradeCode` (string?) | `OfferLetter.GradeCode` |
| `LegalRegimeId` / `PayrollRegimeCode` | `EmploymentRecord.LegalRegimeId` / `ServiceRegimeToPayrollMapper` |
| `EmploymentType` | البند 1 |
| `PositionCode`، `OrgUnitId` | `FundedPosition` |
| `IsInternalMobility` (bool) | `SourceInternalApplicationId.HasValue` |

يُحدَّث `MESSAGE_EVENT_MATRIX.md` و`docs/rel-compat` (خط أساس العقود).

**HR.Contracts:**

- **Migration `FUN029_ContractOfferTerms`:** `GradeCode`، `AgreedBaseSalary`، `LegalRegimeId`، `SourceOfferLetterId` على `Contract`؛ و`RecruitmentApplicationId`، `RequisitionId`، `OfferLetterId` على `ServiceTransaction`، مع backfill من `Notes` عبر `ParseRecruitmentApplicationId` ثم تثبيته.
- **`ContractTypeResolver`:** يربط `(LegalRegimeId, EmploymentType)` بـ `ContractType.Code` عبر جدول إعداد `ContractTypeRegimeMap` (تُبذر الأنظمة المعروفة من `docs/syr/service-regimes.json`)، و`DEFAULT` احتياطي فقط.
- **`ProcessCandidateAcceptedHandler`:**
  - يمرر الشروط إلى `PendingContractProvisioning.CreatePending`.
  - إن لم يُحل نوع عقد: **لا تجاهل صامت**. تُنشأ المعاملة، ويوسم بند `ContractClassified` بـ «يتطلب تدخلاً»، ويُنشر `IProcessInterventionRequested` (موجود في الـ saga) مع مقياس.
- **دالة نشر `IContractActivated` في `MassTransitEventPublisher` (`:43-45`):** تملأ `BaseSalary = contract.AgreedBaseSalary` و`PayGradeCode = contract.GradeCode` بدل `null`.

**Payroll:** المستهلك الحالي لـ `IContractActivated` يستخدم `PayGradeCode/BaseSalary` لإنشاء ملف الأجر، ويتحقق من السلّم الساري؛ وعند المخالفة ينشئ ملفاً موقوفاً مع تنبيه، لا رفضاً صامتاً. **يحتاج تحقق** من المستهلك الحالي قبل التعديل.

### 3. وثائق ونقاط قرار (G-22 + جزء G-18)

**3.1 خطاب العرض PDF:**

- `OfferLetterDocument` (QuestPdf، خط ITF Qomra الموجود): بيانات الجهة، المسمى، الدرجة، الراتب، تاريخ المباشرة، الصلاحية، المرجع القانوني، رقم العرض، رمز QR للتحقق.
- يُولّد عند الانتقال إلى `Issued` ويُخزّن عبر File.Storage (`OfferLetter.DocumentFileId`، Migration `HCM029_OfferDocument`) باستخدام `IFileReferenceOutbox`.
- تنزيل من `OfferLetters/Details`، ومن بوابة المرشح في PLAN-30.
- توقيع إلكتروني اختياري عبر وحدة التوقيعات/EXT-004 عند تفعيلها.

**3.2 قرار التعيين (D-6):**

- خطوة جديدة `HireStep.AppointmentDecisionIssued` في `HireChecklistDefaults` (مالكها `hr-contracts`، SLA 48 ساعة، تتطلب دليلاً).
- تُكمَّل عند إنشاء `AdministrativeDecision` من نوع تعيين مرتبط بالمعاملة: زر «إصدار قرار التعيين» في `HireWizard/Step2` يملأ القرار من بيانات العرض والطلب، ويسجله في الديوان.
- علم `HireChecklist:RequireAppointmentDecision` (افتراضي `true`).

**3.3 الفحص الطبي قبل التعيين (اختياري):**

- `HireStep.PreEmploymentMedical` يُفعَّل بـ `HireChecklist:RequireMedicalFitness` لكل نظام خدمة.
- يُكمَّل يدوياً بدليل، أو آلياً عند استهلاك حدث لياقة من HR.Health إن توفر (**يحتاج تحقق** من وجود الحدث).

**3.4 التنقل الداخلي** (إن أكد البند 0 المشكلة):

- `ProcessCandidateAcceptedHandler` يتجاهل الرسائل ذات `IsInternalMobility=true`.
- مسار العقود للتنقل يُبنى على `IInternalPlacementCompleted` (معاملة نقل/ترقية لا تعيين).

**3.5 عدم المباشرة (اختياري، مرتبط بـ PLAN-27):**

- إن لم يُسجل `CommencementRecord` خلال `Recruitment:CommencementDeadlineDays` (افتراضي 30): تدخل في الـ saga + خيار «إلغاء التعيين لعدم المباشرة».
- الإلغاء يعيد المنصب `Vacant` ويعيد فتح الطلب، ويقترح التالي من الاحتياط، مع تعويض عبر الـ saga (`Compensate=true`).

---

## Out of scope

- محرك رواتب جديد أو تعديل سلالم الرواتب
- التوقيع الرقمي الوطني الكامل (EXT-004)

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-29a | تعيين مرشح جديد ← موظف + عقد بنوع مشتق من النظام + درجة وراتب العرض | — |
| AC-HCM-29b | — | «إنشاء جديد» برقم وطني لموظف قائم ← رسالة تحقق لا 500 |
| AC-HCM-29c | — | رقم وطني مُدخل ≠ رقم الطلب بلا سبب ← رفض |
| AC-HCM-29d | تفعيل العقد ← `IContractActivated` يحمل `BaseSalary/PayGradeCode` ← ملف أجر بالدرجة | راتب خارج السلّم ← ملف موقوف + تنبيه |
| AC-HCM-29e | — | نظام بلا نوع عقد مطابق ← تدخل مرئي في المعالج لا تجاهل |
| AC-HCM-29f | `Issued` ← PDF مخزّن وقابل للتنزيل | — |
| AC-HCM-29g | إصدار قرار التعيين ← بند `AppointmentDecisionIssued` ✓ | إكمال المعاملة دون القرار (والعلم مفعّل) ← رفض |
| AC-HCM-29h | `ServiceTransaction.RecruitmentApplicationId` مملوء، وbackfill للسجلات القديمة | — |
| AC-HCM-29i | (إن فُعّل 3.4) تعيين داخلي ← لا معاملة `Appointment` | — |

## Verification

```powershell
powershell -File scripts/hcm029-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm029|HireWorkflowRules|Fun001"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm029|HireCase|Fun001HireSaga|RecruitmentHire"
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcHcm29|Ac15CorrelationIdHireJourney"
```

## Definition of done

- لا 500 في نموذج التعيين لأي حالة بيانات متوقعة.
- العقد وملف الأجر يحملان شروط العرض، و`IContractActivated` لا ينشر `null` لهما عند توفرهما.
- النسب بحقول صريحة، و`ParseRecruitmentApplicationId` يُستخدم للـ backfill فقط.
- خطاب العرض PDF وقرار التعيين ضمن المسار.
- AC-HCM-29a..i = Pass، وانحدار FUN-001 (saga + HireWizard + E2E) أخضر.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-29-hcm-hire-contract-documents.md` |
| HR.Core | `HirePersonMatchService.cs`، `HireClosureException.cs`، `OfferLetterDocument.cs`، Migration `HCM029_OfferDocument` |
| Contracts | `ContractTypeResolver.cs`، `ContractTypeRegimeMap` + seed، Migration `FUN029_ContractOfferTerms`، تحديث `ProcessCandidateAcceptedHandler` و`MassTransitEventPublisher`، `HireStep` (+2) |
| Messages | `ICandidateAccepted` v4 |
| Tests | `Hcm029PersonMatchTests`، `Hcm029OfferTermsPropagationIntegrationTests`، `Hcm029InternalMobilityProbeTests`، `AcHcm29HireContractTests` |
| Docs | `MESSAGE_EVENT_MATRIX.md`، `HIRE_ONBOARDING_GUIDE.md` (القرار والفحص)، `FUN_001_JOURNEY_MATRIX.md` |

## Effort estimate

7–9 أيام عمل: منع التكرار والفئات (1.5)، v4 + العقود + backfill (2.5)، الرواتب (1)، PDF (1)، قرار التعيين/الفحص (1)، القبول (1). يضاف 1–2 يوم إن أكد البند 0 مشكلة التنقل الداخلي.
