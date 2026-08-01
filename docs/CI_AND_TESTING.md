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

## مطابقة CI محلياً

```powershell
dotnet format HRManagement.slnx --verify-no-changes
dotnet build HRManagement.slnx -c Release
Remove-Item -Recurse -Force ./TestResults -ErrorAction SilentlyContinue
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj -c Release --no-build --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults/unit
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj -c Release --no-build --filter "Category!=Testcontainers" --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults/integration
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj -c Release --no-build --filter "Category=Testcontainers" --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults/integration-tc
.\scripts\check-coverage.ps1 -CoverageThreshold 60
dotnet list HRManagement.slnx package --vulnerable --include-transitive
# أو: bash scripts/check-nuget-vulnerabilities.sh
# bash scripts/check-base-image-pins.sh && bash scripts/check-supply-chain-exceptions.sh && bash scripts/check-license-policy.sh
docker compose build
```

## CI

### `ci.yml` (كل PR/push على `main`)

| البوابة | إلزامية؟ | ملاحظات |
|---------|----------|---------|
| Format | نعم | `dotnet format --verify-no-changes` |
| Build | نعم | الحل بالكامل |
| Unit + coverage | نعم | `coverlet.runsettings` |
| Integration (InMemory) + coverage | نعم | بدون Testcontainers |
| Testcontainers (PG/Rabbit/Keycloak/MinIO) | نعم | `RUN_TESTCONTAINERS=true`؛ QA-003 |
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
