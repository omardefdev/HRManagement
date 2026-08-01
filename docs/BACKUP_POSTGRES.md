# نسخ PostgreSQL احتياطي واستعادة (مختصر تشغيلي)

للـ**failover الحي** (CNPG / pooler) انظر [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md) (OPS-002).  
للـ**PITR والنسخ خارج الموقع والكتالوج والتدريبات** انظر [OPS_BACKUP_TOPOLOGY.md](./OPS_BACKUP_TOPOLOGY.md) و[OPS_BACKUP_VERIFICATION.md](./OPS_BACKUP_VERIFICATION.md) (OPS-003). ADR: [ADR-OPS-003-backup-pitr.md](./ADR-OPS-003-backup-pitr.md).

## نسخ احتياطي منطقي (pg_dump)

يفضّل دور `hrm_backup` ([`scripts/sql/create-hrm-backup-role.sql`](../scripts/sql/create-hrm-backup-role.sql)):

```bash
BACKUP_PGUSER=hrm_backup BACKUP_PGPASSWORD=... ./scripts/backup-postgres.sh ./backups/postgres
```

الجرد = **16** قاعدة تطابق `docker/init-databases.sql` بما فيها **`file_storage_db`**. يُكتب `checksums.sha256` ويُحدَّث `backup-catalog.jsonl`.

### قواعد الخدمات (أسماء افتراضية)

| الخدمة | قاعدة البيانات |
|--------|----------------|
| FileStorage | `file_storage_db` |
| HR.Core | `hr_core_db` |
| HR.Contracts | `hr_contracts_db` |
| Payroll | `payroll_db` |
| Keycloak | `keycloak_db` |
| … | القائمة الكاملة في `scripts/backup-postgres.sh` |

## PITR (إنتاج Kubernetes)

CloudNativePG: `barmanObjectStore` + `ScheduledBackup` في `deploy/k8s/data/base/postgres/`. Secret: `cnpg-backup-store`. تدريبات: `scripts/restore-pitr-drill.sh`.

## جدولة (إنتاج)

سكربتات Cron/Compose/`CronJob` تنفّذ النسخ وتكتب الكتالوج. **IT.Infra `BackupJobs` يزامن الحالة من الكتالوج** عند `Run` — لا ينفّذ `pg_dump` داخل عملية الويب.

### ربط تشغيلي

1. **Compose:** `--profile backup` → `postgres-backup` (+ MinIO عند التفعيل).
2. **k8s:** CNPG ScheduledBackup + CronJobs لـMinIO/Keycloak.
3. **Offsite:** `scripts/backup-offsite-sync.sh` يرفع الأشجار المحلية إلى `s3://hrm-backups/`.
4. **اختبار استعادة:** شهرياً — انظر [OPS_BACKUP_VERIFICATION.md](./OPS_BACKUP_VERIFICATION.md).

## استعادة dump

```bash
./scripts/restore-postgres.sh --host localhost --port 5433 --user hrmanagement --db hr_core_db --file ./backups/postgres/.../hr_core_db.dump
```

## استعادة تجريبية (يدوي)

```bash
pg_restore -h localhost -U hrmanagement -d hr_core_restore --clean --if-exists hr_core_db.dump
```
