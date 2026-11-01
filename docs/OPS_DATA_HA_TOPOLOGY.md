# OPS-002 — طوبولوجيا HA لمستوى البيانات

القرار: [ADR-OPS-002-data-ha.md](./ADR-OPS-002-data-ha.md). التحقق: [OPS_DATA_HA_VERIFICATION.md](./OPS_DATA_HA_VERIFICATION.md). تطبيقات: [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md).

## نطاق البطاقة

| داخل OPS-002 | خارج النطاق |
|---|---|
| Postgres replication/failover + PgBouncer | PITR / نسخ خارج الموقع / استعادة كاملة → **OPS-003** |
| RabbitMQ 3-node + quorum + publisher confirms | إقليم ثانوي / DR → **OPS-004** |
| Keycloak ISPN cluster | Terraform كامل → **OPS-005** |
| MinIO distributed erasure + versioning | تغيير منطق الأعمال |
| TLS داخلي موثّق؛ لا shared superuser | |

Compose للتطوير = عقدة واحدة لكل مكوّن. **إنتاج بيانات HA =** [`deploy/k8s/data/`](../deploy/k8s/data/).

## Namespaces

| Namespace | المحتوى |
|---|---|
| `hrmanagement` | Gateway + Web apps (OPS-001) |
| `hrmanagement-data` | Postgres / RabbitMQ / Keycloak / MinIO (OPS-002) |

## رسم الاتصالات

```text
  [hrmanagement apps]
        |         |          |           |
        v         v          v           v
   pg-pooler   rabbitmq   keycloak     minio
   (PgBouncer)  :5672      :8080       :9000
        |
        v
   hr-postgres (CNPG: primary + 2 replicas)
```

## خدمات DNS (ClusterIP)

| Service | الغرض | المنفذ |
|---|---|---|
| `hr-postgres-rw` | كتابة مباشرة (CNPG) | 5432 |
| `hr-postgres-r` | قراءة (replicas) | 5432 |
| `hr-postgres-pooler-rw` | كتابة عبر PgBouncer — **المسار الافتراضي للتطبيقات** | 5432 |
| `rabbitmq` | AMQP (load-balanced) | 5672 |
| `rabbitmq-headless` | اكتشاف أقران | 5672 |
| `keycloak` | OIDC / admin HTTP | 8080 |
| `minio` | S3 API | 9000 |
| `minio-headless` | distributed peer DNS | 9000 |

FQDN مثال: `hr-postgres-pooler-rw.hrmanagement-data.svc.cluster.local`.

## أهداف RPO / RTO (failover حي)

| مكوّن | RPO مستهدف (حي) | RTO مستهدف | آلية |
|---|---|---|---|
| PostgreSQL | ≈ 0 (sync replica حسب إعداد CNPG) | ≤ 60s لترقية primary | CNPG fencing + pooler reconnect |
| RabbitMQ | 0 للرسائل المؤكَّدة (quorum + confirms) | ≤ 30s بعد فقد عقدة واحدة | quorum (n=3) |
| Keycloak | جلسات حسب cache؛ الهوية في Postgres HA | ≤ 30s | 3 pods + ISPN |
| MinIO | 0 ضمن تحمّل erasure (فقد عقدة واحدة من 4) | ≤ 60s | EC 4 nodes |

النسخ الاحتياطي المنطقي وPITR وقياس RPO خارج الموقع = **OPS-003** — انظر [OPS_BACKUP_TOPOLOGY.md](./OPS_BACKUP_TOPOLOGY.md).

## لا shared superuser

| ممنوع في مسار التطبيقات (إنتاج HA) | البديل |
|---|---|
| دور `postgres` / مالك العنقود للتطبيقات | أدوار لكل خدمة (`hr_core_app`, …) عبر Secret |
| `guest` على RabbitMQ | مستخدمو `mq_*` من `hrm-bootstrap` (الخطوة 6، مصفوفة `deploy/bootstrap/rabbitmq-users.json`، PLAN-38) |
| `minioadmin` / root في FileStorage | `MINIO_FILESTORAGE_*` من `scripts/init-minio-filestorage-user.sh` |
| سر Keycloak admin في pods التطبيق | عملاء OIDC فقط؛ admin عبر قناة مقيّدة |

## Secrets المتوقعة (`hrmanagement-data`)

| Secret | محتوى أساسي |
|---|---|
| `hr-postgres-superuser` | مستخدم/كلمة مرور مشغّل CNPG (ليس للتطبيقات) |
| `hr-postgres-app` | كلمات مرور أدوار الخدمات + connection fragments |
| `rabbitmq-default-user` | مستخدم الإدارة الأولي |
| `rabbitmq-service-users` | كلمات `mq_*` (أو تُنشأ بـJob) |
| `keycloak-db` / `keycloak-admin` | JDBC + حساب إدارة |
| `minio-root` | جذر التهيئة فقط |
| `minio-filestorage` | مفاتيح تطبيق FileStorage |
| `data-tls` (اختياري) | شهادات TLS داخلية إن لم يُستخدم cert-manager |

تطبيقات `hrmanagement` تستهلك نُسخاً/مراجعاً عبر ConfigMap/Secret في namespace التطبيقات (لا نسخ كلمة مرور الـsuperuser).

## Npgsql بعد failover

- الاتصال الافتراضي: `Host=hr-postgres-pooler-rw.hrmanagement-data.svc.cluster.local;Port=5432;…;TargetSessionAttrs=read-write;Timeout=15;Keepalive=30`
- عند ترقية primary يعيد Npgsql فتح الاتصالات الفاشلة؛ الـpooler يوجّه إلى الـprimary الجديد.
- Migrate Job يتصل بالـ**RW** (pooler أو `hr-postgres-rw`) مع advisory lock الحالي — ليس بكل replica.

## RabbitMQ وMassTransit

- `RabbitMQ__Host=rabbitmq.hrmanagement-data.svc.cluster.local`
- طوابير الاستقبال: `x-queue-type=quorum` (قابل للتعطيل بـ`RabbitMQ:UseQuorumQueues=false` للتطوير إن لزم).
- `PublisherConfirmation=true` على الـhost.
- HPA للتطبيقات لا يغيّر نوع الطابور؛ Inbox/Outbox + lease يمنعان الأثر المزدوج.

## MinIO

- 4 pods + PVC؛ الأمر الموزّع عبر `minio-headless`.
- Versioning مفعّل على buckets التطبيق (Job تهيئة أو سكربت `init-minio-filestorage-user.sh` موسَّع).
- Endpoint التطبيقات: `minio.hrmanagement-data.svc.cluster.local:9000`.

## TLS

- Ingress Keycloak/Edge كما في OPS-001.
- بين المكوّنات: cert-manager أو سر `data-tls` موثّق في overlay الإنتاج؛ لا تعريض منافذ إدارة MinIO/Rabbit للعامة (SEC-001).

## علاقة OPS-001

حدّث ConfigMap التطبيقات (`hr-app-config`) ليشير إلى خدمات `hrmanagement-data` بدل stubs. الطوبولوجيا التطبيقية تبقى في [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md).
