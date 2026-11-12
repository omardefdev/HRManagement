# الاختبارات والتكامل المستمر

## المتطلبات

- .NET SDK 10.0.203 (انظر [`global.json`](../global.json))
- Docker Desktop (لـ Testcontainers واختبارات E2E)
- قواعد التنسيق من `.editorconfig`
- [`coverlet.runsettings`](../coverlet.runsettings) لتصفية assemblies التغطية

## اختبارات الوحدة (تغذية سريعة)

```powershell
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj
```

## الاختبارات المعمارية (PLAN-51)

```powershell
dotnet test src/Tests/HRManagement.ArchitectureTests/HRManagement.ArchitectureTests.csproj
```

- مشروع واحد يحمّل تجميعات Web الخمس عشرة (أربع عشرة خدمة والبوابة) ومتحكمات ServiceDefaults المشتركة (`WebAssemblyCatalog`)؛
  تجميعة لا تُحمَّل تُفشل الاختبار ولا تُتخطى (`AllWebAssembliesLoadTests`). خطوة «Architecture tests» في `scripts/ci/steps.json` بعد
  اختبارات الوحدة، في CI و`ci-local.ps1`، بلا جامع تغطية.
- الاختبارات: `WriteActionsHaveExplicitPolicyTests` (كل كتابة تسمّي سياستها)، `SensitiveActionsHaveApprovalPolicyTests` (الأسماء الكاملة
  وبادئات الخطة وقائمتها المسماة)، `InternalRoutesAreServiceOnlyTests` (منقول من `S2s042…`)، `ModuleOverridesAreDeclaredTests`
  (`[ModulePolicyOverride]` لا يجيز كتابة وحده)، `MassAssignmentArchitectureTests` (منقول، يمسح الخمس عشرة)، و`GuardSelfTests`
  (عيب مزروع لكل حارس).
- قوائم السقف تحت `tests/architecture/`: صف لكل سطر `Key | reason`، والسبب يبدأ بالخطة المالكة (`PLAN-53:` أو `IAM-F:`)، والسقف
  مثبّت في الاختبار ولا يرتفع؛ صف لم يعد مخالفاً يُفشل الاختبار حتى يُحذف. بعد PLAN-51: `write-policy-allowlist/payroll.txt` (16، PLAN-53)
  و`entity-binding-allowlist/*.txt` خارج HR.Movements (113، IAM-F) فقط؛ PLAN-65 يراجعها.
- أول تشغيل بعد البناء يستغرق نحو دقيقتين ونصف على جهاز المالك (بدء المضيف على التجميعات المبنية حديثاً)، ثم نحو ثانية ونصف.

## اختبارات التكامل (MassTransit InMemory + SQLite)

```powershell
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj --filter "Category!=Testcontainers"
```

## اختبارات Testcontainers (QA-003 — إلزامي في CI)

صور مثبتة: PostgreSQL 16، RabbitMQ 3.13، Keycloak 24، MinIO (انظر [ADR-QA-003](./ADR-QA-003-real-infra-tests.md) و[QA_REAL_INFRA_TOPOLOGY.md](./QA_REAL_INFRA_TOPOLOGY.md)).

تتطلب Docker وتفعيلاً صريحاً محلياً:

```powershell
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj --filter "Category=Testcontainers"
```

في bash:

```bash
RUN_TESTCONTAINERS=true dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj --filter "Category=Testcontainers"
```

في CI (`ci.yml`) خطوة Testcontainers **إلزامية** على كل PR/push — ليست مشروطة بوسم `integration`.
SQLite/InMemory تبقى لمسار سريع ولا تغلق وحدها مسارات إنتاجية.

**اتصالات الخادم المشترك (PLAN-49).** كل اختبار في مجموعة `PostgreSql` (وكذلك `PgRabbit` و`PgMinio`) يأخذ قاعدة بيانات
خاصة به، أي حوض اتصالات خاصاً به، وللخادم الواحد 100 منفذ اتصال. Npgsql يُبقي الاتصال الخامل خمس دقائق افتراضياً، فكان
الخادم يحمل اتصالات كل اختبار انتهى في آخر خمس دقائق؛ ولما كبرت المجموعة لم يجد صنف إثباتات RLS (ثماني عشرة قاعدة)
منفذاً فارغاً (`53300` في طلب الدمج 47: تسعون اتصالاً على الخادم، تسعة وثمانون منها خاملة). لذلك تغلق سلاسل الاتصال التي
يعطيها `PostgreSqlFixture` (`CreateDatabaseAsync` و`CreateAppConnectionAsync`) الاتصال الخامل بعد خمس ثوانٍ، والاختبار
`PostgreSqlFixtureIdleConnectionTests` يثبت ذلك على الخادم نفسه. قاعدتان لمن يضيف اختباراً:

- خذ سلسلة الاتصال من هاتين الدالتين أو اشتقها من ناتجهما (بتغيير المستخدم مثلاً)، ولا تبنِ سلسلة إلى قاعدة جديدة من
  `ConnectionString` مباشرة.
- شغّل قبل طلب الدمج خطوة Testcontainers **كاملة** بمرشح CI نفسه (`Category=Testcontainers&Category!=ObjectStorage`)، لا
  أصناف خطتك وحدها. صنف يفشل في التشغيل الكامل وينجح وحده فشلٌ حقيقي: الخادم مشترك، وCI يشغّل الخطوة كاملة.

## التغطية محلياً (QA-001)

```powershell
Remove-Item -Recurse -Force ./TestResults -ErrorAction SilentlyContinue
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj `
  --settings coverlet.runsettings `
  --collect:"XPlat Code Coverage" `
  --results-directory ./TestResults/unit
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj `
  --filter "Category!=Testcontainers" `
  --settings coverlet.runsettings `
  --collect:"XPlat Code Coverage" `
  --results-directory ./TestResults/integration
# التقرير: TestResults/**/coverage.cobertura.xml
```

**بوابة التغطية (QA-001 → QA-002):**
- غياب أي ملف `coverage.cobertura.xml` يُعد فشلاً صريحاً.
- السكربت يقرأ كل التقارير تحت `./TestResults` ويحسب **عتبة عامة 60%** على `gateAssemblies` في [`coverage-floors.json`](../coverage-floors.json) (استراتيجية مخاطر — انظر [ADR-QA-002](./ADR-QA-002-risk-based-coverage.md)).
- **أرضيات per-assembly** في نفس الملف؛ لا تخفيض دون تحديث الـADR (ratchet).
- التصفية عبر `coverlet.runsettings`؛ استثناءات موقّعة في [QA_COVERAGE_EXCLUSIONS.md](./QA_COVERAGE_EXCLUSIONS.md).
- `COVERAGE_THRESHOLD` يتجاوز العتبة العامة مخبرياً فقط؛ الافتراضي من JSON = 60.
- مصفوفة مخاطر→اختبار: [QA_RISK_TO_TEST_MATRIX.md](./QA_RISK_TO_TEST_MATRIX.md).
- Mutation (Payroll.Domain): `.github/workflows/mutation.yml` + `tests/stryker-payroll.json` (ليس حاجز كل PR).

ملاحظة: حزم OpenTelemetry رُفعت إلى 1.17.0 و`SQLitePCLRaw.bundle_e_sqlite3` إلى 2.1.12 و`SkiaSharp` إلى 3.119.0 لإزالة ثغرات Critical/High من بوابة الأمان.

على Windows PowerShell:

```powershell
.\scripts\check-coverage.ps1
```

## فحص الأمان (QA-001)

```powershell
dotnet list HRManagement.slnx package --vulnerable --include-transitive
```

في CI تفشل الخطوة عند وجود حزم بخطورة Critical أو High.

## اختبارات E2E (Playwright) — QA-004

```powershell
# Terminal 1 — تشغيل المكدس
docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait

# Terminal 2 — رحلات الأعمال (Category=Journey)
$env:RUN_PLAYWRIGHT_E2E = "true"
$env:GATEWAY_BASE_URL = "http://localhost:5000"
# Personas: testdata/e2e/personas.json (+ realm-qa004-e2e.json) أو E2E_PERSONA_*_PASSWORD
dotnet test src/Tests/HRManagement.E2E/HRManagement.E2E.csproj -c Release `
  --filter "Category=Journey|FullyQualifiedName~E2eSeedPackTests"
```

Smoke بوابة (وسم PR `e2e`): بدون filter.  
Nightly / pre-release على صور الإصدار: `.github/workflows/e2e-journeys.yml` — انظر [QA_E2E_VERIFICATION.md](./QA_E2E_VERIFICATION.md).

**قواعد مضاد الصمت:** `RUN_PLAYWRIGHT_E2E` غير مفعّل → `Skipped`؛ مفعّل بلا مكدس → `Failed`؛ **503/5xx ≠ نجاح** (`E2eHttp`).

## تشغيل فئة اختبار واحدة

```powershell
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj `
  --filter "FullyQualifiedName~ContractLifecycleIntegrationTests"
```

## مطابقة CI محلياً — `scripts/ci-local.ps1` (PLAN-34)

خطوات وظيفة `build-and-test` لها **مصدر واحد**: [`scripts/ci/steps.json`](../scripts/ci/steps.json) (87 خطوة، بالترتيب).
`ci.yml` يشغّل كل خطوة بـ `bash scripts/ci/run-steps.sh --step "<name>"`، و`CiStepsParityTests` يرفض أي انحراف (اسم أو ترتيب
أو `run:` مضمّن أو استبعاد فئة بلا مالك). أي خطوة جديدة تُضاف إلى `steps.json` ثم إلى `ci.yml` بالسطر نفسه.

التشغيل المحلي في حاوية Linux (الصورة `docker/ci-local/Dockerfile`: SDK `10.0.400` بالضبط، python3، docker CLI/compose/buildx،
actionlint، `powershell`←`pwsh`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1                       # كل الخطوات على نسخة نظيفة من HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1 -ContinueOnError -Out artifacts/ci-local/run1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1 -Step "Restore (locked mode),Build,Unit tests"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1 -Source WorkingTree   # يشمل التعديلات غير المودَعة
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1 -SkipTestcontainers   # الخطوة تُطبع «not-run» صراحة
```

- **`-Source Head`** (الافتراضي): الحاوية تستنسخ HEAD المودَع وتشغّل فيه، فلا يدخل تعديل `global.json` المحلي ولا مجلدات
  `bin/obj` الخاصة بـ Windows. `-Source WorkingTree` ينسخ فوق النسخة الملفات التي يراها git معدّلة أو جديدة ويعيد `global.json`
  المودَع دائماً.
- **`summary.json`** (في `-Out`): `gitSha`، `dirtyTracked`، `knownLocalOverrides` (`global.json` حين يختلف `rollForward` وحده)،
  ولكل خطوة `status`/`exit`/`durationSec`، وعدّ trx، و`complete`. يُعدّ دليلاً فقط إذا كان `gitSha` = HEAD و`dirtyTracked` فارغة
  وكل الخطوات `passed`.
- **مطابقة المشغّل الافتراضي:** `DOTNET_RUNNING_IN_CONTAINER` يُلغى (المشغّل VM لا حاوية)، ومجلد المؤقتات مركّب بالمسار نفسه
  (`TMPDIR=/tmp/hrm-ci-local`) لأن Testcontainers يركّب ملفات منه، و`TESTCONTAINERS_HOST_OVERRIDE` على عنوان IPv4 لـ
  `host.docker.internal`.
- لا مفهوم «إخفاقات معروفة»: أي خطوة فاشلة حمراء.

## الالتزامات المؤرخة (QA-034)

كل تاريخ قد يوقف CI أو خدمة في يوم معيّن مسجّل في [`docs/qa/dated-obligations.json`](qa/dated-obligations.json)
(استثناءات STR-005/STR-002، ختم الرصد التشريعي، تراخيص/EOL الموردين، استثناءات SCA، الحجر، استبعادات CI، أرضيات التغطية المؤقتة،
تاريخ إغلاق بذرة القبول). `scripts/qa/dated-obligations-check.ps1` يُنذر قبل 30 يوماً ويُفشل قبل 7 أيام أو عند الانتهاء أو
عند تاريخ ليس `yyyy-MM-dd`؛ يعمل خطوةً قبل مدقّقات STR/MIG وأسبوعياً في `.github/workflows/dated-obligations.yml`.
`DatedObligationsTests` يفشل عند ملف JSON جديد يحمل مفتاحاً مؤرخاً دون تسجيل. الالتزام لا يتغير بتمديد التاريخ، بل بحالته
(`Closed`/`Renewed`) وبقرار مسجّل. اختبارات الوحدة لا تقرأ الساعة الحقيقية (`FixedTimeProvider`، `-AsOfDate`)، عدا الكناري
`RuntimeGovernanceDocumentsTests`.

## ثقافة الاختبارات

كل مشاريع الاختبار تعمل بثقافة **ar-SY** (تقويم ميلادي، ثقافة الطلبات في الإنتاج) عبر `TestCulture.cs` (`[ModuleInitializer]`)،
فيتطابق Windows (ar-SA) وLinux. `TestCultureGuardTests` يحمرّ إن أُزيل. الشيفرة الجديدة تحلّل وتنسّق التواريخ والأرقام الآلية
بـ `CultureInfo.InvariantCulture`.

## استبعادات وحجر

- `Category=ObjectStorage` (`Qa003MinioFileScanTests`) مستبعدة من خطوة Testcontainers حتى تختار PLAN-38 صورة MinIO
  (`HRM_MINIO_IMAGE`)؛ البند في [`tests/ci-exclusions.json`](../tests/ci-exclusions.json) بتاريخ مراجعة.
- [`tests/quarantine.json`](../tests/quarantine.json) فارغ؛ كل بند فيه سيكون `Category=Flaky` بتاريخ مراجعة.
- المكدّس الأساسي (`docker-compose.yml` + `docker-compose.services.yml`) يشغّل File.Storage بالمزوّد `Local`؛ MinIO عبر
  `docker-compose.object-storage.yml` و`MINIO_IMAGE` مثبّت بالـ digest.

## CI

### `ci.yml` (كل PR/push على `main`)

| البوابة | إلزامية؟ | ملاحظات |
|---------|----------|---------|
| Dated obligations horizon | نعم | `scripts/qa/dated-obligations-check.ps1` (QA-034) |
| Workflow lint | نعم | `scripts/ci/actionlint.sh`: actionlint + shellcheck بصورة مثبّتة بالـ digest (`docs/qa/tool-pins.json`)؛ و`WorkflowYamlLintTests` (لا `
` حرفي في `run`، و`--locked-mode` في كل استعادة، وحارس المجدولات) — PLAN-35 |
| Format | نعم | `dotnet format --verify-no-changes` |
| Build | نعم | الحل بالكامل، `-warnaserror`؛ NU1903/NU1904 أخطاء في `Directory.Build.props` |
| Unit + coverage | نعم | `coverlet.runsettings` |
| Integration (InMemory) + coverage | نعم | بدون Testcontainers |
| Testcontainers (PG/Rabbit/Keycloak) | نعم | `RUN_TESTCONTAINERS=true`؛ QA-003؛ `Category!=ObjectStorage` (PLAN-38) |
| Coverage gate | نعم | `check-coverage.py`؛ 60% على gateAssemblies + floors |
| Security scan | نعم | `check-nuget-vulnerabilities.sh`؛ Crit/High + استثناءات `supply-chain-exceptions.json` (SUP-001) |
| Base image pins / license / exceptions | نعم | `check-base-image-pins.sh`, `check-license-policy.sh`, `check-supply-chain-exceptions.sh` |
| SCA fixture | نعم | `verify-sca-fixture.sh` يثبت فشل Crit/High بلا استثناء |
| No external CDN / XSS DOM sinks | نعم | `check-no-external-cdn.sh`, `check-xss-dom-sinks.sh` (UX-001) |
| Icon button accessible names | نعم | `check-icon-button-names.py` (UX-002) |
| Non-prod data policy (no raw prod dumps) | نعم | `check-non-prod-data-policy.py` (GOV-001) |
| No LEGAL_TBD in payroll release path | نعم | `check-no-legal-tbd.py` (GOV-002) |
| Master data DQ thresholds (offline fixture) | نعم | `gov003-data-quality-report.py` + `testdata/gov003` (GOV-003) |
| Docker compose build | نعم | تحقق بناء الصور |
| E2E (وظيفة `E2E`) | نعم (فحص مطلوب منذ PLAN-35 P7: أخضر مرتين متتاليتين على `main`، التشغيلان 41130907741 و41140221205، D-QA-2) | المكدّس الأساسي عبر `scripts/ci/compose-up-retry.sh` (ملكية وحدة File.Storage، ثم PostgreSQL أولاً وانتظار قبوله اتصال TCP لأن `pg_isready` عبر المقبس ينجح أثناء سكربتات التهيئة، وإعادة محاولة واحدة للإقلاع البارد، PLAN-35)؛ يرفع `docker-compose.ci-e2e.yml` حدّ معدل البوابة لكل عنوان IP إلى 2000 في الدقيقة لأن كل طلبات E2E تأتي من عنوان المشغّل الواحد (عند الافتراضي 120 انتظر الطلب الزائد في طابور المحدِّد حتى النافذة التالية، حتى ~41 ث، فتجاوز مهلة Playwright البالغة 30 ث؛ ومنذ إصلاح العيب 15 لا طابور، فيُرد الطلب الزائد بـ 429 فوراً ويفشل الاختبار كذلك)؛ وعند الفشل ترفع الوظيفة سجلات الخدمات باسم `e2e-stack-logs` وآثار Playwright باسم `e2e-traces` (7 أيام لكلٍّ منهما): كل اختبار يفتح صفحة متصفح يعمل عبر `PlaywrightFixture.RunTracedAsync`، فإذا فشل ترك trace (`<test>.zip`، يُفتح بـ `playwright.ps1 show-trace`) ولقطة شاشة للصفحة كاملة (`<test>.png`) في `E2E_ARTIFACTS_DIR`، وهو مسار مطلق تحت مساحة العمل لأن الاختبارات تعمل في مجلد `bin` للمشروع فيبقى المسار النسبي خارج الرفع؛ المعزول في [`tests/e2e/quarantine.json`](../tests/e2e/quarantine.json) يُطبع في ملخص الوظيفة |
| Acceptance | نعم (فحص مطلوب منذ PLAN-41، D-QA-2) | `scripts/ci/acceptance-job.ps1` على كل PR إلى `main` وكل دفع إليه، بالتوازي مع `CI`: الملف المختبري ثم الشبيه بالإنتاج تحت `ACCEPTANCE_REQUIRE_ALL=true`. انظر «وظيفة القبول» أدناه |
| E2E Journeys (QA-004) | weekly (Monday 04:00 UTC) / pre-release | `.github/workflows/e2e-journeys.yml`؛ صور الإصدار عند `IMAGE_TAG`؛ المجدول لا يعمل إلا مع `ENABLE_SCHEDULED=true` (أُعيد إلى `false` في 2026-10-31 بعد حساب دقائق Actions للمستودع الخاص؛ يعيده المالك عند الحاجة)؛ خطوة التبديل إلى الملف الشبيه بالإنتاج تستورد بيئة القبول بـ `scripts/acceptance-local-env.ps1` (PLAN-35) |
| E2E axe a11y (UX-002) | مع `RUN_PLAYWRIGHT_E2E=true` | `A11yAxeE2ETests` + `testdata/e2e/axe.min.js` |
| CodeQL | نعم (وظيفة `CodeQL` في `ci.yml`) | التحليل ثم `scripts/ci/codeql-sarif-gate.ps1`: يفشل على نتيجة `error` أو `security-severity` ≥ 7.0 خارج [`docs/qa/codeql-baseline.json`](qa/codeql-baseline.json) أو ببند منتهٍ؛ الرفع إلى code scanning فقط مع `vars.CODE_SCANNING_UPLOAD == 'true'` (D-QA-18)؛ `codeql.yml` أسبوعي ويدوي فقط |
| Gitleaks | نعم (وظيفة `Gitleaks` في `ci.yml`) | `scripts/ci/gitleaks.sh`: صورة gitleaks مثبّتة على التاريخ كاملاً بـ `.gitleaks.toml` (القواعد الافتراضية + استثناءات ضيقة بمسار ونص)؛ يفشل أيضاً إن فحص commits أقل من HEAD (PLAN-35) |
| Deploy SBOM + Cosign verify | مسار الإصدار | `deploy.yml`؛ انظر [SUP_001_VERIFICATION.md](./SUP_001_VERIFICATION.md) |
| CSP / security headers | نعم (runtime) | `UseHrSecurityHeaders`؛ انظر [UX_001_VERIFICATION.md](./UX_001_VERIFICATION.md) |

### وظيفة القبول (PLAN-41)

- **ما تشغّله:** [`tests/acceptance/ci-scope.json`](../tests/acceptance/ci-scope.json). كل صنف من `HRManagement.AcceptanceTests` مرة واحدة: في `lab[]` أو `prodLike[]` (`class`، `required`، `group`) أو `excluded[]` (`reason`، `ownerPlan`، `reviewBy`، ومسجّل في `docs/qa/dated-obligations.json`). و`order[]` يحدد المراحل والمكدّس الذي تحتاجه كل منها.
- **الصنف الجديد:** يُضاف إلى `ci-scope.json` في الالتزام نفسه، وإلا سقطت الوظيفة («ran and is not in the scope file»). ولا يُستبعد إلا لبيئة لا يوفرها CI، ولا يُستبعد لأنه أحمر.
- **الترتيب:** الأصناف التي توقف الحاويات أو تعيد إنشاءها في المجموعة `isolated`: كل صنف وحده في `dotnet test` خاص به (P-7). وبين الملفين يُعاد المكدّس من مجلدات فارغة لأن بذور القبول تُستهلك.
- **طبقة لمرحلة (PLAN-42):** مرحلة في `order[]` تحمل `composeFiles` و`recreate` تُطبَّق ملفاتها على المكدّس الجاري قبل تشغيلها، فتُعاد الخدمات المسمّاة في `recreate` وحدها. وأصنافها تعمل كل صنف وحده كالمجموعة `isolated`، ويُهدم المكدّس قبل استعماله التالي. لذلك تأتي هذه المرحلة آخر مراحل مكدّسها: المجموعة `s2s42` بعد `final` على prod-like.
- **طبقة ثانية على المكدّس نفسه (PLAN-49):** المجموعة `pol49` (`docker-compose.acceptance-pol49.yml`: `Core.Recruitment` بوضع Enforce مع قاعدة القبول `ACC-POL49-WEAK-OFFER`، لسيناريو AC-POL-49f وحده) لها وظيفة مرحلة خاصة بها، `prodlike-policy`، على مكدّس يبدأ لها. فتبقى كل مرحلة بطبقة آخر مراحل مكدّسها في وظيفتها (`Qa066WorkflowTests`). وتسبق `s2s42` في `order[]`، لتبقى في وظائف المراحل آخر مرحلة حيّة، وهي التي يقرأ فحص PII مكدّسها، هي التي حملت المجموعتين `main` و`isolated`. أما التشغيل بلا `-Stage` فيبدأ مكدّساً جديداً بعد كل مرحلة بطبقة، ولذلك يفحص `acceptance-job.ps1` فيه المكدّس الذي عُلّم بطبقة قبل أن يُهدم، ثم المكدّس الأخير. محلياً: `acceptance-job.ps1 -Stage prodLike:pol49`.
- **الحارس:** `scripts/ci/acceptance-guard.ps1` يسقط الوظيفة إن كان في صنف مطلوب نتيجة غير `Passed`، أو سيناريو `Fail`/`Blocked` في مجلده، أو لم يعمل أصلاً، أو ظهر في trx صنف غير مدرج، أو قال trx إن التشغيل أُجهض، أو تعذرت قراءة `result.json`. والأصناف غير المطلوبة تظهر في ملخص الوظيفة فقط.
- **السلبي اليدوي:** `gh workflow run ci.yml --ref main -f simulate_missing=<Class.Method>` يحجب سيناريو مطلوباً، فتحمر الوظيفة وتسمّيه. و`-f classes=<A,B>` يعيد تشغيل أصناف بعينها، للتشخيص فقط: خارج `main` تنتهي الوظيفة حمراء وإن نجحت أصنافها، حتى لا تقف فحصاً أخضر لالتزام فرع. واسم صنف خارج النطاق خطأ.
- **محلياً (المالك):** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile Both`، ويكتب كله في `artifacts/acceptance/run/`، ولا يمس `acceptance-evidence/` المودَع. والمرحلة `meta` (AC-QA-34/35) في CI فقط لأنها تقرأ نتائج وظيفة `CI` للالتزام نفسه. وPowerShell 5.1 لا يكفي لبعض سكربتات AC-01..37 (`??`، وتمرير علامات الاقتباس إلى `docker exec`)، وهي غير مطلوبة.
- **الأثر:** `acceptance-evidence` (ما كتبه التشغيل، والمجلدات التي لم يُعد كتابتها كما هي مودَعة، و`TestResults/`، و`summary.md`، و`job-timing.json`). ويفحصه `scripts/ci/acceptance-artifact-scan.ps1` قبل الرفع: لا كلمة مرور مولّدة ولا سر ولا JWT ولا ملف ذاكرة، وإلا لا يُرفع.

**عقد الأدلة (P-4، D-QA-20، D-QA-21):**

- دليل أي خطة هو أثر تشغيل PR الخاص بها، ورابطه في تقريرها النهائي.
- التشغيل المحلي مسوّدة (`source: local`) ولا يودَع أبداً.
- الخطة التي تحدّث الأدلة المودَعة تنسخها من أثر CI كاملة، وعند تغيّر `acceptance-evidence/**` تشغّل خطوة `Committed acceptance evidence` المدقّق بوضع `-Committed`: الملخص من CI، والتشغيل أخضر من PR أو دفع في هذا المستودع، والشيفرة هي نفسها.
- الحداثة بشجرة الشيفرة لا بالالتزام (`codeTree`: كل المسارات عدا `acceptance-evidence/` و`reports/` و`docs/`)، فإيداع الدليل نفسه لا يجعله قديماً. والدليل `Stale` إن اختلفت الشجرة، أو بُنيت صورة من مصدر آخر أو من شجرة معدّلة، أو مضى عليه أكثر من 30 يوماً.
- حالات `summary.md` بترتيبها: `NotRun`، `Incomplete`، `Stale`، `Fail`/`Blocked`، `Pass`. و`Pass` مع `provesClaim: false` في [`tests/acceptance/ac-ownership.json`](../tests/acceptance/ac-ownership.json) يُعدّ «ناجحاً لا يثبت»، لا مثبتاً.

**ملف إضافي لمجموعة (PLAN-42، P-7):** يجوز لمرحلة في `order` من `ci-scope.json` أن تسمّي `composeFiles` و`recreate`. تطبّق الوظيفة الملفات على المكدس الجاري وتعيد إنشاء تلك الخدمات فقط قبل المرحلة، ثم تعلّم المكدس متغيراً، فتبدأ المرحلة التالية التي تحتاجه من مكدس جديد. مثال: `s2s42` مع `docker-compose.acceptance-s2s42.yml`.

### المراحل المتوازية، وإعادة الاستعمال، وفحوص الحوكمة عند الحاجة (PLAN-66)

**التخطيط.** الفحص المطلوب `Acceptance` صار جامعاً، واسمه لم يتغير:

| الوظيفة | ما تفعله |
|---------|----------|
| `Reuse check` | تكتب هوية التشغيل (`run-identity`)، وتقرر هل يعيد استعمال تشغيل سابق، وهل تعمل فحوص الحوكمة |
| `Acceptance images` | تبني صور الخدمات مرة واحدة بوسم `SOURCE_SHA`، وتسحب صور الأطراف الثالثة مرة واحدة بإعادة محاولة، وتحفظها في أثر `acceptance-images` عمره يوم |
| `Acceptance stage (<name>)` | وظيفة لكل عنصر في `stageJobs[]` من `ci-scope.json`: تحمّل الصور، وترفض صورة لا تحمل الالتزام نفسه، ثم `acceptance-job.ps1 -Stage ... -NoBuild`، وتفحص نتائجها قبل رفعها لأن كلمات مرورها مولَّدة على مشغّلها |
| `Acceptance` | يعمل دائماً. يجمع نتائج المراحل في تخطيط التشغيل المتتابع (`scripts/ci/acceptance-merge.ps1`)، ثم الملخص والمدقق والحارس وفحص الأثر، ويسقط إن لم تنجح وظيفة من الوظائف التي يحتاجها (`scripts/ci/acceptance-needs-check.ps1`) |

- `prodlike-final` تحمل `AcQa41AcceptanceBaselineTests`، وهو يقرأ مجلدات التشغيل ويحتاج مكدساً حياً. فتبدأ مكدسها بالتوازي، وتنتظر نتائج ما سمّته `waitFor`، وتقرأها ولا تعيد رفعها.
- `prodlike-policy` (PLAN-49) تحمل `AcPol49EnforceOverlayTests` وحده: مكدس prod-like جديد، ثم طبقة `pol49` عليه. وهي وظيفة مستقلة لأن الطبقة تغيّر المكدس، ولأن بدء مكدس ثانٍ داخل `prodlike` كان يتجاوز مهلتها.
- `meta` تبدأ مكدسها وتنتظر نتائج `CI` كما كانت.
- `E2E` لا ينتظر `CI`: لم يكن يأخذ منه غير نجاحه.
- دفعة جديدة إلى PR تلغي تشغيله السابق كله (`concurrency` على مستوى سير العمل).

**المرحلة محلياً.** `acceptance-job.ps1 -Stage lab:main` يشغّل تلك المرحلة بمكدسها، ويكتب في `artifacts/acceptance/stages/<name>/`، ويُنزل المكدس في آخرها. و`-Stage` تقبل عدة عناصر `part:group` مفصولة بفاصلة، وتتبع ترتيب `order[]` لا ترتيب كتابتها، واسم مجهول يوقف التشغيل. و`-ListStages` يطبع ما سيعمل. وبلا `-Stage` يعمل كل شيء بالترتيب كما في PLAN-41.

**إعادة الاستعمال (D-QA-66-2).** التزام لا يغيّر شجرة الشيفرة (دليل، تقرير، ملاحظة تقدّم) لا يعيد العمل: `CI` و`E2E` و`Acceptance images` والمراحل تنجح دون عمل وتسمّي التشغيل المعاد في ملخص الوظيفة، و`Acceptance` ينسخ أثر `acceptance-evidence` للتشغيل الأصلي ومعه `reuse.json`. و`CodeQL` و`Gitleaks` وخطوة `Committed acceptance evidence` تعمل دائماً. والقرار في `scripts/ci/acceptance-reuse.ps1`:

- التشغيل المعاد: أحدث تشغيل ناجح لـ PR نفسه، من هذا المستودع، نطاقه `full`، وكل وظائفه نجحت، وشجرته مساوية، و`outsideTree` مساوٍ، وأثر أدلته باقٍ.
- `outsideTree` تجزئة ما يقرؤه التشغيل خارج شجرة الشيفرة: `docs/` كلها عدا `*.md` (ملفات `docs/*.json` محتوى تشغيل، ويقرؤها CI)، وما تقرؤه فحوص الحوكمة. فلولاها لأعاد تغييرٌ فيها استعمال تشغيل لم يره. أما `reports/` و`acceptance-evidence/` ونصوص `docs/**/*.md` فلا تعيد التشغيل.
- سلسلة إعادة الاستعمال تشير دائماً إلى التشغيل الأصلي، فأثر الأدلة الذي تودعه الخطة يبقى من تشغيل كامل.
- لا إعادة استعمال لـ fork، أو دفع، أو تشغيل يدوي أو مجدول، أو مضيَّق، أو فاشل أو ملغى أو مقفَز جزئياً. وأي خطأ يعني تشغيلاً كاملاً.

**فحوص الحوكمة عند الحاجة (D-QA-66-1).** `AcQa34PushableBaselineTests` و`AcQa35RemoteCiTests` تعملان على PR حين يمس ملفاً في `metaRule.triggerPaths` من `ci-scope.json`، وكل ليلة على `main` (`schedule` في `ci.yml`، ولا يتبع `ENABLE_SCHEDULED` لأن القرار يطلبه)، وفي كل تشغيل يدوي. وفي غير ذلك يكتب `Acceptance` الملف `acceptance-evidence/not-run-by-rule.json` بالسبب ورابط آخر تشغيل ليلي، ويظهر قسم «Not run by rule» في `summary.md`. ولا يقبل الحارس والمدقق ذلك إلا لصنفي هذه المرحلة. والصنف الذي يقرأ ملفاً جديداً يضيف مساره إلى `triggerPaths` في الالتزام نفسه.

**ما يفعله مؤلف الخطة قبل الدفع.** ما تطلبه البوابات اليوم (`scripts/ci-local.ps1` ثم `scripts/plan035-prepush.ps1`)، وأصناف القبول التي تمسها الخطة فقط:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile Lab -Classes AcHcm29HireContractTests
```

الحزمة الكاملة تعمل مرة واحدة، في CI. فتقصر التشغيلات المحلية، ويقصر معها حجز `acceptance-stack`.

### سير العمل المحذوفة والمجدولة (PLAN-35)

- حُذف `gateway-ci.yml` (كان يكرر الوحدة والتكامل، ويستعيد مشروعين ثم ينسّق الحل كله، و`e2e-gateway` بلا `--profile full-stack`) و`gitleaks.yml` (مكرر لوظيفة `Gitleaks`). `ci.yml` يغطي مساراتهما.
- المجدولات (`e2e-journeys`، `mutation`، `advisory-to-issue`، `codeql` الأسبوعي، `iac-drift`) لا تعمل بالجدول إلا حين `vars.ENABLE_SCHEDULED == 'true'`؛ يضبطه المالك بعد الاخضرار. `dated-obligations.yml` يعمل أسبوعياً دائماً. `advisory-to-issue` يحتاج أيضاً السر `DEPENDABOT_ALERTS_TOKEN` والمتغير `DEPENDABOT_ALERTS_TOKEN_CONFIGURED=true` (الالتزام `SUP001-DEPENDABOT-ALERTS-TOKEN`)، وسكربته يفشل صراحة عند رفض الطلب.
- حماية `main`: المستودع عام، فتُطبَّق الحماية من الملف عبر `scripts/scm/apply-branch-protection.ps1` بعد دمج PLAN-41، ويبقى حارس pre-push المحلي (`install-hooks.ps1 -ProtectMain`، D-QA-1) بديلًا إن ردّ GitHub بـ 403 (الاستثناء `SCM-NO-GITHUB-PROTECTION`). كل تغيير يصل إلى `main` عبر PR. الفحوص المطلوبة في [`docs/scm-governance/branch-protection-main.json`](scm-governance/branch-protection-main.json): `CI` و`CodeQL` و`Gitleaks` و`E2E` و`Acceptance`.
- فحص ما قبل الدفع: `powershell -File scripts/plan035-prepush.ps1` (gitleaks على التاريخ كاملاً والمدى، actionlint، ملخص ci-local، الملفات الممنوعة، GOV-001-D، التوقيع)، وخطاف اختياري `powershell -File scripts/scm/install-hooks.ps1`.

قائمة تحقق يدوية للواجهة: [UI_CHECKLIST.md](UI_CHECKLIST.md).
