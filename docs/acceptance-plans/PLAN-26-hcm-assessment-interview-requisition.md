# PLAN-26 — معايير التقييم ودورة المقابلة وسلامة طلب الشاغر (PLAN-HCM-D)

**Scope:** HCM-001 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-03, G-08, G-11، وجزء من G-20 (إغلاق التقديم بالتاريخ)  
**Status:** Proposed  
**Priority:** P0 (المعايير) / P1 (المقابلة وسلامة الطلب)  
**Depends on:** خط أساس محلي (فرع git + بناء + اختبارات). PLAN-25 مؤجلة لعدم توفر خادم بعد؛ بيئة التطوير (`IsLabScale=true`) كافية لتشغيل الاعتمادات محلياً  
**Related cards:** HCM-001, EST-001, GOV-001

## Goal

أن يستطيع مستخدم حقيقي — دون بذور اختبار — تنفيذ ما يلي:

1. تعريف معايير تقييم مُصدَّرة (versioned) لكل طلب شاغر.
2. تسجيل درجات منظمة لكل معيار من أكثر من مقيّم.
3. إدارة المقابلة من الجدولة حتى النتيجة.
4. ضمان ألا يُفتح طلبا شاغر على المنصب نفسه، وأن يتسق الطلب مع تغييرات الملاك ومع تاريخ إغلاق التقديم.

## الوضع الحالي

| البند | الحالة | المرجع |
|-------|--------|--------|
| إنشاء معايير التقييم | لا يوجد إلا في `AcceptanceDataSeeder.cs:637` | `SelectionWorkflowRules.cs:57-62` يشترط `CriteriaVersionId` |
| الدرجة | رقم واحد حر 0–100 (`RecordAssessment`) | `RecruitmentSelectionService.cs:37-67` |
| المقابلة | `ScheduleAsync` فقط؛ تعيد الحالة من `Assessed/Ranked` إلى `InterviewScheduled` | `RecruitmentInterviewService.cs:32-38` |
| الطلب النشط | `ActiveStatuses` بلا `Published` | `RequisitionWorkflowRules.cs:9` |
| إلغاء طلب منشور | غير مسموح (`CanCancel` :81) رغم `CanTransition` | — |
| تجميد المنصب | لا يمس الطلبات المفتوحة | `EstablishmentService.FreezePosition` |
| تاريخ الإغلاق | `null` عند النشر | `RecruitmentRequisitionService.cs:65, 86` |

---

## In scope

### 1. إدارة معايير التقييم (G-03)

**النطاق (Domain):**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `AssessmentCriteriaItem` (record) | `HR.Core.Domain/Recruitment/AssessmentCriteriaItem.cs` | `Key`، `NameAr`، `Weight` (٪)، `MaxScore`، `Description`، `Stage` (Screening/Exam/Interview/Experience) |
| `AssessmentCriteriaRules` | `HR.Core.Domain/Services/` | `Validate(items)`: بند واحد على الأقل، مفاتيح فريدة، مجموع الأوزان = 100، `MaxScore > 0` |
| حقول جديدة على `RecruitmentAssessmentCriteria` | Migration `HCM026_CriteriaGovernance` | `IsActive`، `LockedAt`، `ChangeReason`، `ApprovedBy` |

**القواعد:**

- إصدار واحد نشط لكل طلب شاغر. الإصدار الجديد يتطلب `ChangeReason`، ويُقفل (`LockedAt`) عند أول تقييم مسجل عليه، فلا يُعدّل بعد ذلك.
- `RequisitionWorkflowRules.CanPublish` يتطلب إصداراً نشطاً (`Recruitment:RequireCriteriaBeforePublish=true`) برسالة «يجب اعتماد معايير التقييم قبل النشر».
- تغيير الإصدار بعد بدء التقييم يتطلب `HrCoreApprove` ويُسجَّل في التدقيق، والتقييمات السابقة تبقى على إصدارها.

**الواجهة:**

- تبويب «معايير التقييم» في `RecruitmentRequisitions/Details`: جدول إصدارات + محرر بنود (اسم، وزن، حد أعلى، مرحلة) مع مجموع أوزان حي.
- `RecruitmentAssessmentCriteriaController` (`HrCoreWrite`): `Create`، `Activate`، `Details`.
- (P3) قوالب معايير لكل `JobCatalogEntry` تُنسخ عند إنشاء الطلب.

### 2. الدرجات المنظمة متعددة المقيّمين

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `RecruitmentAssessmentScore` | `HR.Core.Domain/Entities/` | `ApplicationId`، `CriteriaVersionId`، `ItemKey`، `AssessorUserId`، `AssessorEmployeeId`، `Score`، `Notes`، `SubmittedAt`؛ فهرس فريد (Application, Assessor, ItemKey) |
| `RecruitmentScoringService` | `HR.Core.Infrastructure/Services/Recruitment/` | `SubmitScoresAsync` (تحقق `0 ≤ Score ≤ MaxScore`، والمقيّم ليس صاحب تعارض مصالح)، `ComputeAsync` (الدرجة الموزونة = Σ(Score/Max × Weight)، ومتوسط المقيّمين) |
| `AssessmentScore` على الطلب | تبقى | تصبح **محسوبة** لا مُدخلة؛ و`RecordAssessment` القديم يُحذف من الواجهة ويبقى للترحيل فقط |
| الانتقال إلى `Assessed` | `SelectionWorkflowRules` | يتطلب: إصدار معايير + `MinAssessors` (افتراضي 1، وفي الإنتاج 2) أرسلوا كل البنود الإلزامية |

**الواجهة:** ورقة تقييم في `RecruitmentApplications/Details` (جدول البنود × المقيّمين)، تظهر للمقيّم بنوده فقط. حقل الدرجة النهائية للقراءة فقط مع تفصيل الحساب.

### 3. دورة المقابلة الكاملة (G-08)

**النطاق:** توسيع `RecruitmentInterviewSlot` (Migration `HCM026_InterviewLifecycle`):

| الحقل | النوع | الغرض |
|-------|-------|--------|
| `Mode` | enum InPerson/Remote | — |
| `CompletedAt` | DateTime? | — |
| `Outcome` | enum Attended/NoShow/Rescheduled | — |
| `CancelledReason` | string? | — |
| `RescheduledFromSlotId` | Guid? | سلسلة إعادة الجدولة |

وكيان جديد `RecruitmentInterviewPanelMember` (SlotId, EmployeeId, Role Chair/Member, CoiDeclared).

**الخدمة** `RecruitmentInterviewService`:

- `ScheduleAsync`:
  - الموعد في المستقبل.
  - لا تداخل لنفس المرشح.
  - الحالة المسموحة `Shortlisted` أو `InterviewScheduled` فقط.
  - **إزالة الرجوع من `Assessed/Ranked`** (`:32-38`).
  - لجنة مقابلة ≥ 1.
- `RescheduleAsync(slotId, newTime, reason)`، `CancelAsync(slotId, reason)`.
- `CompleteAsync(slotId, outcome, notes)`: `Attended` يفتح ورقة التقييم لمرحلة Interview؛ `NoShow` يقترح الرفض بسبب «عدم الحضور» (قرار HR) أو إعادة الجدولة مرة واحدة (`Recruitment:MaxReschedules=1`).

**القاعدة:** `InterviewScheduled → Assessed` يتطلب مقابلة `Completed` بنتيجة `Attended`، ما لم تكن المعايير بلا مرحلة Interview (مسار الاختبار الكتابي، انظر PLAN-31).

**الواجهة:** أزرار إكمال/إعادة جدولة/إلغاء في بطاقة المقابلة؛ اختيار أعضاء اللجنة بمنتقي موظفين (`/api/lookup`)؛ صفحة «مقابلاتي» لعضو اللجنة (P2).

### 4. سلامة طلب الشاغر (G-11)

| البند | التغيير |
|-------|---------|
| الطلب النشط | `RequisitionWorkflowRules.BlockingStatuses = Draft, PendingApproval, Approved, Published`، تستخدمه `HasActiveRequisitionOnPositionAsync` و`FundedPositionsController:154` |
| حارس DB | Migration: فهرس فريد جزئي `IX_RecruitmentRequisitions_Tenant_Position_Active` على `(TenantId, FundedPositionId)` بشرط `"Status" IN ('Draft','PendingApproval','Approved','Published')` (الحالة مخزنة كنص: `HRCoreDbContext.cs:845`). قبل الترحيل: `scripts/hcm026-find-duplicate-requisitions.ps1` يسرد التكرارات لحسمها يدوياً |
| إلغاء بعد الاعتماد/النشر | `CancelAsync(id, reason)` بسياسة `HrCoreApprove` إن لم يوجد مرشح `Hired`؛ يتتابع: إلغاء نشر الإعلان، إغلاق الشاغر الداخلي، `Revoke` للعروض غير المقبولة، ونقل الطلبات المفتوحة إلى `Rejected` بسبب «إلغاء الشاغر» (إشعار في PLAN-30) |
| الإغلاق | `CloseAsync` يحسم الطلبات غير النهائية (`Rejected` بسبب «إغلاق الشاغر») ما لم تكن في قائمة الاحتياط (PLAN-27) |
| تتابع الملاك | `EstablishmentRecruitmentCascadeRules` + تعديل `EstablishmentChangeApplier`: تجميد أو إلغاء منصب عليه طلب `Published/Approved` ← **يُمنع** إن وُجد عرض `Issued/Accepted`، وإلا يُلغى الطلب تلقائياً بسبب «تجميد المنصب» مع سجل تدقيق |

### 5. فترة التقديم وإغلاقها آلياً (جزء من G-20)

- حقل `ApplicationClosingDate` (DateOnly) على `RecruitmentRequisition` إلزامي عند النشر، و≥ اليوم + `Recruitment:MinPostingDays` (افتراضي 7). ينسخه `MapToPublicJobPosting`/`MapToInternalVacancy`.
- تعديل التاريخ بعد النشر: التمديد فقط، بسبب مكتوب وتدقيق؛ ويُنقل من `PublicJobPostings/Edit` إلى `RecruitmentRequisitions`.
- `RecruitmentIntakeCloseJob` (BackgroundService يومي، مع قفل موزع عبر `pg_try_advisory_lock`): الإعلانات التي تجاوزت تاريخها ← `IsPublished=false` و`IntakeClosedAt` على الطلب. **الطلب يبقى `Published`** لأن الاختيار مستمر.
- مؤشر «التقديم مغلق» في قوائم HR وفي `/careers`.

---

## Out of scope

- الاختبارات الكتابية وجلسات الامتحان ← PLAN-31
- اقتصار العرض على الأعلى رتبة ← PLAN-27
- ربط اللجنة بالدرجات ← PLAN-28

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-26a | HR ينشئ معايير v1 (3 بنود، المجموع 100) ← ينشر الطلب | النشر دون معايير نشطة ← رفض |
| AC-HCM-26b | — | معايير مجموع أوزانها 90 ← رفض الحفظ |
| AC-HCM-26c | مقيّمان يرسلان الدرجات ← درجة موزونة محسوبة ← `Assessed` | مقيّم واحد والإعداد `MinAssessors=2` ← لا انتقال |
| AC-HCM-26d | — | تعديل إصدار مُقفل ← رفض |
| AC-HCM-26e | جدولة ← إكمال `Attended` ← تقييم | جدولة لموعد مضى ← رفض؛ جدولة لطلب `Ranked` ← رفض دون تغيير الحالة |
| AC-HCM-26f | — | إنشاء طلب ثانٍ على منصب طلبه `Published` ← رفض (UI + فهرس DB) |
| AC-HCM-26g | إلغاء طلب منشور بسبب ← إلغاء نشر الإعلان + رفض الطلبات المفتوحة | إلغاء طلب له مرشح `Hired` ← رفض |
| AC-HCM-26h | تجميد منصب طلبه منشور بلا عروض ← إلغاء الطلب تلقائياً | تجميد منصب له عرض `Issued` ← منع التغيير |
| AC-HCM-26i | تاريخ إغلاق أمس ← المهمة تغلق التقديم و`/careers` يرفض | — |

## Verification

```powershell
powershell -File scripts/hcm026-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm026|SelectionWorkflowRules|RequisitionWorkflowRules"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm026|EstablishmentHire|RecruitmentRequisition"
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm26
```

## Definition of done

- لا توجد أي حاجة لبذور اختبار للوصول إلى `Ranked` بالمسار السليم.
- `RecordAssessment` الحر لم يعد متاحاً في الواجهة، والدرجة محسوبة وقابلة للتفسير.
- المقابلة تملك حالات نهائية، ولا رجوع للحالة من `Assessed/Ranked`.
- الفهرس الجزئي مطبّق دون تعارض بيانات.
- AC-HCM-26a..i = Pass مع أدلة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md` |
| Migrations | `HCM026_CriteriaGovernance`، `HCM026_AssessmentScores`، `HCM026_InterviewLifecycle`، `HCM026_RequisitionIntegrity` |
| Scripts | `scripts/hcm026-validate.ps1`، `scripts/hcm026-find-duplicate-requisitions.ps1` |
| Unit | `Hcm026AssessmentCriteriaRulesTests`، `Hcm026ScoringServiceTests`، `Hcm026InterviewLifecycleTests`، `Hcm026RequisitionIntegrityTests` |
| Integration | `Hcm026EstablishmentCascadeIntegrationTests`، `Hcm026IntakeCloseJobTests` |
| Acceptance | `AcHcm26AssessmentInterviewTests.cs` |
| Docs | `HCM_001_RECRUITMENT.md` (نموذج المعايير والدرجات)، `HIRE_ONBOARDING_GUIDE.md` (خطوات المعايير والمقابلة) |

## Effort estimate

8–10 أيام عمل: المعايير (2.5)، الدرجات (2)، المقابلة (2)، سلامة الطلب والتتابع (2)، الإغلاق الآلي (0.5)، القبول (1).
