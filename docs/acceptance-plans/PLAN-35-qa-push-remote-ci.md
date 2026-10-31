# PLAN-35 — الدفع الأول وتشغيل CI البعيد وحوكمة الفحوص (QA-B)

**Scope:** QA — [دراسة الفجوات](../PRG_001_QUALITY_GAP_STUDY.md): G-QA-01, G-QA-07, G-QA-11, G-QA-15  
**Status:** Implemented (remote CI green; GitHub protection replaced by the local guard, D-QA-1) — 2026-10-31  
**Priority:** P0  
**Depends on:** PLAN-34 (CI محلي مطابق وأخضر، وسجل الالتزامات المؤرخة)  
**Related cards:** SCM-001، SUP-001 (CodeQL/Gitleaks/Dependabot)، QA-002 (mutation)، QA-004 (`e2e-journeys`)، G0/G9 في `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md`

## Goal

أن يصل العمل المحلي كله إلى GitHub ويمر بالـ CI البعيد أخضر، وأن تصبح الفحوص المطلوبة حقيقية:

- **قبل الدفع:**
  - يقرر المالك خصوصية المستودع.
  - يُفحص التاريخ كاملاً بـ gitleaks محلياً، ولا يُدفع شيء فيه سرّ أو ملف ممنوع.
- **الدفع:** **يقوم به المالك بنفسه**. الوكيل يجهّز الفرع والأوامر، ثم يفرز نتائج CI ويصلح من سجلاتها.
- **CI على `main`:** وظائف `CI` و`E2E` و`CodeQL` و`Gitleaks` خضراء. ووظيفة القبول معطّلة بالتزام مؤرخ حتى PLAN-41.
- **حماية الفرع** مطبّقة كما قرر المالك وموثّقة، وCODEOWNERS يسمّي حساباً موجوداً.
- **لا بوابة شكلية:**
  - CodeQL يُفشل على النتائج الجديدة عالية الشدة، أياً كانت خصوصية المستودع.
  - سكربت Dependabot لا يصمت عند الرفض.
  - لا سير عمل مكرر أو مكسور.
  - المجدولات لا تعمل قبل الاخضرار.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-QA-15 خصوصية المستودع قبل الدفع (حالياً **عام**: `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md:3`) | **خاص (private)، يحوّله المالك قبل الدفع** (`gh repo edit --visibility private --accept-visibility-change-consequences`). كل البوابات أدناه تعمل في الحالتين. تبعات الخاص، وكلها **يحتاج تحقق** حسب خطة الحساب: حماية الفرع للمستودع الخاص تتطلب GitHub Pro أو أعلى (الدليل نفسه، `:22`)؛ رفع نتائج code scanning يتطلب ترخيص GitHub Code Security؛ دقائق Actions محدودة (2,000 دقيقة شهرياً لمستودع خاص على الخطة المجانية). والتاريخ الذي كان عاماً يبقى في أي نسخة أو fork أُخذت منه، فيُعامل أي سرّ فيه كمكشوف | §1، §3، §4 |
| D-QA-1 حماية `main` لمطوّر واحد | PR إلزامي، والفحوص المطلوبة خضراء و`strict`، و`required_approving_review_count: 0` مؤقتاً، و`enforce_admins: true`، ولا دفع قسري ولا حذف. استثناء «مراجع ثانٍ» مسجّل التزاماً مؤرخاً (90 يوماً، SYD-017). إن تعذرت الحماية (مستودع خاص بلا Pro): تُطبّق قاعدة محلية `scripts/scm/pre-push-guard.ps1` كخطاف `pre-push`، ويُسجَّل الالتزام | §4 |
| D-QA-2 الفحوص المطلوبة | `CI`، `CodeQL`، `Gitleaks`، و`E2E` بعد تشغيلين أخضرين متتاليين. ويضيف PLAN-41 `Acceptance` | §4 |
| D-QA-16 توقيع الـ commits (`required_signatures: true` في `branch-protection-main.json`) | غير مطلوب حتى يضبط المالك مفتاح توقيع SSH. استثناء موثّق ومسجّل التزاماً مؤرخاً. لا إعادة كتابة للتاريخ لأجل التوقيع | §4 |
| D-QA-17 إن وجد gitleaks سرّاً | في الـ commits غير المدفوعة: إعادة كتابتها بـ `git filter-repo` قبل الدفع، وتدوير السر. في التاريخ المدفوع سابقاً: تدوير السر فوراً، ولا إعادة كتابة لتاريخ عام إلا بقرار | §1 |
| طريقة الدمج الأولى | PR من الفرع `release/plan-35-first-push` إلى `main` كبوابة CI، ثم `git push origin main` (fast-forward) من المالك **قبل** تطبيق الحماية. فلا ضغط للـ commits الـ140 ولا إعادة إنشاء لها | §3 |
| D-QA-18 CodeQL بلا رفع (خاص بلا ترخيص) | تحليل SARIF محلي في الوظيفة نفسها بسكربت بوابة، مع خط أساس للتنبيهات القائمة، لكل تنبيه مالك وتاريخ مراجعة (التزامات مؤرخة) | §2 |

---

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | PLAN-34 مدموجة محلياً | `reports/plan34-final-report.md` موجود، و`pwsh scripts/ci-local.ps1` أخضر على HEAD | `artifacts/ci-local/summary.json` بـ `gitSha` = HEAD وبلا فشل |
| 0.2 | فرع | `git switch -c feature/plan-35-qa-push-remote-ci` | — |
| 0.3 | المسافة عن البعيد | `git fetch origin; git rev-list --count origin/main..main` | العدد مسجّل في `reports/plan35-baseline.md` (الدراسة تقول 140، والموثّق 128 قبل PLAN-25: `reports/plan25-final-report.md:211`) |
| 0.4 | حالة GitHub | `gh auth status`؛ `gh repo view --json visibility,owner,defaultBranchRef`؛ `gh api repos/{owner}/{repo}/branches/main/protection` (404 = لا حماية)؛ `gh api users/omard` | الخصوصية والمالك والحماية الحالية مسجّلة، وهل `@omard` (`CODEOWNERS:4-5`) حساب موجود وله صلاحية |
| 0.5 | أدوات الفحص | `docker pull` لصورتَي gitleaks (8.x) وactionlint (1.7.x) بالـ digest | الـ digest مسجّل في `docs/qa/tool-pins.json` |

---

## In scope

### 1. فحص ما قبل الدفع (G-QA-01)

**المشكلة:**
- 140 commit محلية لم تُفحص بـ gitleaks قط، والمستودع **عام**.
- إعداد gitleaks يستثني مسارين فقط (`.gitleaks.toml:4-9`).
- ملفات بيانات CI المختبرية (`.env.ci`) فيها قيم تشبه الأسرار.
- لا ضمان أن تعديل المالك المحلي على `global.json` أو ملفات `testdata/*/out/*.json` لم تدخل commit.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| سكربت ما قبل الدفع | `scripts/plan035-prepush.ps1` | يخرج بغير صفر عند أي فشل، ويكتب `artifacts/prepush/report.md`. فحوصه في القواعد أدناه |
| إعداد gitleaks | `.gitleaks.toml` | `[[allowlists]]` ضيق لقيم المختبر الموسومة صراحة (`-lab-only`، `hrm_ci_`، `HrAdmin123!` في ملفات CI والاختبار فقط) بمسار وتعبير معاً، لا بالمسار وحده. وأي إضافة تتطلب سبباً في تعليق |
| حارس ما قبل الدفع | `scripts/scm/pre-push-guard.ps1` | خطاف `pre-push` اختياري (يثبّته المالك بـ `scripts/scm/install-hooks.ps1`) يشغّل الفحوص السريعة: gitleaks على المدى المدفوع، والملفات الممنوعة |

**القواعد (فحوص `plan035-prepush.ps1`):**
1. **gitleaks على التاريخ كاملاً:** `detect --source . --redact` يغطي التاريخ المدفوع سابقاً، وعلى المدى غير المدفوع: `--log-opts "origin/main..HEAD"`. ويُحفظ التقرير في `artifacts/prepush/gitleaks*.json` (منقّح).
2. **actionlint** على `.github/workflows/*.yml`.
3. **ملخص `ci-local`** موجود و`gitSha` = HEAD وأخضر.
4. **ملفات ممنوعة في المدى `origin/main..HEAD`:** `git diff --name-only origin/main..HEAD` لا يضم `global.json`، ولا `.env`، ولا `.acceptance/`، ولا `artifacts/`، ولا `testdata/*/out/*.json`، ولا ملفاً أكبر من 50 MB.
5. **بيانات غير إنتاجية:** `python3 scripts/check-non-prod-data-policy.py` (GOV-001-D) ينجح.
6. **حالة التوقيع** تُطبع (`git log --show-signature`) ولا تُفشل إلا إن قرر المالك D-QA-16 «مطلوب».
7. **الأوامر:** السكربت يطبع الأوامر الدقيقة للمالك، ولا يدفع شيئاً بنفسه.

### 2. إصلاح سير العمل والبوابات الشكلية (G-QA-07، G-QA-11، G-QA-15)

**المشكلة والتصميم:**

| المشكلة (متحققة) | التصميم |
|------------------|---------|
| `\n` حرفي في `e2e-journeys.yml:149`: خطوة الملف الشبيه بالإنتاج تفشل دائماً، ثم حارسها (`:165-176`) يفشل | `run: \|` متعدد الأسطر بـ `\` حقيقي. واختبار `WorkflowYamlLintTests` (UnitTests): لا سطر `run` يحوي `\n` حرفياً خارج سلسلة مقتبسة. وactionlint خطوة في `scripts/ci/steps.json` |
| CodeQL بـ `upload: false` (`ci.yml:519-522`، `codeql.yml:39-42`)، فلا يفشل أبداً | `ci.yml` (وظيفة `CodeQL`): `upload: ${{ vars.CODE_SCANNING_UPLOAD == 'true' }}` و`output: sarif-results`. ثم خطوة `python3 scripts/ci/codeql-sarif-gate.py sarif-results --baseline docs/qa/codeql-baseline.json`: تفشل على أي نتيجة `error`، أو `security-severity` ≥ 7.0، غير موجودة في خط الأساس، أو موجودة بتاريخ مراجعة منتهٍ. تسجّل خط الأساس بنوده في `docs/qa/dated-obligations.json` |
| تكرار CodeQL وGitleaks: وظائف في `ci.yml:500-536` وملفان مستقلان | تبقى الوظيفتان في `ci.yml`، لأن أسماءهما هي الفحوص المطلوبة. يُحذف `gitleaks.yml`. ويبقى `codeql.yml` بـ `schedule` و`workflow_dispatch` فقط (بلا `push`/`pull_request`) |
| `gateway-ci.yml` يكرر الوحدة والتكامل. يستعيد مشروعين ثم يشغّل `format` على الحل كله (`:38-42`). و`e2e-gateway` بلا `--profile full-stack` (`:94`) | حذف `gateway-ci.yml`، لأن `ci.yml` يغطي كل مساراته، مع تحديث `docs/CI_AND_TESTING.md:153-155` |
| `mutation.yml:19,26`: `dotnet-version: "10.0.x"` خلافاً لـ `global.json`، و`dotnet new tool-manifest --force` يمحو `dotnet-ef` | `global-json-file: global.json`، و`dotnet tool install dotnet-stryker --tool-path .tools --version 4.5.1` |
| `sync-dependabot-alerts-to-issues.sh:12` يحوّل أي رفض إلى «0 alerts» | فشل صريح عند غير 200 برسالة الصلاحية الناقصة. والسر `DEPENDABOT_ALERTS_TOKEN` (PAT دقيق بصلاحية قراءة التنبيهات). دون السر: الوظيفة `workflow_dispatch` فقط، والتزام مؤرخ |
| المجدولات (`e2e-journeys.yml:6`، `mutation.yml:6`، `advisory-to-issue.yml:5`، `codeql.yml:9`، `iac-drift.yml:6`) ستعمل كل ليلة على وظائف حمراء | حارس `if: github.event_name != 'schedule' \|\| vars.ENABLE_SCHEDULED == 'true'` على وظائفها. يضبط المالك المتغير بعد الاخضرار. ولا يشمل الحارس `dated-obligations.yml` من PLAN-34، فهو يعمل أسبوعياً دائماً |
| وظيفة القبول في `ci.yml:408-498` لا يمكن أن تنجح قبل PLAN-41 | `if: vars.ENABLE_ACCEPTANCE_JOB == 'true'`، مع خطوة ملخص تطبع «معطّلة حتى PLAN-41»، والتزام مؤرخ (+45 يوماً، المالك PLAN-41). وتغيير الاسم إلى `Acceptance` (ثابت للفحص المطلوب) |
| `restore` بلا `--locked-mode` في سير عمل غير `ci.yml` (`mutation.yml`، وكان `gateway-ci.yml`) | `--locked-mode` في كل استعادة. واختبار `WorkflowYamlLintTests` يرفض `dotnet restore` بدونه |

**القواعد:**
1. لا وظيفة تُعطَّل إلا بشرط صريح، والتزام مؤرخ له مالك، وسطر في ملخص الوظيفة.
2. أي تنبيه CodeQL قائم يُقبل فقط ببند في خط الأساس، بمالك وتاريخ مراجعة.
3. لا يُعاد اسم فحص مطلوب دون تحديث `branch-protection-main.json` والمدقّق.

### 3. الدفع الأول والفرز (G-QA-01)

**التسلسل** (الوكيل = A، المالك = O):

| # | من | الخطوة |
|---|----|--------|
| 3.1 | O | يقرر D-QA-15، ويحوّل الخصوصية إن قرر |
| 3.2 | A | `pwsh scripts/plan035-prepush.ps1` أخضر، ثم دمج هذه الخطة في `main` محلياً. وإنشاء `release/plan-35-first-push` من `main` |
| 3.3 | O | `git push -u origin release/plan-35-first-push` ثم `gh pr create --base main --head release/plan-35-first-push --title "First push: PLAN-25..35" --body-file reports/plan35-first-push.md` |
| 3.4 | A | الفرز: `gh run list --branch release/plan-35-first-push`، ثم `gh run view <id> --log-failed`، ثم `gh run download <id>`. لكل فشل: إصلاح على الفرع (commit صغير برسالة تسمّي الخطوة) وتشغيل `ci-local` للخطوة نفسها |
| 3.5 | O | دفع الإصلاحات (`git push`). تتكرر 3.4–3.5 حتى تخضرّ `CI` و`CodeQL` و`Gitleaks` و`E2E` |
| 3.6 | O | `git switch main; git merge --ff-only release/plan-35-first-push; git push origin main` |
| 3.7 | A | التحقق من تشغيل `push` على `main` أخضر بالـ SHA نفسه، وتسجيل الروابط في `reports/plan35-final-report.md` |
| 3.8 | O | تطبيق الحماية (§4)، ثم `gh variable set ENABLE_SCHEDULED --body true`، ثم `gh workflow run e2e-journeys.yml --ref main` |

**قواعد الفرز:**
1. فشل بسبب البيئة البعيدة (صلاحيات، سحب صور، مهلة): يُصلح في سير العمل أو السكربت.
2. فشل اختبار حقيقي لم يظهر محلياً: يُصلح إن كان في الاختبار أو المضيف. وإن كان عيب منتج: يُسجَّل في التقرير مع خطة مالكه، ويُعزل ببند في `tests/quarantine.json` بالتزام مؤرخ، ولا يُحذف الاختبار.
3. حالات E2E السطحية التي تفشل لسبب منتج تُعزل بالآلية نفسها (`tests/e2e/quarantine.json`)، ويُطبع المعزول في ملخص الوظيفة. وإصلاحها ← QA-H (الموجة 2).
4. لا تخفيض لحد تغطية ولا حذف لخطوة لإخضرار CI.

### 4. حماية الفرع والحوكمة (G-QA-01، G-QA-11)

**المشكلة:**
- `docs/scm-governance/branch-protection-main.json:3-26` يفرض مراجعتين وتوقيعاً و`enforce_admins`، وهذا لا يمكن لمطوّر واحد استيفاؤه.
- `CODEOWNERS:4-5` يسمّي `@omard`، والمالك `omardefdev` (`GITHUB_CI_GOVERNANCE_RUNBOOK.md:3`).
- الدليل التشغيلي يضيف `E2E` إلى الفحوص المطلوبة (`:8-13`)، والملف والمدقّق لا يضيفانه (`scm001-branch-protection-validate.ps1:38`).
- `SCM_001_BRANCH_PROTECTION.md:20-23`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| ملف الحماية | `docs/scm-governance/branch-protection-main.json` | قيم D-QA-1/2/16. الاستثناءات (`required_approving_review_count: 0`، التوقيع) مسجّلة بمعرّف التزام في `exceptionRefs` |
| المدقّق | `scripts/scm001-branch-protection-validate.ps1` | يقبل قيمة أقل من السياسة **فقط** مع `exceptionRefs` لالتزام مفتوح غير منتهٍ في `docs/qa/dated-obligations.json`. و`-CheckLiveGitHub` يقارن الحماية الحية بالملف |
| CODEOWNERS | `.github/CODEOWNERS` | `@omard` ← مالك المستودع من `gh repo view --json owner`، بعد التحقق في 0.4 |
| سكربت التطبيق | `scripts/scm/apply-branch-protection.ps1` | للمالك: `gh api -X PUT .../branches/main/protection --input` من الملف، ثم `-CheckLiveGitHub` |
| الوثائق | `GITHUB_CI_GOVERNANCE_RUNBOOK.md`، `SCM_001_BRANCH_PROTECTION.md`، `CI_AND_TESTING.md:125-151` | الخصوصية، الفحوص المطلوبة الفعلية، جرد الأسرار (لا شيء لـ `ci.yml`؛ و`deploy.yml`: `DEPLOY_SSH_*` وسلاسل الاتصال الخمس عشرة؛ و`iac-*`: AWS؛ واختيارياً `DEPENDABOT_ALERTS_TOKEN` و`STRYKER_DASHBOARD_API_KEY`)، والوظيفة المعطّلة حتى PLAN-41 |

**القواعد:**
1. تُطبّق الحماية **بعد** دفع `main`، فلا يُقفل المالك عن الدفع الأول.
2. اسم كل فحص مطلوب هو اسم الوظيفة كما يعرضه GitHub (`CI`، `CodeQL`، `Gitleaks`، `E2E`).
3. القيمة الحية تُقارن بالملف بعد كل تغيير (`-CheckLiveGitHub`)، والنتيجة في التقرير.

---

## Out of scope

- إصلاح الاختبارات والحراس محلياً ← PLAN-34 (هذه الخطة تفرز ما يظهر بعيداً فقط).
- تشغيل وظيفة القبول في CI وجعلها مطلوبة ← PLAN-41.
- تحويل «الرحلات» السطحية إلى رحلات حقيقية ← QA-H (الموجة 2).
- النشر (`deploy.yml`) وأسراره وبيئاته ← PLAN-36.
- مراجع ثانٍ حقيقي (SYD-017): خارج الشيفرة، ومسجّل التزاماً.

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-QA-35a | `plan035-prepush.ps1` على HEAD: gitleaks بلا نتائج، actionlint نظيف، لا ملفات ممنوعة ← خروج 0 | مستودع مؤقت (`$env:TEMP`) فيه commit بسر مزروع، أو بتعديل `global.json` في المدى ← خروج 1 يسمّي الملف |
| AC-QA-35b | أول PR: `CI` و`CodeQL` و`Gitleaks` و`E2E` خضراء، والروابط في `reports/plan35-final-report.md` | — |
| AC-QA-35c | تشغيل `push` على `main` بالـ SHA المدموج أخضر لكل الوظائف المفعّلة. يسجّل `AcQa35RemoteCiTests` (`[RequiredAcceptance]`، يقرأ عبر `gh api`) أرقام التشغيل في `acceptance-evidence/AC-QA-35/` | دون `GH_TOKEN` أو `gh` مصادَق عليه ← `Blocked`، ويفشل تحت `ACCEPTANCE_REQUIRE_ALL=true` |
| AC-QA-35d | الحماية الحية تطابق الملف (`-CheckLiveGitHub` = PASS) | PR تجريبي بفرع `plan35-negative-check` فيه اختبار أحمر مقصود ← `gh pr view --json mergeStateStatus` = `BLOCKED`، ثم يُغلق. ودفع مباشر إلى `main` ← مرفوض (إن طُبقت الحماية) |
| AC-QA-35e | `gh workflow run e2e-journeys.yml`: وظيفة `recruitment-journey` تطبع `Passed: 1, skipped: 0` مرتين، للملف المختبري والشبيه بالإنتاج | `WorkflowYamlLintTests` يرفض ملفاً تجريبياً فيه `\n` حرفي في `run` |
| AC-QA-35f | بوابة CodeQL تنجح على SARIF مطابق لخط الأساس | SARIF تجريبي بنتيجة `security-severity` 8.1 خارج خط الأساس، أو بند خط أساس منتهٍ ← خروج 1 (`CodeqlSarifGateTests`) |
| AC-QA-35g | `dated-obligations.yml` يعمل يدوياً على `main` | سكربت Dependabot بلا صلاحية ← خطوة حمراء برسالة صريحة، لا «0 alerts» |

## Verification

```powershell
pwsh scripts/plan035-prepush.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "WorkflowYamlLint|CodeqlSarifGate|CiStepsParity|Scm001"
# بعد دفع المالك
gh run list --branch release/plan-35-first-push --limit 5
gh run view <id> --log-failed
gh pr checks <pr>
# بعد الدمج والحماية
pwsh scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcQa35
```

## Definition of done

- قرار الخصوصية مسجّل (D-QA-15) قبل الدفع، وتقرير gitleaks للتاريخ كاملاً نظيف أو كل نتيجة معالجة وفق D-QA-17.
- `main` البعيد = `main` المحلي. وآخر تشغيل `push` على `main` أخضر لـ `CI` و`CodeQL` و`Gitleaks` و`E2E`، مع الروابط.
- لا سير عمل مكرر أو بخطوة مكسورة. والمجدولات مفعّلة بعد الاخضرار، وتشغيل يدوي واحد ناجح لـ `e2e-journeys.yml` بوظيفتيه.
- بوابة CodeQL تُفشل على النتائج الجديدة عالية الشدة.
- الحماية الحية تطابق الملف، والمدقّق يمر بـ `-CheckLiveGitHub`، وCODEOWNERS يسمّي حساباً صالحاً.
- AC-QA-35a..g = Pass، ومجلد `acceptance-evidence/AC-QA-35/` بالعناصر الثمانية.
- أسطر «الحالة» محدّثة في الدراسة لـ G-QA-01 وG-QA-07 وG-QA-11 وG-QA-15. ورأس الدراسة يسجّل الـ SHA الذي اخضرّ عليه CI.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-35-qa-push-remote-ci.md` |
| Scripts | `scripts/plan035-prepush.ps1`، `scripts/scm/pre-push-guard.ps1`، `scripts/scm/install-hooks.ps1`، `scripts/scm/apply-branch-protection.ps1`، `scripts/ci/codeql-sarif-gate.py`، `scripts/sync-dependabot-alerts-to-issues.sh` |
| Workflows | `.github/workflows/ci.yml`، `e2e-journeys.yml`، `codeql.yml`، `mutation.yml`، `advisory-to-issue.yml`، `iac-drift.yml`؛ حذف `gitleaks.yml` و`gateway-ci.yml` |
| Config | `.gitleaks.toml`، `.github/CODEOWNERS`، `docs/scm-governance/branch-protection-main.json`، `docs/qa/codeql-baseline.json`، `docs/qa/tool-pins.json`، `tests/e2e/quarantine.json`، بنود جديدة في `docs/qa/dated-obligations.json` |
| Tests | `WorkflowYamlLintTests`، `CodeqlSarifGateTests`، `Scm001BranchProtectionExceptionTests`؛ `AcQa35RemoteCiTests` |
| Reports | `reports/plan35-baseline.md`، `reports/plan35-first-push.md` (وصف الـ PR)، `reports/plan35-final-report.md` (روابط التشغيلات وما فُرز) |
| Docs | `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md`، `docs/SCM_001_BRANCH_PROTECTION.md`، `docs/CI_AND_TESTING.md`، `docs/PRG_001_QUALITY_GAP_STUDY.md` (أسطر «الحالة» والرأس) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| gitleaks يجد سراً في تاريخ كان عاماً | D-QA-17: تدوير فوري. والتقرير منقّح لا يحوي القيمة |
| تحويل المستودع إلى خاص يعطّل الحماية أو code scanning حسب الخطة | البوابات مصممة للحالتين: حارس `pre-push` محلي، وبوابة SARIF محلية |
| دقائق Actions تنفد في المستودع الخاص | المجدولات خلف متغير، ووظيفة القبول معطّلة حتى PLAN-41، والتقرير يقيس دقائق كل تشغيل |
| فشل بعيد لا يتكرر محلياً (موارد المشغّل، سحب الصور) | `gh run download` للأدلة، والإصلاح في السكربت المشترك من PLAN-34 حتى يتطابق المحلي والبعيد |
| المالك يطبّق الحماية قبل الدفع فيُقفل عن `main` | الترتيب في §3: الحماية في 3.8 بعد الدفع |
| CodeQL يكشف تنبيهات كثيرة في أول تشغيل | خط أساس صريح لكل تنبيه، بمالك وتاريخ، لا كتم عام |

## Effort estimate

5–7 أيام عمل للوكيل، دون وقت انتظار المالك:
- فحص ما قبل الدفع وgitleaks: 1.
- إصلاح سير العمل وبوابة CodeQL: 1.5–2.
- الفرز بعد الدفع: 1.5–2.5، حسب ما يظهر.
- الحماية والوثائق والدليل: 1.

## ملاحظات التنفيذ والانحرافات (2026-10-31)

التفاصيل في [`reports/plan35-final-report.md`](../../reports/plan35-final-report.md) و[`reports/plan35-progress.md`](../../reports/plan35-progress.md).

- **gitleaks:** ملف `.gitleaks.toml` القديم بلا قواعد، فكان كل فحص ينجح. صار بالقواعد الافتراضية واستثناءين ضيقين (المسار والنص معاً)
  لسرّي مختبر QA-003؛ لا سرّ حقيقي في التاريخ كله.
- **بوابة CodeQL بـ PowerShell لا Python:** لا Python على جهاز Windows، واختبارات `CodeqlSarifGateTests` تعمل على النظامين. كشف أول
  تشغيل حقيقي 108 تنبيهات قائمة سُجّلت بنداً بنداً بمالك وتاريخ (`QA-CODEQL-BASELINE`، 2027-01-28)؛ إصلاحها دَين أمني لخطة لاحقة.
- **OI-1 (a) وD-QA-15:** أزال المالك الحماية القديمة قبل الدفع، وصار المستودع خاصاً. GitHub لا يحمي مستودعاً خاصاً على الخطة المجانية
  (403)، فالبديل (D-QA-1) حارس pre-push محلي بـ `-ProtectMain` والاستثناء `SCM-NO-GITHUB-PROTECTION`؛ `-CheckLiveGitHub` ناجح على هذا
  البديل. لكن PR أحمر يبقى قابلاً للدمج على GitHub (PR #25: `MERGEABLE`)، فالمنع إجرائي حتى يصبح المستودع عاماً أو على Pro.
- **OI-3 (a):** أُصلح عيب SEC-001 (14 مسار `health-*` مجهولة في البوابة) مع اختبار وحدة، بدل حجر اختبار E2E.
- **الفرز البعيد:** ستة إصلاحات على فرع الإصدار، واحد لكل فشل: مساحة القرص، أساس CodeQL، ملف BLD-002، Playwright على Ubuntu 26.04
  (تثبيت `ubuntu-24.04` بالتزام مؤرخ)، سباق تهيئة PostgreSQL (فحص `pg_isready` عبر المقبس ينجح أثناء سكربتات التهيئة → HTTP 500)،
  وخطأ pipefail أسقط التحضير بصمت. كلها في مسار CI فقط (P-1).
- **E2E فحص مطلوب:** بعد تشغيلين أخضرين متتاليين على `main` (41130907741، 41140221205، D-QA-2).
- **`e2e-journeys`:** خطوة الملف الشبيه بالإنتاج كانت معطّلة بـ `\n` حرفي؛ بعد إصلاحه ظهرت ثلاثة عيوب متتالية في الخطوة نفسها (بيئة
  القبول غير مستوردة، 502 بعد إعادة الإنشاء، مسار صحة البوابة يجيب 500). التشغيل اليدوي الناجح للملفين على فرع الإصدار لأن الإصلاحات
  تصل إلى `main` مع PR التقرير (P-5). نجح الملفان في التشغيل 41158446834.
- **دفع Claude بنفسه:** دفع `main` (P7) وفرع الإصدار بأمر صريح من المالك («نفذها انت»)، خلافاً لقاعدة «لا تدفع إلى main بنفسك» وP-2؛
  مسجّل بكلمات المالك.
- **دقائق Actions:** المستودع خاص (2,000 دقيقة شهرياً)؛ استهلكت الخطة نحو 900 دقيقة. بقرار المالك أُعيد `ENABLE_SCHEDULED` إلى `false`
  وصار `e2e-journeys` أسبوعياً؛ فلم يتحقق «ثلاث ليالٍ متتالية خضراء» (G-QA-15 جزئي).
