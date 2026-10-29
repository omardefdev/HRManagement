# PLAN-33 — التحقق الحي لمسار التوظيف وتصحيح التوثيق (PLAN-HCM-K)

**Scope:** HCM-001 + QA-004 + REL-001 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-23, G-24  
**Status:** Implemented (CI not executed) — 2026-10-29  
**Priority:** P0 (البنود 1–2 مع PLAN-25)، ثم مستمر: يُغلق جزء منه مع كل خطة  
**Depends on:** PLAN-26..32 منفذة؛ PLAN-25 مؤجلة (G-02 مفتوحة)

## تعديلات قبل التنفيذ (2026-10-29)

نُفِّذت هذه الخطة بموجّه تنفيذ مفصّل ([`execution/PLAN-33-AGENT-PROMPT.md`](execution/PLAN-33-AGENT-PROMPT.md))؛ وحيث يختلفان يُقدَّم الموجّه. القرارات:

1. **PLAN-25 ما زالت مؤجلة.** الفجوة G-02 مفتوحة: مع `NationalScope:IsLabScale=false` ومحرك السياسات `Off` لا تبدأ مسارات اعتماد الطلب والعرض واللجنة (`RecruitmentRequisitionLifecycleService`، `RecruitmentOfferService`، `SelectionCommitteeLifecycleService` مشروطة بـ `IsLabScale`). لذلك تعمل رحلة E2E المطلوبة على **ملف القبول** (نطاق مختبري).
2. **نسخة شبيهة بالإنتاج** من الرحلة تُكتب الآن: `docker-compose.acceptance-prodlike.yml` بإعدادات الإنتاج، وفئة `Category=JourneyProdLike` **مستبعدة من CI حتى PLAN-25**. تُشغَّل مرة واحدة ويُسجَّل بصدق موضع توقفها (المتوقع: أول اعتماد بسبب G-02)، ويغذّي دليلها PLAN-25. لا يُصلَح G-02 هنا.
3. **التخطي صريح.** أي شرط مسبق ناقص يمر عبر مساعد واحد (`AcceptanceGate`) يكتب `result.json = Blocked` مع السبب ثم يستدعي `Skip.If`. ومع `ACCEPTANCE_REQUIRE_ALL=true` يصبح تخطي سيناريو مُعلَّم `[RequiredAcceptance]` **فشلاً**.
4. **تُزال كل أنماط النجاح الصامت، لا أنماط التوظيف وحدها:** `WriteResult("Skip", …); return;` في `AcHcm21` و`AcHcm22` و`AcEst24`، و`WriteResult("Pass", "<not seeded…>"); return;` في `Ac33AssetLossLiveTests` و`Ac37JourneyRebuildLiveTests`. ويمنع اختبار معماري عودتها.
5. **تُكيَّف السيناريوهات مع التصميم الحالي لا مع نصها الأصلي.** AC-HCM-22d يتبع PLAN-28: الموارد البشرية تقيّد الاستئناف ← يُسنَد مراجع `compliance` مستقل ← المراجع يقرر؛ والسالب: رفض مراجع غير مُسنَد. وسلوك «Off + غير مختبري ← تحذير» في AC-HCM-21d/22e هو سلوك G-02 الحالي، ويُثبت **باختبار تكامل** لا بإعادة تهيئة بيئة القبول المشتركة.
6. **بيئة القبول يجب أن تكون كاملة:** File.Storage وClamAV ضمنها. سجّلت PLAN-32 تخطي AC-30c وفشل `Ac27_f_g` لأن File.Storage لم تكن تعمل. يُصلَح ملف البيئة أو سكربت تشغيلها، لا الاختبارات.
7. **لا دفع، ولا يمكن تشغيل CI البعيد هنا.** تُحدَّث ملفات YAML وتُتحقَّق محلياً (تحليل، و`actionlint` إن توفر)، ويذكر التقرير «CI not executed».
8. **عيب المنتج** الذي يكشفه تشغيل حي يُصلَح مع اختبار انحدار **فقط** إن كان الإصلاح موضعياً وواضحاً. وإلا يُسجَّل متابعةً ويُعلَّم السيناريو `Fail` مع الدليل، ولا يُخفى أبداً.

## Goal

أن تصبح عبارة «مسار التوظيف مكتمل» **مثبتة بالدليل**:

- لا اختبار قبول ينجح دون أن يفحص.
- سيناريو E2E كامل من طلب الشاغر حتى معاملة التعيين **بإعدادات شبيهة بالإنتاج**.
- أدلة `acceptance-evidence/AC-HCM-*` لكل سيناريو.
- وثائق تطابق الشيفرة.

## الوضع الحالي

| البند | المرجع |
|-------|--------|
| AC-HCM-22: اختبار واحد من 5؛ يكتب `Skip` ثم `return` فينجح دون فحص | `AcHcm22RecruitmentWorkflowsUiTests.cs:27, 34` |
| AC-HCM-21: اختبار واحد من 4 | `AcHcm21RequisitionApprovalUiTests.cs` |
| لا مجلدات أدلة لـ AC-HCM-21/22 ولا AC-WF-23 | `acceptance-evidence/` (يحوي `HCM-01/steps.md` فقط) |
| `Hcm001RecruitmentTests` اختبارات انعكاس على `SharedKernel.Recruitment.*` غير المستخدمة، ومذكورة كدليل إغلاق | `completion-backlog/09:93-97` |
| E2E يبدأ من HireWizard | `HireSeparationJourneyE2ETests.cs:12` |
| تناقض في دليل التعيين (Shortlisted مقابل OfferAccepted) | `HIRE_ONBOARDING_GUIDE.md` §2 مقابل §4 أ و§5 |

---

## In scope

### 1. تصحيح إطار القبول (مع PLAN-25)

- استبدال كل نمط `recorder.WriteResult("Skip", …); return;` في `src/Tests/HRManagement.AcceptanceTests` بـ `Skip.If(condition, reason)` (Xunit.SkippableFact الموجود)، فيظهر الاختبار كـ **Skipped** لا كـ **Passed**.
- `AcceptanceEnv`: متغير `ACCEPTANCE_REQUIRE_ALL=true` في CI يحوّل أي Skip لسيناريو مُعلَّم `[RequiredAcceptance]` إلى فشل.
- **اختبار معماري:** يمنع `return` بعد `WriteResult("Skip"…)` (فحص نصي بسيط على ملفات القبول).

### 2. بذور حالات التوظيف الحتمية

`AcceptanceDataSeeder.SeedRecruitmentStatesAsync` لكل جهة قبول ينشئ بمعرّفات حتمية:

- طلب شاغر في كل حالة: Draft، PendingApproval مع مسار، Approved، Published مع تاريخ إغلاق مستقبلي.
- معايير تقييم v1 نشطة.
- 6 مرشحين: Submitted، Shortlisted، Assessed (مقيّمان)، Ranked 1/2/3.
- عرض `PendingApproval` أنشأه `central-a`، وعرض `Issued`.
- لجنة بـ 3 أعضاء + رئيس.
- طلب مرفوض عليه استئناف معلق مراجعه `a-compliance` (بـ `sub` الحقيقي من realm القبول).

تُوثَّق المعرّفات في `testdata/acceptance/recruitment-states.json` لتستخدمها الاختبارات بدل البحث في HTML.

### 3. سيناريوهات القبول

| المجموعة | السيناريوهات | الحالة المطلوبة |
|----------|--------------|------------------|
| AC-HCM-21 | a, b, c, d | إكمال 3 سيناريوهات ناقصة |
| AC-HCM-22 | a, b, c, d, e | إكمال 4 سيناريوهات ناقصة (22c/22d بعد PLAN-28) |
| AC-WF-23 | كما في PLAN-23 | أدلة |
| AC-HCM-25..32 | كما في كل خطة | تُكتب مع الخطة |

كل سيناريو يُنتج العناصر الثمانية في `acceptance-evidence/AC-HCM-xx/` حسب عقد الأدلة في `acceptance-plans/README.md`.

### 4. E2E الرحلة الكاملة

`RecruitmentToHireJourneyE2ETests` في `src/Tests/HRManagement.E2E`، عبر البوابة وبملف `docker-compose.acceptance-prodlike.yml` (من PLAN-25):

```text
central-a: إنشاء طلب شاغر + معايير → تقديم
br1-mgr → a-management-approver: اعتماد خطوتين → نشر بتاريخ إغلاق
مواطن (HTTP مجهول): /careers/apply مع مرفق
central-a: فرز → COI → مقابلة → إكمال → درجات مقيّمَين → ترتيب
(إن RequireCommitteeApproval) لجنة → اعتماد
central-a: عرض → br1-mgr → a-management-approver → Issued
مواطن: OTP → قبول العرض
central-a: تعيين (موظف جديد)
تحقق: معاملة Appointment في العقود + عقد بالدرجة/الراتب + CorrelationId متسق (Ac15)
```

- مسار سالب واحد على الأقل لكل مرحلة (مثل: عرض لغير المرتب، اعتماد خطوة بدور خاطئ، تعيين قبل القبول).
- إضافة الرحلة إلى `.github/workflows/e2e-journeys.yml` مع عدم السماح بالتخطي (zero-skip الموجود).

### 5. إزالة الأدلة الوهمية

- التأكد من عدم وجود مراجع تشغيلية لـ `src/Shared/HRManagement.SharedKernel/Recruitment/*`. الحالي: فقط `Hcm001RecruitmentTests`.
- حذف الملفات الثلاثة (`RecruitmentRequisition.cs`، `CandidateApplication.cs`، `OfferLetter.cs`)، أو وسمها `[Obsolete]` إن كان مخطط الأطروحة يعتمدها.
- إعادة كتابة `Hcm001RecruitmentTests` لاختبارات سلوك على كيانات `HR.Core.Domain` (آلات الحالات + القواعد)، وتحديث `scripts/hcm001-validate.ps1`.

### 6. تصحيح التوثيق

| الوثيقة | التصحيح |
|---------|---------|
| `HCM_001_RECRUITMENT.md` | قسم Domain Model يصف الكيانات الفعلية؛ جدول Runtime Status يُستبدل بمصفوفة (الخطة، الحالة، الدليل)؛ وتغيير «Done» إلى «Implemented — verification pending» حتى اكتمال الأدلة |
| `completion-backlog/09` (HCM-001) | تحديث الحالة وأدلة الإغلاق لتشير إلى الكيانات والاختبارات الفعلية وإلى هذه الدراسة |
| `HIRE_ONBOARDING_GUIDE.md` | إزالة تناقض Shortlisted/OfferAccepted (§4 أ، §5)؛ إضافة خطوات المعايير والمقابلة والعرض الذاتي والاحتياط؛ إزالة شرط IsLabScale |
| `HIRE_RBAC_MATRIX.md` | `compliance` (مراجع مستقل)، رئيس/عضو لجنة، مقيّم، مصحح، المرشح (OTP)؛ وتصحيح سطر ESS (`hr-employee` لا `hr-core`) |
| `MESSAGE_EVENT_MATRIX.md` | `ICandidateAccepted` v4، `IContractActivated` بالقيم الفعلية، حدث الإشعارات الداخلي |
| `DATABASE_SCHEMA_INVENTORY.*` | الجداول والأعمدة الجديدة من PLAN-26..31 |
| `THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md` | تحديث سطر التوظيف بالحالة المثبتة |

---

## Acceptance scenarios (لهذه الخطة)

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-33a | تشغيل القبول دون بيانات ← الاختبارات تظهر Skipped لا Passed | — |
| AC-HCM-33b | — | CI بـ `ACCEPTANCE_REQUIRE_ALL=true` وسيناريو مطلوب Skipped ← فشل |
| AC-HCM-33c | E2E الرحلة الكاملة أخضر بملف prod-like | — |
| AC-HCM-33d | مجلدات أدلة AC-HCM-21/22/25.. موجودة بالعناصر الثمانية | — |

## Verification

```powershell
powershell -File scripts/hcm033-validate.ps1   # يتحقق من وجود الأدلة والوثائق والتصحيحات
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcHcm|AcWf23"
dotnet test src/Tests/HRManagement.E2E --filter RecruitmentToHireJourney
```

## Definition of done

- صفر اختبارات قبول تنجح دون فحص.
- E2E الرحلة الكاملة إلزامي في CI وأخضر.
- أدلة كاملة لكل سيناريوهات PLAN-21/22/25..30 (و31/32 إن نُفذتا).
- الوثائق الست أعلاه محدّثة، و`SharedKernel.Recruitment` محذوف أو مُعلَّم.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-33-hcm-verification-docs.md` |
| Tests | إصلاح `AcHcm21*`، `AcHcm22*`، `AcWf23*`؛ `RecruitmentToHireJourneyE2ETests.cs`؛ `AcceptanceSkipPatternArchitectureTests` |
| Seeds | `AcceptanceDataSeeder.SeedRecruitmentStatesAsync`، `testdata/acceptance/recruitment-states.json` |
| CI | تحديث `e2e-journeys.yml` و`ci.yml` (`ACCEPTANCE_REQUIRE_ALL`) |
| Script | `scripts/hcm033-validate.ps1` |
| Docs | الجدول في §6 |

## Effort estimate

6–8 أيام عمل موزعة: الإطار والبذور (2)، إكمال AC-21/22 (1.5)، E2E (2)، الإزالة والتوثيق (1.5). سيناريوهات PLAN-25..32 محسوبة داخل خططها.

## ملاحظات التنفيذ والانحرافات (2026-10-29)

التقرير النهائي: [`reports/plan33-final-report.md`](../../reports/plan33-final-report.md)؛ سجل التقدم والقرارات: [`reports/plan33-progress.md`](../../reports/plan33-progress.md).

- **الإطار:** `AcceptanceGate` و`[RequiredAcceptance]` و`ACCEPTANCE_REQUIRE_ALL` واختبار المعمارية `AcceptanceSkipPatternArchitectureTests`؛ أُزيلت كل أنماط النجاح الصامت ومرّت كل تخطيات البيانات الخام (34) عبر البوابة، وصار `result.json` يحفظ نتيجة كل سيناريو وأسوأها للمجلد. كشفت المراجعة المستقلة (P8) أن صفة «مطلوب» كانت تضيع بعد `await` داخل الدوال المساعدة، فصُحّح ذلك (`AsyncLocal`) وأُثبت حيّاً.
- **البذور:** `AcceptanceRecruitmentStatesPatch` (بدل `AcceptanceDataSeeder.SeedRecruitmentStatesAsync`: البذرة الأساسية لا تُعاد على بيئة قائمة، فاتُّبع نمط الرقع) و`testdata/acceptance/recruitment-states.json`. الحالات للقراءة والرفض فقط، والسيناريوهات المغيِّرة تنشئ طلباتها على مجموعة مناصب `AC33-POOL`.
- **النتائج الحية** (بيئة نظيفة، `ACCEPTANCE_REQUIRE_ALL=true`): AC-HCM-21a–d و22a–e وAC-WF-23a–f و26..32 ناجحة عدا AC-HCM-26h **محجوب** (لا شخصية مركزية ثانية بدور `hr-core`)؛ رحلة E2E ناجحة على ملف القبول؛ النسخة الشبيهة بالإنتاج تتوقف عند أول اعتماد (G-02).
- **انحرافات:** رحلة E2E عبر HTTP من البوابة لا عبر متصفح Playwright (المتصفحات غير مثبّتة ويتطلب تثبيتها pwsh 7 وتنزيلاً)؛ لم يُعَد توليد `DATABASE_SCHEMA_INVENTORY` بالمولّد لغياب Python (أعيد تنفيذ فحوصه يدوياً)؛ لم تُشغَّل CI البعيدة ولم يُدفع شيء؛ تحقُّق YAML بنيوي فقط.
- **إصلاح البيئة:** File.Storage بمزوّد Local في بيئة القبول (صورة MinIO لم تعد متاحة)، وترميز UTF-8 دون BOM لمزامنة Keycloak في `acceptance-up.ps1` على PowerShell 5.1.
