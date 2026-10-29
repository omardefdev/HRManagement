# PLAN-34 — خط أساس قابل للدفع: CI محلي مطابق، لا قنابل زمنية، صفر اختبارات حمراء (QA-A)

**Scope:** QA — [دراسة الفجوات](../PRG_001_QUALITY_GAP_STUDY.md): G-QA-02, G-QA-03, G-QA-04, G-QA-05, G-QA-12؛ والحد الأدنى من G-IAM-04 لإخضرار `BolaArchitectureTests` (النطاق الكامل في PLAN-52)  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** —  
**Related cards:** QA-001، QA-002، QA-003، STR-005، STR-002، SAN-001، SUP-001، IAM-002، TEN-002، SYR-001؛ PLAN-33 (`AcceptanceGate`)، PLAN-25 §7 (قائمة «الإخفاقات المعروفة»)

## Goal

أن يصبح `main` قابلاً للدفع بلا مفاجآت، قبل أن يدفع المالك أي شيء في PLAN-35:

- **CI محلي مطابق:** خطوات وظيفة `build-and-test` لها مصدر واحد. تُشغَّل بالترتيب نفسه في GitHub وفي حاوية Linux على جهاز المالك، وتنتهي بـ 0 فشل.
- **لا قنبلة زمنية:** كل التزام مؤرَّخ في المستودع مسجَّل، ويُنذر عنه CI قبل 30 يوماً ويُفشل قبل 7. والحراس التي تقرأ التاريخ تُختبر بساعة ثابتة. ويُغلق استثناءا محاكاة STR-005 بقرار حوكمة، لا بتمديد التاريخ.
- **صفر اختبار أحمر حتمي** على Linux وعلى جهاز المالك (Windows، ثقافة ar-SA)، بلا قائمة «إخفاقات معروفة».
- **لا ثغرة High في الحزم**، و`-warnaserror` مفعّل في البناء.
- **المكدّس الأساسي يقلع** دون صورة MinIO التي لم تعد قابلة للسحب.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-QA-3 استثناءا المحاكاة في مصفوفة STR-005 (`EX-STR005-SIM-INCIDENT-WINDOW` منتهٍ 2026-09-15، و`EX-STR005-SIM-LAW-INTERIM` ينتهي 2026-10-30) | **إغلاق** الاثنين: `status: "Closed"`، `closedOn: "2026-10-30"`، `closedBy: "architecture"`، `closureNote: "انتهت محاكاة STR-005؛ السيناريو باقٍ في testdata/str005/scenarios"`. **لا تمديد للتاريخ.** والسجل في `decision-log.json` بقرار مرقّم | §2؛ يعيد إقلاع البوابة وHR.Core والرواتب في Staging/Production |
| D-QA-10 أفق الإنذار للالتزامات المؤرخة | إنذار (`::warning::`) عند ≤ 30 يوماً، وفشل عند ≤ 7 أيام أو عند الانتهاء. المفتاحان `-WarnDays`/`-FailDays` في السكربت | §2 |
| D-QA-5 مخالفات `BolaArchitectureTests` الاثنتا عشرة | **تقييد فعلي** للإجراءات الاثني عشر (لا قائمة تجميد). باقي الكشف والتصدير والإجراءات في PLAN-52 | §4 |
| D-QA-7 صورة MinIO | التطوير وCI: File.Storage بالمزوّد `Local` (كما في مكدّس القبول منذ PLAN-33)، وMinIO خلف profile `object-storage` بصورة من المتغير `MINIO_IMAGE`. Testcontainers: فئة `ObjectStorage` مستبعدة من CI حتى يحدد المالك صورة (التزام مؤرخ مالكه PLAN-38). الإنتاج (k8s/compose) ← PLAN-36/38 | §6 |
| D-QA-11 ثقافة الاختبارات | `ar-SY` (الثقافة الافتراضية للطلبات في الإنتاج، تقويم ميلادي)، تُضبط في كل مشروع اختبار | §3 |
| D-QA-12 `global.json` | **لا يُودَع أبداً.** المودَع يبقى `rollForward: disable` و`10.0.400`، والتعديل المحلي للمالك (`latestPatch`) يبقى محلياً. يوصى بتثبيت SDK `10.0.400` بالضبط (`dotnet-install.ps1 -Version 10.0.400`) ليستغني المالك عن التعديل. ومدقّق SAN-001 يقرأ النسخة المودَعة | §3 |
| D-QA-13 بوابة التغطية إن فشلت عند أول قياس | لا خفض صامت: يُضبط حدّ التجميعة الفاشلة على القيمة المقيسة مقرّبة للأسفل، مع ملاحظة في `ADR-QA-002` والتزام مؤرخ (60 يوماً) لإعادته. لا يمس `globalThreshold` | §7 |
| D-QA-14 الاختبار المتقلّب `Hcm029CandidateAcceptedV4Tests.External_hire_publishes_v4_with_the_offer_terms` | تحقيق لنصف يوم. إن لم يُعرف السبب: `[Trait("Category","Flaky")]` + بند في `tests/quarantine.json` بتاريخ مراجعة 30 يوماً (مسجّل التزاماً مؤرخاً)، وتُشغَّل الفئة في خطوة غير حاجبة تطبع نتيجتها | §3 |

كل افتراض قابل للتغيير بإعداد أو بملف بيانات، فلا يتوقف التنفيذ على المالك.

---

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-34-qa-pushable-baseline` | — |
| 0.2 | النسخة المودَعة من `global.json` | `git show HEAD:global.json` | المحتوى مسجّل في `reports/plan34-baseline.md`، ولا يظهر `global.json` في `git diff --cached` في أي commit |
| 0.3 | البناء على Windows | `dotnet build HRManagement.slnx` ثم `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء؛ ويُسجَّل فشل `-warnaserror` عند الاستعادة بـ NU1903 كما في `reports/plan25-baseline.md:10-11` |
| 0.4 | الأساس على Windows (ar-SA) | `dotnet test src/Tests/HRManagement.UnitTests` و`dotnet test src/Tests/HRManagement.IntegrationTests --filter "Category!=Testcontainers"` | قائمة الإخفاقات الكاملة في `reports/plan34-baseline.md` |
| 0.5 | الأساس على Linux | بعد البند 1: `pwsh scripts/ci-local.ps1 -ContinueOnError -Out artifacts/ci-local/baseline` | لكل خطوة رمز خروجها، ولكل اختبار فاشل اسمه. تُقارن بجداول G-QA-03 |
| 0.6 | جرد الالتزامات المؤرخة | بحث عن المفاتيح `expiresOn|nextDue|reviewBy|renewBy|expiryDate|validUntil|validTo` في `docs/` و`testdata/` و`supply-chain-exceptions.json` و`src/**/appsettings*.json` | قائمة الملفات (المتوقع: `governance-matrix.json`، `control-catalog.json`، `legislative-watch-stamp.json`، `source-inventory.json`، `testdata/mig001/...`، `testdata/mig002/...`، `docs/syr/currency-era.json`) |

---

## In scope

### 1. CI محلي مطابق ومصدر واحد لخطوات البناء (G-QA-12، والجزء المحلي من G-QA-01)

**المشكلة:**
- خطوات `build-and-test` مكتوبة مباشرة في `.github/workflows/ci.yml:14-334` (89 خطوة)، ولم تُشغَّل كاملة قط. لم تُشغَّل في أي جلسة `dotnet format --verify-no-changes` (`:230-231`)، ولا فحص EF للسياقات الخمسة عشر (`:236-237`)، ولا بوابة التغطية (`:281-285`).
- كل خطوط الأساس في التقارير تشغيل على Windows بثقافة ar-SA، بلا Python ولا `actionlint` (`reports/plan33-baseline.md`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| قائمة الخطوات | `scripts/ci/steps.json` | قائمة مرتبة: `{ "name", "run", "kind": "guard\|build\|test\|gate", "env": {} }`. تُنقل إليها كل خطوات `build-and-test` من `ci.yml:20-323` بالأسماء نفسها |
| المشغّل | `scripts/ci/run-steps.sh` | `--all` أو `--step "<name>"`، و`--continue-on-error`. يكتب `artifacts/ci-local/summary.json`: `gitSha`، `dirtyTracked[]`، `knownLocalOverrides[]` (`global.json` إن اختلف `rollForward` وحده)، ولكل خطوة `exit` و`durationSec`، وعدّ نتائج trx لكل مشروع (`total/failed/skipped`) |
| `ci.yml` | `.github/workflows/ci.yml` | كل خطوة في `build-and-test` تصير `run: bash scripts/ci/run-steps.sh --step "<name>"`، عدا `checkout` و`setup-dotnet` ورفع الأدلة |
| صورة Linux محلية | `docker/ci-local/Dockerfile` | `FROM mcr.microsoft.com/dotnet/sdk:10.0` (مدرجة في `docker/base-image-pins.json`)، وتثبيت `python3` و`docker-ce-cli` و`actionlint` بإصدار مثبّت، ورابط `powershell`←`pwsh` كما في `ci.yml:20-24`. وتثبيت SDK `10.0.400` بالضبط (`dotnet-install.sh --version 10.0.400`) ليطابق `global.json` المودَع |
| المدخل على Windows | `scripts/ci-local.ps1` | يبني الصورة عند الحاجة، ويشغّلها مع: المستودع مركّباً، و`/var/run/docker.sock` (لـ Testcontainers)، و`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`، ومجلد NuGet مشترك. المعاملات `-Step` و`-ContinueOnError` و`-Out`. يطبع جدولاً ويخرج بغير صفر عند أي فشل |
| حارس التطابق | `src/Tests/HRManagement.UnitTests/Architecture/CiStepsParityTests.cs` | يقرأ `ci.yml` و`steps.json`: الأسماء والترتيب متطابقان، ولا `run:` مضمّن في `build-and-test` غير `run-steps.sh` |

**القواعد:**
1. `steps.json` هو المصدر الوحيد لخطوات البناء والاختبار. أي خطوة جديدة تُضاف إليه، و`CiStepsParityTests` يرفض الانحراف.
2. لا مفهوم «إخفاقات معروفة»: أي خطوة فاشلة حمراء. الاستثناء الوحيد `tests/quarantine.json`، وكل بند فيه بتاريخ مراجعة مسجّل التزاماً مؤرخاً (§2).
3. `summary.json` يُعدّ دليلاً فقط إذا كانت `dirtyTracked` فارغة. الاستثناء المسموح الوحيد هو `global.json` عندما يختلف فيه `rollForward` وحده، ويُدرج في `knownLocalOverrides`.
4. لا تُودَع ملفات `artifacts/`، ولا ملفات `testdata/*/out/*.json` التي تعيد السكربتات كتابتها. تُستعاد قبل كل commit بـ `git checkout -- testdata`.

### 2. صنف القنابل الزمنية: سجل الالتزامات المؤرخة وساعة قابلة للحقن (G-QA-02)

**المشكلة:**
- `docs/str-governance/governance-matrix.json:146-157`: استثناءان `Declared`، الأول انتهى 2026-09-15 والثاني ينتهي 2026-10-30.
- المدقّق `scripts/str005-governance-runtime-validate.ps1:33,57-66,159-163` يقارن بـ `[datetime]::UtcNow` ويخرج بـ 1، وخطوته السابعة عشرة في `ci.yml:71-72`، قبل `Restore`.
- `ProgramGovernanceStartupValidator` (`ProgramGovernanceGuard.cs:74-106`، والساعة في `:90`) يرمي استثناءً عند الإقلاع مع `EnforceOnStartup=true`:
  - `HR.Core.Web/appsettings.Production.json:58-59`؛
  - `Payroll.Web/appsettings.Production.json:59-60`؛
  - `HRManagement.Gateway/appsettings.Production.json:83-84`؛
  - وملفات Staging المقابلة.
- المصفوفة تُنسخ إلى مخرجات البناء (`HRManagement.ServiceDefaults.csproj:56`)، فلا يُجدَّد الاستثناء على خادم دون إعادة بناء الصورة.
- `LegalControlGuard` (`LegalControlGuard.cs:63`، والساعة في `:130,139`) ينتظر القنبلة التالية: `nextDue: 2027-01-31` في `docs/str-legal/legislative-watch-stamp.json`، و6 بنود `expiresOn: 2027-01-31` في `docs/str-legal/control-catalog.json`.
- `ProgramGovernanceEvaluator.cs:42` يحلّل التاريخ بـ `DateTime.TryParse` بثقافة الخيط، فيفشل على ar-SA.
- الاختبارات تقرأ الساعة الحقيقية: `Str005ProductionBindingTests.cs:121-134,136-156`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| سجل الالتزامات | `docs/qa/dated-obligations.json` | `obligations[]`: `{ id, file, selector (مثل "exceptions[*]"), idField, dateField, statusField, closedStatuses: ["Closed","Renewed"], enforcedBy: ["startup:ProgramGovernanceGuard","ci:str005-governance-runtime-validate"], owner, warnDays?, failDays? }`. و`historical[]`: ملفات تحمل تواريخ هي وقائع لا التزامات (`docs/syr/currency-era.json` → `validUntil`) مع سبب |
| السكربت | `scripts/qa/dated-obligations-check.ps1` | `-AsOfDate` (الافتراضي اليوم UTC)، `-WarnDays 30`، `-FailDays 7`، `-Registry`. يحلّل `yyyy-MM-dd` بثقافة ثابتة. يطبع جدولاً مرتباً بأقرب أجل، ويكتب `artifacts/dated-obligations.json`، ويطلق `::warning::` و`::error::`. يخرج بـ 1 عند أي التزام مفتوح منتهٍ أو ≤ `FailDays` أو تاريخ غير قابل للتحليل |
| خطوة CI | `scripts/ci/steps.json` | «Dated obligations horizon (QA-034)» **قبل** خطوات STR/MIG، فيرى المطوّر الإنذار قبل أن يسقط مدقّق التشغيل |
| سير عمل أسبوعي | `.github/workflows/dated-obligations.yml` | `schedule` (الاثنين) + `workflow_dispatch`، يشغّل السكربت حتى دون دفع. يعمل بعد الدفع في PLAN-35 |
| ساعة قابلة للحقن | `ProgramGovernanceGuard.cs`، `LegalControlGuard.cs`، `ProgramGovernanceExtensions.AddProgramGovernance`، `LegalControlExtensions.AddLegalControl` | معامل `TimeProvider` في الحارسين بدل `DateTime.UtcNow`، و`services.TryAddSingleton(TimeProvider.System)` |
| تحليل ثابت للتاريخ | `ProgramGovernanceEvaluator.cs:42`، `LegalControlCatalogEvaluator.cs`، والسكربتات `str005-*`/`str002-*` | `DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal \| DateTimeStyles.AdjustToUniversal, out ...)`، و`[datetime]::ParseExact(..., 'yyyy-MM-dd', [cultureinfo]::InvariantCulture)` |
| ساعة ثابتة للاختبار | `src/Tests/HRManagement.TestSupport/Time/FixedTimeProvider.cs` | `TimeProvider` يعيد لحظة محددة |
| قرار D-QA-3 | `docs/str-governance/governance-matrix.json`، `docs/str-governance/decision-log.json`، `docs/STR_005_EXCEPTION_PATHS.md:18-19`، `docs/STR_005_SIMULATION_PACK.md` | الإغلاق بقرار مرقّم، وتحديث الجدولين، وملاحظة «التجديد يتطلب إعادة بناء الصورة أو `ProgramGovernance:MatrixPath` يشير إلى ملف مركّب» |

**الاختبارات:**
- `DatedObligationsTests` (UnitTests):
  - **اكتشاف:** كل ملف JSON يحمل أحد المفاتيح السبعة إما مسجَّل أو في `historical` بسبب. وإضافة ملف مؤرخ جديد دون تسجيل تُفشل الاختبار.
  - **منطق:** يُشغَّل السكربت عبر `RepoScriptRunner` على سجل اختبار في `testdata/qa/dated-obligations/` مع `-AsOfDate`. النتائج المتوقعة: 31 يوماً ← 0، و30 ← إنذار، و7 ← فشل، ومنتهٍ ← فشل، و`Closed` ← متجاهل، و`2026-13-01` ← فشل.
- `Str005ProductionBindingTests` و`Str005GovernancePackTests` تستخدم `FixedTimeProvider` على `asOfDate` المصفوفة، ويُمرَّر `-AsOfDate` إلى اختبار السكربت. فلا يصير اختبار وحدة قنبلة زمنية.
- `RuntimeGovernanceDocumentsTests` (جديد، «كناري»):
  - يحمّل الوثائق من مخرجات `ServiceDefaults` (`ProgramGovernance/*`، `LegalControl/*`) بخيارات Production و`TimeProvider.System`.
  - يتأكد أن `StartAsync` لا يرمي **اليوم**.
  - يحمرّ في اليوم نفسه الذي يمتنع فيه الإقلاع في الإنتاج، والإنذار يسبقه بـ 30 يوماً.

**القواعد:**
1. لا يُخضَّر CI بتمديد تاريخ. الالتزام يتغير فقط بتغيير حالته: `Renewed` بمراجع مختلف، أو `Closed`، بقرار في `decision-log.json`.
2. مدقّقات التشغيل في CI تبقى على تاريخ اليوم، لأنها بوابة إصدار. اختبارات الوحدة لا تقرأ الساعة الحقيقية، عدا الكناري.
3. كل تحليل لتاريخ في الحراس والسكربتات بالصيغة `yyyy-MM-dd` وثقافة ثابتة.
4. الإقلاع بحاوية كاملة بإعدادات Production خارج هذه الخطة (PLAN-36). هنا يُثبت الحارسان على وثائق التشغيل الحقيقية فقط.

### 3. اختبارات الوحدة الحمراء الحتمية والبيئية (G-QA-03 وحدة، G-QA-12)

**المشكلة والتصميم:**

| الاختبار | السبب (متحقق) | الإصلاح |
|----------|---------------|---------|
| `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider` (`Syr001ProductionBindingTests.cs:9-19`) | `LegislationRegisterProvider(HRCoreDbContext db)` (`LegislationRegisterProvider.cs:8`) والحاوية بلا `HRCoreDbContext` | الاختبار يسجل `AddDbContext<HRCoreDbContext>(o => o.UseSqlite(...))` ويبني بـ `ValidateOnBuild`/`ValidateScopes` ويحلّ من نطاق |
| `Ten002ArchitecturalTests.IgnoreQueryFilters_only_in_sanctioned_bypass` (`:95`) | `.IgnoreQueryFilters()` في `BenefitPlanSeeder.cs:26` و`AcceptanceCandidateExperiencePatch.cs:42` | `.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)` (`TenantFilterBypass.cs:66`) كما في `AcceptanceAuthSeeder.cs:27` |
| `Str005ProductionBindingTests` ×3، `Str005GovernancePackTests:68` | استثناء منتهٍ، وتحليل تاريخ بثقافة ar-SA | §2 |
| `San001SanctionsTests.Validate_script_passes` (`:85-104`) | `san001-validate.ps1:60-66` يقرأ `global.json` من شجرة العمل، وفيها تعديل المالك غير المودَع. وعلى Linux يفشل الاختبار أيضاً لو حُمل الملف المعدَّل | المعامل `-GlobalJsonSource Committed\|WorkingTree` (الافتراضي `Committed`): يقرأ `git show HEAD:global.json` حين يكون المستودع git، ويطبع المصدر المستخدم، ويرجع إلى الملف إن لم يوجد git (حاوية بلا `.git`). والقيمة المودَعة هي ما يُفحص دائماً |
| السبعة `*_validate_script_passes` (`Iam002OrgScopeTests:154`، `Iam005LifecycleTests:96`، `Psirt001VulnerabilityTests:99`، `Rel006ReleaseTrainTests:71`، `Doc001DocumentsTests:97`، `ArabicPdfGenerationTests:109`، `Prv003BreachTests:92`) | `powershell -File` بلا `-ExecutionPolicy Bypass` | الانتقال إلى `RepoScriptRunner.RunScript` (`HRManagement.TestSupport/RepoScriptRunner.cs:11-36`). وحارس `ScriptInvocationArchitectureTests`: أي `FileName = "powershell"` في الاختبارات مع `-File` يجب أن يحمل `-ExecutionPolicy Bypass` أو يمر عبر `RepoScriptRunner` |
| `MissingModulesRulesTests:117` (`Certificate_Number…`) | ثقافة ar-SA (تقويم هجري) | ضبط ثقافة الاختبار (أدناه). إصلاح المنتج (`LearningCompletionRules.cs:28` بثقافة الخيط) ← TRN (الموجة 2) |
| `BolaArchitectureTests` (`:38`) | 12 إجراء `Index` بلا حارس | §4 |
| `Hcm029CandidateAcceptedV4Tests:38` (متقلب) | غير محدد | D-QA-14 |

**ضبط الثقافة:**
- `TestCulture.cs` في `UnitTests` و`IntegrationTests` و`AcceptanceTests`: `[ModuleInitializer]` يضبط `CultureInfo.DefaultThreadCurrentCulture` و`DefaultThreadCurrentUICulture` على `ar-SY`.
- `TestCultureGuardTests` يتأكد أن ثقافة الخيط `ar-SY` وتقويمها `GregorianCalendar`.

### 4. التقييد الأدنى للإجراءات الاثني عشر (لإخضرار `BolaArchitectureTests`)

**المشكلة:** إعادة تطبيق تعبير `BolaArchitectureTests.cs:21-35` تعطي 12 إجراء `Index(..., Guid? employeeId, ...)` بلا حارس:
- **HR.Contracts.Web/Controllers:** `SeparationRecordsController.cs:26`، `ProbationRecordsController.cs:24`، `CommencementRecordsController.cs:23`، `ServiceTransactionsController.cs:33`، `ContractsController.cs:28`، `HireWizardController.cs:44`.
- **Payroll.Web/Controllers:** `PaymentHoldsController.cs:27`، `EndOfServiceController.cs:39`، `PayrollExtendedControllers.cs:17,64,138`، `FraudAlertsController.cs:28`.

ومثال على الثغرة: `ContractsController.Index` (`:28-50`) يفلتر بـ `employeeId` القادم من الطلب دون أي نطاق.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| مرشح الموظفين في النطاق (العقود) | `src/Shared/HRManagement.ServiceDefaults/EmployeeOrgScopeGuard.cs` | `IQueryable<Guid> EmployeeIdsInScope(this IQueryable<EmployeeOrgUnitSnapshot> employees, OrgAccessScope scope)` بجانب `ForbidIfEmployeeOutOfScopeAsync` الموجود (`:11-35`) |
| الإجراءات الستة في العقود | المتحكمات أعلاه | `GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots)` ← `ForbidIfOrgScopeDenied` ← إن وُجد `employeeId`: `ForbidIfEmployeeOutOfScopeAsync(User, id, _db.EmployeeOrgUnitSnapshots, _db.OrgUnitSnapshots)` ← فلترة القائمة بـ `Where(x => inScope.Contains(x.EmployeeId))` حين `scope.MustFilter` |
| مرشح الرواتب | `src/Services/Payroll/Payroll.Web/Services/PayrollEmployeeScope.cs` (جديد) | الرواتب بلا `EmployeeOrgUnitSnapshots`، فالمرجع `EmployeePayProfiles.OrgUnitId` للملف النشط، وهو ما يستعمله `EmployeePayProfilesController.cs:30-36`. الأسلوبان `ForbidIfEmployeeOutOfScopeAsync(controller, scope, employeeId)` و`IQueryable<Guid> EmployeeIdsInScope(scope)` |
| الإجراءات الستة في الرواتب | المتحكمات أعلاه | النمط نفسه |

**القواعد:**
1. نطاق مرفوض ← 403.
2. `employeeId` خارج النطاق ← 403، لا قائمة فارغة.
3. القائمة بلا `employeeId` تُفلتر إلى موظفي النطاق.
4. المستخدم غير المقيّد (مركزي) لا يتغير سلوكه.
5. موظف بلا لقطة أو بلا ملف أجر نشط ← خارج النطاق للمستخدم المقيّد (فشل مغلق، كما في `ForbidIfOrgUnitOutOfScope`، `OrgScopeControllerExtensions.cs:29-44`).
6. لا تغيير في `Details` ولا التصدير ولا POST هنا (PLAN-52).

**الاختبارات:**
- `Plan034ScopedIndexTests` (IntegrationTests، SQLite): لكل إجراء من الاثني عشر، بمستخدم يحمل مطالبة `scope_org_unit_id` لفرع BR1:
  - موظف الفرع ← صفوف؛
  - موظف BR2 ← 403؛
  - بلا مرشح ← صفوف BR1 فقط؛
  - مستخدم مركزي ← كل الصفوف.
- `Plan034ScopedIndexPostgreSqlTests` (`Category=Testcontainers`): إجراء `ContractsController.Index` و`PaymentHoldsController.Index` على PostgreSQL، لإثبات ترجمة الاستعلام الفرعي `Contains` بـ Npgsql.

### 5. مضيفات اختبار التكامل (G-QA-03 تكامل)

**المشكلة:**

| المجموعة | المرجع |
|----------|--------|
| `PublishSeparationHandler` يطلب `IHrCoreActiveRegimeClient` (`SeparationRecordHandlers.cs:123-127`)، والمضيفات لا تسجله (8 اختبارات) | `ContractLifecycleIntegrationTests.cs:30-42` (:105,126,169,313)، `Fun002SeparationRecordIntegrationTests.cs:18-36` (:59,75)، `Int003ProcessSagaTests.cs:157-177` (:23)، `EndToEndReleaseWorkflowSmokeIntegrationTests.cs:134-187` (:38) |
| الحاسب يستدعي حاجز أنظمة الخدمة (`PayrollRunParallelCalculator.cs:44-57`) ويرفض بلا عميل (`PayrollRegimeGovernance.cs:19-23`)، والمضيف بلا `IHrCoreServiceRegimeClient` (`PayrollIntegrationTestSetup.cs:16-43`): 10 على SQLite و4 على PostgreSQL، حسب G-PAY-05 | `PayrollRunLifecycleIntegrationTests`، `Perf001PayrollBatchTests`، `Pay003ResumablePayrollRunTests`، `Pay021/022/023`، `Perf001PayrollBatchPostgreSqlTests` |
| `ProcessPartyAckConsumers` يطلب `IFundedPositionVersionService` (`ProcessPartyAckConsumers.cs:25-29`)، والمضيف لا يسجله (`EstablishmentHireIntegrationTests.cs:145-166`) | `:21,53` |
| `Mock.Of<IHrCoreActiveRegimeClient>()` يعيد `null` لـ `IReadOnlyList`، فيرمي `regimes.FirstOrDefault()` (`TransferRequestsController.cs:453-454`) | `SoDForgedApproverTests.cs:84`، `SoDPolicyIntegrationTests.cs:115` |
| تنسيق تاريخ بثقافة الخيط (سنة هجرية على ar-SA) | `Hcm028AppealGovernanceMigrationTests.cs:104` |

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `FakeHrCoreActiveRegimeClient` | `src/Tests/HRManagement.TestSupport/Integration/` | الافتراضي يعيد `law50` (من `docs/syr/service-regimes.json`) لكل موظف مطلوب. والوضعان `NoRegime` (قائمة فارغة) و`Unavailable` (يرمي `HttpRequestException`) لاختبار السالب |
| `FakeHrCoreServiceRegimeClient` | ↑ | الافتراضي «غير محجوب» لكل موظف، ومعرّفات محجوبة قابلة للضبط |
| `ContractsTestServices.AddContractsApplicationForTests` | `src/Tests/HRManagement.IntegrationTests/Support/` | المستودع وMediatR والبوابة والعميل المزيف في مكان واحد، تستعمله المضيفات الأربعة |
| تسجيل الرواتب | `PayrollIntegrationTestSetup.cs:16-43` | `services.TryAddScoped<IHrCoreServiceRegimeClient, FakeHrCoreServiceRegimeClient>()` |
| مضيف التعيين | `EstablishmentHireIntegrationTests.cs:145-166` | `services.AddHrEstablishmentTemporal()` (`EstablishmentDependencyInjection.cs:7-13`) |
| اختبارات SoD | `SoDForgedApproverTests.cs:84`، `SoDPolicyIntegrationTests.cs:115` | `new FakeHrCoreActiveRegimeClient()` بدل `Mock.Of` |
| `Hcm028` | `:104` | `reviewedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)` |
| تحقق البناء | كل المضيفات المشتركة | `BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true })` |
| `IntegrationHostValidationTests` | IntegrationTests | يبني كل مضيف مشترك ويفشل برسالة صريحة إن نقص اعتماد |

**القواعد:**
1. هذا البند تغيير في الاختبارات فقط، لا في شيفرة المنتج.
2. العيوب المنتجية التي ظهرت تُسجَّل ولا تُصلح هنا:
   - ← CON: النشر يتطلب HR.Core حياً، `HrCoreActiveRegimeClient.cs:32-33`.
   - ← MOV: الاعتماد يرمي عند تعطّل HR.Core، `TransferRequestsController.cs:453-454`.
3. **انتقال نطاق:** تسجيل العميل المزيف في مضيف الرواتب مأخوذ من PLAN-59، لأن CI لا يخضرّ بدونه. ويبقى في PLAN-59 اختبار مسار الحجب (العميل يعيد `Blocked`) وإعادة كتابة AC-12.

### 6. NU1903 وتحذيرات كأخطاء (G-QA-04)

**المشكلة:**
- `Testcontainers 4.13.0` (`Directory.Packages.props:77-79`) يجرّ `SSH.NET 2025.1.0` (`src/Tests/HRManagement.IntegrationTests/packages.lock.json:868-878`)، وعليه GHSA-mggc-4xg6-vcxf وGHSA-q939-rpr3-3284.
- فيفشل `scripts/check-nuget-vulnerabilities.sh:16-57` (خطوة `ci.yml:295-296`)، ويفشل `-warnaserror` عند الاستعادة.
- ولا يوجد `TreatWarningsAsErrors` في أي ملف (`Directory.Build.props:3-10`).

**التصميم والقواعد:**
1. ترقية Testcontainers إلى أحدث إصدار 4.x لا يجرّ نسخة متأثرة. وإلا: `<PackageVersion Include="SSH.NET" Version="…"/>` بأول إصدار لا يظهر في `dotnet list package --vulnerable --include-transitive`، مع `<PackageReference Include="SSH.NET" />` مباشر في `HRManagement.IntegrationTests.csproj` (بعد `:14`) وتعليق السبب.
2. `dotnet restore HRManagement.slnx --force-evaluate` وإيداع ملفات القفل المتغيرة.
3. `Directory.Build.props`: `<WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>`.
4. خطوة البناء في `steps.json` بـ `-warnaserror`.
5. لا بند لـ SSH.NET في `supply-chain-exceptions.json`. وإن لم يوجد إصدار مُصلَح: بند استثناء بتاريخ انتهاء مسجّل التزاماً مؤرخاً، بقرار من المالك.

### 7. MinIO والخطوات التي لم تُشغَّل قط (G-QA-05)

**المشكلة:**
- `docker-compose.yml:42-55`: `minio/minio:RELEASE.2025-04-22T22-12-26Z` خدمة أساسية، و`file-storage-web` يعتمد عليها (`docker-compose.services.yml:431-447`).
- الصورة لم تعد قابلة للسحب (`reports/plan30-final-report.md:296-298`).
- Testcontainers: `TestFixtures.cs:20,286-292` و`Qa003MinioFileScanTests` (`[Collection("PgMinio")]`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| compose أساسي | `docker-compose.yml` | `minio`: `profiles: ["object-storage"]` و`image: ${MINIO_IMAGE:?set MINIO_IMAGE to a vetted, digest-pinned image}` |
| File.Storage | `docker-compose.services.yml:431-447` | `FileStorage__Provider: ${FILE_STORAGE_PROVIDER:-Local}`، و`FileStorage__LocalRootPath: /var/hrm/file-storage` مع مجلد، وحذف `depends_on.minio` |
| طبقة اختيارية | `docker-compose.object-storage.yml` (جديد) | `FILE_STORAGE_PROVIDER=Minio` + `depends_on: minio`، لمن يملك صورة |
| Testcontainers | `TestFixtures.cs:20` | `RealInfra.MinioImage` من `HRM_MINIO_IMAGE`. و`Qa003MinioFileScanTests` تحمل `[Trait("Category","ObjectStorage")]`، وخطوة Testcontainers في `steps.json` تستبعد `Category=ObjectStorage` |
| الالتزام | `docs/qa/dated-obligations.json` | «استبعاد ObjectStorage من CI» بتاريخ مراجعة +45 يوماً، المالك PLAN-38 |

**الخطوات التي لم تُشغَّل قط** (تُصلح هنا لأنها شرط الدفع):
1. **`dotnet format`:** `dotnet format HRManagement.slnx whitespace` يصلح الملفات الـ35 التي بلا سطر نهائي (`.editorconfig:7`)، ثم `--verify-no-changes`.
2. **EF:** `bash scripts/check-pending-ef-model-changes.sh` للسياقات الخمسة عشر (`:17-33`).
   - إن ظهر تغيير معلّق لا يحذف عموداً ولا يغيّر نوعه (فهرس، طول): ترحيل `PLAN034_SyncPendingModel` للسياق.
   - غير ذلك: يُسجَّل ويتوقف البند (قرار).
3. **التغطية:** `bash scripts/check-coverage.sh`. وإن فشل: D-QA-13.
4. **بقية الخطوات:** `verify-sca-fixture.sh`، `bld002-docker-build-validate.ps1`، وبناء الصور.

**القواعد:**
1. لا تُخفض الحدود ولا يُضاف استبعاد إلا بالتزام مؤرخ له مالك.
2. الإنتاج (`docker-compose.production.yml:88`، `deploy/k8s/data/base/minio/statefulset.yaml:74`) خارج هذه الخطة.

---

## Out of scope

- الدفع إلى GitHub والتشغيل البعيد وحماية الفرع وCodeQL والتكرار وسير العمل المجدولة ← PLAN-35.
- إقلاع الحاويات بإعدادات Production كاملة، وصورة MinIO الإنتاجية ← PLAN-36/38.
- نطاق الفرع على `Details` والتصدير وPOST وبقية المتحكمات، وشخصيات قبول مقيّدة بالعقود والرواتب ← PLAN-52.
- مسار حجب الرواتب (العميل يعيد `Blocked`) وإعادة كتابة AC-12 ← PLAN-59.
- عيب ثقافة رقم الشهادة في المنتج ← TRN (الموجة 2).
- وظيفة القبول في CI ← PLAN-41.
- اختبارات «جذر التركيب» لكل خدمة (`WebApplicationFactory`)، ونقل مسارات الرواتب من SQLite (G-QA-14) ← QA-H (الموجة 2).

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-QA-34a | `pwsh scripts/ci-local.ps1` على نسخة نظيفة من HEAD: كل الخطوات تخرج بـ 0، و`summary.json` فيه `gitSha` = HEAD و`dirtyTracked` فارغة. يسجّل `AcQa34PushableBaselineTests` الدليل | ملخص من SHA آخر، أو فيه خطوة فاشلة ← السيناريو `Blocked`، ويفشل تحت `ACCEPTANCE_REQUIRE_ALL=true` |
| AC-QA-34b | `dated-obligations-check.ps1` على السجل الحقيقي اليوم ← خروج 0، وأقرب التزام مطبوع (STR-002، 2027-01-31) | سجل اختبار فيه التزام بعد 5 أيام ← خروج 1؛ بعد 20 يوماً ← إنذار وخروج 0؛ `Declared` منتهٍ ← خروج 1 |
| AC-QA-34c | `RuntimeGovernanceDocumentsTests`: الحارسان يبدآن بخيارات Production اليوم | `FixedTimeProvider` على 2026-09-16 مع نسخة اختبار من المصفوفة قبل الإغلاق ← الإقلاع يرمي ويذكر `expired` |
| AC-QA-34d | مستخدم BR1 يرى سجلات موظفي فرعه في الإجراءات الاثني عشر، و`BolaArchitectureTests` أخضر | `employeeId` لموظف BR2 ← 403 في كل إجراء، على SQLite وعلى PostgreSQL |
| AC-QA-34e | نشر الفصل بالعميل المزيف (`law50`) ينجح، والاختبارات الثمانية خضراء | `NoRegime` ← رفض بـ `RegimeRulesNotFoundMessage` بلا حدث؛ و`Unavailable` ← خطأ بلا حدث |
| AC-QA-34f | حساب التشغيل على SQLite وعلى PostgreSQL ← `Calculated` و`ValidatedAtCalculate`، والاختبارات الأربعة عشر خضراء | مضيف بلا تسجيل العميل ← `IntegrationHostValidationTests` أحمر برسالة تسمّي النوع الناقص |
| AC-QA-34g | `docker compose -f docker-compose.yml -f docker-compose.services.yml --profile full-stack up -d --wait` بلا MinIO ← `file-storage-web` سليم، والرفع والفحص يعطيان `Clean` | ملف EICAR ← محجور. و`--profile object-storage` بلا `MINIO_IMAGE` ← `docker compose config` يفشل برسالة صريحة |
| AC-QA-34h | التشغيل الكامل للوحدة على Windows/ar-SA = التشغيل على Linux = 0 فشل | حذف `TestCulture` ← `TestCultureGuardTests` أحمر |
| AC-QA-34i | `dotnet list package --vulnerable --include-transitive` بلا High/Critical، و`dotnet build -warnaserror` = 0 | فرع تجريبي بإصدار SSH.NET المتأثر ← الاستعادة تفشل بـ NU1903 |

## Verification

```powershell
# Windows (ar-SA)
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Category!=Testcontainers"
# Linux، مطابق لـ CI (يشمل Testcontainers دون ObjectStorage)
pwsh scripts/ci-local.ps1
# الالتزامات المؤرخة: الحقيقي، ثم السالب
pwsh scripts/qa/dated-obligations-check.ps1
pwsh scripts/qa/dated-obligations-check.ps1 -Registry testdata/qa/dated-obligations/near-expiry.json   # يتوقع خروج 1
# المكدّس الأساسي بلا MinIO
bash scripts/prepare-ci-compose-env.sh
docker compose -f docker-compose.yml -f docker-compose.services.yml --profile full-stack up -d --wait --wait-timeout 600
# الدليل
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcQa34
```

## Definition of done

- `pwsh scripts/ci-local.ps1` أخضر على HEAD بنسخة نظيفة. لا قائمة «إخفاقات معروفة»، و`tests/quarantine.json` فارغ أو كل بند فيه مسجّل التزاماً مؤرخاً.
- الاختبارات الستة عشر وفئات التكامل الأربع خضراء على Windows وعلى Linux.
- `BolaArchitectureTests` و`Ten002ArchitecturalTests` و`CiStepsParityTests` و`DatedObligationsTests` و`RuntimeGovernanceDocumentsTests` خضراء.
- لا ثغرة High، و`-warnaserror` في البناء.
- `governance-matrix.json` بلا استثناء `Declared` منتهٍ أو قريب الأجل، والتغيير بقرار في `decision-log.json`.
- AC-QA-34a..i = Pass، ومجلد `acceptance-evidence/AC-QA-34/` بالعناصر الثمانية.
- `global.json` غير موجود في أي commit من هذه الخطة.
- أسطر «الحالة» محدّثة في الدراسة لـ G-QA-02 وG-QA-03 وG-QA-04 وG-QA-05 وG-QA-12.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-34-qa-pushable-baseline.md` |
| CI | `scripts/ci/steps.json`، `scripts/ci/run-steps.sh`، `scripts/ci-local.ps1`، `docker/ci-local/Dockerfile`، `.github/workflows/ci.yml` (`build-and-test`)، `.github/workflows/dated-obligations.yml`، `docker/base-image-pins.json` |
| Dated obligations | `docs/qa/dated-obligations.json`، `scripts/qa/dated-obligations-check.ps1`، `testdata/qa/dated-obligations/*.json`، `tests/quarantine.json` |
| Governance | `governance-matrix.json`، `decision-log.json`، `ProgramGovernanceGuard.cs`، `ProgramGovernanceEvaluator.cs`، `LegalControlGuard.cs`، `LegalControlCatalogEvaluator.cs`، `str005-*.ps1`، `str002-control-runtime-validate.ps1`، `san001-validate.ps1` |
| Product (minimal) | المتحكمات الاثنا عشر، `EmployeeOrgScopeGuard.cs`، `Payroll.Web/Services/PayrollEmployeeScope.cs`، `BenefitPlanSeeder.cs`، `AcceptanceCandidateExperiencePatch.cs` |
| Compose | `docker-compose.yml`، `docker-compose.services.yml`، `docker-compose.object-storage.yml` |
| Packages | `Directory.Packages.props`، `Directory.Build.props`، `HRManagement.IntegrationTests.csproj`، ملفات `packages.lock.json` |
| Unit | `CiStepsParityTests`، `DatedObligationsTests`، `RuntimeGovernanceDocumentsTests`، `ScriptInvocationArchitectureTests`، `TestCultureGuardTests`، وتعديل `Syr001`/`Str005*`/`San001` والسبعة |
| Integration | `Plan034ScopedIndexTests`، `Plan034ScopedIndexPostgreSqlTests`، `IntegrationHostValidationTests`، `TestSupport/Integration/Fake*RegimeClient.cs`، `TestSupport/Time/FixedTimeProvider.cs`، `Support/ContractsTestServices.cs` |
| Acceptance | `AcQa34PushableBaselineTests.cs` (`[RequiredAcceptance]`) |
| Reports | `reports/plan34-baseline.md`، `reports/plan34-final-report.md` |
| Docs | `docs/CI_AND_TESTING.md` (`ci-local`، الالتزامات المؤرخة، الثقافة، `ObjectStorage`)، `docs/STR_005_EXCEPTION_PATHS.md`، `docs/STR_005_SIMULATION_PACK.md`، `docs/ADR-QA-002-risk-based-coverage.md` (إن طُبق D-QA-13)، `docs/PRG_001_QUALITY_GAP_STUDY.md` (أسطر «الحالة») |

## Risks

| الخطر | التخفيف |
|-------|---------|
| Testcontainers داخل حاوية على Docker Desktop (Windows) لا يصل إلى الحاويات الشقيقة | `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`. وإن تعذّر: `ci-local.ps1 -SkipTestcontainers` يطبع الخطوة «لم تُشغَّل» صراحة، ويبقى الدليل الكامل لـ CI البعيد (PLAN-35) |
| `dotnet format` يغيّر ملفات كثيرة | تغيير مسافات فقط، في commit مستقل بلا تغيير منطق |
| تغييرات EF معلّقة سلوكية | توقف البند وقرار؛ لا ترحيل يحذف أو يغيّر نوعاً |
| بوابة التغطية تفشل بعد أشهر بلا قياس | D-QA-13 بشفافية والتزام مؤرخ |
| تقييد الإجراءات الاثني عشر يحجب شاشة يعتمد عليها مستخدم مركزي | المستخدم غير المقيّد لا يتغير، واختبار لكل إجراء بمستخدم مركزي |
| الكناري يحمرّ مستقبلاً على commit لا علاقة له | مقصود: الإنذار يسبقه بـ 30 يوماً في CI والسير الأسبوعي، والرسالة تسمّي الالتزام والملف |

## Effort estimate

8–10 أيام عمل:
- CI المحلي ومصدر الخطوات: 1.5.
- الالتزامات المؤرخة والساعة وقرار STR-005: 1.5.
- اختبارات الوحدة والثقافة: 1.
- تقييد الإجراءات الاثني عشر مع اختباراتها: 1.5.
- مضيفات التكامل: 1.
- NU1903: 0.5.
- MinIO: 0.5.
- `format` وEF والتغطية في أول تشغيل: 1–1.5.
- الدليل والتوثيق: 0.5–1.
