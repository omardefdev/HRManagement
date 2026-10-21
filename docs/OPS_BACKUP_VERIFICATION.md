# OPS-003 — قائمة تحقق النسخ وPITR والاستعادة

طوبولوجيا: [OPS_BACKUP_TOPOLOGY.md](./OPS_BACKUP_TOPOLOGY.md). سكربت: [`scripts/verify-backup-catalog.sh`](../scripts/verify-backup-catalog.sh).  
DR إقليم ثانوي = **OPS-004**.

## قبل التدريب

- [ ] جرد `backup-postgres.sh` يشمل `file_storage_db` (16 قاعدة)
- [ ] دور `hrm_backup` منشأ ([`scripts/sql/create-hrm-backup-role.sql`](../scripts/sql/create-hrm-backup-role.sql))
- [ ] Secret `cnpg-backup-store` / `backup-encryption` ليست أسرار التطبيق
- [ ] `kubectl kustomize deploy/k8s/data/overlays/production-data-ha` يظهر `ScheduledBackup` + `barmanObjectStore`
- [ ] CronJobs `backup-minio` / `backup-keycloak-realm` مجدولة

## Logical dump

- [ ] تشغيل dump ناجح + `checksums.sha256`
- [ ] تلف متعمد لملف dump → `restore-postgres.sh` أو verify يفشل على checksum
- [ ] استعادة قاعدة واحدة في بيئة معزولة عبر `scripts/restore-postgres.sh --create-db`

## PITR (CNPG)

- [ ] WAL يصل إلى `s3://hrm-backups/cnpg`
- [ ] `RECOVERY_TARGET_TIME=... bash scripts/restore-pitr-drill.sh` ينجح أو يوثّق السبب
- [ ] قياس RTO الفعلي مسجّل أدناه

## MinIO / Keycloak / FileStorage

- [ ] مرآة buckets بما فيها `file-storage` وquarantine
- [ ] `restore-minio-drill.sh` يطابق عدد الكائنات
- [ ] realm export في الكتالوج؛ `keycloak_db` في dump/PITR

## كتالوج وتنبيه

- [ ] `backup-catalog.jsonl` يُحدَّث
- [ ] IT.Infra `BackupJobs` → Run يزامن من الكتالوج (لا stub صامت)
- [ ] `verify-backup-catalog.sh` يفشل إذا تجاوز العمر العتبة أو status≠ok

## نتائج قياس (املأ بعد التدريب)

| التاريخ | المسار | RPO ملحوظ | RTO ملحوظ | ملاحظات |
|---|---|---|---|---|
| 2026-08-24 | AC-34 acceptance-backup-restore.ps1 | logical dump | see acceptance-evidence/AC-34/timing.json | relationship-checks.json |
| | dump كامل | | | |
| | PITR drill | | | |
| | MinIO drill | | | |

**PLAN-13 (AC-34):** `pwsh scripts/acceptance-backup-restore.ps1` — logical backup + throwaway restore + `verify-restore-relationships.sh` → `acceptance-evidence/AC-34/`.

## إغلاق البطاقة

- [ ] ADR + topology + verification
- [ ] الإشارة: **DR الإقليم الثانوي = OPS-004**
