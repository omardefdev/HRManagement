# جدول الاحتفاظ (GOV-001)

| policyId | الوصف | أيام / حدث | التنفيذ |
|---|---|---|---|
| ret-employee-lifecycle | موظف منفصل → anonymize | حدث فصل + hold | DATA-002 `TryAnonymize` |
| ret-medical | سجلات طبية | 3650 | تشغيل يدوي/مستقبلي؛ hold يمنع |
| ret-complaint | شكاوى | 2555 | تشغيل يدوي/مستقبلي |
| ret-payroll | ملفات أجور | 3650 | أرشيفي / سياسة مالية |
| ret-directory | حسابات دليل | مع دورة الحساب | Auth.Directory + IdP |
| ret-files-purge | ملفات soft-delete | PendingPurge + RetainUntil | File purge worker |
| ret-audit-gateway | تدقيق البوابة | `AuditRetention:RetentionDays` | `GatewayAuditRetentionService` (AllowPurge) |
| ret-inbox | إيصالات inbox | `Inbox:RetentionDays` (افتراضي 90) | `InboxRetentionHostedService` |
| ret-recruitment-application | طلبات توظيف (زوج: Recruitment + Citizen/Internal) | `Recruitment:Retention:Months` (افتراضي 36 شهراً) من إغلاق الشاغر أو إلغائه، وإلا من تاريخ التقديم؛ لا يُحتسب لشاغر مفتوح | `CandidateRetentionJob` أسبوعي (تجريبي ما لم `Recruitment:Retention:Apply`) + `CandidateLifecycle/Index`؛ `TryAnonymize` |
| ret-archive-docs | أرشيف | RetentionYears على التصنيف | Archive rules |

مرجع: [GOV_001_RETENTION_RUNBOOK.md](./GOV_001_RETENTION_RUNBOOK.md)، dry-run: `scripts/gov001-retention-dry-run.py`.
