# ADR-OPS-002 — HA لمستوى البيانات

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OPS-002 |
| التاريخ | 2026-07-28 |

## السياق

OPS-001 ثبّت Kubernetes لتطبيقات Gateway/Web (stateless) وأبقى PostgreSQL وRabbitMQ وKeycloak وMinIO خارج نطاق التكرار. اليوم كلها أحادية العقدة في Compose؛ أي فقد node يوقف البيانات.

## القرار

1. **Namespace بيانات منفصل** `hrmanagement-data` عبر [`deploy/k8s/data/`](../deploy/k8s/data/) (Kustomize: `base` + `staging-data-ha` / `production-data-ha`).
2. **PostgreSQL:** CloudNativePG — Cluster بـ primary + 2 replicas + Pooler (PgBouncer) للمسار الكتابي.
3. **RabbitMQ:** عنقود 3 عقد؛ طوابير **quorum** و**publisher confirms** من MassTransit.
4. **Keycloak:** 3 replicas في الإنتاج مع `KC_CACHE=ispn` واكتشاف K8s؛ JDBC إلى Postgres HA.
5. **MinIO:** 4 خوادم distributed (erasure) + object versioning على buckets التطبيق.
6. **Compose** يبقى أحادي العقدة للتطوير المحلي (غير HA للبيانات).
7. **PITR / نسخ خارج الموقع / استعادة كاملة** = **OPS-003**. الإقليم الثانوي = **OPS-004**.

## البدائل المرفوضة

| بديل | سبب الرفض |
|---|---|
| إبقاء بيانات Compose فقط مع replicas يدوية | لا multi-AZ ولا fencing موحّد مع تطبيقات OPS-001 |
| Helm فقط دون مانيفستات في المستودع | يتعارض مع أسلوب OPS-001 (Kustomize أولاً) |
| Postgres داخل كل Pod تطبيق | يكسر الترحيل الموحّد والـadvisory lock |

## العواقب

- تطبيقات `hrmanagement` تتصل عبر DNS خدمات `*.hrmanagement-data.svc.cluster.local` (انظر [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md)).
- لا تستخدم التطبيقات حسابات superuser (`postgres` / `minioadmin` / guest) في الإنتاج.
- مشغّل CNPG يُثبَّت قبل تطبيق `Cluster` CR (سكربت `scripts/apply-cnpg-operator.sh`).
