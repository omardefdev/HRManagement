# تحقق رحلات E2E (QA-004)

## أوامر محلية

```powershell
# Seed personas: import docker/keycloak/realm-qa004-e2e.json users into realm (lab)
# أو عيّن E2E_PERSONA_*_PASSWORD

docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait

$env:RUN_PLAYWRIGHT_E2E = "true"
$env:GATEWAY_BASE_URL = "http://localhost:5000"
dotnet test src/Tests/HRManagement.E2E/HRManagement.E2E.csproj -c Release `
  --filter "Category=Journey|FullyQualifiedName~E2eSeedPackTests"
```

Smoke سريع (وسم PR `e2e`): بدون filter — يشمل Gateway smoke.

## Pre-release على صورة الإصدار

```powershell
$env:IMAGE_TAG = "<sha12>"
$env:GITHUB_REPOSITORY_OWNER = "<owner>"
docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml up -d --wait
# ثم نفس أوامر الاختبار أعلاه
```

أو workflow: `.github/workflows/e2e-journeys.yml` (`schedule` nightly / `workflow_dispatch`).

## بوابة الإغلاق

| تحقق | دليل |
|---|---|
| مصفوفة happy+≥3 fail/auth | [QA_E2E_JOURNEY_MATRIX.md](./QA_E2E_JOURNEY_MATRIX.md) |
| Skip عند إطفاء العلم | `E2eEnv.RequireEnabled` |
| Fail عند غياب المكدس مع العلم | `EnsureGatewayReachableAsync` |
| 503/5xx ≠ نجاح | `E2eHttp.EnsureNotServerError` |
| Seed إصداري 1.0.0 | `testdata/e2e/manifest.json` + `E2eSeedPackTests` |
| Traces/screenshots | `PlaywrightFixture` → `TestResults/e2e-artifacts` |
| Nightly/pre-release | `e2e-journeys.yml` |
| UAT | مرجع FUN-001..؛ ليس توقيع هذه البطاقة |

## مراجع

- [ADR-QA-004-e2e-journeys.md](./ADR-QA-004-e2e-journeys.md)
- [CI_AND_TESTING.md](./CI_AND_TESTING.md)
