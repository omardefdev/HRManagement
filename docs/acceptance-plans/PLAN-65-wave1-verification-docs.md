# PLAN-65 — التحقق الحي لموجة 1 وتصحيح الوثائق (V1)

**Scope:** QA وكل مناطق الموجة 1 — إغلاق فجوات التحقق والتوثيق:
- [دراسة الجودة](../PRG_001_QUALITY_GAP_STUDY.md): G-QA-09, G-QA-13
- [دراسة سير العمل](../WF_001_GAP_STUDY.md): G-WF-13
- [دراسة السياسات](../POL_001_GAP_STUDY.md): G-POL-16
- [دراسة النطاق والصلاحيات](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-17
- [دراسة الرواتب](../PAY_001_GAP_STUDY.md): G-PAY-22
- [دراسة إنهاء الخدمة](../FUN_002_SEPARATION_GAP_STUDY.md): G-SEP-19
- [دراسة التشغيل](../PRG_002_OPERATIONS_GAP_STUDY.md): G-OPS-16

**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-34..64 (الأخيرة في الموجة 1)، وخصوصاً PLAN-41 (وظيفة القبول والملخص الصادق) وPLAN-37 (الملف الشبيه بالإنتاج)  
**Related cards:** QA-002/003/004، FUN-001..006، WF-001/002، IAM-001..004، PROD-001، PAY-001/003/004، FRD-001، SYR-005/006، DOM-001، INT-001/003، DEP-001، SEC-001/004، OPS-001..005، OBS-001/002، NFR-001/002؛ قاعدة الإغلاق في `docs/completion-backlog/README.md:38-63`

## Goal

أن تُغلق الموجة 1 **بدليل حي واحد صادق**، وأن تقول الوثائق ما يقوله الدليل:

- **تشغيل واحد نظيف** على الملف الشبيه بالإنتاج، في وظيفة `Acceptance` على `main`، يضم كل سيناريو قبول كتبته الخطط 34..64، وAC-01..37 المقوّاة. ونتيجته `summary.md` بـ `gitSha` وصور = HEAD.
- **لا اختبار قبول ينجح دون أن يثبت اسمه:** `db-checks` من استعلامات لا ثوابت، ولا تأكيد تحصيلي، ولا كتابة SQL في سكربتات السيناريو، وكل سيناريو مطلوب موسوم ومُعاد التشغيل.
- **الوثائق تطابق الدليل:** كل بطاقة «Done» في نطاق الموجة 1 تحمل الدليل الحي ورقم تشغيل CI والتوقيع، وإلا تعود `Blocked` بحسب قاعدة المستودع نفسه. وحزم UAT «Template» حتى توقيعها.
- **حذف الدليل الوهمي:** النماذج غير المستعملة التي تُذكر دليلاً، والاختبارات التي تفحص نصاً أو ملفاً لا سلوكاً، لا تُحسب.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-QA-4 أي AC-01..37 مطلوب في CI | **مطلوب:** 01–13، 15، 16، 19–21، 23–33، 37. **خارج CI:** 17، 18، 22 (فوضى)، 34 (تمرين نسخ)، 35 (أداء)، 36 (توقيع بشري). هذه تبقى `NotRun` بمالك QA-G «ops-drill» (الموجة 2). وAC-14 `NotRun` بمالك COD-G (الموجة 2)، كما أسندته PLAN-48 | §1، §3 |
| D-V1-1 مفردات حالة البطاقة | اتباع `completion-backlog/README.md:40-44` حرفياً: البطاقة التي لا تملك (1) تنفيذاً حياً (2) ناتجاً مربوطاً بتشغيل CI (3) توقيعاً مسمّى تعود `Blocked` بسبب «لا دليل حي» | §4 |
| D-V1-2 حزم UAT | «ReadyForSign-off» ← «Template» حتى يُملأ عمود التوقيع ويُربط كل سطر بدليل (D-QA-8) | §4 |
| D-V1-3 الموقّع | المالك بصفته «مالك المنتج»، بالاسم والتاريخ، مع سطر «لا مراجع مستقل (SYD-017)» مرتبط بالتزام مؤرخ | §4 |
| D-V1-4 هدف AC-09/10 (أسندت PLAN-47 إعادة كتابتهما على خدمات المجال إلى هذه الخطة) | AC-09: وضع Shadow على `Core.Recruitment` (قيمة الإنتاج) عبر نقطة استدعاء المجال لا API خام. AC-10: Enforce على المجال الذي ربطته PLAN-49، بالنشر بشخصيتين كما عدّلته PLAN-47. وإن لم تربط PLAN-49 مجالاً للإنفاذ: AC-10 `Blocked` بمالك POL، ولا يُكتب فوق ذلك | §1 |
| D-V1-5 نطاق مدقّق الوثائق | **إنفاذ** على بطاقات المناطق الست وQA (القائمة في §4). **تقرير** فقط على البقية، والقائمة لا تقبل إلا الزيادة | §4 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-65-wave1-verification-docs` | — |
| 0.2 | جرد أصناف الموجة 1 | `[RequiredAcceptance]` ومعرّفات `AC-<CODE>-<NN>` في `src/Tests/HRManagement.AcceptanceTests` للخطط 34..64. يُضاف `[Trait("Wave","1")]` و`[Trait("Plan","NN")]` لكل صنف | جدول في `reports/plan65-baseline.md`: الخطة، الصنف، السيناريوهات، الملف، آخر نتيجة في CI |
| 0.3 | الملخص الحالي | آخر أثر `Acceptance` على `main` (`gh run download`)، ثم `acceptance-baseline-validate.ps1` (PLAN-41) | لقطة «قبل» لكل AC، وقائمة ما لم يُثبت |
| 0.4 | جرد البطاقات | `pwsh scripts/v1-doc-truth-validate.ps1 -ReportOnly` (البند 4) | عدد بطاقات «Done» في النطاق وخارجه، وكم منها بلا دليل (المرجع: 170 سطر «Done» في `completion-backlog/01..15`) |
| 0.5 | التحقق من التسليمات المتوقعة من الخطط | AC-11 (PLAN-42 ثم رحلةً حقيقية في PLAN-45) وAC-13 (PLAN-42 ثم PLAN-48)، وAC-10 (الحد الأدنى) PLAN-47، وAC-20 الحي PLAN-38، وAC-33 PLAN-45، وAC-24/25 PLAN-58، وAC-12 وسكربتات إلغاء الرواتب بـ SQL (AC-21..23) PLAN-59 | لكل واحد: موسوم ويمر، أو يُسجَّل `Blocked` بمالكه. لا يُكتب هنا ما تملكه خطة أخرى |

---

## In scope

### 1. تقوية اختبارات AC-01..37 المتبقية (G-QA-09، G-IAM-17، G-OPS-16، G-POL-16)

**المشكلة:**
- لا وسم `[RequiredAcceptance]` في أي ملف `Ac0*`–`Ac3*`.
- الأنماط الضعيفة في §4.5 من دراسة الجودة: `db-checks` ثابتة، وتأكيد لا يطابق الاسم، وفحص بالسكربت وحده، وسيناريوهات لا تُعاد.
- أدلة AC-02..04 طلب واحد على `lookup` عند `9cb3705` (G-IAM-17).
- AC-09/10 يرسلان `Mode` إلى API خام، وAC-10 يتجاوز أربع العيون (`Ac10EnforcePolicyTests.cs:31`).

**التصميم (لكل اختبار تغيير محدد):**

| الاختبار | التغيير |
|----------|---------|
| `Ac01`..`Ac04` (`Ac01…:62-66`، `Ac02…:46,49-54`، `Ac04…:30`) | `db-checks` من استعلام psql حقيقي (`AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync`). AC-02/03: الحالة المتوقعة واحدة (403 أو 404) كما يقررها PLAN-52، لا «أيهما». AC-02/03/04 تغطي القائمة والتفاصيل والتصدير لا `lookup` وحده (G-IAM-17) |
| `Ac05` (`:38-46`، `:52`) | استعلام بنطاق الجهة بعد `enter-tenant` قبل `exit`، وسيناريوهان بمفتاحين |
| `Ac06` (`:51-55`) | نقل فعلي BR1←BR2 عبر HR.Movements (`a-movements` منشئاً، ومعتمد مختلف)، ثم انتظار `ITransferExecuted`، ثم التحقق أن `br1-mgr` فقد `EmpBr1` و`br2-mgr` اكتسبه. موظف خاص بكل تشغيل |
| `Ac07` | معرّف حزمة فريد لكل تشغيل، وعدّ الشجرة من DB لا من الملف |
| `Ac08` | تأكيد نتيجتي المصالحة قبل/بعد في C#. وبذر الموظف اليتيم بـ SQL في `acceptance-quarantine-flow.ps1:53-75` يبقى تجهيزاً مسمّى في قائمة السماح (§2)، لأن سجلاً سابقاً للترحيل لا يُنشأ عبر API عمداً |
| `Ac09`/`Ac10` | D-V1-4: استدعاء عبر نقطة المجال على الملف الشبيه بالإنتاج، بعد أن حذفت PLAN-47 `forceSameActorForLab` (`Ac10…:31`). السجل (`ledger`) يُفحص بـ DB لا بالنص الفرعي (`Ac09…:74`). و`v2EffectiveToClosedOnPublish` يُقرأ من DB (`Ac10…:99`) |
| `Ac13`، `Ac20`، `Ac32` | وسم فقط. `Ac13` كما عدّلته PLAN-42 ثم PLAN-48، و`Ac20` دليله الحي من PLAN-38، و`Ac32` محكم |
| `Ac15` (`:38-40`) | رحلة تعيين حقيقية عبر `RecruitmentJourneyHarness` (مشروع E2E المرجعي)، ثم تتبّع معرّف الارتباط في HR.Core والعقود والرواتب وAuth.Directory عبر `acceptance-correlation-trace.ps1`، وتأكيد العدد في C# |
| `Ac16` (`:36`)، `Ac19`، `Ac28` | نقل التحقق من رمز خروج السكربت إلى C#: عدّ الإيصالات، ونتائج المصالحة، ونتيجة الفحص، كلها باستعلام |
| `Ac21`، `Ac23` | وسم، وفترات فريدة لكل تشغيل. حالة تعديل الملف تُفحص (`Ac23`). وسكربتات الإلغاء بـ SQL أعادت كتابتها PLAN-59 على `CancelRun` من PLAN-56 |
| `Ac26` | استدعاء التفاصيل الحقيقي بدل تكرار البحث |
| `Ac27` (`:34-35`) | البوابة قبل التحليل |
| `Ac29` | انتظار انتهاء الرابط (دقيقة + هامش) ثم التأكد من 403 |
| `Ac30` | «لا أثر جانبي» في C# (عدد المراجع قبل/بعد) |
| `Ac31` (`:52-53`) | التأكد أن `CreatedAt` و`ApprovedBy` المزوّرين لم يُكتبا |
| `Ac37` (`:21-22,35`) | تأكيد 200 لبحث br1، وأن الرحلة تحتوي ذلك الطلب بمعرّفه |
| `Ac11`، `Ac12`، `Ac24`، `Ac25`، `Ac33` | لا تغيير هنا. تُستهلك من PLAN-45/59/58/58/45، ويُتحقق في 0.5 أنها موسومة وخضراء |

**القواعد:**
1. كل صنف في القائمة المطلوبة (D-QA-4) يحمل `[RequiredAcceptance]`، ويُنقل في `tests/acceptance/ci-scope.json` (PLAN-41) إلى `required: true` على الملف الشبيه بالإنتاج.
2. كل سيناريو قابل للإعادة على المكدّس نفسه: بيانات فريدة لكل تشغيل، أو تنظيف عبر API. لا `UPDATE` لإعادة الحالة.
3. عيب المنتج الذي يكشفه الاختبار المقوّى لا يُخفى: السيناريو `Fail` بالدليل، وبند في التقرير بالخطة المالكة. ويُصلح هنا فقط إن كان موضعياً وواضحاً، مع اختبار انحدار (قاعدة PLAN-33 8).

### 2. حراس معمارية تمنع الدليل الوهمي (G-QA-09، G-PAY-22، G-SEP-19)

**المشكلة:**
- `AcceptanceSkipPatternArchitectureTests.cs:49-52` يمنع «Skip/return» فقط.
- `db-checks` ثابتة في Ac01..Ac05 وAc09/10.
- تأكيدات تحصيلية: `Int003ProcessSagaTests.cs:145-146` (`Consumed || Published`)، و`HireSeparationJourneyE2ETests.cs:57` (`|| response.Status == 200`).
- اختبار إلغاء يفرض حالة مستحيلة (`Fun002SeparationRecordIntegrationTests.cs:95-99`).
- سكربتات قبول تكتب SQL: `acceptance-payroll-determinism.ps1` (8)، و`acceptance-payroll-resume-chaos.ps1` (8)، و`acceptance-quarantine-flow.ps1` (3)، و`acceptance-analytics-reconcile.ps1:77-81` (حقن الانحراف)، و`acceptance-payroll-audit-fixture.ps1`. ولا حارس يمنع عودة هذا النمط بعد أن تعيد PLAN-59 كتابة سكربتَي الرواتب وتحذف PLAN-58 سكربت التدقيق.
- نموذج غير مستعمل يُذكر دليلاً: `SharedKernel/Payroll/ArrearsLedgerEntry.cs` (لا مستعمل له؛ `PAY_004_ARREARS.md:5,17` يصفه ويعلن Done).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| توسيع الحارس | `src/Tests/HRManagement.UnitTests/Architecture/AcceptanceSkipPatternArchitectureTests.cs` | قواعد جديدة: (أ) داخل `WriteDbChecks`/`WriteAfter`/`WriteAudit` لا عضو بقيمة حرفية منطقية (`= true\|false`) أو `Guid` ثابت من `AcceptanceSeedIds` وحده دون متغير استعلام؛ (ب) لا `\|\| true` ولا `\|\| response.Status == 200` ولا `Consumed…() \|\| …Published…()` في أي مشروع اختبار؛ (ج) كل صنف في القائمة المطلوبة من `ci-scope.json` يحمل `[RequiredAcceptance]` |
| حارس SQL | `src/Tests/HRManagement.UnitTests/Architecture/AcceptanceScriptSqlWriteTests.cs` | `scripts/acceptance-*.ps1` لا تحوي `INSERT\|UPDATE\|DELETE\|TRUNCATE` في أوامر psql إلا ما في `tests/acceptance/sql-write-allowlist.json`. القائمة الأولية: `acceptance-reset.ps1` (إعادة الضبط)، و`acceptance-quarantine-flow.ps1` (بذر اليتيم السابق للترحيل)، و`acceptance-analytics-reconcile.ps1` (الانحراف هو السيناريو). كل بند بسبب وسطر. وسكربتا الرواتب يجب أن يكونا نظيفين بعد PLAN-59، وإلا الحارس أحمر و`Blocked` بمالك PLAN-59 |
| حذف النموذج الوهمي | `src/Shared/HRManagement.SharedKernel/Payroll/ArrearsLedgerEntry.cs` | حذف. و`PAY_004_ARREARS.md` يشير إلى `Payroll.Domain/Entities/ArrearsLedgerEntry.cs` |
| إصلاح تحصيلي | `Int003ProcessSagaTests.cs:145-146`، `Fun002SeparationRecordIntegrationTests.cs:95-99`، `HireSeparationJourneyE2ETests.cs:57` | تأكيد النتيجة الصحيحة الواحدة، وحالة ابتدائية ممكنة. والحالة السطحية في E2E تُحذف (التحويل الكامل ← QA-H، الموجة 2) |

**القواعد:**
1. الحارس يعمل على كل ملفات القبول، لا الجديدة وحدها.
2. أي استثناء من حارس SQL يحمل سبباً، ومالكاً، وتاريخ مراجعة مسجّلاً في `docs/qa/dated-obligations.json`.

### 3. إعادة التشغيل الحي الموحّدة (كل الفجوات الثماني)

**التصميم:**
- `tests/acceptance/ci-scope.json`: `prodLike[]` = كل صنف `Wave=1`، وAC-01..37 المطلوبة، وAC-HCM-25، و`JourneyProdLike`. و`lab[]` = AC-HCM-21/22/26..33، وAC-WF-23، وAC-EST، كما في PLAN-41.
- تشغيل وظيفة `Acceptance` على PR هذه الخطة، ثم على `main` بعد الدمج. ثم تشغيل ثانٍ على المكدّس نفسه دون إعادة ضبط، لإثبات قابلية الإعادة (`scripts/ci/acceptance-job.ps1 -SkipStackUp -Repeat`).
- **الفرز:**
  - فشل اختبار ← إصلاح هنا.
  - فشل منتج ← `Fail` مسجّل بمالكه في `reports/plan65-final-report.md`، و`ac-ownership.json` يحمل الخطة التالية.
- استبدال `acceptance-evidence/` و`summary.md` من أثر `main` (D-QA-20 من PLAN-41).

**القواعد:**
1. لا يُعلَن إغلاق الموجة 1 إلا إن كانت كل الأصناف المطلوبة `Pass` في تشغيل واحد على HEAD، بلا `Stale` ولا `Incomplete`.
2. السيناريوهات غير المطلوبة بقرار (D-QA-4) تظهر `NotRun` بمالكها، لا تختفي.

### 4. صدق الوثائق (G-QA-13، والأجزاء التوثيقية من G-WF-13 وG-POL-16 وG-IAM-17 وG-PAY-22 وG-SEP-19 وG-OPS-16)

**المشكلة (عيّنة متحققة):**
- قاعدة الإغلاق في `completion-backlog/README.md:40-44` («تُعاد فوراً إلى Blocked») و«ما لا يُقبل دليلاً» (`:53-63`) غير مطبّقة: 170 سطر حالة «Done» في `completion-backlog/`.
- 74 وثيقة فيها «ReadyForSign-off»، منها 37 حزمة UAT، والحزم قوالب بتوقيع `____` (مثلاً `FUN_001_UAT_PACK.md:3`، `FUN_002_UAT_PACK.md:13`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| المدقّق | `scripts/v1-doc-truth-validate.ps1` | لكل بطاقة في `docs/qa/v1-card-scope.json`، الحالة `Done` تتطلب ثلاثة أسطر: `- **الدليل الحي:** acceptance-evidence/AC-…` (مجلد موجود، وحالته `Pass`، و`gitSha` في تاريخ `main`)، و`- **تشغيل CI:** <run id>` (يُتحقق بـ `gh api` حين يتوفر الرمز)، و`- **التوقيع:** <الاسم/الصفة> <التاريخ>`. وإلا يجب أن تكون `Blocked` بسبب. وحزمة UAT بحالة غير «Template» تتطلب جدول توقيع مملوءاً وروابط أدلة. و`-ReportOnly` لما خارج القائمة |
| نطاق الإنفاذ | `docs/qa/v1-card-scope.json` | البطاقات والوثائق في الجدول أدناه. تزيد ولا تنقص (اختبار ratchet) |
| خطوة CI | `scripts/ci/steps.json` | «Doc truth (V1)» بعد الحراس |
| اختبار | `V1DocTruthValidatorTests` | يشغّل المدقّق على وثائق اختبار في `testdata/qa/doc-truth/` (Done بلا دليل ← فشل؛ Done كامل ← نجاح؛ UAT «ReadyForSign-off» بلا توقيع ← فشل) |

**التصحيحات حسب الفجوة:**

| الفجوة | الوثيقة: المرجع ← التصحيح |
|--------|---------------------------|
| G-QA-13 | `completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md:5-48` (QA-002/003/004 Done): ← `Blocked` بسبب، أو Done بدليل الموجة 1 (`ci-local` وCI وCodeQL وAC-QA-34/35/41). `FUN_001..006_VERIFICATION.md` (`FUN_001_VERIFICATION.md:17`): «Done» ← حسب الدليل. حزم UAT الـ37 ← «Template»، وبقية الوثائق الـ74 ← حالة تطابق الدليل. `QA_COVERAGE_VERIFICATION.md:21` ← رقم آخر تشغيل CI. `docs/qa/test-count-registry.json` ← مولَّد في CI على HEAD. `QA_E2E_VERIFICATION.md` (أمر بلا `--profile full-stack`) ← مصحّح، و«47 رحلة» ← «47 حالة: رحلة حقيقية واحدة و46 فحص سطح» حتى QA-H. `05-TRACEABILITY.md`: أسطر QA-004 وOPS-003 وINT-001/002 وPAY-003 وDOM-001 تُطابق الملخص |
| G-WF-13 | `completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md:176-192` (WF-001) و`WF_002_WORK_INBOX.md:12-13,73-77`: إزالة `[x]` على ما ليس في المحرك (توازٍ، نصاب، مهل وتصعيد، تفويض، SLA، تدفقات فرعية)، وحذف `CreateViaEngineOrLegacy` من أدلة الإغلاق (`ApprovalWorkflowService.cs:21-56`، لا يستدعيه إلا `Wf001WorkflowEngineTests`). `02-P1-PLATFORM-INTEGRITY.md:47-57` (IAM-003) ← حسب AC-WF من PLAN-43..46 |
| G-POL-16 | `07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md:397,400,411` و`PROD_001_VERIFICATION.md:10` و`prod-policy/policy-platform-matrix.json:53-54`: حالة واحدة متسقة (`live-policy-engine` لا تكون Satisfied وBlocked معاً). `PROD_001_ROLLBACK.md` و`PROD_001_FOUR_EYES_PUBLISH.md` ← وصف الفعلي بعد PLAN-47. حراس `IPolicyPlatformClaimGuard` المختبرة وحدها (`Prod001ProductionBindingTests.cs:213-222`) لا تُذكر دليلاً. `FUN_005_JOURNEY_MATRIX.md:26`، `POLICY_DOMAIN_REGISTRY.md`، `POLICY_ENGINE_UAT.md`، `THESIS_GRADUATION_CURRENT.md:352` ← مطابقة ما ربطته PLAN-49 |
| G-IAM-17 | `01-P0-RELEASE-BLOCKERS.md:113-120` (IAM-002، دليله `OrgScopeRequirement` غير المستعمل)، و`02-P1-PLATFORM-INTEGRITY.md:50,69` (IAM-003/004)، و`SERVICE_TRUST_INVENTORY.md:11,21`، و`HIRE_RBAC_MATRIX.md` ← مطابقة الشيفرة بعد PLAN-50..53. وأدلة AC-01..06 وAC-26..31 من §3 |
| G-PAY-22 | `01-P0-RELEASE-BLOCKERS.md:141-161` (PAY-001) و`PAY_001_APPROVAL_BYPASS.md:94-97`: `[x]` فقط لما أثبته PLAN-56/58/59. `PAYROLL_RUN_RESUME.md:31` (الإلغاء). `PAY_004_ARREARS.md:5,17` (النموذج الصحيح، وحذف التسوية غير الموجودة). `FRD_001_FRAUD.md:4,42-45`. `acceptance-plans/PLAN-10-payroll-money.md:14` (الصرف الجزئي يُنشئ أوامر دفع) |
| G-SEP-19 | `FUN_002_VERIFICATION.md:17`، `FUN_002_UAT_PACK.md:13`، `01-P0-RELEASE-BLOCKERS.md:138` (DOM-001)، `02-P1-PLATFORM-INTEGRITY.md:161` (INT-003)، `04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md:167` (FUN-002) ← حسب AC-SEP من PLAN-60..64. `FUN_002_JOURNEY_MATRIX.md:7,13` ← خطوات الكيانات الفعلية (`SeparationDecision`/`ITAccessRevoked` كما صارت بعد PLAN-60/62)، وإضافة HR.Health طرفاً إلزامياً كما ربطته PLAN-62 (§4) |
| G-OPS-16 | `01-P0-RELEASE-BLOCKERS.md:77,89,174,198-210` (SEC-004، IAM-001، INT-001، DEP-001)، و`03-P2-SCALE-AND-RELIABILITY.md:72-220` (OPS-001..005، OBS-001/002، NFR-001/002) ← حسب أدلة PLAN-36/38/39/40. `Sec001PortScanTests.cs:38-74` (تعابير على نص YAML) لا يُذكر دليلاً. `RELEASE_READINESS_STAGING.md` و`rel-staging/parity-diff-last.json` ← «مقارنة ملفات، لا بيئة» |

**القواعد:**
1. لا «Done» بلا الأسطر الثلاثة في النطاق المنفَّذ. ولا «ReadyForSign-off» بلا توقيع.
2. التصحيح يصف ما في الشيفرة اليوم ويشير إلى الدليل، ولا يعد بما لم يُنفّذ.
3. `THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md` يُزامن فقط من `Pass` على HEAD (`scripts/sync-thesis-capability-verification.ps1 -Apply` بعد تعديله ليقرأ الحداثة من ملخص PLAN-41).
4. **أسطر «الحالة»:** كل فجوة من الثماني تحصل على سطر «الحالة» في دراستها (الدراسات الست ودراسة الجودة)، بالخطة والدليل.

---

## Out of scope

- تمارين الفوضى والنسخ والأداء وتوقيع PDF البشري (AC-17، 18، 22، 34، 35، 36) ← QA-G «ops-drill» (الموجة 2). تظهر `NotRun` بمالكها.
- تحويل «الرحلات» السطحية إلى رحلات حقيقية، ومنها رحلة الفصل من الطلب حتى إخلاء المنصب (جزء E2E من G-SEP-19) ← QA-H (الموجة 2).
- استبدال اختبارات الانعكاس والوجود في سير العمل (`Wf002WorkInboxTests.cs:22-140`، جزء من G-WF-13) ← الموجة 2 (خطة WF المالكة). هنا تُمنع فقط من أن تُذكر دليلاً.
- سيناريوهات AC-<منطقة> الجديدة نفسها: كل خطة 34..64 تكتب سيناريوهاتها وتثبتها. هذه الخطة تعيد تشغيلها معاً فقط.
- إعادة كتابة AC-11 (PLAN-42/45) وAC-13 (PLAN-42/48)، وAC-12 (PLAN-59)، وAC-24/25 (PLAN-58)، وAC-33 (PLAN-45)، والدليل الحي لـ AC-20 (PLAN-38).
- سكربتات إلغاء الرواتب بـ SQL لـ AC-21..23 ← PLAN-59. وحذف `acceptance-payroll-audit-fixture.ps1` ← PLAN-58.
- AC-14 وأدلة AC-EST ← الموجة 2 (COD-G)، كما في PLAN-48.
- البطاقات خارج مناطق الموجة 1: تقرير فقط (D-V1-5).

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-V1-65a | وظيفة `Acceptance` على `main` (الملف الشبيه بالإنتاج): كل صنف `Wave=1` مطلوب `Pass`، و`summary.md` بـ `gitSha` وصور = HEAD، ولا `Stale` ولا `Incomplete` | سيناريو مطلوب يفشل ← الوظيفة حمراء، والملخص يسمّيه ومالكه |
| AC-V1-65b | AC-01..13، 15، 16، 19..21، 23..33، 37 المطلوبة `Pass` على HEAD. AC-06: بعد النقل يفقد `br1-mgr` رؤية `EmpBr1` ويكتسبها `br2-mgr` | AC-02: `br1-mgr` يطلب تفاصيل موظف BR2 وتصديره ← الرفض المقرر، ولا صف في التصدير |
| AC-V1-65c | تشغيلان متتاليان على المكدّس نفسه بالنتائج نفسها | — |
| AC-V1-65d | الحراس خضراء على المستودع | ملفات اختبار تجريبية: `db-checks` بثابت منطقي، و`\|\| true`، و`UPDATE` في سكربت سيناريو ← كل حارس أحمر |
| AC-V1-65e | `v1-doc-truth-validate.ps1` = PASS على النطاق | بطاقة «Done» في النطاق بلا دليل، أو حزمة UAT «ReadyForSign-off» بلا توقيع ← خروج 1 |
| AC-V1-65f | لا مرجع إلى `SharedKernel/Payroll/ArrearsLedgerEntry`، و`PAY_004_ARREARS.md` يشير إلى الكيان المستعمل | — |
| AC-V1-65g | سجل قدرات الأطروحة يُحدَّث فقط من `Pass` حديث | دليل `Stale` ← المزامنة لا تكتب سطره |
| AC-V1-65h | AC-09 يثبت Shadow عبر نقطة مجال (السجل في DB والطلب مسموح). AC-10 يثبت Enforce بمعتمدَين مختلفين | AC-10 بالشخصية نفسها للنشر والاعتماد ← رفض أربع العيون |

**الدليل الحي:**
- `AcV1Wave1ClosureTests` (`[RequiredAcceptance]`، الملف الشبيه بالإنتاج) يقرأ ملخص التشغيل الحالي، ويتأكد من AC-V1-65a/c/g، ويسجّل في `acceptance-evidence/AC-V1-65/`.
- بقية السيناريوهات في أصنافها المقوّاة.

## Verification

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "AcceptanceSkipPattern|AcceptanceScriptSqlWrite|V1DocTruth|CiStepsParity"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Int003|Fun002"
pwsh scripts/v1-doc-truth-validate.ps1
pwsh scripts/v1-doc-truth-validate.ps1 -ReportOnly -All   # تقرير خارج النطاق
# التشغيل الموحّد محلياً ثم بعيداً
pwsh scripts/ci/acceptance-job.ps1 -Profile Both
pwsh scripts/ci/acceptance-job.ps1 -Profile ProdLike -SkipStackUp -Repeat
pwsh scripts/acceptance-baseline-validate.ps1
gh run list --workflow ci.yml --branch main --limit 3
```

## Definition of done

- تشغيل `Acceptance` واحد على `main` بالملف الشبيه بالإنتاج: كل الأصناف المطلوبة `Pass`، وتشغيل ثانٍ بالنتيجة نفسها. و`summary.md` المودَع من أثره.
- AC-01..37: المطلوبة `Pass`، والباقية `NotRun` بمالك الموجة 2. لا `Pass` لا يثبت ادعاءه في عمود «يثبت».
- الحراس الثلاثة الجديدة خضراء، و`ArrearsLedgerEntry` في `SharedKernel` محذوف.
- `v1-doc-truth-validate.ps1` = PASS على النطاق، وخطوته في CI. وحزم UAT الـ37 «Template» أو موقّعة، ولا «ReadyForSign-off» بلا توقيع في الوثائق الـ74.
- أسطر «الحالة» محدّثة في الدراسات الست ودراسة الجودة للفجوات الثماني.
- AC-V1-65a..h = Pass.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-65-wave1-verification-docs.md` |
| Acceptance | تعديل `Ac01`..`Ac10`، `Ac15`، `Ac16`، `Ac19`، `Ac21`، `Ac23`، `Ac26`..`Ac31`، `Ac37`؛ وسم `Ac13`، `Ac20`، `Ac32`؛ سمات `Wave`/`Plan` على أصناف 34..64؛ جديد `AcV1Wave1ClosureTests` |
| Guards | `AcceptanceSkipPatternArchitectureTests` (موسّع)، `AcceptanceScriptSqlWriteTests`، `tests/acceptance/sql-write-allowlist.json`، `V1DocTruthValidatorTests` |
| Scripts | `scripts/v1-doc-truth-validate.ps1`، `docs/qa/v1-card-scope.json`؛ تعديل `acceptance-quarantine-flow.ps1` و`acceptance-analytics-reconcile.ps1` (إخراج للتأكيد في C#)، و`acceptance-correlation-trace.ps1`، و`sync-thesis-capability-verification.ps1`، و`scripts/ci/acceptance-job.ps1` (المعاملان `-SkipStackUp` و`-Repeat`) |
| Code | حذف `src/Shared/HRManagement.SharedKernel/Payroll/ArrearsLedgerEntry.cs`؛ إصلاح `Int003ProcessSagaTests.cs:145-146`، `Fun002SeparationRecordIntegrationTests.cs:95-99`، `HireSeparationJourneyE2ETests.cs:57` |
| CI | `tests/acceptance/ci-scope.json` (المطلوب الموسّع)، `scripts/ci/steps.json` (Doc truth) |
| Evidence | `acceptance-evidence/**` و`summary.md` من أثر `main`؛ `acceptance-evidence/AC-V1-65/` |
| Docs | جدول §4، و`THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md`، وأسطر «الحالة» في الدراسات السبع |
| Reports | `reports/plan65-baseline.md`، `reports/plan65-final-report.md` (نتيجة كل صنف، والإخفاقات بمالكها، ومقارنة قبل/بعد للبطاقات) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| خطة سابقة لم تسلّم سيناريوهاتها أو لم توسمها | 0.5 يكشفها: `Blocked` بمالكها في الملخص، ولا تُكتب هنا |
| التشغيل الموحّد يكشف عيوب منتج كثيرة في آخر الموجة | الفرز بالقاعدة 3 من §1: لا إخفاء، وكل فشل بمالكه. ولا يُعلن الإغلاق إلا بالأصناف المطلوبة كلها `Pass` |
| إعادة نحو مئة بطاقة إلى `Blocked` تبدو تراجعاً | هي قاعدة المستودع نفسه (`README.md:40-44`)، والتقرير يعرض قبل/بعد. وما يملك دليلاً من الموجة 1 يبقى Done بأسطره الثلاثة |
| تعارض تعديلات الوثائق مع خطط تعمل بالتوازي | هذه الخطة الأخيرة في الموجة 1. التعديل يعتمد على HEAD بعد دمج 34..64 |
| AC-06/15 الحقيقيان يطيلان الوظيفة | الملف الشبيه بالإنتاج وحده، ووقت كل صنف في التقرير |

## Effort estimate

9–10 أيام عمل (الحد الأعلى للحجم):
- الجرد: 0.5.
- تقوية AC-01..37: 3.5–4، أثقلها AC-06 وAC-15 وAC-09/10.
- الحراس والحذف والإصلاحات التحصيلية: 1.
- التشغيل الموحّد والإعادة والفرز: 1.5–2.
- مدقّق الوثائق وتصحيحات الفجوات الثماني: 2–2.5.
- الدليل: 0.5.

لا مكان لتمارين التشغيل ولا لرحلات E2E الحقيقية ضمن هذا الحد، ولذلك نُقلت إلى الموجة 2.
