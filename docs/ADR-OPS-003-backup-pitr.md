# ADR-OPS-003 — نسخ احتياطي وPITR

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OPS-003 |
| التاريخ | 2026-07-28 |

## السياق

OPS-002 وفّر HA حياً (CNPG / Rabbit / Keycloak / MinIO) دون WAL/PITR أو نسخ خارج الموقع. النسخ الحالية منطقية (`pg_dump`) وتغفل `file_storage_db`؛ `BackupJobs` في IT.Infra metadata فقط.

## القرار

1. **PITR للإنتاج k8s:** CloudNativePG `barmanObjectStore` + `ScheduledBackup` إلى دلو S3-compatible `hrm-backups` عبر Secret `cnpg-backup-store`.
2. **Compose / مضيف أحادي:** `pg_dump -Fc` يومي لكل قواعد `init-databases.sql` (بما فيها `file_storage_db`) + checksum + كتالوج؛ مزامنة اختيارية عبر `backup-offsite-sync.sh`.
3. **مفاتيح منفصلة:** Secret `backup-encryption` / بيانات اعتماد مخزن النسخ ≠ أسرار التطبيق.
4. **Retention:** hot 7 / warm 30 / cold 90 يوماً (lifecycle على الدلو + متغيرات محلية).
5. **MinIO + Keycloak:** مرآة buckets إلى بادئة النسخ؛ realm export مجدول؛ `keycloak_db` ضمن dump/PITR.
6. **صلاحيات:** دور Postgres `hrm_backup`؛ مستخدم MinIO `backup-reader` للقراءة من دلاء التطبيق والكتابة إلى `hrm-backups`.
7. **DR إقليم ثانوي / تمارين موقع كامل = OPS-004.**

## البدائل المرفوضة

| بديل | سبب الرفض |
|---|---|
| الاعتماد على dumps فقط في الإنتاج k8s | لا يحقق PITR / RPO الدقيق |
| تخزين WAL داخل دلاء بيانات التطبيق | خطر خلط وفقدان عزل WORM |
| تشغيل pg_dump من داخل process IT.Infra Web | سطح هجوم وصلاحيات واسعة |

## العواقب

- تطبيقات النسخ تكتب `backup-catalog.jsonl`؛ الواجهة تزامن الحالة من الكتالوج ولا تدّعي تنفيذاً صامتاً.
- تحقق دوري: `scripts/verify-backup-catalog.sh` يفشل عند تأخر/فساد checksum.
