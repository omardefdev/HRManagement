# PLAN-41 — وظيفة القبول في CI وخط أساس قبول صادق (QA-C)

**Scope:** QA — [دراسة الفجوات](../PRG_001_QUALITY_GAP_STUDY.md): G-QA-06, G-QA-08  
**Status:** Proposed — 2026-10-30  
**Priority:** P0 (وظيفة القبول) / P1 (خط الأساس)  
**Depends on:** PLAN-35 (CI بعيد أخضر، ووظيفة القبول معطّلة بالتزام مؤرخ)، PLAN-37 (الملف الشبيه بالإنتاج يعكس توصيلات الإنتاج)  
**Related cards:** PLAN-00 (`EvidenceRecorder`، عقد العناصر الثمانية)، PLAN-33 (`AcceptanceGate`، `[RequiredAcceptance]`)، PLAN-13 (AC-34/36)، QA-004، BLD-001 (مصدر الصور)

## Goal

أن يصير دليل القبول **قابلاً لإعادة الإنتاج في CI وصادقاً**:

- وظيفة `Acceptance` في `ci.yml` تشغّل المكدّس نفسه الذي تُكتب له الاختبارات:
  - مشروع `hrm-acceptance`؛
  - realm بشخصيات acceptance-v2 وكلمات مرور مولّدة على المشغّل؛
  - File.Storage بالمزوّد `Local`؛
  - على الملف المختبري **ثم الشبيه بالإنتاج**؛
  - تحت `ACCEPTANCE_REQUIRE_ALL=true`.
  - تصبح فحصاً مطلوباً.
- `acceptance-evidence/summary.md` يقول الحقيقة لكل AC:
  - الحالة، والـ SHA، وصور الخدمات، والملف، والحداثة؛
  - هل يثبت الاختبار ادعاءه؛
  - الخطة المالكة.
- لا «Pass» تعاقدي أو قديم أو بعقد ناقص يُحسب نجاحاً.
- أي سيناريو مطلوب محجوب يُسقط الوظيفة، ولا يمر صامتاً.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-QA-6 ملف التشغيل لكل مجموعة | **الشبيه بالإنتاج:** كل صنف قبول من PLAN-37 فما بعد، وAC-HCM-25، والرحلة `JourneyProdLike`. **المختبري:** AC-HCM-21/22/26..33 وAC-WF-23 وAC-EST وAC-01..37 (كما نُفذت). الملف يُسجَّل في `metadata.json` | §1، §2 |
| D-QA-19 متى تعمل الوظيفة | كل PR إلى `main` وكل دفع إليه، لأنها فحص مطلوب (D-QA-2)، مع `concurrency` يلغي التشغيل الأقدم على الفرع نفسه. ويقيس التقرير الدقائق، وللمالك تقليصها إلى الوسم `acceptance` + ليلي | §1 |
| D-QA-20 مصدر الأدلة المودَعة | `acceptance-evidence/` في المستودع يُستبدل فقط من أثر تشغيل CI على `main` (`source: ci`، `ciRunId`). التشغيل المحلي مسوّدة (`source: local`) لا تُحسب نجاحاً في الملخص المودَع | §2 |
| D-QA-21 الحداثة | الدليل `Stale` إن اختلف `gitSha` عن HEAD، أو اختلف مصدر أي صورة خدمة عن HEAD، أو مضى عليه أكثر من 30 يوماً (للملخص المودَع) | §2 |
| D-QA-4 (جزئي) نطاق CI | الأصناف المطلوبة: كل AC-HCM/WF/EST الحالية وAC-QA-34/35 وأصناف الموجة 1 المدموجة. وAC-01..37 تُشغَّل **غير مطلوبة**: نتيجتها تظهر ولا تُسقط الوظيفة، عدا المستبعدة صراحة في `ci-scope.json` بمالك. ووسمها مطلوبة ← PLAN-65 | §1 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | عقد مطلوب من PLAN-37 | ملف `docker-compose.acceptance-prodlike.yml` يغطي كل الخدمات، و`scripts/acceptance-up.ps1 -ProdLike` (PLAN-37 §3) يشغّله على مكدّس `hrm-acceptance` | موجود. و`-Profile Lab\|ProdLike` في هذه الخطة غلاف رفيع: `ProdLike` يمرّر `-ProdLike`، فلا تعريف ثانٍ للملفات. وإن غاب المفتاح تُسجّل الانحراف في `reports/plan41-progress.md` |
| 0.2 | فرع | `git switch -c feature/plan-41-qa-ci-acceptance-honest-baseline` | — |
| 0.3 | جرد الأصناف | قائمة أصناف `HRManagement.AcceptanceTests` مع: `[RequiredAcceptance]`، ونمط البوابة، والمجلد، وآخر نتيجة | `reports/plan41-baseline.md`، مقارنة بـ `reports/plan33-acceptance-inventory.md` |
| 0.4 | تشغيل محلي كما سيعمل CI | `pwsh scripts/ci/acceptance-job.ps1 -Profile Both` (البند 1) على نسخة نظيفة | النتيجة لكل صنف في `reports/plan41-baseline.md` |
| 0.5 | الملخص الحالي | `pwsh scripts/acceptance-evidence-summarize.ps1` قبل التغيير | لقطة «قبل» (27 Pass، 2 Blocked، 3 NoResult، 23 لم يُشغَّل) |

---

## In scope

### 1. وظيفة القبول في CI (G-QA-06)

**المشكلة (متحققة):**
- `ci.yml:408-498` يشغّل compose الأساسي على المنفذ 5000 بلا `docker-compose.acceptance.yml`.
- `scripts/prepare-ci-compose-env.sh:13-15` يولّد realm بالملف `Demo` (`prepare-keycloak-import.ps1:9-10,126-131`)، فلا يوجد إلا `hradmin`. و`docker/keycloak/realm-acceptance.json` بلا مستخدمين.
- `AcceptanceHarness.cs:51-68` يقرأ كلمة مرور لكل شخصية من متغير أو من `.acceptance/credentials.json` (في `.gitignore`)، وإلا `Assert.Fail`. والمتغير `ACCEPTANCE_PERSONA_PASSWORD` (`ci.yml:468`) لا يقرؤه إلا `acceptance-file-scan.ps1:8`.
- الاختبارات تبحث عن الحاويات بالوسم `com.docker.compose.project=hrm-acceptance` (`AcceptanceMessagingHelper.cs:117-129`، `AcHcm25ProductionApprovalTests.cs:411`)، والوظيفة لا تضبط المشروع.
- `ACCEPTANCE_REQUIRE_ALL` لا يُسقط إلا الموسوم (`AcceptanceGate.cs:76-79`)، ولا حارس تخطٍّ في الوظيفة.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| سكربت الوظيفة | `scripts/ci/acceptance-job.ps1` | مصدر واحد للـ CI وللمالك. `-Profile Lab\|ProdLike\|Both`، `-Scope tests/acceptance/ci-scope.json`، `-SourceSha` (الافتراضي `git rev-parse HEAD`). الخطوات بالترتيب أدناه |
| نطاق CI | `tests/acceptance/ci-scope.json` | `lab[]` و`prodLike[]`: لكل صنف `{ class, required }`. و`excluded[]`: `{ class, reason, ownerPlan, reviewBy }`، مسجّلة في `docs/qa/dated-obligations.json` بمحدد يقرأ `excluded[*]` |
| `ci.yml` | الوظيفة `Acceptance` | `actions/checkout` و`setup-dotnet`، ثم `pwsh scripts/ci/acceptance-job.ps1 -Profile Both`، ثم رفع `acceptance-evidence/**` و`TestResults/acceptance/**` و`artifacts/acceptance/summary.md`. وحذف `ACCEPTANCE_PERSONA_PASSWORD` وخطوات المنفذ 5433 القديمة (`:437-453`، `:475-477`)، فهي تُنقل إلى السكربت على المنفذ 6433 |
| مصدر الصورة | `docker/DotnetService.Dockerfile`، `docker-compose.services.yml` | `ARG SOURCE_SHA=unknown` و`LABEL org.opencontainers.image.revision=$SOURCE_SHA`، و`build.args.SOURCE_SHA: ${SOURCE_SHA:-unknown}` لكل خدمة |
| تشغيل المكدّس | `scripts/acceptance-up.ps1` | يمرر `SOURCE_SHA` (مع `-dirty` إن وُجد تغيير متتبَّع)، ويقبل `-Profile` (البند 0.1) |
| كلمات المرور | `scripts/acceptance-file-scan.ps1:8` | تقرأ كلمة مرور الشخصية من `.acceptance/credentials.json` كما يفعل `AcceptanceHarness`. ويُحذف `ACCEPTANCE_PERSONA_PASSWORD` من كل الملفات |
| أداة اختبار للبوابة | `AcceptanceGate.cs` | المتغير `ACCEPTANCE_SIMULATE_MISSING=<Class.Method>` يجعل أول `RequireData` في ذلك السيناريو كاذباً. شيفرة اختبار فقط، لإثبات السالب في CI |

**خطوات `acceptance-job.ps1`:**
1. `bash scripts/prepare-ci-compose-env.sh` (ملف `.env`)، ثم توليد `ACCEPTANCE_GATEWAY_CALLER_PROOF_SECRET` عشوائياً للتشغيل.
2. `scripts/acceptance-up.ps1 -RequireCleanBaseline`: صور مبنية من `SourceSha`، وrealm بالملف `Acceptance`، و`credentials.json` على المشغّل، ومشروع `hrm-acceptance`.
3. أدوار PostgreSQL وعزلها (AC-20) على المنفذ 6433.
4. اختبارات `lab[]` بـ `RUN_ACCEPTANCE=true` و`ACCEPTANCE_REQUIRE_ALL=true` و`ACCEPTANCE_PROFILE=lab` و`GATEWAY_BASE_URL=http://localhost:6200` و`KEYCLOAK_BASE_URL=http://localhost:6388` و`COMPOSE_PROJECT_NAME=hrm-acceptance`.
5. تبديل الخدمات إلى الملف الشبيه بالإنتاج (عقد PLAN-37)، ثم اختبارات `prodLike[]` بـ `ACCEPTANCE_PROFILE=prodlike`.
6. فحص PII (`ac026-scan-pii.ps1`)، ثم الملخص (البند 2) بـ `-Head <SourceSha>`.
7. **الحارس:**
   - أي صنف `required: true` فيه حالة غير `Passed` في trx، أو مجلد `Fail`/`Blocked` ← خروج 1.
   - الأصناف غير المطلوبة: نتائجها في ملخص الوظيفة ولا تُسقطها.
   - أي صنف في الـ trx غير موجود في `ci-scope.json` ← خروج 1، فلا ينجو صنف جديد خارج القائمة.

**إصلاح الاختبارات الثابتة الحمراء:**
- **`AcceptanceV2CatalogTests.cs:82-90`** يتوقع 60 و60 و54 و58، والكتالوج 61 (`AcceptancePersonaCatalog.cs:216-227`: 2/43/8/4 + 4 تجهيزات سالبة، و`testdata/acceptance/personas.json`: 61، منها 59 مفعّلة).
  - تُشتق الأعداد من التوزيع المعتمد في الكتالوج بدل الثوابت.
  - تُصحَّح التسمية (`…57…`).
  - حذف الثابت `-ne 61` من `prepare-keycloak-import.ps1:137`: يتحقق السكربت من `schemaVersion` وتفرّد المعرّفات فقط، ويحكم العددَ `AcceptanceV2CatalogTests` من الكتالوج. فتضيف الخطط اللاحقة شخصياتها (PLAN-43 وPLAN-46 وPLAN-48 وPLAN-51…) إلى `personas.json` و`AcceptancePersonaCatalog` معاً، دون تعديل ثابت.
- **`AcceptanceIdentityPopulationTests.cs:7-12,71-72`** (60، و56 ناجحة): يُشتق المتوقع (المفعّلة ناقص الرفض المتوقع)، وتُصحّح التسمية.

**القواعد:**
1. الوظيفة لا تعرّف مكدّساً خاصاً بها: هي `acceptance-up.ps1` نفسه الذي يشغّله المالك.
2. لا كلمة مرور ولا سر في `ci.yml`. كل شيء مولّد على المشغّل وغير مرفوع ضمن الأدلة.
3. الصنف الجديد يجب أن يُضاف إلى `ci-scope.json` (مطلوباً أو مستبعداً بمالك)، وإلا تفشل الوظيفة.
4. بعد اخضرارها مرتين على `main`: يضيف المالك `Acceptance` إلى الفحوص المطلوبة (`branch-protection-main.json`)، ويُغلق الالتزام المؤرخ الذي فتحه PLAN-35.

### 2. خط أساس قبول صادق (G-QA-08)

**المشكلة (متحققة؛ G-QA-08 مصحّحة بعد التحقق):**
- `acceptance-evidence/summary.md:7-20,65` يعدّ 27 Pass، منها:
  - AC-34 وAC-36 بسبب تعاقدي من 2026-08-24 @`9cb3705` («Script contract verified…»، «Run acceptance-pdf-arabic.ps1…»)، ولا `drill-result.json` ولا `samples/`؛
  - AC-01..05 من 2026-08-24 قبل PLAN-25..33، ولـ AC-02..05 عقد ناقص؛
  - AC-33 لا يفحص حقول لقطة الفقد (`Ac33AssetLossLiveTests.cs:31-41`)؛
  - AC-11 على كيان وهمي (G-POL-08).
- المولّد لا يعرف الحداثة: `scripts/acceptance-evidence-summarize.ps1` و`AcceptanceEvidenceSummary.cs:83,96-102` يحسبان العقد فقط. ويوجد مولّدان.
- `WriteMergedResult` (`EvidenceRecorder.cs:96-100,206-232`) يكتب فوق `status` حين لا يُضبط `ScenarioKey`، وهي حال AC-01..37 (AC-05: الإيجابي يمحو السالب).
- `metadata.json` يسجّل `gitSha` الاختبار (`EvidenceRecorder.cs:41-61`)، لا مصدر الصور التي اختُبرت، ولا الملف، ولا مصدر التشغيل.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| بيانات الدليل | `EvidenceRecorder.WriteMetadata` (`:41-61`) | إضافة `profile` (من `ACCEPTANCE_PROFILE`، أو الكشف كما في `AcHcm25ProductionApprovalTests.cs:411`)، و`imageRevisions` (`docker inspect` لحاويات `hrm-acceptance`: الوسم `org.opencontainers.image.revision`)، و`source` (`ci` إن وُجد `GITHUB_RUN_ID`، وإلا `local`)، و`ciRunId` |
| مفتاح السيناريو | `EvidenceRecorder` | إن لم يُضبط `ScenarioKey`: الافتراضي اسم أسلوب الاختبار (من مكدّس الاستدعاء كما في `AcceptanceGate.IsRequiredScenario`). فكل مجلد يحمل خريطة `scenarios` وأسوأ حالة، ولا يمحو سيناريو آخر |
| مولّد واحد | `scripts/acceptance-evidence-summarize.ps1` | المصدر الوحيد. يُحذف `AcceptanceEvidenceSummary.Rebuild`، وتستدعي `AcceptanceEvidenceSummaryTests` السكربت عبر `RepoScriptRunner` على مجلدات اختبار |
| ملكية AC | `tests/acceptance/ac-ownership.json` | لكل AC-01..37 (وAC-EST وAC-HCM وAC-WF): `{ ownerPlan, provesClaim: true\|false, reason, legacyContractOnly }`. القيم الأولية في الجدول أدناه |
| أعمدة الملخص | `summary.md` | `AC \| Status \| Scenarios (P/F/B) \| Contract \| Profile \| gitSha \| Images \| Recorded \| Source/Run \| Freshness \| Proves claim \| Owner plan`، ومجاميع منفصلة |
| مدقّق خط الأساس | `scripts/acceptance-baseline-validate.ps1` | يفشل إن: وُجد AC بلا مالك؛ أو حُسب `Pass` بـ `provesClaim: false` ضمن «مثبت»؛ أو وُجد `Pass` بعقد ناقص؛ أو كان الملخص المودَع من `source: local` (D-QA-20) |

**قواعد الحالة (بالترتيب):**
1. `NotRun`: لا مجلد، أو `legacyContractOnly: true` (AC-34، AC-36). الأدلة القديمة تبقى للتاريخ ولا تُحسب.
2. `Incomplete`: ينقص أحد العناصر الثمانية، أو لا `result.json` (AC-16، AC-30، AC-37 الحالية).
3. `Stale`: `gitSha` ≠ HEAD، أو أي `imageRevisions` ≠ HEAD، أو تجاوز 30 يوماً (D-QA-21).
4. `Fail` / `Blocked`: من `result.json`. أسوأ سيناريو يحكم المجلد.
5. `Pass`: كل السيناريوهات `Pass`، والعقد كامل، والحداثة صالحة.
6. في المجاميع يُعدّ `Pass` مع `provesClaim: false` «ناجح ولا يثبت»، لا «مثبتاً».

**الملكية الأولية (`ac-ownership.json`، تُراجع مع الخطط المعنية):**

| AC | المالك | يثبت ادعاءه؟ |
|----|--------|---------------|
| 01–05 | PLAN-65 (تقوية)، وPLAN-52 (النطاق) | جزئياً: `db-checks` ثابتة |
| 06–10، 15، 16، 19، 26–31، 37 | PLAN-65 (وAC-10 بالحد الأدنى في PLAN-47) | لا: لم يُشغَّل أو ضعيف |
| 11، 13 | PLAN-42 (G-POL-08، G-COD-05)؛ ثم AC-11 رحلةً حقيقية في PLAN-45، وAC-13 بمستأجرين وثلاثة أنواع في PLAN-48 | لا: كيان وهمي، والوضع من العميل |
| 14 | COD-G (الموجة 2)، كما في PLAN-48 | لا: غير قابل للإعادة |
| 20 | PLAN-38 (الدليل الحي) | لم يُشغَّل |
| 12 | PLAN-59 | محجوب: خلل تصميم الاختبار |
| 17، 18، 22، 34، 35، 36 | QA-G «ops-drill» (الموجة 2) | لا: لم يُجرَ؛ و34/36 تعاقدية |
| 21، 23–25 | PLAN-58/59 | لم يُشغَّل |
| 32 | PLAN-52 | نعم |
| 33 | PLAN-45 | لا: يقبل 403 |

**إعادة التشغيل على HEAD** (من وظيفة CI، لا محلياً):
- **المختبري:** AC-01..05، AC-11، AC-32، AC-33، وكل AC-HCM-21/22/26..33، وAC-WF-23، وAC-EST-10/11/12/24، وAC-QA-34.
- **الشبيه بالإنتاج:** AC-HCM-25، و`JourneyProdLike`، وأصناف PLAN-36..40 المدموجة.
- AC-12 يبقى `Blocked` بمالك PLAN-59. ولا يُصلح هنا.

---

## Out of scope

- وسم AC-01..37 بـ `[RequiredAcceptance]` وتقوية اختباراتها الضعيفة ← PLAN-65.
- إصلاح AC-12 ← PLAN-59. وإعادة كتابة AC-33 ← PLAN-45. وAC-11 ← PLAN-42.
- تمارين الفوضى والنسخ والأداء وتوقيع PDF (AC-17/18/22/34/35/36) ← QA-G (الموجة 2).
- جعل الملف الشبيه بالإنتاج يعكس توصيلات الإنتاج ← PLAN-37 (هنا يُستهلك فقط).
- رحلات E2E الحقيقية ← QA-H (الموجة 2).

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-QA-41a | وظيفة `Acceptance` على PR: الملفان أخضران، و`summary.md` في الأثر بـ `gitSha` وصور = HEAD، و`source: ci` | `ACCEPTANCE_SIMULATE_MISSING=AcHcm26AssessmentInterviewTests.Ac26_i_intake_close_job_closes_expired_posting` في تشغيل يدوي ← السيناريو `Blocked`، والوظيفة حمراء برسالة تسمّيه |
| AC-QA-41b | مجلد اختبار حديث كامل ← `Pass` | مجلد بـ `gitSha` قديم ← `Stale`؛ وبسبب تعاقدي (`legacyContractOnly`) ← `NotRun`؛ وبعقد ناقص ← `Incomplete`. ولا شيء منها يُعدّ Pass (`AcceptanceEvidenceSummaryTests`) |
| AC-QA-41c | الاختبارات الثابتة في `AcceptanceTests` خضراء (61 شخصية) | إضافة شخصية إلى `personas.json` دون الكتالوج ← اختبار التطابق أحمر |
| AC-QA-41d | AC-05: الإيجابي والسالب محفوظان في خريطة `scenarios` | فشل السالب وحده ← المجلد `Fail` |
| AC-QA-41e | `imageRevisions` لكل خدمة = HEAD في `metadata.json` | صورة مبنية بـ `SOURCE_SHA` أقدم ← المجلد `Stale` |
| AC-QA-41f | لا `ACCEPTANCE_PERSONA_PASSWORD` في المستودع، و`acceptance-file-scan.ps1` يعمل بكلمة الشخصية من `credentials.json` | صنف جديد غير مدرج في `ci-scope.json` ← الوظيفة حمراء |
| AC-QA-41g | الملخص المودَع على `main` مولَّد من أثر CI: كل AC-01..37 بحالة ومالك، و`acceptance-baseline-validate.ps1` = PASS | ملخص مودَع من `source: local`، أو AC بلا مالك ← المدقّق أحمر |

**الدليل الحي:** `AcQa41AcceptanceBaselineTests` (`[RequiredAcceptance]`، الملف الشبيه بالإنتاج):
- يقرأ `metadata.json` لمجلدات التشغيل الحالي، ويتأكد من `profile` و`imageRevisions` و`source`.
- يشغّل المدقّق، ويسجّل AC-QA-41a/e/g في `acceptance-evidence/AC-QA-41/`.

## Verification

```powershell
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcceptanceEvidenceSummary|AcceptanceV2Catalog|AcceptanceTenantConsistency|AcceptanceGate"
# محلياً كما في CI
pwsh scripts/ci/acceptance-job.ps1 -Profile Both
pwsh scripts/acceptance-evidence-summarize.ps1 -Head (git rev-parse HEAD)
pwsh scripts/acceptance-baseline-validate.ps1
# سالب البوابة
$env:ACCEPTANCE_SIMULATE_MISSING = "AcHcm26AssessmentInterviewTests.Ac26_i_intake_close_job_closes_expired_posting"
pwsh scripts/ci/acceptance-job.ps1 -Profile Lab   # يتوقع خروج 1
Remove-Item Env:ACCEPTANCE_SIMULATE_MISSING
# بعيداً (بعد دفع المالك)
gh run list --workflow ci.yml --branch main --limit 3
gh run download <id> --name acceptance-evidence
```

## Definition of done

- وظيفة `Acceptance` مفعّلة على `main`، خضراء مرتين متتاليتين، وتشغّل الملفين. ثم أضافها المالك إلى الفحوص المطلوبة، وأُغلق التزام PLAN-35.
- `ci-scope.json` يغطي كل أصناف `AcceptanceTests`. كل استبعاد له مالك وتاريخ مراجعة.
- `summary.md` المودَع مولَّد من أثر تشغيل CI على `main`:
  - كل AC-01..37 بحالة صادقة ومالك؛
  - AC-34/36 `NotRun`؛
  - لا `Pass` بعقد ناقص؛
  - `acceptance-baseline-validate.ps1` = PASS.
- مولّد ملخص واحد. و`EvidenceRecorder` يسجّل الملف ومصدر الصور ومصدر التشغيل، ولا يمحو سيناريو آخر.
- AC-QA-41a..g = Pass، ومجلد `acceptance-evidence/AC-QA-41/`.
- أسطر «الحالة» محدّثة في الدراسة لـ G-QA-06 وG-QA-08.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-41-qa-ci-acceptance-honest-baseline.md` |
| CI | `.github/workflows/ci.yml` (الوظيفة `Acceptance`)، `scripts/ci/acceptance-job.ps1`، `tests/acceptance/ci-scope.json` |
| Stack | `scripts/acceptance-up.ps1` (`SOURCE_SHA`، `-Profile` إن لزم)، `docker/DotnetService.Dockerfile`، `docker-compose.services.yml` (وسيط البناء)، `scripts/acceptance-file-scan.ps1` |
| Evidence | `EvidenceRecorder.cs`، `AcceptanceGate.cs` (`ACCEPTANCE_SIMULATE_MISSING`)، حذف `AcceptanceEvidenceSummary.Rebuild`، `scripts/acceptance-evidence-summarize.ps1`، `scripts/acceptance-baseline-validate.ps1`، `tests/acceptance/ac-ownership.json` |
| Tests | تعديل `AcceptanceV2CatalogTests` و`AcceptanceIdentityPopulationTests` و`AcceptanceEvidenceSummaryTests`؛ جديد `AcQa41AcceptanceBaselineTests` |
| Evidence data | `acceptance-evidence/` و`summary.md` من أثر CI على `main` |
| Reports | `reports/plan41-baseline.md`، `reports/plan41-final-report.md` (الروابط، الدقائق لكل تشغيل) |
| Docs | `docs/acceptance-plans/execution/VERIFICATION-COMPLETION.md:54` (لا `acceptance-live`)، `docs/acceptance-plans/README.md` (البيئة وكلمات المرور)، `docs/CI_AND_TESTING.md`، `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md` (الفحص المطلوب)، `docs/PRG_001_QUALITY_GAP_STUDY.md` (أسطر «الحالة») |

## Risks

| الخطر | التخفيف |
|-------|---------|
| بناء 15 صورة وتشغيل ملفين يستهلك 40–60 دقيقة لكل تشغيل | `concurrency` يلغي القديم، وذاكرة طبقات `buildx` (`cache-from: type=gha`)، والدقائق في التقرير (D-QA-19) |
| ذاكرة المشغّل (7 GB) لا تكفي 15 خدمة وKeycloak وClamAV | قياس في أول تشغيل. وإن لزم: إيقاف الخدمات التي لا يحتاجها النطاق عبر profile، أو مشغّل أكبر بقرار |
| تشغيلات القبول تغيّر البيانات فتتعارض الإعادة | مكدّس نظيف في كل تشغيل CI (`-RequireCleanBaseline`)، والسيناريوهات غير القابلة للإعادة مستبعدة بمالك (PLAN-65) |
| أصناف الموجة 1 تُدمج بعد هذه الخطة دون إدراجها | الحارس: صنف غير مدرج ← الوظيفة حمراء |
| الملف الشبيه بالإنتاج من PLAN-37 يكسر سيناريوهات التوظيف المختبرية | الملفان منفصلان: التوظيف المختبري على `lab[]`، والتبديل بعده |

## Effort estimate

6–8 أيام عمل:
- سكربت الوظيفة و`ci.yml` ومصدر الصور: 2.
- إصلاح الاختبارات الثابتة وكلمات المرور: 0.5.
- `EvidenceRecorder` والمولّد الموحّد والحالات والملكية: 2.
- التشغيل البعيد والفرز: 1–2.
- الدليل والوثائق: 0.5–1.
