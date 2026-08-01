# تحقق البنية التحتية الحقيقية (QA-003)

## أوامر محلية

```powershell
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj -c Release `
  --filter "Category=Testcontainers" --blame-hang-timeout 15m
```

## جدول بوابة الإغلاق

| تحقق | اختبار / دليل |
|---|---|
| empty→head migrations | `Qa003MigrationUpgradeTests` |
| N−1→head | `Payroll_NMinus1_To_Head_Upgrades` / Contracts |
| عزل DB متوازٍ | `Qa003ParallelDbIsolationTests` |
| Invariants PG | `Data001ConcurrencyMatrixTests` |
| Inbox duplicate | `Inbox_Duplicate_Message_Single_Effect_On_Postgres` |
| Outbox + broker down | `Outbox_Publish_Persists_When_Broker_Unavailable` |
| PG down | `Postgres_Unavailable_Inbox_Does_Not_Succeed_Silently` |
| Token matrix | `Qa003KeycloakTokenMatrixTests` |
| Keycloak down | `Keycloak_Stopped_Token_Fetch_Fails` |
| MinIO scan path | `Qa003MinioFileScanTests` |
| MinIO down | `Minio_Stopped_Upload_Fails_Explicitly` |
| CI إلزامي | `.github/workflows/ci.yml` خطوة Testcontainers بلا شرط وسم |
| Artifacts | TRX تحت `TestResults/integration-tc`؛ بلا أسرار في fixtures (realm-qa003-test secrets ثابتة للاختبار فقط) |

## Clean clone

على runner/جهاز نظيف: .NET SDK + Docker + `git clone` + `dotnet restore` + الأوامر أعلاه — دون أسرار إنتاج.

## مراجع

- [ADR-QA-003-real-infra-tests.md](./ADR-QA-003-real-infra-tests.md)
- [QA_REAL_INFRA_TOPOLOGY.md](./QA_REAL_INFRA_TOPOLOGY.md)
- [CI_AND_TESTING.md](./CI_AND_TESTING.md)
