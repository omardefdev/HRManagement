# دراسة البناء والتكامل المستمر وصحة الاختبارات وتغطية القبول على مستوى البرنامج (PRG-001) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** سير عمل GitHub Actions (`.github/workflows/*`) وحماية الفرع، صحة البناء (`Directory.Build.props`، `Directory.Packages.props`، تحذير NU1903، حدود التغطية)، الاختبارات الحمراء المعروفة على `main` وأسبابها، تغطية القبول المنصّي AC-01..AC-37 (PLAN-00..13) والأدلة في `acceptance-evidence/`، رحلات Playwright الـ47، وادعاءات التوثيق (حزم UAT، بطاقات `04-P3`، `05-TRACEABILITY`، `FUN_00x_VERIFICATION`).  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات وملفات سير العمل والتقارير والأدلة عند `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. أُعيد تطبيق منطق بعض الاختبارات ساكناً (مثل تعبير `BolaArchitectureTests` وقائمة الشخصيات) لمعرفة نتيجتها المتوقعة.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات ولا سير العمل (لا يوجد .NET SDK). النسخة بلا مجلد `.git`، فلا يمكن فحص الـ commits ولا الإعدادات الحية لـ GitHub (حماية الفرع، الأسرار). أسماء الاختبارات الفاشلة مأخوذة من تقارير PLAN-25..33، والسجلات الخام (`artifacts/plan25/*.log`) غير مودعة. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [CI_AND_TESTING.md](CI_AND_TESTING.md) · [GITHUB_CI_GOVERNANCE_RUNBOOK.md](GITHUB_CI_GOVERNANCE_RUNBOOK.md) · [SCM_001_BRANCH_PROTECTION.md](SCM_001_BRANCH_PROTECTION.md) · [acceptance-plans/README.md](acceptance-plans/README.md) · [VERIFICATION-COMPLETION.md](acceptance-plans/execution/VERIFICATION-COMPLETION.md) · [04-P3](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [05-TRACEABILITY](completion-backlog/05-TRACEABILITY.md) · [QA_E2E_VERIFICATION.md](QA_E2E_VERIFICATION.md) · [QA_COVERAGE_VERIFICATION.md](QA_COVERAGE_VERIFICATION.md) · [FUN_001..006_VERIFICATION](FUN_001_VERIFICATION.md) · [plan25-final-report](../reports/plan25-final-report.md) · [plan33-acceptance-inventory](../reports/plan33-acceptance-inventory.md) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md)  
**CI البعيد (PLAN-35، 2026-10-30):** أول تشغيل أخضر كامل على `444e8c2` (PR [#24](https://github.com/omardefdev/HRManagement/pull/24)، [التشغيل 41107410607](https://github.com/omardefdev/HRManagement/actions/runs/41107410607))؛ و`main` = `origin/main` أخضر لـ `CI` و`CodeQL` و`Gitleaks` و`E2E` على `052b506` ([التشغيل 41140221205](https://github.com/omardefdev/HRManagement/actions/runs/41140221205)).  

---

## 1. الخلاصة

1. **البنية التحتية للجودة واسعة على الورق، لكنها لم تُشغَّل بعيداً على العمل الحالي.** يوجد 12 ملف سير عمل، ونحو 4,134 اختبار وحدة، و113 ملف اختبار تكامل، وأكثر من 100 أسلوب قبول مع `AcceptanceGate`، و47 رحلة Playwright. لكن `main` يسبق `origin` بـ140 commit لم يمر أيّ منها بالـ CI البعيد. كل «أخضر» في التقارير هو تشغيل محلي على Windows بإعدادات ar-SA، مع قائمة «إخفاقات معروفة» تضم 16 اختبار وحدة و~25 اختبار تكامل.
2. **سبعة عوائق P0 تمنع CI أخضر وأساس قبول صادق:**
   - **(أ)** مسار الدفع والدمج نفسه غير محسوم: حماية الفرع الموثّقة (مراجعتان، توقيع، `enforce_admins`) لا يستطيع مطوّر واحد تجاوزها، وCODEOWNERS يسمّي `@omard` لا مالك المستودع (G-QA-01).
   - **(ب)** قنبلة زمنية: استثناء STR-005 انتهى في 2026-09-15، فيفشل CI قبل البناء، ويفشل اختباران، وتمتنع خدمات Production/Staging عن الإقلاع (G-QA-02).
   - **(ج)** 5 اختبارات وحدة حمراء حتمية على Linux، و~22–25 اختبار تكامل أحمر، أغلبها انجراف في حاويات DI يدوية في الاختبارات (G-QA-03).
   - **(د)** NU1903 (SSH.NET، شدة High) يُسقط خطوة الفحص الأمني ويُسقط `-warnaserror` (G-QA-04).
   - **(هـ)** صورة MinIO المثبّتة لم تعد قابلة للسحب، فلا يقلع مكدّس E2E ولا القبول في CI (G-QA-05).
   - **(و)** وظيفة القبول في `ci.yml` لا يمكن أن تنجح بنيوياً: realm بلا شخصيات القبول، وكلمات مرور غير متاحة، ومشروع compose مختلف، واختبار ثابت يتوقع 60 شخصية والكتالوج 61 (G-QA-06).
   - **(ز)** خطوة الرحلة الشبيهة بالإنتاج في `e2e-journeys.yml` معطّلة بـ`\n` حرفي، فلا يُشغَّل بعيداً الدليلُ الوحيد على قيم الإنتاج (G-QA-07).
3. **ثغرات سلامة التحقق (P1):** ملخص الأدلة يعدّ 10 «Pass» للمنصة، منها اثنان تعاقديان فقط (AC-34/36)، وخمسة من نسخة قديمة (AC-01..05 في 2026-08-24)، وواحد لا يفحص ادعاءه (AC-33). AC-12 «Blocked» بسبب خلل في تصميم الاختبار لا في المنتج. أنماط ضعيفة باقية في AC-01..37، ولا أحد منها موسوم `[RequiredAcceptance]`. «الرحلات» الـ47 فحوص سطح؛ واحدة فقط رحلة حقيقية. بوابة CodeQL لا تفشل أبداً (`upload: false`). الوثائق تقول Done وReadyForSign-off بلا تشغيل.
4. **فجوات P2:** مسارات الرواتب والتعيين وسلسلة العقود مثبتة على SQLite فقط، خلافاً لادعاء QA-003. لا اختبار يقلع الخدمات بإعدادات Production. لا ضبط للاختبارات المتقلبة.
5. **فجوة التحقق:** من AC-01..37 لم يُشغَّل 23 قط. ولم ينجح على شيفرة اليوم إلا AC-11 وAC-32. ولم تُشغَّل أيٌّ من الرحلات الـ46 غير التوظيفية في أي مكان: محلياً لأن المتصفحات غير مثبتة، وبعيداً لأن الـ CI لم يعمل.

**التقدير الإجمالي:** نحو 50–65 يوم عمل لمطوّر واحد. منها 17–22 يوماً لخطة الاستقرار (QA-A وQA-B وQA-C) التي تنتهي بـ CI بعيد أخضر وأساس قبول صادق، و17–23 يوماً لخطط القبول الحي للسيناريوهات التي لم تُشغَّل (QA-E وQA-F وQA-G).

---

## 2. خريطة المسار كما هو في الشيفرة

المسار هنا هو مسار «إثبات الجودة» من الـ commit حتى الدليل الموقّع.

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | الدفع والدمج | 140 commit محلية؛ حماية فرع موثّقة بمراجعتين وتوقيع و`enforce_admins`؛ CODEOWNERS = `@omard` | ❌ لم يُدفع شيء، والمسار غير محسوم (G-QA-01) |
| 2 | حراس ما قبل البناء | 64 خطوة تحقق (SUP/UX/GOV/STR/MIG/EXT/PROD/SOV/ARC) في `ci.yml:29-225` | ❌ STR-005 تفشل بالتاريخ (G-QA-02) |
| 3 | الاستعادة والتنسيق والبناء | `--locked-mode` ✅؛ البناء 0 أخطاء محلياً؛ `dotnet format --verify-no-changes` لم يُشغَّل في أي جلسة | ⚠️ (G-QA-01) |
| 4 | فحوص EF | `has-pending-model-changes` لـ15 سياقاً | ⚠️ لم يُشغَّل إلا لـ3 سياقات في التقارير |
| 5 | اختبارات الوحدة | ~4,134 اختباراً | ❌ 5 حمراء حتمية على Linux، و11 بيئية محلياً (G-QA-03، G-QA-12) |
| 6 | تكامل InMemory/SQLite | 113 ملفاً، 24 منها تبني DI يدوياً | ❌ ~22 حمراء (G-QA-03، G-QA-14) |
| 7 | تكامل Testcontainers | 27 فئة | ❌ MinIO لا يُسحب (G-QA-05)؛ الرواتب على PostgreSQL ⚠️ |
| 8 | بوابة التغطية | 60% على 7 تجميعات + حدود لكل تجميعة | ⚠️ آخر قياس 2026-07-28 |
| 9 | الفحص الأمني للحزم | `check-nuget-vulnerabilities.sh` يفشل على High | ❌ SSH.NET (G-QA-04) |
| 10 | E2E في `ci.yml` | إلزامي، مع حارس «صفر تخطٍّ» | ❌ المكدّس لا يقلع (G-QA-05)، والرحلات فحوص سطح (G-QA-10) |
| 11 | القبول في `ci.yml` | `RUN_ACCEPTANCE` + `ACCEPTANCE_REQUIRE_ALL` | ❌ بنيوياً (G-QA-06) |
| 12 | CodeQL / Gitleaks | وظيفتان مكررتان في ملفين | ⚠️ CodeQL لا يفشل أبداً (G-QA-11) |
| 13 | الرحلات الليلية والشبيهة بالإنتاج | `e2e-journeys.yml` | ❌ `\n` حرفي (G-QA-07) |
| 14 | ملخص الأدلة | `acceptance-evidence/summary.md` | ❌ Pass قديم وتعاقدي (G-QA-08) |
| 15 | الوثائق وحزم UAT | Done / ReadyForSign-off | ❌ بلا تشغيل (G-QA-13) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- خط CI غني ومنظم: استعادة `--locked-mode`، منع الإصدارات العامة (`*`)، تثبيت صور الأساس، سياسة الترخيص، منع CDN خارجي، فحص XSS، فحوص STR/MIG/EXT/PROD/SOV/ARC، وبناء كل الصور.
- بوابة تغطية مدروسة: دمج اتحادي للأسطر يمنع العدّ المزدوج ([check-coverage.py:38-60](../scripts/check-coverage.py))، وحدود لكل تجميعة مع ratchet ([coverage-floors.json](../coverage-floors.json)).
- حراس «صفر تخطٍّ» في وظيفتَي E2E ([ci.yml:391-402](../.github/workflows/ci.yml)، [e2e-journeys.yml:67-78](../.github/workflows/e2e-journeys.yml))، وحارس «نُفّذ ونجح» لرحلة التوظيف ([e2e-journeys.yml:132-143](../.github/workflows/e2e-journeys.yml)).
- إطار قبول ناضج للتوظيف:
  - `AcceptanceGate` يكتب `Blocked` ويُفشل السيناريو المطلوب ([AcceptanceGate.cs:67-80](../src/Tests/HRManagement.AcceptanceTests/AcceptanceGate.cs)).
  - اختبار معماري يمنع «Skip ثم return» في **كل** ملفات القبول، لا التوظيف وحده.
  - 11 مجلد أدلة AC-HCM حديثة وكاملة (2026-10-29).
  - الرحلة الشبيهة بالإنتاج نجحت محلياً.
- `ci.yml` لا يحتاج أي سرّ مستودع: بيانات المختبر مضمّنة ([.env.ci](../.env.ci))، والنشر يوقّع بـ OIDC/cosign.
- نمط «قائمة مجمّدة تتقلّص فقط» موجود ويمكن إعادة استخدامه ([hrcore-post-policy-allowlist.txt](../tests/architecture/hrcore-post-policy-allowlist.txt)).
- إدارة مركزية للحزم مع ملفات قفل، وSDK مثبّت في `global.json`.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-QA-01 — 140 commit بلا CI بعيد، ومسار الدمج نفسه غير محسوم
- **الدليل:**
  - [plan25-final-report.md:209-235](../reports/plan25-final-report.md): «Nothing was pushed». و`main` يسبق `origin/main` بـ128 commit قبل PLAN-25، ثم 140 الآن.
  - [branch-protection-main.json:3-26](scm-governance/branch-protection-main.json) يفرض:
    - الفحوص `CI` و`CodeQL` و`Gitleaks`؛
    - `required_approving_review_count: 2` و`require_code_owner_reviews` و`require_last_push_approval`؛
    - `required_signatures: true` و`enforce_admins: true`.
  - [GITHUB_CI_GOVERNANCE_RUNBOOK.md:3,22](GITHUB_CI_GOVERNANCE_RUNBOOK.md): المستودع `omardefdev/HRManagement` عام، و«protection is applied live». لكن [CODEOWNERS:4-5](../.github/CODEOWNERS) يسمّي `@omard`، وهو حساب غير مالك المستودع (**يحتاج تحقق**: `gh api repos/omardefdev/HRManagement/branches/main/protection`).
  - الوثائق متناقضة في الفحوص المطلوبة: الدليل التشغيلي يضيف `E2E` ([GITHUB_CI_GOVERNANCE_RUNBOOK.md:8-13](GITHUB_CI_GOVERNANCE_RUNBOOK.md))، وملف الحماية والمدقّق لا يضيفانه ([scm001-branch-protection-validate.ps1:38](../scripts/scm001-branch-protection-validate.ps1)).
  - خطوات لم تُشغَّل في أي جلسة موثقة:
    - `dotnet format --verify-no-changes` ([ci.yml:230-231](../.github/workflows/ci.yml)). ويوجد 35 ملف `.cs` بلا سطر نهائي، رغم أن `.editorconfig` يفرض `insert_final_newline = true` (مثل [BolaArchitectureTests.cs](../src/Tests/HRManagement.UnitTests/Architecture/BolaArchitectureTests.cs) و[LegislationRegisterDependencyInjection.cs](../src/Services/HRCore/HR.Core.Infrastructure/LegislationRegisterDependencyInjection.cs)) — **يحتاج تحقق**.
    - فحص EF لـ15 سياقاً ([ci.yml:236-237](../.github/workflows/ci.yml))، والتقارير لا تذكر إلا HR.Core وDev.Analytics وFile.Storage.
    - بوابة التغطية: آخر قياس 2026-07-28 ([QA_COVERAGE_VERIFICATION.md:21](QA_COVERAGE_VERIFICATION.md)).
  - `needs: [build-and-test]` ([ci.yml:340,411](../.github/workflows/ci.yml)): أي فشل في الوظيفة الأولى يُخفي E2E والقبول تماماً.
- **الأثر:**
  - إن طُبّقت الحماية كما وُثّقت، يُرفض الدفع المباشر إلى `main` (PR إلزامي وتوقيع)، ولا يمكن دمج PR بمراجعتين لمطوّر واحد مع `enforce_admins`.
  - وإن لم تُطبّق، فادعاء SCM-001 غير صحيح.
  - في الحالتين لا يوجد commit واحد من PLAN-25..33 مُثبت البناء خارج جهاز المطوّر.
- **الإصلاح المقترح:**
  - قرار D-QA-1 قبل أي دفع.
  - الدفع عبر فرع `stabilize/ci` وPR.
  - تشغيل محلي مكافئ لـ CI على Linux قبل الدفع: حاوية تطوير بثقافة `en`/Invariant وpwsh وDocker، وسكربت ينفّذ خطوات `ci.yml` بالترتيب.
  - تصحيح CODEOWNERS واختيار مجموعة الفحوص المطلوبة (D-QA-2).
- **الخطة:** [PLAN-35](acceptance-plans/PLAN-35-qa-push-remote-ci.md)، و[PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md) (جزئياً: CI محلي مطابق وأخضر قبل الدفع)
- **الحالة:** ✅ عولجت في PLAN-35 (`2809a4e`، `4f953cb`، `c8ef2b1`، `6e907f5`) — فحص ما قبل الدفع على التاريخ كله (`scripts/plan035-prepush.ps1`: gitleaks مثبّت بقواعد فعلية، actionlint، ملخص ci-local، الملفات الممنوعة، GOV-001-D) أخضر على `0f69f52`؛ أول دفع عبر PR [#24](https://github.com/omardefdev/HRManagement/pull/24) ثم `main` = `origin/main`، و`CI` و`CodeQL` و`Gitleaks` و`E2E` خضراء على `main` (التشغيلان 41130907741 و41140221205). الحماية ملفاً في `docs/scm-governance/branch-protection-main.json` (0 مراجعات، بلا توقيع ولا تاريخ خطي، باستثناءات مؤرخة)؛ CODEOWNERS = `@omardefdev`. المستودع خاص على الخطة المجانية فيرد GitHub على الحماية بـ 403، فالبديل (D-QA-1) حارس pre-push محلي يرفض الدفع المباشر إلى `main` (`SCM-NO-GITHUB-PROTECTION`). السالب: PR [#25](https://github.com/omardefdev/HRManagement/pull/25) بفحص أحمر مقصود ← `CI` = FAILURE وأُغلق بلا دمج؛ لكن GitHub لا يمنع دمجه (`MERGEABLE`) ما دامت الحماية غير متاحة — يبقى المنع إجرائياً حتى يصبح المستودع عاماً أو على Pro. الدليل: AC-QA-35a..d.
- **التحقق المطلوب:**
  - موجب: PR من `stabilize/ci` تظهر فيه الفحوص المطلوبة كلها خضراء، ويُدمج وفق القاعدة المعتمدة؛ ورابط التشغيل في التقرير.
  - سالب: دفع مباشر إلى `main` مرفوض، ومحاولة دمج مع فحص أحمر مرفوضة.

#### G-QA-02 — قنبلة زمنية STR-005: يفشل CI قبل البناء، ولا تقلع الخدمات في Production/Staging
- **الدليل:**
  - [governance-matrix.json:146-157](str-governance/governance-matrix.json): الاستثناء `EX-STR005-SIM-INCIDENT-WINDOW` (`"expiresOn": "2026-09-15"`, `Declared`) منتهٍ. والاستثناء `EX-STR005-SIM-LAW-INTERIM` ينتهي في 2026-10-30. كلاهما «simulation seed».
  - [str005-governance-runtime-validate.ps1:33,57-66,159-163](../scripts/str005-governance-runtime-validate.ps1) يقارن بـ`[datetime]::UtcNow` ثم `exit 1`. والخطوة في [ci.yml:71-72](../.github/workflows/ci.yml) قبل `Restore`، فهي أول ما يفشل.
  - [Str005ProductionBindingTests.cs:121-134,136-156](../src/Tests/HRManagement.UnitTests/Str005ProductionBindingTests.cs): `GoLiveDeclared_passes_with_multi_seat_evidence` و`Runtime_validate_script_passes` يحمّلان المصفوفة الحقيقية، فيفشلان على أي جهاز.
  - [ProgramGovernanceGuard.cs:85-106](../src/Shared/HRManagement.ServiceDefaults/ProgramGovernance/ProgramGovernanceGuard.cs) يرمي استثناءً عند الإقلاع إذا كان `EnforceOnStartup=true`، وهي `true` في:
    - [HR.Core appsettings.Production.json:58-59](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)؛
    - [Payroll appsettings.Production.json:59-60](../src/Services/Payroll/Payroll.Web/appsettings.Production.json)؛
    - [Gateway appsettings.Production.json:83-84](../src/Gateway/HRManagement.Gateway/appsettings.Production.json)؛
    - وملفات Staging المقابلة.
  - لم يكتشف أحد ذلك لأن الملف الشبيه بالإنتاج يبقي `ASPNETCORE_ENVIRONMENT=Acceptance` ([plan25-final-report.md:182](../reports/plan25-final-report.md)، القرار R7)، ولا يوجد في CI اختبار إقلاع بإعدادات Production.
  - القنبلة التالية: `nextDue: 2027-01-31` في [legislative-watch-stamp.json](str-legal/legislative-watch-stamp.json) و6 بنود `expiresOn: 2027-01-31` في `control-catalog.json`، ويفحصها [str002-control-runtime-validate.ps1:133-136](../scripts/str002-control-runtime-validate.ps1).
- **الأثر:** CI أحمر في الخطوة 17 من 89، قبل أي بناء. والبوابة وHR.Core والرواتب لا تقلع في Staging/Production منذ 2026-09-15. وفي 2027-02-01 يتكرر الأمر مع STR-002.
- **الإصلاح المقترح:**
  - قرار D-QA-3: إخراج بذور المحاكاة من المصفوفة التشغيلية إلى `testdata/str005/`، أو تجديدها بمراجع.
  - الاختبارات وخطوات CI تمرّر `-AsOfDate` (المعامل موجود في السطر 14) وساعة ثابتة للبيانات الثابتة.
  - خطوة «أفق الانتهاء» منفصلة تحذّر قبل 30 يوماً.
  - اختبار إقلاع في CI بإعدادات Staging/Production للخدمات الثلاث (→ OPS).
- **الخطة:** [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md)
- **الحالة:** ✅ عولجت في PLAN-34 (2026-10-30، `396e73f`، `c7a675c`) — أُغلق استثناءا المحاكاة `Closed` بالقرار `PG-EXC-2026-10-001` في `decision-log.json` دون تمديد أي تاريخ؛ سجل الالتزامات المؤرخة `docs/qa/dated-obligations.json` ومدقّقه `scripts/qa/dated-obligations-check.ps1` (إنذار 30 يوماً، فشل 7 أيام) خطوةً قبل STR/MIG وسيراً أسبوعياً `dated-obligations.yml`؛ `TimeProvider` في حارسي `ProgramGovernance` و`LegalControl`، وتحليل `yyyy-MM-dd` ثابت الثقافة في المقيّمين وسكربتات STR-005/STR-002/SUP-004. الدليل: `DatedObligationsTests`، الكناري `RuntimeGovernanceDocumentsTests` (البوابة وHR.Core والرواتب × Production/Staging اليوم؛ المصفوفة قبل الإغلاق ترمي «expired» في 2026-09-16)، AC-QA-34b/34c. إقلاع الحاويات كاملة بإعدادات Production ← PLAN-36.
- **التحقق المطلوب:**
  - موجب: خطوة STR-005 (السابعة عشرة) تنجح بتاريخ اليوم، والخدمات الثلاث تقلع بإعدادات Staging في CI.
  - سالب: مصفوفة اختبار فيها استثناء `Declared` منتهٍ ← المدقّق يفشل بالرسالة `expired`، والإقلاع بـ`EnforceOnStartup=true` يرمي استثناءً.

#### G-QA-03 — الاختبارات الحمراء على `main` وأسبابها الجذرية
- **الدليل (وحدة):** المصدر [plan25-progress.md:122](../reports/plan25-progress.md) («the 16 failures are the known environment list»)، و[plan31-baseline.md](../reports/plan31-baseline.md)، و[plan30-session2-handoff.md:223](../reports/plan30-session2-handoff.md) للاختبارات السبعة الخاصة بالسكربتات.

| # | الاختبار | السبب المحلي | المتوقع على Linux في CI | السبب الجذري | المالك |
|---|----------|--------------|--------------------------|--------------|--------|
| 1 | [BolaArchitectureTests:38](../src/Tests/HRManagement.UnitTests/Architecture/BolaArchitectureTests.cs) | «بيئي» في التقارير | **أحمر** | 12 إجراء `Index(..., Guid? employeeId)` بلا حارس نطاق: HR.Contracts ×6 (`SeparationRecords:26`، `ProbationRecords:24`، `CommencementRecords:23`، `ServiceTransactions:33`، `Contracts:28`، `HireWizard:44`) وPayroll ×6 (`PaymentHolds:27`، `EndOfService:39`، `PayrollExtendedControllers:17,64,138`، `FraudAlerts:28`). أُعيد تطبيق تعبير الاختبار ساكناً (يحتاج تحقق) | → IAM |
| 2 | [Ten002ArchitecturalTests:95](../src/Tests/HRManagement.UnitTests/Ten002ArchitecturalTests.cs) | «بيئي» | **أحمر** | `IgnoreQueryFilters()` خارج `TenantFilterBypass.cs` في [BenefitPlanSeeder.cs:26](../src/Services/HRCore/HR.Core.Infrastructure/Data/BenefitPlanSeeder.cs) و[AcceptanceCandidateExperiencePatch.cs:42](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceCandidateExperiencePatch.cs) | → IAM (TEN) |
| 3 | [Syr001ProductionBindingTests:10](../src/Tests/HRManagement.UnitTests/Syr001ProductionBindingTests.cs) | «بيئي» | **أحمر** | الاختبار يبني حاوية فيها `AddHrLegislationRegister()` فقط، و`LegislationRegisterProvider` يحتاج `HRCoreDbContext`. خلل في الاختبار | QA |
| 4–5 | [Str005ProductionBindingTests:122,137](../src/Tests/HRManagement.UnitTests/Str005ProductionBindingTests.cs) | «بيئي» | **أحمر** | استثناء منتهٍ (G-QA-02) | → OPS / GOV |
| 6 | Str005ProductionBindingTests:76 | ar-SA: `DateTime.TryParse` بالتقويم الهجري | أخضر غالباً | بيئي | QA |
| 7 | [Str005GovernancePackTests:68](../src/Tests/HRManagement.UnitTests/Str005GovernancePackTests.cs) | ar-SA: `DateTime.Parse` | أخضر غالباً | بيئي | QA |
| 8 | [MissingModulesRulesTests:117](../src/Tests/HRManagement.UnitTests/MissingModulesRulesTests.cs) (`Certificate_Number…`) | ar-SA | أخضر | خلل كامن: `{completedOn:yyyyMMdd}` بثقافة الخيط ([LearningCompletionRules.cs:28](../src/Services/HRTraining/HR.Training.Domain/Services/LearningCompletionRules.cs)) | → TRN (P3) |
| 9 | [San001SanctionsTests:85](../src/Tests/HRManagement.UnitTests/San001SanctionsTests.cs) | `global.json` معدَّل محلياً إلى `latestPatch` ([plan26-baseline.md](../reports/plan26-baseline.md)) | أخضر **إن** كان المودَع `disable` (يحتاج تحقق) | بيئي | QA |
| 10–16 | `Iam002OrgScopeTests:154`، `Iam005LifecycleTests:96`، `Psirt001VulnerabilityTests:99`، `Rel006ReleaseTrainTests:71`، `Doc001DocumentsTests:97`، `ArabicPdfGenerationTests:109`، `Prv003BreachTests:92` | تستدعي `powershell -File` بلا `-ExecutionPolicy Bypass` | أخضر غالباً (رابط `powershell`←`pwsh` في [ci.yml:20-24](../.github/workflows/ci.yml)) | بيئي على Windows | QA |
| + | `Hcm029CandidateAcceptedV4Tests:38` | متقلّب في التشغيل الكامل | متقلّب | غير محدد | → HCM |

- **الدليل (تكامل):** المصدر [plan25-progress.md:122](../reports/plan25-progress.md): «Full integration suite: … 25 are pre-existing — 14 re-run on unmodified `main` … fail identically (hire closure, payroll batch/run, contract chain), the rest are the `IHrCoreActiveRegimeClient` DI family + the 3 baseline».

| المجموعة | الاختبارات | السبب الجذري (ساكناً) | المالك |
|----------|-----------|----------------------|--------|
| فجوة DI `IHrCoreActiveRegimeClient` (8) | [ContractLifecycleIntegrationTests:105,126,169,313](../src/Tests/HRManagement.IntegrationTests/ContractLifecycleIntegrationTests.cs)؛ [Fun002SeparationRecordIntegrationTests:59,75](../src/Tests/HRManagement.IntegrationTests/Fun002SeparationRecordIntegrationTests.cs)؛ [Int003ProcessSagaTests:23](../src/Tests/HRManagement.IntegrationTests/Int003ProcessSagaTests.cs)؛ [EndToEndReleaseWorkflowSmokeIntegrationTests:38](../src/Tests/HRManagement.IntegrationTests/EndToEndReleaseWorkflowSmokeIntegrationTests.cs) | `PublishSeparationHandler` صار يطلب `IHrCoreActiveRegimeClient` ([SeparationRecordHandlers.cs:123-127](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs))، والاختبارات تبني حاوياتها يدوياً بلا تسجيله (مثل [Fun002…:30](../src/Tests/HRManagement.IntegrationTests/Fun002SeparationRecordIntegrationTests.cs)). تسجيل `Mock` لا يكفي: اختبارات النجاح تحتاج مزيّفاً يعيد نظاماً، وإلا رجعت `RegimeRulesNotFoundMessage` ([SeparationRegimeValidationService.cs:14-18](../src/Services/HRContracts/HR.Contracts.Application/Services/SeparationRegimeValidationService.cs)) | QA |
| إغلاق التعيين (4) | [EstablishmentHireIntegrationTests:21,53](../src/Tests/HRManagement.IntegrationTests/EstablishmentHireIntegrationTests.cs) + اثنان آخران (يحتاج تحقق) | `ProcessPartyAckConsumers` يطلب `IFundedPositionVersionService` ([ProcessPartyAckConsumers.cs:25-29](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs))، والمضيف في [EstablishmentHire…:149-157](../src/Tests/HRManagement.IntegrationTests/EstablishmentHireIntegrationTests.cs) لا يسجّله (الإنتاج يسجّله في `EstablishmentDependencyInjection.cs:10`). فلا يُنشأ المستهلك: لا تعويض، ولا `IHirePartyFaulted` | QA |
| الرواتب (دفعة/تشغيل) + سلسلة العقد (≈10) | مرشحة: [PayrollRunLifecycleIntegrationTests](../src/Tests/HRManagement.IntegrationTests/PayrollRunLifecycleIntegrationTests.cs) (7)، [Perf001PayrollBatchTests](../src/Tests/HRManagement.IntegrationTests/Perf001PayrollBatchTests.cs) (4)، [ContractSeparationChainIntegrationTests:23,52](../src/Tests/HRManagement.IntegrationTests/ContractSeparationChainIntegrationTests.cs) | **غير محسوم ساكناً.** القاسم المشترك: `PayrollIntegrationTestSetup.AddPayrollConsumerServices` ([:16-43](../src/Tests/HRManagement.IntegrationTests/PayrollIntegrationTestSetup.cs)) على SQLite. فرضيات: (1) آلة الحالة تطلب `ValidatedAtCalculate` قبل المراجعة ([PayrollRun.cs:67-70](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)) وقد لا تكتمل مرحلة التحقق في الحاسب المتوازي على SQLite ([PayrollRunParallelCalculator.cs:358-384](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs))؛ (2) انجراف حاوية DI كما في المجموعتين أعلاه. لا يُحسم إلا بسجل التشغيل الأول | QA + → PAY |
| تزوير المعتمد (1) | [SoDForgedApproverTests:48](../src/Tests/HRManagement.IntegrationTests/SoDForgedApproverTests.cs) | `Mock.Of<IHrCoreActiveRegimeClient>()` (:84) يعيد `null` لـ`IReadOnlyList`، ثم `regimes.FirstOrDefault()` في [TransferRequestsController.cs:453-454](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs) يرمي `ArgumentNullException (source)` | QA |
| بيئي (1) | `Hcm028AppealGovernanceMigrationTests` | تنسيق تاريخ بثقافة ar-SA (سنة هجرية) | QA |

- **الأثر:**
  - وظيفة `CI` حمراء حتى بعد إصلاح G-QA-02.
  - تسمية التقارير بعض الإخفاقات «بيئية» أخفت ثلاثة إخفاقات حقيقية: BOLA وTen002 وSyr001.
  - مسارات الفصل (FUN-002) وتعويض التعيين (FUN-001) بلا اختبار تكامل يمرّ، رغم ادعاء Done (G-QA-13).
- **الإصلاح المقترح:**
  - مضيفات اختبار مشتركة لكل خدمة تعيد استخدام `AddXxxInfrastructure` الحقيقي، مع `ValidateOnBuild`، ومزيّفات معلنة (`FakeActiveRegimeClient` يعيد نظاماً).
  - تسجيل `IFundedPositionVersionService` في مضيف التعيين.
  - `Mock` يعيد `[]`.
  - إصلاح اختبار Syr001 ليبني DbContext.
  - BOLA وTen002: قرار D-QA-5.
  - الرواتب: أول مهمة هي التقاط سجل الفشل الحقيقي ثم الإصلاح أو التحويل → PAY.
  - تثبيت ثقافة الاختبارات (G-QA-12).
- **الخطة:** [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md)
- **الحالة:** ✅ عولجت في PLAN-34 (`c6086f2`، `8caddff`، `eed6128`، `35c2959`) — الوحدة 4184/0 فشل على Windows/ar-SA وعلى Linux، والتكامل 344/0 (Linux) و340/0 + Testcontainers 81/0 (Windows) لـ HEAD نفسه (`1a3397c`، بعد إصلاحات المراجعة `ffdd35e`). الإجراءات الاثنا عشر مقيّدة فعلاً (`Plan034ScopedIndexTests` 75 حالة، PostgreSQL)، و`BolaArchitectureTests` أخضر بلا قائمة؛ مضيفات التكامل تسجّل عملاء HR.Core المزيّفين (`ContractsTestServices`، `FakeHrCore*RegimeClient`) و`IntegrationHostValidationTests` يسمّي الاعتماد الناقص؛ سبب إخفاق الرواتب على PostgreSQL كان غياب `TenantIdStampInterceptor` في مضيف الاختبار. ظهرت أربعة عيوب منتجية لم تكن معروفة وأُصلحت بقرار المالك: أسماء الملفات المرفوعة على Linux، أرقام CSV بثقافة الطلب، ترحيل INT002 غير المكتشَف، اسم وصولي لرابط أيقونة. لا قائمة «إخفاقات معروفة»؛ `tests/quarantine.json` فارغ (المتقلّب `Hcm029` عُرف سببه وأُصلح). الدليل: `reports/plan34-final-report.md` §3، AC-QA-34d/e/f/h.
- **التحقق المطلوب:**
  - موجب: `dotnet test` للوحدة والتكامل على Linux = 0 فشل، والقائمة المجمّدة (إن قُررت) هي الاستثناء الوحيد.
  - سالب: إضافة اعتماد جديد إلى معالج دون تسجيله في المضيف المشترك ← يفشل اختبار `ValidateOnBuild` برسالة صريحة.

#### G-QA-04 — NU1903: ثغرة SSH.NET (High) تُسقط الفحص الأمني و`-warnaserror`
- **الدليل:**
  - [packages.lock.json:868-878](../src/Tests/HRManagement.IntegrationTests/packages.lock.json): `Testcontainers 4.13.0` ← `SSH.NET 2025.1.0` (عابر).
  - [plan25-baseline.md:10-11](../reports/plan25-baseline.md): GHSA-mggc-4xg6-vcxf وGHSA-q939-rpr3-3284، و«`-warnaserror` fails at restore».
  - [check-nuget-vulnerabilities.sh:16-57](../scripts/check-nuget-vulnerabilities.sh) يفشل على أي سطر High غير مستثنى، و[supply-chain-exceptions.json](../supply-chain-exceptions.json) فارغ. وخطوة `Security scan` في [ci.yml:295-296](../.github/workflows/ci.yml).
  - لا `TreatWarningsAsErrors` في أي ملف ([Directory.Build.props:3-10](../Directory.Build.props))، فلا يحرس شيءٌ في CI من التحذيرات الجديدة، رغم أن PLAN-25 §0.3 يتطلب `-warnaserror`.
- **الأثر:** خطوة إلزامية حمراء. وثغرة High معروفة في سلسلة توريد الاختبارات بلا استثناء موثّق.
- **الإصلاح المقترح:** تثبيت `SSH.NET` بإصدار مُصلَح عبر `PackageVersion` مركزي ومرجع مباشر في مشروع التكامل (أو `CentralPackageTransitivePinningEnabled`)، أو ترقية Testcontainers. ثم تفعيل `-warnaserror` في خطوة البناء، أو `TreatWarningsAsErrors` لمشاريع المنتج مع قائمة `NoWarn` موثّقة.
- **الخطة:** [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md)
- **الحالة:** ✅ عولجت في PLAN-34 (`e422f92`) — Testcontainers 4.16.0 (Minio 4.15.0) يجرّ SSH.NET 2026.0.0، أول إصدار مُصلَح للثغرتين؛ لا تثبيت مباشر ولا بند استثناء. `WarningsAsErrors` لـ NU1903/NU1904 في `Directory.Build.props`، وخطوة البناء في CI بـ `-warnaserror` (أُصلحت تحذيرات المترجم بلا تغيير سلوك بقرار المالك، وكُتم MVC1004 في إجراءين مع تسجيله). `dotnet list package --vulnerable --include-transitive` بلا ثغرات. الدليل: AC-QA-34i (ومسبار SSH.NET 2025.1.0 يفشل بـ NU1903).
- **التحقق المطلوب:**
  - موجب: `dotnet list package --vulnerable --include-transitive` بلا High، و`dotnet build -warnaserror` = 0.
  - سالب: `verify-sca-fixture.sh` ما زال يثبت فشل High بلا استثناء.

#### G-QA-05 — صورة MinIO المثبّتة لم تعد قابلة للسحب
- **الدليل:**
  - [docker-compose.yml:42-43](../docker-compose.yml): `minio/minio:RELEASE.2025-04-22T22-12-26Z` خدمة أساسية بلا profile، و`file-storage-web` يعتمد عليها ([docker-compose.services.yml:446](../docker-compose.services.yml)).
  - [plan30-final-report.md:296-298](../reports/plan30-final-report.md): «can no longer be pulled (Docker Hub «pull access denied … repository does not exist», quay.io 401)».
  - الالتفاف موجود في مكدّس القبول المحلي فقط: `profiles: ["acceptance-minio"]` ([docker-compose.acceptance.yml:31-32](../docker-compose.acceptance.yml)) ومزوّد `Local`.
  - وظائف CI تستخدم compose الأساسي: E2E ([ci.yml:351-352](../.github/workflows/ci.yml))، والقبول ([ci.yml:422-432](../.github/workflows/ci.yml))، والرحلات الليلية ([e2e-journeys.yml:42](../.github/workflows/e2e-journeys.yml)).
  - Testcontainers: [TestFixtures.cs:20,286-292](../src/Tests/HRManagement.IntegrationTests/TestFixtures.cs) (`Qa003MinioFileScanTests`).
  - الإنتاج: [statefulset.yaml:74](../deploy/k8s/data/base/minio/statefulset.yaml) (→ OPS).
- **الأثر:** `docker compose up` يفشل عند السحب، فتسقط وظيفتا E2E والقبول وخطوة Testcontainers قبل أي اختبار (**يحتاج تحقق** على مشغّلات GitHub).
- **الإصلاح المقترح:** قرار D-QA-7: صورة بديلة موثوقة مثبّتة بالـ digest في سجل المؤسسة أو مرآة، أو مزوّد `Local` في ملفات CI. تحديث `RealInfra.MinioImage` وفحص `check-base-image-pins.sh`.
- **الخطة:** [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md)
- **الحالة:** ✅ عولجت للتطوير وCI في PLAN-34 (`35c2959`) — `minio` خلف profile `object-storage` بصورة من `MINIO_IMAGE` وطبقة `docker-compose.object-storage.yml`؛ `file-storage-web` و`hr-core-web` بالمزوّد `Local` بلا `depends_on: minio`. المكدّس الأساسي `--profile full-stack up --wait` يقلع سليماً بلا MinIO (2026-10-30). Testcontainers: الصورة من `HRM_MINIO_IMAGE` و`Category=ObjectStorage` مستبعدة بالتزام مؤرخ (PLAN-38، 2026-12-14). جزء الرفع والفحص من AC-QA-34g `Blocked`: رموز realm الأساسي لا تحمل أدواراً، فيُرفض `/api/files` لكل شخصية (← PLAN-41). الإنتاج ← PLAN-36/38.
- **التحقق المطلوب:**
  - موجب: `docker compose … --profile full-stack up --wait` ينجح في CI، و`Qa003MinioFileScanTests` تمر.
  - سالب: صورة غير مثبّتة بالـ digest ← يفشل فحص تثبيت الصور.

#### G-QA-06 — وظيفة القبول في `ci.yml` لا يمكن أن تنجح بنيوياً
- **الدليل:**
  - **realm بلا شخصيات القبول:**
    - [prepare-ci-compose-env.sh:13-15](../scripts/prepare-ci-compose-env.sh) يستدعي `prepare-keycloak-import.ps1` بالملف الافتراضي `Demo` ([prepare-keycloak-import.ps1:9-10,126-131](../scripts/prepare-keycloak-import.ps1))، فلا يُولَّد إلا `hradmin`.
    - `docker/keycloak/realm-acceptance.json` المدموج يحوي 0 مستخدم.
    - شخصيات acceptance-v2 الـ61 لا تُنشأ إلا بالملف `Acceptance` عبر `acceptance-up.ps1` ([acceptance-up.ps1:26,34,43](../scripts/acceptance-up.ps1)).
  - **كلمات المرور غير متاحة:**
    - [AcceptanceHarness.cs:51-68](../src/Tests/HRManagement.AcceptanceTests/AcceptanceHarness.cs) يقرأ متغيراً لكل شخصية (`ACCEPTANCE_*_PASSWORD`) أو `.acceptance/credentials.json`، وإلا `Assert.Fail`. والملف في `.gitignore`.
    - `ACCEPTANCE_PERSONA_PASSWORD: "Accept-Hr123!"` ([ci.yml:468](../.github/workflows/ci.yml)) لا يقرؤه أي اختبار، بل يقرؤه سكربت واحد فقط (`acceptance-file-scan.ps1:8`).
  - **مشروع compose مختلف:**
    - الاختبارات تبحث عن الحاويات بالوسم `com.docker.compose.project=hrm-acceptance` ([AcceptanceMessagingHelper.cs:117-129](../src/Tests/HRManagement.AcceptanceTests/AcceptanceMessagingHelper.cs)، [AcHcm25ProductionApprovalTests.cs:411](../src/Tests/HRManagement.AcceptanceTests/AcHcm25ProductionApprovalTests.cs)).
    - وظيفة CI لا تضبط `COMPOSE_PROJECT_NAME` ولا تحمّل `docker-compose.acceptance.yml` (FileDrop، File.Storage Local، المنافذ 6200/6388/6433).
  - **اختبار ثابت أحمر حتماً:**
    - [AcceptanceV2CatalogTests.cs:83-90](../src/Tests/HRManagement.AcceptanceTests/AcceptanceV2CatalogTests.cs) يتوقع 60 شخصية و54 بموظف و58 مفعّلة، بينما الكتالوج 2+43+8+4+4 = **61** ([AcceptancePersonaCatalog.cs:216-227](../src/Shared/HRManagement.ServiceDefaults/Seeding/AcceptancePersonaCatalog.cs)، `personas.json`: 61، منها 59 مفعّلة) منذ إضافة `central-a2` في PLAN-25.
    - وكذلك [AcceptanceIdentityPopulationTests.cs:8-12,71](../src/Tests/HRManagement.AcceptanceTests/AcceptanceIdentityPopulationTests.cs) (60 و56).
    - هذه `[Fact]` بلا بوابة، ولا تُشغَّل إلا في وظيفة القبول، فلم يرها أحد.
  - **لا حارس تخطٍّ:** [ci.yml:460-473](../.github/workflows/ci.yml) بلا خطوة «صفر تخطٍّ». و`ACCEPTANCE_REQUIRE_ALL` لا يؤثر إلا في الموسوم `[RequiredAcceptance]` ([AcceptanceGate.cs:76-79](../src/Tests/HRManagement.AcceptanceTests/AcceptanceGate.cs))، ولا أحد من AC-01..37 موسوم.
- **الأثر:**
  - كل اختبار قبول حي يفشل عند تسجيل الدخول.
  - وبعد إصلاح ذلك يمرّ كل سيناريو منصّي محجوب كـ Skipped، فيصير «أخضر» بلا فحص.
  - النتيجة: لا يوجد مسار آلي يعيد إنتاج أي دليل في `acceptance-evidence/`.
- **الإصلاح المقترح:**
  - وظيفة القبول تستخدم `acceptance-up.ps1` (realm بملف Acceptance، وكلمات مرور مولّدة داخل الوظيفة، ومشروع `hrm-acceptance`) ثم `acceptance-reset.ps1`.
  - تصحيح الأعداد إلى 61 مع اشتقاقها من الكتالوج بدل الثوابت.
  - خطوة «لا Blocked لسيناريو مطلوب، ولا تخطٍّ غير مبرر» تقرأ `result.json`.
  - رفع `summary.md` المولَّد كأثر.
- **الخطة:** [PLAN-41](acceptance-plans/PLAN-41-qa-ci-acceptance-honest-baseline.md)
- **التحقق المطلوب:**
  - موجب: وظيفة القبول على `main` تنتج `summary.md` بـ`gitSha` = HEAD، وكل AC-HCM `Pass`.
  - سالب: إزالة بذرة سيناريو مطلوب ← الوظيفة حمراء برسالة `Blocked`، لا خضراء.

#### G-QA-07 — الرحلة الشبيهة بالإنتاج في CI معطّلة بـ`\n` حرفي
- **الدليل:**
  - [e2e-journeys.yml:147-149](../.github/workflows/e2e-journeys.yml): الأمر في سطر واحد يحوي `\n` حرفياً (بايتات `\` `n`). في bash يصير `n` وسيطاً، فيُقرأ `docker compose … n -f …` أمراً فرعياً مجهولاً.
  - الخطوات التالية بلا `if: always()` فتُتخطّى. ثم حارس [e2e-journeys.yml:165-176](../.github/workflows/e2e-journeys.yml) يجد ملف trx مفقوداً فيفشل («No prod-like test results»).
  - الوظيفة تعمل على `schedule`/`workflow_dispatch` فقط ([e2e-journeys.yml:4-12](../.github/workflows/e2e-journeys.yml))، فبعد الدفع تفشل كل ليلة.
  - [HCM_001_GAP_STUDY.md:347](HCM_001_GAP_STUDY.md) يقول إن الرحلة «ضمن مرشح رحلات CI … بخطوة صفر تخطٍّ».
- **الأثر:** الدليل الآلي الوحيد على قيم الإنتاج (`IsLabScale=false`، `Shadow`) لا يُعاد إنتاجه بعيداً. والوظيفة الليلية حمراء دائماً، فتُطبّع الفشل.
- **الإصلاح المقترح:** تحويل الخطوة إلى `run: |` متعدد الأسطر مع `\` حقيقي، وفحص YAML بـ`actionlint` في CI (لم يكن مثبتاً في PLAN-33 K5).
- **الخطة:** [PLAN-35](acceptance-plans/PLAN-35-qa-push-remote-ci.md)
- **التحقق المطلوب:**
  - موجب: `gh workflow run e2e-journeys.yml` ← وظيفة `recruitment-journey` تطبع `Passed: 1, skipped: 0` مرتين.
  - سالب: `actionlint` يرفض ملف سير عمل فيه خطأ مماثل.

### 4.2 ثغرات سلامة التحقق والحوكمة (P1)

#### G-QA-08 — أساس القبول المنصّي غير صادق
- **الدليل:**
  - [summary.md:7-20,65](../acceptance-evidence/summary.md): «27 Pass». وللمنصة 10 منها:
    - **AC-34 وAC-36:** سبب النجاح نفسه تعاقدي («Script contract verified; run … for live RTO»، «Run acceptance-pdf-arabic.ps1 for sample PDFs»)، من 2026-08-24 @`9cb3705`. لا `drill-result.json` ولا `samples/`. والشيفرة الحالية ([Ac34…:27-41](../src/Tests/HRManagement.AcceptanceTests/Ac34BackupRestoreLiveTests.cs)، [Ac36…:23-55](../src/Tests/HRManagement.AcceptanceTests/Ac36ArabicPdfLiveTests.cs)) لا تنتج هذا السبب أصلاً.
    - **AC-01..05:** من 2026-08-24 @`9cb3705`، قبل PLAN-25..33. وأدلة AC-01/02 من نسخة اختبار أقدم ([plan33-acceptance-inventory.md](../reports/plan33-acceptance-inventory.md)). ولـ AC-02..05 عقد ناقص.
    - **AC-33:** `Pass` بتاريخ 2026-10-29 دون فحص حقول لقطة الفقد (G-QA-09).
  - المولّد لا يفحص الحداثة: [AcceptanceEvidenceSummary.cs:83,96-102](../src/Tests/HRManagement.AcceptanceTests/AcceptanceEvidenceSummary.cs) يحسب «complete/incomplete» فقط، بلا مقارنة `gitSha` ولا تاريخ ولا ملف تشغيل (lab/prod-like). و«Pass» بعقد ناقص يُعدّ Pass في المجموع.
  - **AC-12 «Blocked» خلل في الاختبار لا في المنتج:**
    - [Ac12PayrollSodTests.cs:34-60](../src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs) ينشئ التشغيل ثم يرسله للمراجعة مباشرة دون استدعاء `runs/{id}/calculate` ([PayrollApiController.cs:330](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)).
    - و`ValidateCanSubmitForReview` يتطلب `Calculated` وأسطراً و`ValidatedAtCalculate` ([PayrollRun.cs:67-70](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)). فالحجب مضمون في كل تشغيل.
  - **AC-16 وAC-30 وAC-37 «NoResult»** منذ 2026-08-24/25: `db-checks ok=false`، و`before.json = -1`، وملف `metadata.json` فقط.
  - **AC-06..10، 13..15، 17..29، 31، 35 (23 سيناريو)** بلا أي دليل.
  - `WriteMergedResult` ([EvidenceRecorder.cs:96-100,206-232](../src/Tests/HRManagement.AcceptanceTests/EvidenceRecorder.cs)) يكتب فوق `status` في `result.json` حين لا يُضبط `ScenarioKey`، وهي حال مجلدات AC-01..37 (مثلاً AC-05: الأسلوب الإيجابي يمحو نتيجة السالب). أما مجلدات AC-HCM فتحتفظ منذ PLAN-33 بخريطة `scenarios` وبأسوأ حالة. واختفاء فشل `Ac27_f_g` في PLAN-32 سببه استعادة مجلد الأدلة بـ git دون إيداعه، لا الكتابة فوقه.
  - الجدول الكامل في §4.5.
- **الأثر:** الأطروحة والوثائق تستشهد بـ«Pass» لـ AC-34 (النسخ والاستعادة) وAC-36 (PDF العربي) ولم يُجرَ أيٌّ منهما. ولا يمكن لأحد أن يعرف من الملخص ما الذي ثبت على شيفرة اليوم. *(صُحّح بعد التحقق المستقل: الكتابة فوق `result.json` تخص المجلدات بلا `ScenarioKey` فقط، وفقد دليل `Ac27_f_g` سببه استعادة git لا الشيفرة)*
- **الإصلاح المقترح:**
  - المولّد يضيف أعمدة `gitSha` وتاريخ وملف تشغيل وحالة «Stale» عندما يختلف `gitSha` عن HEAD أو يتجاوز العمر حداً.
  - لا «Pass» بعقد ناقص.
  - إعادة تصنيف AC-34/36 إلى `NotRun`.
  - إصلاح AC-12 بإضافة خطوة الحساب.
  - خريطة `scenarios` لكل مجلد منصّي كما في AC-HCM.
- **الخطة:** [PLAN-41](acceptance-plans/PLAN-41-qa-ci-acceptance-honest-baseline.md)
- **التحقق المطلوب:**
  - موجب: `summary.md` المولَّد في CI يُظهر لكل AC-01..37 حالة و`gitSha` = HEAD، و0 «Stale».
  - سالب: تعديل `gitSha` في `metadata.json` لمجلد ناجح ← يظهر «Stale» ولا يُعدّ Pass.

#### G-QA-09 — أنماط صامتة وضعيفة باقية في AC-01..37
- **الدليل:**
  - **لا وسم `[RequiredAcceptance]`:** لا يوجد في أي ملف `Ac0*`–`Ac3*` (الوسم في AcHcm21..32 وAcWf23 فقط). فكل `AcceptanceGate.RequireData` في Ac12 وAc20 وAc27 وAc29 وAc30 وAc32 وAc33 وAc37 يتحول إلى Skip صامت في CI (G-QA-06).
  - **تأكيد لا يطابق اسم الاختبار:**
    - [Ac33…:31-41](../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs) يقبل `Forbidden` نجاحاً ولا يفحص حقول اللقطة أبداً، و`WriteAfter` ثابت.
    - [Ac37…:20-35](../src/Tests/HRManagement.AcceptanceTests/Ac37JourneyRebuildLiveTests.cs) لا يفحص حالة بحث br1، ويكتفي بالنص الفرعي `httpEvents`.
    - [Ac06…:51-55](../src/Tests/HRManagement.AcceptanceTests/Ac06TransferAccessTests.cs) لا يُجري نقلاً، والإغلاق مُدّعى في نص `db-checks`.
    - [Ac15…:38-40](../src/Tests/HRManagement.AcceptanceTests/Ac15CorrelationIdHireJourneyTests.cs) ينشر `IEmployeeCreated` اصطناعياً بدل رحلة تعيين.
    - [Ac10…:31,99](../src/Tests/HRManagement.AcceptanceTests/Ac10EnforcePolicyTests.cs) يستخدم `forceSameActorForLab = true` و`v2EffectiveToClosedOnPublish = true` ثابتاً.
    - [Ac09…:74](../src/Tests/HRManagement.AcceptanceTests/Ac09ShadowPolicyTests.cs) يكتفي بالنص الفرعي `"Shadow"`.
  - **تحقق في السكربت فقط** (رمز الخروج): [Ac16:36](../src/Tests/HRManagement.AcceptanceTests/Ac16DuplicateMessageTests.cs)، Ac18، Ac28، وAc20 جزئياً ([Ac20…:40](../src/Tests/HRManagement.AcceptanceTests/Ac20DatabaseIsolationTests.cs)).
  - **db-checks/audit من ثوابت:** [Ac01:62-66](../src/Tests/HRManagement.AcceptanceTests/Ac01CentralTenantIsolationTests.cs)، [Ac02:49-54](../src/Tests/HRManagement.AcceptanceTests/Ac02BranchManagerScopeTests.cs)، Ac03، [Ac04:30](../src/Tests/HRManagement.AcceptanceTests/Ac04CrossTenantGuessTests.cs)، Ac05.
  - **اختبار قديم سيرفضه المنتج:** [Ac17…:33-38,60,74](../src/Tests/HRManagement.AcceptanceTests/Ac17OutboxSurvivesRabbitDownTests.cs) يكشط رابط `Hire` من HTML ويرسل رقماً وطنياً مخالفاً (`"AC17"+timestamp`) وتاريخ بدء ماضياً (`2026-08-01`). وقاعدة PLAN-29 ترفض الرقم المخالف بلا سبب (AC-HCM-29c) — يحتاج تحقق.
  - **غير قابل للإعادة:** Ac07 (`alreadyApplied=false`)، [Ac14:72](../src/Tests/HRManagement.AcceptanceTests/Ac14TemporalOrgStructureTests.cs) (`NotEqual`)، Ac10 (ينشر v3)، Ac25 (يغيّر ملف أجر البذرة).
  - الاختبار المعماري [AcceptanceSkipPatternArchitectureTests.cs:49-52](../src/Tests/HRManagement.UnitTests/Architecture/AcceptanceSkipPatternArchitectureTests.cs) يمنع «Skip/return» فقط، ولا يكشف التأكيد الضعيف ولا الثوابت.
- **الأثر:** حتى بعد إصلاح CI، سينجح عدد من السيناريوهات دون أن يثبت ما يقوله اسمه. والسيناريوهات المحجوبة تمرّ صامتة.
- **الإصلاح المقترح:**
  - وسم AC-01..37 بـ`[RequiredAcceptance]` وفق D-QA-4.
  - إعادة كتابة Ac06 وAc15 وAc17 وAc33 وAc37 لتنفّذ الادعاء.
  - نقل فحوص السكربتات إلى C# (عدد الإيصالات، المستخدمين، اللقطات).
  - إزالة الثوابت من `db-checks`.
  - جعل كل سيناريو قابلاً للإعادة (معرّفات فريدة أو إعادة ضبط).
  - توسيع الاختبار المعماري ليكشف `db-checks` الثابتة و`||` التحصيلية.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)
- **التحقق المطلوب:**
  - موجب: تشغيلان متتاليان لـ AC-01..37 على المكدّس نفسه بالنتيجة نفسها.
  - سالب: حذف أصل البذرة لـ AC-33 ← الوظيفة حمراء (Required)، لا Pass ولا Skip.

#### G-QA-10 — «الرحلات» الـ47 فحوص سطح، وتشغيل Chromium لطلبات API سبب فشل «المتصفحات غير مثبتة»
- **الدليل:** الجدول في §4.6.
  - من 47 حالة `Category=Journey`:
    - **1** رحلة حقيقية (التوظيف).
    - 22 فحص «مجهول ← تحدٍّ».
    - 7 فحوص «لا 5xx».
    - 9 فحوص «سطح مصرّح يُفتح» تقبل أي حالة أقل من 500 (403 و404 تمرّان، [HireSeparationJourneyE2ETests.cs:103,193](../src/Tests/HRManagement.E2E/HireSeparationJourneyE2ETests.cs)، [PayrollLeaveTransferFilesRoleJourneyE2ETests.cs:191](../src/Tests/HRManagement.E2E/PayrollLeaveTransferFilesRoleJourneyE2ETests.cs)).
    - 2 فحوص «دخيل» أحدها تحصيل حاصل (`|| response.Status == 200`، [HireSeparationJourneyE2ETests.cs:57](../src/Tests/HRManagement.E2E/HireSeparationJourneyE2ETests.cs)).
    - 6 تقارن ملف `personas.json` ولا تلمس المكدّس (PAY_F2:60، LEV_F2:85، TRF_F2:105، FIL_F2:145، ROL_F2:166، HTC_H1:30).
  - `EnsureSuccessNot5xx` يقبل 404 نجاحاً ([E2eHarness.cs:84-90](../src/Tests/HRManagement.E2E/E2eHarness.cs)).
  - فحوص API المجهولة تُطلق Chromium لتحصل على `APIRequest` فقط ([Fun004…:21](../src/Tests/HRManagement.E2E/Fun004HealthTrainingComplianceE2ETests.cs)، [HireSeparation…:20,73,129,167](../src/Tests/HRManagement.E2E/HireSeparationJourneyE2ETests.cs) وغيرها)، والمجموعة كلها تعتمد على `PlaywrightFixture` ([PlaywrightFixture.cs:14-20](../src/Tests/HRManagement.E2E/PlaywrightFixture.cs)). لذلك فشلت الحالات الـ46 محلياً بـ`Executable doesn't exist … ms-playwright` ([plan25-final-report.md:157](../reports/plan25-final-report.md)).
  - في CI تُثبَّت المتصفحات ([ci.yml:375-376](../.github/workflows/ci.yml)، [e2e-journeys.yml:50-51](../.github/workflows/e2e-journeys.yml))، لكن CI لم يعمل. فعدد التشغيلات الناجحة الموثّقة للحالات الـ46 **صفر**.
  - [04-P3:35-48](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) يقول QA-004 Done: «assertions على DB/events/audit؛ no silent return».
- **الأثر:** لا يوجد اختبار E2E يثبت رحلة الفصل أو الرواتب أو الإجازة أو النقل أو الملفات. والرقم «47 رحلة» يضلّل قارئ الوثائق.
- **الإصلاح المقترح:**
  - الفحوص المجهولة تستخدم `playwright.APIRequest.NewContextAsync()` بلا متصفح.
  - فحوص `personas.json` تنتقل إلى اختبارات الوحدة.
  - «سطح مصرّح» يفرض 200 ونصاً مرئياً متوقعاً.
  - إعادة تسمية الفئة إلى `Surface`، وحجز `Journey` للرحلات الحقيقية.
  - إضافة 4 رحلات حقيقية بأسلوب `RecruitmentJourneyHarness`: الفصل، تشغيل رواتب ← اعتماد ← ترحيل، النقل، الإجازة.
- **التحقق المطلوب:**
  - موجب: `Category=Journey` يضم رحلات تنتهي بفحص DB/حدث.
  - سالب: سطح يعيد 403 لشخصية مصرّحة ← الفحص يفشل.

#### G-QA-11 — بوابات أمان وحوكمة شكلية
- **الدليل:**
  - **CodeQL:** `upload: false` في [ci.yml:519-522](../.github/workflows/ci.yml) و[codeql.yml:39-42](../.github/workflows/codeql.yml)، فلا تُرفع النتائج ولا تُفشل الفحص. «CodeQL» المطلوب أخضر دائماً.
  - **تكرار:** CodeQL وGitleaks موجودان في `ci.yml` ([:500-536](../.github/workflows/ci.yml)) وفي ملفين مستقلين، فيُبنى الحل ثلاث مرات في كل دفع.
  - **الفحوص المطلوبة** ([branch-protection-main.json:5-9](scm-governance/branch-protection-main.json)) لا تشمل `Acceptance (PLAN-00)` ولا `E2E`.
  - **صمت عند 403:** [sync-dependabot-alerts-to-issues.sh:12](../scripts/sync-dependabot-alerts-to-issues.sh) (`|| echo '[]'`) يحوّل الرفض إلى «0 alerts». و`GITHUB_TOKEN` لا يقرأ تنبيهات Dependabot (يحتاج تحقق).
  - **gateway-ci:** يستعيد مشروعين فقط ([gateway-ci.yml:38-39](../.github/workflows/gateway-ci.yml)) ثم يشغّل `dotnet format HRManagement.slnx --no-restore` (:42) على الحل كله، و`e2e-gateway` يشغّل `docker compose up -d --wait` بلا `--profile full-stack` (:94)، فلا تقلع البوابة (يحتاج تحقق).
  - **mutation:** يستخدم `dotnet-version: "10.0.x"` و`dotnet new tool-manifest --force` ([mutation.yml:19,26](../.github/workflows/mutation.yml)) خلافاً لـ`global.json`.
- **الأثر:** ثلاث بوابات من بوابات SUP-001 لا تمنع شيئاً، والمستودع عام (رفع CodeQL مجاني).
- **الإصلاح المقترح:**
  - `upload: true` مع قاعدة «code scanning results» في الحماية.
  - حذف التكرار (إبقاء الملفات المستقلة أو وظائف `ci.yml`، لا الاثنين).
  - إضافة `Acceptance` إلى الفحوص المطلوبة بعد QA-C (D-QA-2).
  - سكربت Dependabot يفشل عند 403.
  - إصلاح `gateway-ci` و`mutation`.
- **الخطة:** [PLAN-35](acceptance-plans/PLAN-35-qa-push-remote-ci.md)
- **الحالة:** ✅ عولجت في PLAN-35 (`e3ae8c3`، `5d0b9b6`) — وظيفة `CodeQL` في `ci.yml` تحلّل ثم تمرّ على `scripts/ci/codeql-sarif-gate.ps1`: تفشل على نتيجة `error` أو `security-severity` ≥ 7.0 خارج [`docs/qa/codeql-baseline.json`](qa/codeql-baseline.json) أو ببند منتهٍ أو غير مفتوح (`CodeqlSarifGateTests`). كشف أول تشغيل حقيقي 108 تنبيهات قائمة سُجّلت بنداً بنداً (القاعدة + المسار + بصمة الموقع) بمالك ومراجعة حتى 2027-01-28 (`QA-CODEQL-BASELINE`) — معالجتها دَين أمني لخطة لاحقة. gitleaks صار بالقواعد الافتراضية (الملف القديم بلا قواعد كان ينجح دائماً) واستثناءين ضيقين بمسار ونص؛ حُذف `gitleaks.yml` و`gateway-ci.yml` المكرران؛ سكربت Dependabot يفشل برسالة صريحة عند الرفض. الدليل: AC-QA-35f وAC-QA-35g.
- **التحقق المطلوب:**
  - موجب: تنبيه CodeQL جديد عالي الشدة في PR ← الفحص أحمر.
  - سالب: سكربت Dependabot بلا صلاحية ← خطوة حمراء برسالة صريحة.

#### G-QA-12 — بيئة المطوّر ليست بيئة CI، و«الإخفاقات المعروفة» تطبّع الفشل الحقيقي
- **الدليل:**
  - 11 من الـ16 بيئية: ثقافة ar-SA بتقويم أم القرى، و`ExecutionPolicy` على Windows، و`global.json` معدَّل محلياً غير مودَع ([plan26-baseline.md](../reports/plan26-baseline.md)، [plan33-baseline.md](../reports/plan33-baseline.md)).
  - التقارير صنّفت BOLA وTen002 وSyr001 «environment-only» ([plan31-baseline.md](../reports/plan31-baseline.md)، [plan32-final-report.md:94](../reports/plan32-final-report.md))، وهي حقيقية (G-QA-03).
  - لا Python ولا `actionlint` ولا متصفحات Playwright على جهاز التنفيذ ([plan33-baseline.md](../reports/plan33-baseline.md)).
  - لا ضبط لثقافة الاختبارات في أي مشروع اختبار. و`Hcm029CandidateAcceptedV4Tests` متقلب بلا حجر رغم وجود [QA_FLAKY_TEST_POLICY.md](QA_FLAKY_TEST_POLICY.md).
- **الأثر:** كل خطة تقبل «لا جديد خارج القائمة» معياراً، فتتراكم الإخفاقات الحقيقية تحت وصف «بيئي».
- **الإصلاح المقترح:**
  - حاوية تطوير موحّدة (Linux وpwsh وPython وactionlint ومتصفحات Playwright).
  - `AssemblyFixture` يضبط `CultureInfo` للاختبارات (ar-SY بتقويم ميلادي كما في الإنتاج، [HrLocalizationExtensions.cs:17](../src/Shared/HRManagement.ServiceDefaults/Localization/HrLocalizationExtensions.cs)).
  - اختبارات السكربتات تمرّر `-ExecutionPolicy Bypass`.
  - إلغاء مفهوم «القائمة المعروفة» واستبداله بقائمة مجمّدة لكل منها مالك وتاريخ.
- **الخطة:** [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md)
- **الحالة:** ✅ عولجت في PLAN-34 (`c7a675c`، `c6086f2`) — `scripts/ci/steps.json` مصدر واحد لخطوات `build-and-test` (87 خطوة) و`CiStepsParityTests` يحرسه؛ `scripts/ci-local.ps1` يشغّلها كلها على نسخة نظيفة من HEAD في حاوية Linux (SDK 10.0.400، python3، actionlint): 85 ناجحة، 0 فاشلة، 2 «not-run» صراحة (Testcontainers وبوابة التغطية؛ شُغّلتا على Windows للـ HEAD نفسه، القاعدة P-3). ثقافة الاختبارات ar-SY بـ `[ModuleInitializer]` و`TestCultureGuardTests`؛ اختبارات السكربتات عبر `RepoScriptRunner` و`ScriptInvocationArchitectureTests`؛ SAN-001 يقرأ `global.json` المودَع. الدليل: `artifacts/ci-local/head/summary.json` (غير مودَع) ومعطياته في `acceptance-evidence/AC-QA-34`، AC-QA-34a/34h.
- **التحقق المطلوب:**
  - موجب: التشغيل الكامل على Windows/ar-SA وعلى Linux يعطي النتيجة نفسها.
  - سالب: حذف ضبط الثقافة ← اختبار حارس يفشل.

#### G-QA-13 — ادعاءات التوثيق لا تطابق الأدلة
- **الدليل (عيّنة):**

| الوثيقة | الادعاء | الواقع |
|---------|---------|--------|
| [04-P3:35-48](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) QA-004 | Done؛ «assertions على DB/events/audit؛ no silent return» | 46 من 47 فحوص سطح؛ صفر تشغيل (G-QA-10) |
| [04-P3:20-33](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) QA-003 | Done؛ «لا SQLite/InMemory كدليل وحيد لمسار إنتاجي» | 47 ملف تكامل على SQLite، منها الرواتب وسلسلة العقد والتعيين (G-QA-14) |
| [04-P3:5-18](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) QA-002 | Done؛ بوابة 60% وmutation | آخر قياس 2026-07-28؛ لا تقرير mutation؛ سجل العدّ [test-count-registry.json](qa/test-count-registry.json) من 2026-08-01 (2,211 أسلوباً مقابل ~4,134 اليوم) |
| [FUN_001_VERIFICATION.md](FUN_001_VERIFICATION.md) | «Timeout/fault/compensate/resume — OK»، FUN-001 = Done | `EstablishmentHireIntegrationTests` (التعويض والفشل) حمراء (G-QA-03) |
| [FUN_002_VERIFICATION.md](FUN_002_VERIFICATION.md) | «Exceptions + Fun002* tests — OK» | `Fun002SeparationRecordIntegrationTests:59,75` و4 من `ContractLifecycle` حمراء |
| [FUN_001_UAT_PACK.md:3](FUN_001_UAT_PACK.md) و73 وثيقة أخرى | «ReadyForSign-off» | قوالب من 4 أسطر، عمود التوقيع `____`، بلا رابط تشغيل |
| [05-TRACEABILITY](completion-backlog/05-TRACEABILITY.md) | OPS-003 ✓ (نسخ/PITR)، INT-001/002 ✓، PAY-003 ✓، QA-004 ✓ | AC-34 تعاقدي؛ AC-16 NoResult وAC-17/18 لم تُشغَّل؛ AC-21/22 لم تُشغَّل وAC-12 محجوب |
| [VERIFICATION-COMPLETION.md](acceptance-plans/execution/VERIFICATION-COMPLETION.md) | وظيفة `acceptance-live` تشغّل `acceptance-up.ps1`، و`ACCEPTANCE_PERSONA_PASSWORD` لكلمات الشخصيات | الوظيفة اسمها `acceptance` ولا تستدعي السكربت؛ والمتغير غير مقروء (G-QA-06) |
| [CI_AND_TESTING.md:146](CI_AND_TESTING.md) | «E2E Playwright smoke — اختياري (وسم e2e)» | وظيفة `E2E` إلزامية في `ci.yml:336` |
| [GITHUB_CI_GOVERNANCE_RUNBOOK.md:8-13](GITHUB_CI_GOVERNANCE_RUNBOOK.md) | `E2E` فحص مطلوب | غير موجود في ملف الحماية |
| [QA_E2E_VERIFICATION.md](QA_E2E_VERIFICATION.md) | أمر محلي بلا `--profile full-stack` | لا تقلع الخدمات (أصلحه PLAN-33 في سير العمل فقط) |

- **الأثر:** صاحب العمل ولجنة الأطروحة يقرآن «Done/Ready» لمسارات لم تُثبت. وهذا النمط نفسه الذي وجدته دراسة التوظيف.
- **الإصلاح المقترح:**
  - مفردات حالة موحّدة: `Implemented` / `Verified (run link)` / `Signed`.
  - حزم UAT تصبح «Template» حتى التوقيع.
  - كل «Done» يحمل رابط تشغيل CI أو مجلد أدلة بـ`gitSha`.
  - مدقّق يرفض «Done» بلا رابط.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)
- **التحقق المطلوب:**
  - موجب: المدقّق ينجح على الوثائق المصحّحة.
  - سالب: إضافة «Done» بلا رابط ← المدقّق يفشل.

### 4.3 قدرات ناقصة (P2)

#### G-QA-14 — هندسة الاختبارات: دليل SQLite فقط، وحاويات DI يدوية، ولا اختبار إقلاع
- **الدليل:**
  - 24 ملف تكامل يبني `new ServiceCollection()` يدوياً، و47 تستخدم SQLite.
  - `WebApplicationFactory` موجود لـ HR.Core والبوابة وFile.Storage فقط، لا للعقود ولا الرواتب ولا التنقلات.
  - تعليق [EndToEndReleaseWorkflowSmokeIntegrationTests.cs:29-33](../src/Tests/HRManagement.IntegrationTests/EndToEndReleaseWorkflowSmokeIntegrationTests.cs): «SQLite RowVersion limits multi-step post here».
  - لا اختبار يقلع أي خدمة بإعدادات `Production`/`Staging` (G-QA-02).
- **الأثر:** كل اعتماد جديد في الإنتاج يكسر الاختبارات بصمت حتى التشغيل الكامل (G-QA-03). ومسارات المال تُثبت على محرك غير محرك الإنتاج.
- **الإصلاح المقترح:**
  - اختبار «جذر التركيب» لكل خدمة (`WebApplicationFactory` + `ValidateOnBuild` بإعدادات Testing ثم Staging).
  - نقل اختبارات دورة الرواتب وسلسلة العقد إلى Testcontainers PostgreSQL.
  - مضيفات اختبار مشتركة.
- **التحقق المطلوب:**
  - موجب: اختبار جذر التركيب أخضر لـ15 خدمة.
  - سالب: إزالة تسجيل من `Program.cs` ← الاختبار يفشل.

### 4.4 تحسينات (P3)

#### G-QA-15 — ضجيج المجدولات وصيانة سير العمل
- **الدليل:**
  - بعد الدفع ستعمل `e2e-journeys` كل ليلة ([:6](../.github/workflows/e2e-journeys.yml)) و`advisory-to-issue` يومياً و`mutation` وCodeQL أسبوعياً، وكلها بلا إصلاحات G-QA-05/07/11.
  - `gateway-ci` يستعيد بلا `--locked-mode`.
  - `AcceptanceCoverageTests.Evidence_recorder_supports_eight_file_contract` تحصيل حاصل ([plan33-acceptance-inventory.md](../reports/plan33-acceptance-inventory.md)).
- **الإصلاح المقترح:** تعطيل المجدولات حتى يخضرّ QA-B، ثم إعادتها مع تنبيه فشل. إضافة `--locked-mode` لكل استعادة.
- **الخطة:** [PLAN-35](acceptance-plans/PLAN-35-qa-push-remote-ci.md)
- **الحالة:** ⏳ جزئياً في PLAN-35 (`e3ae8c3`) — كل وظيفة مجدولة (عدا `dated-obligations`) لا تعمل بالجدول إلا حين `vars.ENABLE_SCHEDULED == 'true'`، وضُبط بعد الاخضرار في 2026-10-30 ثم أُعيد إلى `false` في 2026-10-31 بقرار المالك: المستودع خاص على الخطة المجانية (2,000 دقيقة Actions شهرياً) وتشغيل `e2e-journeys` الليلي وحده نحو 60 دقيقة، فصار أسبوعياً (الاثنين 04:00 UTC)؛ `codeql.yml` أسبوعي ويدوي فقط؛ `dated-obligations` أخضر يدوياً على `main` (التشغيل 41140248696). لم يتحقق «ثلاث ليالٍ متتالية خضراء»: يصير ثلاثة تشغيلات أسبوعية خضراء متتالية بعد أن يعيد المالك `ENABLE_SCHEDULED` (PLAN-36).
- **التحقق المطلوب:** ثلاث ليالٍ متتالية خضراء.

### 4.5 جدول تغطية القبول المنصّي AC-01..AC-37

المفتاح:
- «Req»: هل يحمل `[RequiredAcceptance]`. الجواب «لا» للجميع، فكل `RequireData` يصير Skip صامتاً في CI.
- «الحالة»: من [summary.md](../acceptance-evidence/summary.md) بتاريخ 2026-10-29.
- مسارات الاختبارات تحت `src/Tests/HRManagement.AcceptanceTests/`.

| AC | الخطة | الاختبار | Req | نمط صامت/ضعيف حالي | الحالة والدليل | لماذا لم يُثبت | ما يحتاجه |
|----|-------|----------|-----|--------------------|----------------|----------------|-----------|
| 01 | PLAN-01 | [Ac01…:12](../src/Tests/HRManagement.AcceptanceTests/Ac01CentralTenantIsolationTests.cs) | لا | `db-checks` ثابتة (:62-66) | Pass، كامل، 2026-08-24 @9cb3705، نسخة اختبار أقدم | قديم | `central-a`؛ بذرة v2 (EmpBr1/Br2/B)؛ إعادة على HEAD |
| 02 | PLAN-01 | [Ac02…:12](../src/Tests/HRManagement.AcceptanceTests/Ac02BranchManagerScopeTests.cs) | لا | يقبل 403 أو 404 (:46)؛ `db-checks` ثابتة | Pass، ناقص (steps/before/audit)، 2026-08-24 | قديم | `br1-mgr` |
| 03 | PLAN-01 | [Ac03…:12](../src/Tests/HRManagement.AcceptanceTests/Ac03SiblingAccessDeniedTests.cs) | لا | `audit.json` من ثوابت؛ 403 أو 404 | Pass، ناقص، 2026-08-24 | قديم | `br1-mgr` |
| 04 | PLAN-01 | [Ac04…:12](../src/Tests/HRManagement.AcceptanceTests/Ac04CrossTenantGuessTests.cs) | لا | «layers» نص ثابت (:30) | Pass، ناقص، 2026-08-24 | قديم | `central-a`؛ موظف الجهة B |
| 05 | PLAN-01 | [Ac05…:12,52](../src/Tests/HRManagement.AcceptanceTests/Ac05PlatformEnterTenantTests.cs) | لا | لا استعلام بنطاق الجهة بعد الدخول (:38-46)؛ السالب يقبل 401/403/405 | Pass، ناقص، 2026-08-24 | قديم | `platform-admin`، `br1-mgr` |
| 06 | PLAN-01 | [Ac06…:11](../src/Tests/HRManagement.AcceptanceTests/Ac06TransferAccessTests.cs) | لا | لا نقل فعلي؛ الإغلاق نص (:51-55) | لا دليل | لم يُشغَّل؛ الاختبار لا يغطي الادعاء | `br1-mgr`، `br2-mgr`، `a-movements`؛ نقل BR1←BR2 بمعتمد مختلف وانتظار `ITransferExecuted` |
| 07 | PLAN-08 | [Ac07…:12](../src/Tests/HRManagement.AcceptanceTests/Ac07AgencyOnboardingTests.cs) | لا | الشجرة تُعدّ من الملف؛ `alreadyApplied=false` يكسر الإعادة | لا دليل | لم يُشغَّل؛ غير قابل للإعادة | `platform-admin`؛ `seed-packs/examples/agency-acceptance-test.json`؛ إعادة ضبط |
| 08 | PLAN-08 | [Ac08…:14](../src/Tests/HRManagement.AcceptanceTests/Ac08QuarantineReconciliationTests.cs) | لا | قبل/بعد المصالحة غير مفحوصين | لا دليل | لم يُشغَّل | `central-a`، `platform-admin`، شخصية `negative-quarantined`؛ `acceptance-quarantine-flow.ps1` |
| 09 | PLAN-02 | [Ac09…:14](../src/Tests/HRManagement.AcceptanceTests/Ac09ShadowPolicyTests.cs) | لا | النص الفرعي `"Shadow"` (:74)؛ قيم منطقية ثابتة | لا دليل | لم يُشغَّل | `central-a`؛ PolicyVersion2؛ نطاق بوضع Shadow (→ POL)؛ ملف شبيه بالإنتاج (D-QA-6) |
| 10 | PLAN-02 | [Ac10…:14](../src/Tests/HRManagement.AcceptanceTests/Ac10EnforcePolicyTests.cs) | لا | `forceSameActorForLab=true` (:31) يتجاوز فصل المهام؛ `v2EffectiveToClosedOnPublish` ثابت (:99) | لا دليل | لم يُشغَّل؛ يغيّر الحالة | `central-a` + معتمد ثانٍ؛ مسودة v3؛ إعادة ضبط |
| 11 | PLAN-03 | [Ac11…:14](../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs) | لا | السجل يُلتقط ولا يُفحص | **Pass**، كامل، 2026-10-29 (lab) | — | `b-branch-manager`، `b-approver`؛ إعادة على prod-like |
| 12 | PLAN-03 | [Ac12…:12](../src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs) | لا | محجوب دائماً: لا خطوة حساب قبل المراجعة (:54-60) | **Blocked** 2026-10-29 | خلل تصميم الاختبار (G-QA-08) | `payroll-processor`، `payroll-approver`؛ إعدادات رواتب منشورة وملفات أجر؛ `POST runs/{id}/calculate` |
| 13 | PLAN-04 | [Ac13…:14](../src/Tests/HRManagement.AcceptanceTests/Ac13ParallelCodeGenerationTests.cs) | لا | — | لا دليل | لم يُشغَّل | `central-a`؛ خدمة توليد الرموز؛ 120 طلباً متوازياً |
| 14 | PLAN-04 | [Ac14…:12](../src/Tests/HRManagement.AcceptanceTests/Ac14TemporalOrgStructureTests.cs) | لا | `NotEqual` (:72) يفشل في التشغيل الثاني | لا دليل | لم يُشغَّل؛ يغيّر الوحدة | `central-a`؛ اسم وحدة فريد لكل تشغيل |
| 15 | PLAN-05 | [Ac15…:11](../src/Tests/HRManagement.AcceptanceTests/Ac15CorrelationIdHireJourneyTests.cs) | لا | لا تعيين: حدث اصطناعي (:38-40) | لا دليل | لم يُشغَّل؛ لا يثبت الادعاء | `central-a` + رحلة تعيين حقيقية (`RecruitmentJourneyHarness`)؛ `acceptance-correlation-trace.ps1`؛ سجلات الخدمات |
| 16 | PLAN-05 | [Ac16…:9](../src/Tests/HRManagement.AcceptanceTests/Ac16DuplicateMessageTests.cs) | لا | رمز خروج السكربت فقط (:36)؛ تأخير ثابت | **NoResult** (`db-checks ok=false`، 2026-08-25) | فشل ولم يُعَد | `central-a`؛ إيصالات Inbox في Auth.Directory؛ فحص العدد في C# |
| 17 | PLAN-05 | [Ac17…:11](../src/Tests/HRManagement.AcceptanceTests/Ac17OutboxSurvivesRabbitDownTests.cs) | لا | كشط HTML (:33-38)؛ رقم وطني مخالف (:60) وتاريخ ماضٍ (:74) سيرفضهما PLAN-29 | لا دليل | لم يُشغَّل؛ متقادم | `central-a`؛ طلب `OfferAccepted` مبذور؛ صلاحية `docker stop/start rabbitmq`؛ psql |
| 18 | PLAN-05 | [Ac18…:9](../src/Tests/HRManagement.AcceptanceTests/Ac18ConsumerCatchUpTests.cs) | لا | رمز خروج السكربت فقط؛ تأخير ثابت | لا دليل | لم يُشغَّل | `central-a`؛ `docker stop` لمستهلك؛ `acceptance-consumer-catchup.ps1` |
| 19 | PLAN-09 | [Ac19…:11](../src/Tests/HRManagement.AcceptanceTests/Ac19AnalyticsReconciliationTests.cs) | لا | التحقق في السكربت | لا دليل | لم يُشغَّل | `a-analytics`؛ `acceptance-analytics-reconcile.ps1` |
| 20 | PLAN-09 | [Ac20…:10](../src/Tests/HRManagement.AcceptanceTests/Ac20DatabaseIsolationTests.cs) | لا | ينجح على رمز الخروج إذا حُجبت الملفات (Skip صامت) | لا دليل (خطوة CI [ci.yml:437-453](../.github/workflows/ci.yml) لم تعمل) | لم يُشغَّل | `platform-admin`؛ أدوار PostgreSQL؛ `acceptance-verify-db-isolation.ps1` |
| 21 | PLAN-06 | [Ac21…:10](../src/Tests/HRManagement.AcceptanceTests/Ac21PayrollDeterminismTests.cs) | لا | يضبط متغيرات العملية العامة | لا دليل | لم يُشغَّل | `payroll-processor`؛ إعدادات منشورة؛ فترة 2027-01 |
| 22 | PLAN-06 | [Ac22…:10](../src/Tests/HRManagement.AcceptanceTests/Ac22PayrollResumeAfterKillTests.cs) | لا | `RUN_CHAOS` يُسجَّل ولا يحرس؛ `result.json` مشترك مع السكربت | لا دليل | لم يُشغَّل | chaos: قتل `payroll-web` أثناء الحساب؛ `payroll-processor` |
| 23 | PLAN-10 | [Ac23…:12](../src/Tests/HRManagement.AcceptanceTests/Ac23PayrollFreezeTests.cs) | لا | حالة تعديل الملف غير مفحوصة | لا دليل | لم يُشغَّل | `payroll-processor`؛ فترتا 2027-02/03 |
| 24 | PLAN-10 | [Ac24…:12](../src/Tests/HRManagement.AcceptanceTests/Ac24PartialDisbursementTests.cs) | لا | — | لا دليل | لم يُشغَّل | المعالج والمعتمد؛ فترة 2027-04؛ قناة بنك وهمية |
| 25 | PLAN-10 | [Ac25…:13](../src/Tests/HRManagement.AcceptanceTests/Ac25FraudHoldTests.cs) | لا | يغيّر ملف أجر البذرة | لا دليل | لم يُشغَّل | المعالج، المعتمد، `a-payroll-poster`، `auditor`؛ `acceptance-payroll-audit-fixture.ps1` |
| 26 | PLAN-07 | [Ac26…:14](../src/Tests/HRManagement.AcceptanceTests/Ac26NationalIdPrivacyTests.cs) | لا | «التفاصيل» تكرر بحث التجزئة | لا دليل (`ac026-scan-report.json` 2026-08-25 في الجذر، خارج العقد) | لم يُشغَّل | `central-a`، `br1-mgr`؛ `ac026-scan-pii.ps1` |
| 27 | PLAN-11 | [Ac27…:15](../src/Tests/HRManagement.AcceptanceTests/Ac27DsarExportTests.cs) | لا | التحليل قبل البوابة (:34-35) | لا دليل | لم يُشغَّل | `a-compliance`؛ واجهة DSAR |
| 28 | PLAN-11 | [Ac28…:14](../src/Tests/HRManagement.AcceptanceTests/Ac28FileScanTests.cs) | لا | رمز خروج السكربت فقط | لا دليل | لم يُشغَّل | `a-file-custodian`؛ File.Storage + ClamAV + تخزين (G-QA-05) |
| 29 | PLAN-11 | [Ac29…:14](../src/Tests/HRManagement.AcceptanceTests/Ac29FileAccessExpiryTests.cs) | لا | الانتهاء نفسه لا يُختبر (رابط لدقيقة دون انتظار) | لا دليل | لم يُشغَّل | `a-file-custodian`، `a-archive`؛ File.Storage |
| 30 | PLAN-11 | [Ac30…:13](../src/Tests/HRManagement.AcceptanceTests/Ac30S2sFileReferenceRejectionTests.cs) | لا | «لا أثر جانبي» في السكربت | **NoResult** (`before.json = -1`، 2026-08-25) | ناقص | `a-file-custodian`؛ رمز خدمة دخيل؛ `acceptance-s2s-negative.ps1` |
| 31 | PLAN-07 | [Ac31…:13](../src/Tests/HRManagement.AcceptanceTests/Ac31MassAssignmentTests.cs) | لا | كشط HTML؛ `CreatedAt`/`ApprovedBy` المزوّرة لا تُفحص (:52-53) | لا دليل | لم يُشغَّل | `a-logistics-maker`، `b-logistics`؛ psql |
| 32 | PLAN-12 | [Ac32…:11](../src/Tests/HRManagement.AcceptanceTests/Ac32LogisticsScopeLiveTests.cs) | لا | محكم الآن | **Pass**، كامل، 2026-10-29 | — | `br1/br2-mgr`، `a-logistics-maker`؛ أصل البذرة |
| 33 | PLAN-12 | [Ac33…:11](../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs) | لا | يقبل 403 (:31)؛ لا يفحص حقول اللقطة؛ `WriteAfter` ثابت (:36-41) | Pass، 2026-10-29 | Pass لا يثبت الادعاء | `br1-mgr`؛ `AssetLossReport` مبذور بلقطتي قبل/بعد |
| 34 | PLAN-13 | [Ac34…:9](../src/Tests/HRManagement.AcceptanceTests/Ac34BackupRestoreLiveTests.cs) | لا | الشيفرة محكمة؛ الدليل تعاقدي قديم | «Pass» تعاقدي، 2026-08-24، بلا `drill-result.json` | لم يُجرَ تمرين حي | `pg_dump` لـ16 قاعدة؛ Docker؛ وظيفة ops-drill (D-QA-4) |
| 35 | PLAN-13 | [Ac35…:11](../src/Tests/HRManagement.AcceptanceTests/Ac35ScopedLookupPerfLiveTests.cs) | لا | لا SLO (`sloClaimed=false`) | لا دليل | لم يُشغَّل | `br1-mgr`؛ `acceptance-perf-run.ps1`؛ قرار SLO |
| 36 | PLAN-13 | [Ac36…:9](../src/Tests/HRManagement.AcceptanceTests/Ac36ArabicPdfLiveTests.cs) | لا | يفحص الترويسة والحجم فقط | «Pass» تعاقدي، 2026-08-24، بلا `samples/` | لم يُجرَ | `acceptance-pdf-arabic.ps1`؛ توقيع مراجع بشري على [AC-36-arabic-pdf-visual.md](acceptance-plans/checklists/AC-36-arabic-pdf-visual.md) |
| 37 | PLAN-13 | [Ac37…:11](../src/Tests/HRManagement.AcceptanceTests/Ac37JourneyRebuildLiveTests.cs) | لا | بحث br1 غير مفحوص (:21-22)؛ نص فرعي (:35) | **NoResult** (`metadata.json` فقط، 2026-08-24) | ناقص | `platform-admin`، `br1-mgr`؛ خط تدقيق دافئ؛ `acceptance-audit-journey.ps1` |

**الحصيلة:**
- أُثبت على شيفرة اليوم بلا تحفظ: **AC-11 وAC-32** فقط.
- قديمة تحتاج إعادة: AC-01..05.
- «Pass» لا يثبت: AC-33 وAC-34 وAC-36.
- محجوب بخلل اختبار: AC-12.
- ناقصة: AC-16 وAC-30 وAC-37.
- لم يُشغَّل: 23 سيناريو.
- خارج AC-01..37: AC-EST-13..18 بلا دليل، وAC-EST-24e محجوب (لا تغيير ملاك معلّق في صندوق العمل).

### 4.6 رحلات Playwright (`Category=Journey`، 47 حالة)

الربط في CI:
- `ci.yml` وظيفة `E2E`: كل ما عدا `JourneyProdLike` و`RecruitmentToHireJourney` ([ci.yml:384-389](../.github/workflows/ci.yml))، بعد تثبيت المتصفحات، مع حارس صفر تخطٍّ.
- `e2e-journeys.yml` وظيفة `journeys` (ليلية): `Category=Journey` عدا التوظيف ([:63](../.github/workflows/e2e-journeys.yml)).
- وظيفة `recruitment-journey`: التوظيف على مكدّس القبول بلا متصفح، فلا تحتاج تثبيتاً.
- عدد التشغيلات الناجحة الموثّقة لكل الحالات عدا التوظيف: **صفر**.

| المجموعة | الحالات | ما تفحصه فعلاً | متصفح؟ |
|----------|---------|----------------|--------|
| مجهول ← تحدٍّ (22) | HTC-F1..F3 ([Fun004:14-17](../src/Tests/HRManagement.E2E/Fun004HealthTrainingComplianceE2ETests.cs))؛ ID-F1..F2، AL-F1، IT-F1..F3 ([Fun005:14-20](../src/Tests/HRManagement.E2E/Fun005IdentityAssetsItE2ETests.cs))؛ AR-F1، AN-F1..F2 ([Fun006](../src/Tests/HRManagement.E2E/Fun006ArchiveAnalyticsE2ETests.cs))؛ PAY/LEV/TRF/FIL/ROL-F1 ([:14-19](../src/Tests/HRManagement.E2E/PayrollLeaveTransferFilesRoleJourneyE2ETests.cs))؛ HIR-F1، SEP-F1؛ REL-H1/F1/F2 ([Rel001:15-18](../src/Tests/HRManagement.E2E/Rel001StagingRehearsalE2ETests.cs)؛ REL-H1 يقبل أقل من 500) | 401/403/302 لطلب مجهول — ضابط حقيقي لكنه ضيق | يُطلق Chromium بلا داعٍ |
| لا 5xx (7) | PAY/LEV/TRF/FIL/ROL-F3، HIR-F3، SEP-F3 | الحالة أقل من 500 فقط | يُطلق Chromium |
| سطح مصرّح (9) | HIR-H1، SEP-H1، PAY-H1، LEV-H1، TRF-H1..H3، FIL-H1، ROL-H1 | تسجيل دخول ثم حالة أقل من 500 (403 و404 تمرّان) | نعم |
| دخيل (2) | HIR-F2 (تحصيل حاصل :57)، SEP-F2 (أقل من 500 فقط) | لا يثبت الرفض | نعم |
| ملف شخصيات فقط (6) | PAY-F2، LEV-F2، TRF-F2، FIL-F2، ROL-F2، HTC-H1 | مقارنة `personas.json`؛ لا مكدّس | لا (لكنها في مجموعة `PlaywrightFixture`) |
| رحلة حقيقية (1) | `Recruitment_requisition_to_hire_journey_on_the_acceptance_profile` | من الطلب حتى معاملة Appointment مع فحص DB | لا (HTTP) |
| خارج `Journey` | A11y ×2، Gateway ×7، `E2eSeedPackTests` ×2، `JourneyProdLike` ×1 | axe، دخول، صحة | متصفح لـ A11y والدخول |

### 4.7 تقاطعات مع الدراسات الأخرى

- → **IAM:** 12 إجراء `Index` بنطاق موظف بلا حارس (HR.Contracts ×6، Payroll ×6) يُسقط `BolaArchitectureTests` — [SeparationRecordsController.cs:26](../src/Services/HRContracts/HR.Contracts.Web/Controllers/SeparationRecordsController.cs)، [PaymentHoldsController.cs:27](../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs) وبقية القائمة في G-QA-03.
- → **IAM (TEN):** `IgnoreQueryFilters` خارج المسار المعتمد — [BenefitPlanSeeder.cs:26](../src/Services/HRCore/HR.Core.Infrastructure/Data/BenefitPlanSeeder.cs)، [AcceptanceCandidateExperiencePatch.cs:42](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceCandidateExperiencePatch.cs).
- → **OPS:** البوابة وHR.Core والرواتب لا تقلع في Production/Staging منذ 2026-09-15 بسبب استثناء STR-005 منتهٍ — [ProgramGovernanceGuard.cs:97-106](../src/Shared/HRManagement.ServiceDefaults/ProgramGovernance/ProgramGovernanceGuard.cs)، [governance-matrix.json:154-157](str-governance/governance-matrix.json). والقنبلة التالية في 2027-01-31 (STR-002).
- → **OPS:** صورة MinIO في الإنتاج غير قابلة للسحب — [statefulset.yaml:74](../deploy/k8s/data/base/minio/statefulset.yaml)، [docker-compose.production.yml:88](../docker-compose.production.yml).
- → **PAY:** فشل تكامل دورة الرواتب وطبقة الدفعات غير محسوم السبب — [PayrollRunLifecycleIntegrationTests.cs:60](../src/Tests/HRManagement.IntegrationTests/PayrollRunLifecycleIntegrationTests.cs)، [Perf001PayrollBatchTests.cs:60](../src/Tests/HRManagement.IntegrationTests/Perf001PayrollBatchTests.cs). ولا يوجد رمز تزامن على كيانات الرواتب في SQLite (`ConfigureOptimisticConcurrency` يعمل على Npgsql فقط، [ConcurrencyExtensions.cs:21-22](../src/Shared/HRManagement.ServiceDefaults/ConcurrencyExtensions.cs)).
- → **CON (العقود):** نشر الفصل يتطلب استعلاماً حياً من HR.Core. إن تعطلت الخدمة يرمي `EnsureSuccessStatusCode`، وإن لم يوجد نظام خدمة يُرفض النشر — [HrCoreActiveRegimeClient.cs:32-33](../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreActiveRegimeClient.cs)، [SeparationRegimeValidationService.cs:14-18](../src/Services/HRContracts/HR.Contracts.Application/Services/SeparationRegimeValidationService.cs).
- → **MOV:** اعتماد النقل يستدعي HR.Core دون معالجة الفشل، فتعطّل HR.Core ← 500 عند الاعتماد — [TransferRequestsController.cs:453-454](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs).
- → **TRN:** رقم الشهادة يتبع ثقافة الخيط (هجري على ar-SA) — [LearningCompletionRules.cs:28](../src/Services/HRTraining/HR.Training.Domain/Services/LearningCompletionRules.cs).
- → **POL:** AC-09/10 تحتاج نطاقاً بوضع Shadow/Enforce، والإنتاج لا يضبط إلا `Core.Recruitment=Shadow` — [Ac09ShadowPolicyTests.cs:14](../src/Tests/HRManagement.AcceptanceTests/Ac09ShadowPolicyTests.cs).
- → **WF:** AC-EST-24e محجوب (لا تغيير ملاك معلّق في صندوق العمل) — [AcEst24EstablishmentWorkflowUiTests.cs](../src/Tests/HRManagement.AcceptanceTests/AcEst24EstablishmentWorkflowUiTests.cs).
- → **HCM:** `Hcm029CandidateAcceptedV4Tests.External_hire_publishes_v4_with_the_offer_terms` متقلب — [Hcm029CandidateAcceptedV4Tests.cs:38](../src/Tests/HRManagement.UnitTests/Hcm029CandidateAcceptedV4Tests.cs).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-QA-1 | حماية `main` ومسار إنزال 140 commit مع مطوّر واحد | PR إلزامي وفحوص مطلوبة خضراء. عدد المراجعات 0 مؤقتاً (استثناء موثّق في STR-005 بتاريخ انتهاء) حتى وجود مشرف ثانٍ (SYD-017). التوقيع إلزامي بعد إعداد المفاتيح. تصحيح CODEOWNERS إلى الحساب المالك | QA-B |
| D-QA-2 | الفحوص المطلوبة | `CI` و`Acceptance` (بعد QA-C) و`CodeQL` (مع رفع النتائج) و`Gitleaks`. والرحلات الليلية بوابة إصدار لا بوابة دمج | QA-B، QA-C |
| D-QA-3 | استثناءات المحاكاة في مصفوفة STR-005، وسلوك الإقلاع عند الانتهاء | نقل بذور المحاكاة إلى `testdata/`. الإقلاع يفشل للاستثناءات الحقيقية فقط، مع تنبيه قبل 30 يوماً | QA-A، → OPS |
| D-QA-4 | أي AC-01..37 يحمل `[RequiredAcceptance]` في CI | الكل، عدا AC-17/18/22 (فوضى) وAC-34 (تمرين نسخ) وAC-35 (أداء) وAC-36 (توقيع بشري)، وهذه في وظيفة ليلية `ops-drill` بحارس صفر تخطٍّ | QA-C..G |
| D-QA-5 | مخالفات BOLA الـ12 وTen002 | قائمة مجمّدة تتقلّص فقط (بمالك وتاريخ) لتخضرّ CI الآن، والإصلاح في خطة IAM | QA-A، → IAM |
| D-QA-6 | ملف تشغيل الدليل (lab أم شبيه بالإنتاج) | AC-09..12 وكل سيناريو مسار عمل أو سياسة على الملف الشبيه بالإنتاج، والبقية على lab، ويُسجَّل الملف في `metadata.json` | QA-C، QA-E، → POL |
| D-QA-7 | بديل صورة MinIO | صورة موثوقة مثبّتة بالـ digest في سجل المؤسسة (أو مزوّد S3 متوافق) للتطوير وCI والإنتاج | QA-A، → OPS |
| D-QA-8 | معنى «ReadyForSign-off» ومن يوقّع حزم UAT | «Template» حتى التنفيذ. والتوقيع من مالك العمل (UAT-001) مع رابط التشغيل | QA-I |
| D-QA-9 | SLO لـ AC-35 | رقم مئيني معتمد للبحث بنطاق فرع (مثلاً p95 أقل من 800 ms)، أو تصنيف AC-35 «قياس بلا ادعاء» | QA-G |

---

## 6. خارطة التنفيذ المقترحة

```text
الاستقرار (تسلسلي)   QA-A (خط أساس قابل للدفع) → QA-B (الدفع والتشغيل البعيد والحوكمة) → QA-C (وظيفة القبول وأساس صادق)
التقوية              QA-D (اختبارات AC-01..37) ║ QA-H (رحلات E2E حقيقية)
القبول الحي          QA-E (العزل والسياسات والرسائل) ║ QA-F (الرواتب والمال) ║ QA-G (البيانات والملفات والتشغيل)
مستمر                QA-I (التوثيق والأدلة)
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة التنفيذ |
|-------|---------|---------|----------|-----------|-------------------|-------------|
| QA-A | خط أساس قابل للدفع: إصلاح الأحمر الحتمي محلياً على Linux | G-QA-02، G-QA-03، G-QA-04، G-QA-05، G-QA-12 | P0 | D-QA-3، D-QA-5، D-QA-7 | 7–9 | [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md) |
| QA-B | الدفع والتشغيل البعيد الأول وحوكمة الفحوص | G-QA-01، G-QA-07، G-QA-11، G-QA-15 | P0 | QA-A، D-QA-1، D-QA-2 | 4–5 | [PLAN-35](acceptance-plans/PLAN-35-qa-push-remote-ci.md) |
| QA-C | وظيفة القبول في CI وأساس قبول صادق | G-QA-06، G-QA-08 | P0/P1 | QA-B، D-QA-4، D-QA-6 | 6–8 | [PLAN-41](acceptance-plans/PLAN-41-qa-ci-acceptance-honest-baseline.md) |
| QA-D | تقوية اختبارات القبول AC-01..37 | G-QA-09 | P1 | QA-C | 6–8 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) |
| QA-E | قبول حي: العزل والسياسات والرسائل (AC-01..10، 13..18) | G-QA-08 (جزء) | P1 | QA-D، → POL | 6–8 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) (إعادة حية على الملف الشبيه بالإنتاج)؛ AC-17/18 «الموجة 2» (QA-G)؛ AC-14 «الموجة 2» (COD-G) |
| QA-F | قبول حي: الرواتب والمال (AC-12، 21..25) | G-QA-08 (جزء) | P1 | QA-D، → PAY | 5–7 | [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md) (AC-24/25)، PLAN-59 (AC-12 وسكربتات AC-21..23)، [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) (إعادة حية)؛ AC-22 «الموجة 2» (QA-G) |
| QA-G | قبول حي: البيانات والملفات والخصوصية والتشغيل (AC-19، 20، 26..31، 33..37) | G-QA-08 (جزء) | P1 | QA-D، QA-A (MinIO)، D-QA-9 | 6–8 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) (AC-19، 20، 26..31، 33، 37)، و[PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md) (AC-20 الحي)، وPLAN-45 (AC-33)؛ AC-34/35/36 وتمارين التشغيل «الموجة 2» (QA-G) |
| QA-H | رحلات E2E حقيقية وهندسة الاختبارات | G-QA-10، G-QA-14 | P1/P2 | QA-B | 6–8 | «الموجة 2» (QA-H) |
| QA-I | التوثيق والأدلة | G-QA-13 | P1 | QA-C، ومستمر | 4 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) |
| **المجموع** | | | | | **≈ 50–65** | |

**ربط الخطط المقترحة بخطط الموجة 1 (2026-10-30):**
- QA-A ← PLAN-34، وQA-B ← PLAN-35، وQA-C ← PLAN-41.
- QA-D وQA-I ← PLAN-65 (إغلاق تحقق الموجة 1 وصدق الوثائق).
- QA-E وQA-F وQA-G موزّعة: إعادة التشغيل الحي للمطلوب في PLAN-65، وإعادة كتابة السيناريوهات المرتبطة بمنطقة في خطتها (AC-11/13 ← PLAN-42، AC-10 بالحد الأدنى ← PLAN-47، AC-20 ← PLAN-38، AC-33 ← PLAN-45، AC-24/25 ← PLAN-58، AC-12 وAC-21..23 ← PLAN-59).
- تبقى بالرمز الحرفي في «الموجة 2»: QA-G لتمارين التشغيل (AC-17، 18، 22، 34، 35، 36)، وQA-H (G-QA-10، G-QA-14). وAC-14 مع COD-G.

**QA-A — خط أساس قابل للدفع (7–9 أيام).** هدفها أن ينجح تشغيل خطوات `build-and-test` كلها بالترتيب داخل حاوية Linux محلية قبل أي دفع.
- حاوية تطوير (Linux، pwsh، Python، actionlint، Docker، متصفحات Playwright) وسكربت `scripts/ci-local.sh` ينفّذ خطوات `ci.yml:14-334`.
- STR-005 وفق D-QA-3، والمدقّقات والاختبارات بساعة ثابتة.
- تثبيت SSH.NET، وتفعيل `-warnaserror`.
- صورة MinIO وفق D-QA-7، في compose وTestcontainers.
- مضيفات اختبار مشتركة بـ`ValidateOnBuild`، و`FakeActiveRegimeClient`، و`IFundedPositionVersionService`، و`Mock` يعيد `[]`، وإصلاح Syr001.
- التقاط سجل فشل الرواتب وسلسلة العقد، ثم إصلاحه أو تحويله → PAY.
- قائمة BOLA/Ten002 المجمّدة وفق D-QA-5.
- ضبط ثقافة الاختبارات، و`-ExecutionPolicy Bypass` للاختبارات السبعة.
- تشغيل `dotnet format` وفحص EF للسياقات الـ15 وبوابة التغطية وتصحيح ما يظهر.
- **سيناريوهات القبول:**
  - AC-QA-Aa: `ci-local.sh` = 0 فشل.
  - AC-QA-Ab: مصفوفة باستثناء منتهٍ ← المدقّق يفشل.
  - AC-QA-Ac: إزالة تسجيل من مضيف مشترك ← `ValidateOnBuild` يفشل.
  - AC-QA-Ad: لا ثغرة High.
  - AC-QA-Ae: النتيجة نفسها على Windows/ar-SA.

**QA-B — الدفع والتشغيل البعيد الأول (4–5 أيام).**
- تطبيق D-QA-1/D-QA-2 وتصحيح CODEOWNERS.
- إصلاح `\n` في `e2e-journeys.yml` وفحص `actionlint` في CI.
- رفع نتائج CodeQL وحذف التكرار.
- إصلاح `gateway-ci` و`mutation` وسكربت Dependabot.
- تعطيل المجدولات حتى الاخضرار.
- الدفع عبر PR وفرز التشغيل الأول، وتسجيل روابط التشغيلات في تقرير.
- **سيناريوهات القبول:**
  - AC-QA-Ba: PR بفحوص مطلوبة خضراء يُدمج.
  - AC-QA-Bb: دفع مباشر مرفوض.
  - AC-QA-Bc: `recruitment-journey` يطبع `Passed: 1, skipped: 0` مرتين.
  - AC-QA-Bd: تنبيه CodeQL High ← أحمر.

**QA-C — وظيفة القبول وأساس صادق (6–8 أيام).**
- وظيفة القبول تستخدم `acceptance-up.ps1`/`acceptance-reset.ps1` ومشروع `hrm-acceptance` وشخصيات acceptance-v2 بكلمات مرور مولّدة في الوظيفة.
- الأعداد 61 مشتقة من الكتالوج.
- خطوة «لا Blocked لمطلوب».
- مولّد ملخص بالحداثة (`gitSha`، تاريخ، ملف تشغيل، «Stale»)، ولا Pass بعقد ناقص.
- إعادة تصنيف AC-34/36، وإصلاح AC-12 (خطوة الحساب).
- إعادة AC-01..05 وAC-11 وAC-32 على HEAD.
- **سيناريوهات القبول:**
  - AC-QA-Ca: ملخص CI بـ`gitSha` = HEAD لكل AC.
  - AC-QA-Cb: حذف بذرة سيناريو مطلوب ← أحمر.
  - AC-QA-Cc: `gitSha` قديم ← «Stale».
  - AC-QA-Cd: AC-12 Pass أو Fail حقيقي، لا Blocked.

**QA-D — تقوية اختبارات AC-01..37 (6–8 أيام).**
- الوسم وفق D-QA-4.
- إعادة كتابة Ac06 وAc15 وAc17 وAc33 وAc37.
- نقل فحوص السكربتات (Ac16 وAc18 وAc20 وAc28) إلى C#.
- إزالة `db-checks` الثابتة، وجعل Ac07/10/14/25 قابلة للإعادة.
- توسيع `AcceptanceSkipPatternArchitectureTests` ليكشف الثوابت والتأكيد التحصيلي.
- **سيناريوهات القبول:**
  - AC-QA-Da: تشغيلان متتاليان بالنتيجة نفسها.
  - AC-QA-Db: اختبار بـ`|| true` أو `db-checks` حرفية ← الاختبار المعماري يفشل.

**QA-E — قبول حي: العزل والسياسات والرسائل (6–8 أيام).** AC-01..10 وAC-13..18 على المكدّس، و`ops-drill` لـ AC-17/18.
- **سيناريوهات القبول:**
  - AC-QA-Ea: نقل BR1←BR2 فعلي يغيّر رؤية المديرين (AC-06).
  - AC-QA-Eb: Shadow يكتب السجل ولا يرفض، وEnforce يرفض بمعتمدَين مختلفين (AC-09/10، prod-like).
  - AC-QA-Ec: معرّف ارتباط واحد عبر رحلة تعيين حقيقية (AC-15).
  - AC-QA-Ed: إيقاف RabbitMQ أثناء التعيين ← صف Outbox ثم تسليم واحد (AC-17).
  - AC-QA-Ee: الرسالة المكررة تترك إيصالاً واحداً (AC-16).

**QA-F — قبول حي: الرواتب والمال (5–7 أيام).**
- **سيناريوهات القبول:**
  - AC-QA-Fa: المنشئ لا يعتمد، والمعتمد لا يرحّل (AC-12).
  - AC-QA-Fb: تشغيلان بالمدخلات نفسها ← فرق فارغ (AC-21).
  - AC-QA-Fc: قتل أثناء الحساب ثم استئناف بلا أسطر مكررة (AC-22، ops-drill).
  - AC-QA-Fd: تعديل الملف بعد التجميد لا يغيّر التشغيل (AC-23).
  - AC-QA-Fe: الصرف الجزئي متساوي القيمة عند الإعادة (AC-24).
  - AC-QA-Ff: حجز الاحتيال يمنع الصرف حتى فكّه بأربع عيون (AC-25).

**QA-G — قبول حي: البيانات والملفات والتشغيل (6–8 أيام).** AC-19 وAC-20 وAC-26..31 وAC-33..37.
- **سيناريوهات القبول:**
  - AC-QA-Ga: المصالحة تكتشف الانحراف وتصلحه، والتشغيل الثاني بلا تغيير (AC-19).
  - AC-QA-Gb: اتصال عبر قواعد البيانات مرفوض (AC-20).
  - AC-QA-Gc: EICAR محجور، ورابط منتهٍ مرفوض بعد مدته (AC-28/29).
  - AC-QA-Gd: رمز خدمة دخيل بلا أثر (AC-30).
  - AC-QA-Ge: تمرين نسخ واستعادة بـ`drill-result.json` (AC-34، ops-drill).
  - AC-QA-Gf: لقطة الفقد قبل/بعد مفحوصة (AC-33).
  - AC-QA-Gg: عينات PDF موقّعة بشرياً (AC-36).

**QA-H — رحلات E2E حقيقية وهندسة الاختبارات (6–8 أيام).**
- فحوص API بلا متصفح، ونقل فحوص `personas.json` إلى الوحدة.
- «سطح مصرّح» يفرض 200، وإعادة تسمية الفئة.
- 4 رحلات حقيقية: الفصل، الرواتب، النقل، الإجازة.
- اختبار جذر التركيب لـ15 خدمة.
- نقل دورة الرواتب وسلسلة العقد إلى Testcontainers.
- **سيناريوهات القبول:**
  - AC-QA-Ha: رحلة فصل تنتهي بتعطيل ملف الأجر في DB.
  - AC-QA-Hb: سطح يعيد 403 لشخصية مصرّحة ← فشل.
  - AC-QA-Hc: `Category=Journey` لا يضم إلا رحلات.

**QA-I — التوثيق والأدلة (4 أيام).**
- تصحيح QA-002/003/004 وFUN-001..006 و05-TRACEABILITY وVERIFICATION-COMPLETION وCI_AND_TESTING والدليل التشغيلي وQA_E2E_VERIFICATION.
- حزم UAT إلى «Template».
- مدقّق «لا Done بلا رابط».
- تجديد `test-count-registry.json`.
- **سيناريوهات القبول:**
  - AC-QA-Ia: المدقّق أخضر على الوثائق.
  - AC-QA-Ib: «Done» بلا رابط ← أحمر.

**ملاحظة على التقدير:** أكبر مصادر عدم اليقين:
- سبب إخفاقات الرواتب وسلسلة العقد (غير محسوم ساكناً).
- نتيجة `dotnet format` وفحص EF للسياقات الـ15 (لم تُشغَّل قط).
- عدد الإخفاقات التي يكشفها التشغيل الحي الأول لـ23 سيناريو لم تُشغَّل، وأي خلل منتج يُكتشف يُحوَّل إلى خطة مالكه.

---

## 7. معيار «اكتمال الجودة على مستوى البرنامج» المقترح

- [ ] **1. CI بعيد أخضر:** ثلاثة تشغيلات متتالية على `main` خضراء لكل وظائف `ci.yml` (CI، E2E، Acceptance، CodeQL مع رفع النتائج، Gitleaks)، وثلاث ليالٍ متتالية خضراء لـ`e2e-journeys.yml` بوظيفتيه، وروابطها مسجّلة في تقرير.
- [ ] **2. لا «إخفاقات معروفة»:** 0 فشل في الوحدة والتكامل على Linux وعلى جهاز المطوّر بالثقافة المضبوطة. أي استثناء موجود في قائمة مجمّدة لكل بند فيها مالك وتاريخ، ولا تزيد.
- [ ] **3. أساس قبول صادق:** `summary.md` يولَّد في CI من HEAD، ولكل AC-01..37:
  - حالة و`gitSha` = HEAD وملف التشغيل؛
  - 0 «Stale»، ولا Pass بعقد ناقص؛
  - كل سيناريو Pass أو مؤجل بقرار موثّق (D-QA-4/9).
- [ ] **4. لا نجاح بلا فحص:** الاختبارات المعمارية تمنع Skip/return والثوابت والتأكيد التحصيلي في القبول وE2E. وفئة `Journey` لا تضم إلا رحلات تنتهي بفحص DB أو حدث.
- [ ] **5. الوثائق تطابق الأدلة:** كل «Done» أو «Verified» في `04-P3` و`05-TRACEABILITY` و`FUN_00x_VERIFICATION` يحمل رابط تشغيل أو مجلد أدلة، وحزم UAT «Template» حتى توقيعها.
