# Runbook احتفاظ GOV-001

## قبل أي purge

1. تأكيد لا يوجد legal hold نشط (Employee / File / Audit).
2. تشغيل dry-run: `python scripts/gov001-retention-dry-run.py`.
3. مراجعة تقرير JSON تحت `artifacts/gov001-retention-dry-run.json`.
4. تفعيل `AllowPurge` فقط لفترة الصيانة المخططة (تدقيق البوابة).

## ترتيب مقترح

1. Inbox receipts (منخفض الحساسية).
2. File PendingPurge بعد RetainUntil.
3. Gateway audit (بعد WORM export).
4. Anonymize موظفين منفصلين بلا hold (DATA-002).

## أدلة تدقيق متوقعة

- أحداث `AuditPurge` في SecurityAudit.
- سجلات HostedService.
- تقرير dry-run مؤرخ.
