# OPS-003 — طوبولوجيا النسخ الاحتياطي وPITR

القرار: [ADR-OPS-003-backup-pitr.md](./ADR-OPS-003-backup-pitr.md). التحقق: [OPS_BACKUP_VERIFICATION.md](./OPS_BACKUP_VERIFICATION.md).  
HA الحي: [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md). DR إقليم ثانوي: **OPS-004** — [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md) / [OPS_DR_BIA_RTO_RPO.md](./OPS_DR_BIA_RTO_RPO.md).

## نطاق البطاقة

| داخل OPS-003 | خارج النطاق |
|---|---|
| WAL/PITR (CNPG → S3) | إقليم ثانوي / DNS failover → **OPS-004** |
| dumps منطقية كاملة (كل قواعد init بما فيها `file_storage_db`) | SIEM كامل → **OBS-003** |
| MinIO mirror + Keycloak realm + DB | Terraform → **OPS-005** |
| كتالوج، checksum، استعادة معزولة، تنبيه تأخر/فساد | |

## أهداف RPO / RTO (نسخ — ليست failover حي)

| مسار | RPO مستهدف | RTO مستهدف (استعادة معزولة) |
|---|---|---|
| CNPG WAL + ScheduledBackup | ≤ 5 دقائق (WAL) / ≤ 24 ساعة (base backup يومي) | ≤ 2 ساعات لاستعادة نقطية drill |
| Logical dump (Compose) | ≤ 24 ساعة | ≤ 4 ساعات لاستعادة كاملة لكل القواعد |
| MinIO object mirror | ≤ 24 ساعة | ≤ 4 ساعات لبادئة اختبار |
| Keycloak realm export | ≤ 24 ساعة (التكوين)؛ الهوية في DB عبر PITR/dump | ≤ 1 ساعة لاستيراد realm |

سجّل القياسات الفعلية بعد كل تدريب في جدول نتائج (انظر [OPS_BACKUP_VERIFICATION.md](./OPS_BACKUP_VERIFICATION.md)).

## Retention tiers

| طبقة | المدة | أين |
|---|---|---|
| Hot | 7 أيام | محلي / بادئة `hot/` |
| Warm | 30 يوماً | دلو `hrm-backups` (افتراضي `BACKUP_RETENTION_DAYS=30`) |
| Cold | 90 يوماً | lifecycle/transition على الدلو (موثّق؛ يُضبط عند المزود) |

Object Lock / WORM: سياسة إلزامية على بادئة الإنتاج؛ stub في overlay إن لم يتوفر المزود بعد.

## مخزن `hrm-backups`

```text
s3://hrm-backups/
  cnpg/           # Barman / CNPG WAL + base
  postgres-dump/  # offsite sync of pg_dump trees
  minio/          # mirrored app buckets
  keycloak/       # realm JSON exports
  catalog/        # backup-catalog.jsonl copies (optional)
```

Secret: `cnpg-backup-store` (S3 endpoint + keys). Encryption: TLS + SSE-S3 أو مفتاح منفصل في `backup-encryption`.

## أدوار least privilege

| دور | الاستخدام |
|---|---|
| Postgres `hrm_backup` | `pg_dump` / CONNECT على قواعد التطبيق — ليس superuser التطبيق | 
| MinIO `backup-reader` | قراءة دلاء التطبيق؛ كتابة `hrm-backups/*` فقط |
| CNPG barman credentials | Secret النسخ فقط — ليس في ConfigMap التطبيقات |

SQL: [`scripts/sql/create-hrm-backup-role.sql`](../scripts/sql/create-hrm-backup-role.sql).

## كتالوج

ملف JSONL (سطر لكل أثر): `takenAt`, `component`, `path`, `checksumSha256`, `bytes`, `expiresAt`, `status`.  
تحديثه من `backup-postgres.sh` / `backup-minio.sh` / Jobs k8s.  
IT.Infra `BackupJobs` يزامن الحالة من الكتالوج عند `Run` (لا ينفّذ dump داخل الويب).

## جرد قواعد PostgreSQL (مصادر الحقيقة)

يطابق [`deploy/bootstrap/ensure-databases.sql`](../deploy/bootstrap/ensure-databases.sql) — **16** قاعدة بما فيها `file_storage_db`.
