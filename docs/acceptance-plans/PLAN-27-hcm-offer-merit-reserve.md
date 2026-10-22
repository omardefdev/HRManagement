# PLAN-27 — سلامة خطاب العرض ومبدأ الجدارة وقائمة الاحتياط (PLAN-HCM-E)

**Scope:** HCM-001/HCM-004 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-06, G-07، وجزء من G-20 (انتهاء العروض)  
**Status:** Implemented — AC-HCM-27a..h Pass (2026-10-22، `acceptance-evidence/AC-HCM-27/`)  
**Priority:** P1  
**Depends on:** PLAN-26 (الترتيب المبني على درجات محسوبة). PLAN-25 مؤجلة (لا خادم بعد)؛ لذلك الخطوة الثالثة لمسار الاستثناء تُسند إلى `hr-management-approver` بمعتمد مختلف بدل `compliance` إلى حين تنفيذ PLAN-25  
**Decisions:** D-3 (تجاوز الجدارة)، D-7 (صلاحية الاحتياط) — انظر الدراسة §5

## Goal

1. لا يحصل على العرض إلا مرشح **مرتَّب**، وبترتيب الجدارة، ويُوثَّق أي استثناء ويعتمَد.
2. شروط العرض (الدرجة والراتب والتواريخ) متسقة مع المنصب الممول وسلّم الرواتب.
3. رفض العرض أو إلغاؤه أو انتهاؤه يحسم حالة الطلب ويقترح المرشح التالي من قائمة الاحتياط.

## الوضع الحالي

| البند | المرجع |
|-------|--------|
| `CreateDraftAsync` لا يتحقق من الحالة ولا الرتبة | `RecruitmentOfferService.cs:27-81` |
| نموذج العرض يظهر لـ `Assessed` أيضاً | `Details.cshtml:273` |
| `Decline`/`Revoke` لا يمسّان الطلب ولا `ActiveOfferLetterId` | `RecruitmentOfferService.cs:176-198` |
| `OfferStatus.Expired` لا يُضبط أبداً | — |
| رفض كل المنافسين عند التعيين (لا احتياط) | `RecruitmentHireClosureService.RejectSiblingApplicationsAsync:331-349` |
| الدرجة والراتب نص حر | `CreateOfferForm` |

---

## In scope

### 1. أهلية إنشاء العرض (G-07)

**قواعد النطاق** (`OfferEligibilityRules` في `HR.Core.Domain/Services/`):

| القاعدة | الرسالة |
|---------|---------|
| الطلب بحالة `Ranked` حصراً | «يمكن إصدار العرض للمرشحين المرتبين فقط» |
| لا استئناف معلق على الطلب (موجود) | — |
| المرشح هو **أعلى رتبة متاحة** على الطلب. المتاح: `Ranked` بلا عرض `Declined/Expired/Revoked` سابق، ولم يُرفض | «المرشح ليس الأعلى رتبة المتاح (الرتبة المتاحة: n)» |
| استثناء الجدارة: `MeritOverrideReason` إلزامي (≥ 30 حرفاً) + `IsMeritOverride=true` | يُضاف خطوة اعتماد (انظر أدناه) |
| لا عرض نشط آخر على الطلب نفسه لعدد يتجاوز `VacancyCount` | «يوجد عرض نشط على هذا الشاغر» |

**مسار الاستثناء:** إن كان `IsMeritOverride=true` فمسار العرض يصبح `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` (3 خطوات: hr-core ← hr-management-approver ← hr-management-approver بمعتمد مختلف؛ تنتقل الخطوة الثالثة إلى compliance بعد PLAN-25). يُبذر في `WorkflowPackSeeder` + قاعدة `R-OFF-OVERRIDE`.

**الواجهة:**

- `Details.cshtml`: إزالة `Assessed` من شرط عرض النموذج.
- لوحة الترتيب تعلّم «التالي بالجدارة».
- خانة «تجاوز الجدارة» تظهر فقط عند اختيار مرشح غير الأول.

### 2. اتساق شروط العرض

| الحقل | التحقق |
|-------|--------|
| `GradeCode` | يُملأ مسبقاً من `FundedPosition.GradeCode`، ويُرفض أي اختلاف ما لم يوجد سبب واعتماد |
| `OfferedSalary` | ضمن حدود الدرجة في سلّم الرواتب الساري (`Payroll.SalaryScale/PayGrade`). يُقرأ عبر واجهة lookup جديدة في Payroll `GET /api/payroll/lookup/grades/{code}?asOf=`، ومع `IPayrollGradeLookup` + fallback ملف (نمط `FileFinanceBudgetPort`) |
| `ProposedStartDate` | ≥ اليوم، و≤ اليوم + `Recruitment:MaxStartDays` (افتراضي 90) |
| `ExpiryDate` | إلزامي؛ الافتراضي تاريخ الإصدار + `Recruitment:OfferValidityDays` (14)، ويُعاد حسابه عند الإصدار الفعلي (`Issued`) لا عند المسودة |

### 3. حسم الرفض والإلغاء والانتهاء (G-06)

**حالات جديدة في `RecruitmentApplicationStatus`** (تُضاف في النهاية فلا تتغير القيم الحالية):

| القيمة | الاسم العربي | المعنى |
|--------|--------------|--------|
| `OfferDeclined = 11` | عرض مرفوض/منتهٍ | المرشح رفض العرض أو انتهت صلاحيته |
| `ReserveListed = 12` | قائمة احتياط | مؤهل ومرتَّب ولم يحصل على الشاغر |

**تحديث `SelectionWorkflowRules`:**

- `OfferExtended → OfferAccepted | OfferDeclined | Rejected | Withdrawn`
- `Ranked → ReserveListed`
- `ReserveListed → OfferExtended | Rejected`

**الخدمة `RecruitmentOfferService`:**

| العملية | الأثر على العرض | الأثر على الطلب |
|---------|-----------------|------------------|
| `DeclineAsync(offerId, reason, declinedBy: Candidate\|HrOnBehalf)` | `Declined` + `DeclineReason` + `DeclinedAt` | `OfferDeclined`، و`ActiveOfferLetterId = null` |
| `RevokeAsync(offerId, reason, revertToRanked)` | `Revoked` + `RevokeReason` | `Ranked` (خطأ إداري) أو `Rejected` (سبب يخص المرشح) |
| `ExpireAsync(offerId)` (من المهمة) | `Expired` | `OfferDeclined` بسبب «انتهاء صلاحية العرض» |

**المهمة `OfferExpiryJob`** (BackgroundService كل ساعة، مع قفل موزع): العروض `Issued` و`ExpiryDate < now` ← `ExpireAsync`، مع تذكير قبل 48 ساعة (يُربط بالإشعارات في PLAN-30).

**Migration `HCM027_OfferOutcome`:** `DeclineReason`، `DeclinedAt`، `RevokeReason`، `IsMeritOverride`، `MeritOverrideReason` على `OfferLetter`.

### 4. قائمة الاحتياط

- `ReserveListService`:
  - `GetNextCandidateAsync(requisitionId)`: أعلى رتبة بحالة `Ranked` أو `ReserveListed` لم يُعرض عليه.
  - `PromoteAsync(applicationId)`: ينقل المرشح إلى مسار عرض جديد.
- **بعد رفض أو انتهاء:** لافتة في `RecruitmentRequisitions/Details` و`OfferLetters/Details`: «المرشح التالي بالجدارة: الاسم (رتبة n) — إنشاء عرض». خيار `Recruitment:AutoDraftNextOffer=false` لإنشاء مسودة تلقائياً.
- **عند التعيين:** تعديل `RejectSiblingApplicationsAsync` بحيث يصبح المنافسون `Ranked` ← `ReserveListed` مع `ReserveUntil = now + Recruitment:ReserveValidityMonths` (D-7)، وغير المرتبين ← `Rejected`.
- **المهمة `ReserveExpiryJob`** (يومي): `ReserveListed` و`ReserveUntil < today` ← `Rejected` بسبب «انتهاء صلاحية قائمة الاحتياط».
- **(اختياري، مرتبط بـ PLAN-29)** عدم مباشرة المعيَّن خلال المهلة يتيح «استدعاء من الاحتياط» على الطلب نفسه.

---

## Out of scope

- المسابقات متعددة الشواغر وتخصيص المرشحين على عدة مناصب ← PLAN-31
- قبول المرشح أو رفضه ذاتياً عبر البوابة ← PLAN-30 (هنا الخدمة فقط بـ `declinedBy`)

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-27a | عرض لصاحب الرتبة 1 ← مسار خطوتين ← `Issued` | عرض لمرشح `Assessed` ← رفض |
| AC-HCM-27b | عرض لصاحب الرتبة 2 مع سبب تجاوز ← مسار 3 خطوات | عرض لصاحب الرتبة 2 بلا سبب ← رفض |
| AC-HCM-27c | — | راتب خارج حدود درجة المنصب ← رفض برسالة الحدود |
| AC-HCM-27d | رفض العرض بسبب ← الطلب `OfferDeclined` ← اقتراح الرتبة 2 ← عرض جديد | — |
| AC-HCM-27e | عرض `Issued` تاريخه أمس ← المهمة ← `Expired` + الطلب `OfferDeclined` | قبول عرض منتهٍ ← رفض |
| AC-HCM-27f | تعيين الرتبة 1 ← الرتبتان 2 و3 `ReserveListed`، وغير المرتبين `Rejected` | — |
| AC-HCM-27g | انتهاء `ReserveUntil` ← `Rejected` | — |
| AC-HCM-27h | `Revoke` بخطأ إداري ← الطلب يعود `Ranked` | — |

## Verification

```powershell
powershell -File scripts/hcm027-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm027|OfferWorkflowRules|SelectionWorkflowRules"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm027|RecruitmentHireClosure"
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm27
```

## Definition of done

- مستحيل الوصول إلى `OfferAccepted` دون `Ranked`، ومستحيل تخطي الجدارة دون مسار 3 خطوات موثق.
- لا يبقى طلب `OfferExtended` بعرض ميت، وانتهاء العروض آلي.
- قائمة الاحتياط مرئية ومُدارة زمنياً.
- AC-HCM-27a..h = Pass مع أدلة؛ وانحدار `RecruitmentHireClosureIntegrationTests` أخضر.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md` |
| Domain | `OfferEligibilityRules.cs`، تحديث `SelectionWorkflowRules.cs`، `RecruitmentApplicationStatus` (+2) |
| Infra | `ReserveListService.cs`، `OfferExpiryJob.cs`، `ReserveExpiryJob.cs`، `IPayrollGradeLookup` + محوّلان (HTTP/File) |
| Payroll | `PayrollLookupController` (grades by code/asOf، سياسة `Service.*` للخدمات) |
| Migration | `HCM027_OfferOutcome`، بذرة `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` + `R-OFF-OVERRIDE` |
| Tests | `Hcm027OfferEligibilityTests`، `Hcm027OfferOutcomeTests`، `Hcm027ReserveListTests`، `AcHcm27OfferMeritTests` |
| Docs | `HCM_001_RECRUITMENT.md` (آلة الحالات المحدثة)، `HIRE_ONBOARDING_GUIDE.md` |

## Implementation notes / deviations

التنفيذ على الفرع `feature/plan-27-offer-merit-reserve` في جلستين. دليل التشغيل: [PLAN-27-EXECUTION](execution/PLAN-27-EXECUTION.md).

**الجلسة 1 (A–D):** قواعد الأهلية وترتيب الجدارة (`OfferEligibilityRules`)، مسار الاستثناء `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` مع منع تكرار المعتمد، الرفض/الإلغاء/الانتهاء والحالتان `OfferDeclined`/`ReserveListed`، مهمة `RecruitmentOfferExpiryJob`. الترحيل `HCM027_OfferOutcome`.

**الجلسة 2 (E–H):** اتساق شروط العرض مع Payroll، قائمة الاحتياط، الاختبارات، التوثيق. الترحيلان `HCM027_OfferTerms` و`HCM027_ReserveList`.

| # | الانحراف أو القرار | السبب |
|---|--------------------|-------|
| 1 | الخطوة 3 من مسار الاستثناء تُسند إلى `hr-management-approver` بمعتمد **مختلف** (شخصية جديدة `a-management-approver-2`) بدل `compliance` | PLAN-25 مؤجلة؛ علامة `// PLAN-25: move step 3 to compliance` في `WorkflowPackSeeder` |
| 2 | **تعميم مُطلِق الاستثناء:** `IsExceptionOffer = IsMeritOverride ∨ اختلاف الدرجة ∨ راتب غير متحقق`؛ حقيقة سياسة جديدة `isExceptionOffer` وقاعدة `R-OFF-EXCEPTION` (أولوية 8) بدل تعديل `R-OFF-OVERRIDE` | قواعد البذور تُضاف «إن لم توجد» فقط؛ قاعدة جديدة تصل إلى قواعد البيانات المبذورة في الجلسة 1 دون إعادة كتابة صفوف |
| 3 | واجهة Payroll: `GET api/internal/pay-grades/{gradeCode}?asOf=` بسياسة `Service.PayrollGradeLookup` (نطاق `payroll.grades.read`، العميل `svc-hr-core`) بدل `api/payroll/lookup/grades` | اتباع نمط `DsarInternalController` للنقاط الداخلية بين الخدمات (مستثناة من إثبات البوابة ومحمية بنطاق) |
| 4 | البديل عند غياب Payroll: `UnavailablePayrollGradeLookup` + «مبرر الراتب» وعلامة `SalaryUnverified` (عرض استثنائي) بدل محوّل ملف | تعليمات الجلسة 2؛ لا يوجد مصدر ملفي موثوق لسلّم الرواتب |
| 5 | المستأجر في الاستدعاء S2S عبر `X-Tenant-ID` (`ForwardTenantHandler`) ومرشحات المستأجر في Payroll — دون أي تجاوز؛ المهمة الخلفية تضبط مستأجر العرض (`TenantIdFlow`) قبل المسودة الآلية | مطابقة `DsarInternalController` |
| 6 | `ExpiryDate` يُحسب عند الإصدار من `ValidityDays` (3..60، افتراضي 14) لا من تاريخ حر في النموذج | الجلسة 1، وفق §2 من الخطة |
| 7 | `PromoteAsync` غير منفّذ كعملية مستقلة: الترقية من الاحتياط = إنشاء عرض عادي (`ReserveListed` قابل للعرض بالجدارة)؛ اقتراح المرشح التالي لافتة + خيار `AutoDraftNextOffer` | أبسط ويعيد استخدام التحققات نفسها |
| 8 | الإلغاء بخطأ إداري لا يُنشئ مسودة آلية للمرشح التالي (المرشح نفسه يعود «مُرتَّب») | المسودة الآلية فقط عندما يصبح الطلب `OfferDeclined`/`Rejected` |
| 9 | عند التعيين يبقى المنافسون «قائمة احتياط» سابقاً بتاريخهم؛ إغلاق الطلب يدوياً (`CloseAsync`) ما زال يرفض الاحتياط | الاستدعاء من الاحتياط عند عدم المباشرة ← PLAN-29 |
| 10 | اختبارات القبول تضبط الترتيب بشرط مسبق SQL موثّق (المسار الكامل للترتيب مغطّى في AC-HCM-26) | تقليل الاعتماد على بذور قابلة للتغيّر |
| 11 | سر `svc-hr-core` في `docker-compose.acceptance.yml`/`local.yml` قيمة مختبر ثابتة `dev-svc-secret-local-only` | قيمة `.env` (`SVC_HR_CORE_CLIENT_SECRET`) لا تطابق ما تضبطه مزامنة Keycloak، فكان الرمز يُرفض 401 |
| 12 | `AcceptanceMessagingHelper.RunProcessAsync` يقرأ مخرجات `docker exec psql` بترميز UTF-8 | النصوص العربية المقروءة من قاعدة البيانات كانت تُفك بصفحة ترميز الطرفية على Windows |
| 13 | تشغيل القبول تطلّب `acceptance-reset.ps1` | تغيّر بصمة بذور القبول في الجلسة 1 (الشخصية الجديدة) |

## Effort estimate

5–7 أيام عمل: قواعد الأهلية والاستثناء (1.5)، اتساق الشروط + lookup الرواتب (1.5)، الحسم والمهام (1.5)، الاحتياط (1)، القبول (1).
