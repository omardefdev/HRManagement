# مسارات الاستثناء — MIG-003

| مسار | السلوك |
|---|---|
| ادعاء ETL حي / عمال إنتاج | مرفوض — خارج نطاق hybrid factory gate pack |
| PII في عيّنة logs | فشل validate |
| إعادة نفس الدفعة تُنشئ duplicates | فشل validate |
| استئناف بلا checkpoint | فشل validate |
| staging بلا تشفير / بلا tenant scope | فشل validate |
| schema فاسد مع silent partial commit | فشل validate |
| ادعاء إغلاق agency-extract-access أو live-cutover-perf | مرفوض — soft blockers |
| ادعاء إغلاق MIG-004/005/006 | مرفوض — خارج النطاق |

راجع [ADR-MIG-003](ADR-MIG-003-secure-reentrant-migration-factory.md).
