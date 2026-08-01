# ADR-QA-003 — تكامل حقيقي للعقود والبنية التحتية

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | QA-003 (البطاقة 42) |
| التاريخ | 2026-07-28 |

## السياق

QA-001/002 ثبّتا بوابات CI والتغطية. أدلة outbox/inbox/الملفات/الصلاحيات ما زالت تعتمد غالباً على SQLite وMassTransit InMemory. Testcontainers يغطي PostgreSQL فقط وبشكل اختياري في CI.

## القرار

1. **طبقة إثبات QA-003 = Testcontainers** على clone نظيف + Docker.
2. **صور مثبتة:** `postgres:16-alpine`، `rabbitmq:3.13-management-alpine`، `quay.io/keycloak/keycloak:24.0`، `minio/minio:RELEASE.2025-04-22T22-12-26Z` (مطابقة compose).
3. **ترحيلات:** `MigrateAsync` فقط كدليل إنتاج — empty→head و N−1→head؛ ليس `EnsureCreated`.
4. **عزل:** حاوية PG مشتركة + `CreateDatabaseAsync` لكل اختبار/مجموعة؛ مجموعات xUnit للتبعيات المشتركة.
5. **CI:** `Category=Testcontainers` إلزامي على كل PR/push للفروع المحمية.
6. **SQLite/InMemory** تبقى لمسار سريع؛ لا تغلق وحدها صفاً إنتاجياً في [QA_REAL_INFRA_TOPOLOGY.md](./QA_REAL_INFRA_TOPOLOGY.md).
7. **خارج النطاق:** Playwright رحلات (QA-004)؛ SBOM/توقيع (SUP-001)؛ Toxiproxy/game-day (NFR-002 Done).

## العواقب

- CI أطول ويتطلب Docker على الـrunner.
- Suites TC جديدة لـRabbit/Keycloak/MinIO بجانب PostgreSQL.
