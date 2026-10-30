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
| E2E Playwright smoke | اختياري | وسم `e2e` على PR |
| E2E Journeys (QA-004) | nightly / pre-release | `.github/workflows/e2e-journeys.yml`؛ صور الإصدار عند `IMAGE_TAG` |
| E2E axe a11y (UX-002) | مع `RUN_PLAYWRIGHT_E2E=true` | `A11yAxeE2ETests` + `testdata/e2e/axe.min.js` |
| CodeQL / Gitleaks | نعم (workflows منفصلة) | `.github/workflows/codeql.yml`, `gitleaks.yml` (SUP-001) |
| Deploy SBOM + Cosign verify | مسار الإصدار | `deploy.yml`؛ انظر [SUP_001_VERIFICATION.md](./SUP_001_VERIFICATION.md) |
| CSP / security headers | نعم (runtime) | `UseHrSecurityHeaders`؛ انظر [UX_001_VERIFICATION.md](./UX_001_VERIFICATION.md) |

### `gateway-ci.yml` (مسارات Gateway/Shared/Tests)

نفس بوابات format + build + unit + integration + coverage gate (متوافق مع `ci.yml` بعد QA-001).

قائمة تحقق يدوية للواجهة: [UI_CHECKLIST.md](UI_CHECKLIST.md).
