# طوبولوجيا البنية التحتية الحقيقية (QA-003)

مرجع القرار: [ADR-QA-003-real-infra-tests.md](./ADR-QA-003-real-infra-tests.md).

## صور مثبتة

| تبعية | صورة |
|---|---|
| PostgreSQL | `postgres:16-alpine` |
| RabbitMQ | `rabbitmq:3.13-management-alpine` |
| Keycloak | `quay.io/keycloak/keycloak:24.0` |
| MinIO | `minio/minio:RELEASE.2025-04-22T22-12-26Z` |

## مصفوفة مسار → حاويات → دليل TC

| مسار إنتاجي | حاويات | اختبار TC (إغلاق) | كان SQLite/InMemory فقط؟ |
|---|---|---|---|
| ترحيل Payroll/Contracts/HR.Core/File/Movements | PG | `Qa003MigrationUpgradeTests` | جزئياً (migrate فقط لبعضها) |
| عزل DB متوازٍ | PG | `Qa003ParallelDbIsolationTests` | — |
| Invariants DATA-001 P0/P1 | PG | `Data001ConcurrencyMatrixTests` + matrix QA-002 | كان TC اختيارياً |
| Outbox ذري + بقاء الصف عند سقوط الوسيط | PG + Rabbit | `Qa003OutboxInboxRealInfraTests` | نعم (`OutboxAtomicity*`) |
| Inbox تكرار رسالة → أثر واحد | PG (+ Rabbit عند النشر) | `Qa003OutboxInboxRealInfraTests` | نعم (`InboxIdempotency*`) |
| JWT audience/صلاحية | Keycloak | `Qa003KeycloakTokenMatrixTests` | نعم (Authority نصي/mocks) |
| رفع ملف + مسح EICAR على تخزين حقيقي | PG + MinIO | `Qa003MinioFileScanTests` | نعم (`File002*` محلي) |
| إيقاف كل تبعية | الكل | suites أعلاه + [QA_REAL_INFRA_VERIFICATION.md](./QA_REAL_INFRA_VERIFICATION.md) | — |

## عقد التشغيل

```text
RUN_TESTCONTAINERS=true
dotnet test ... --filter "Category=Testcontainers"
```

Smoke سريع (غير إغلاق AC): `--filter Category!=Testcontainers`.
