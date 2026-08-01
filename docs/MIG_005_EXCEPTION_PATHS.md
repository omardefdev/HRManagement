# مسارات الاستثناء — MIG-005

| مسار | السلوك |
|---|---|
| gap بلا سبب / غير مفسّر | فشل validate |
| فرق رواتب غير مفسر | فشل validate |
| evidence hash mismatch / قابل للتعديل بلا كشف | فشل validate |
| sign-off بلا dual control | فشل validate |
| ادعاء EVD-003 / MIG-006 Done | مرفوض — خارج النطاق |
| ادعاء إغلاق agency-extract-access أو live-agency-recon أو live-independent-sql | مرفوض — soft blockers |

راجع [ADR-MIG-005](ADR-MIG-005-migration-reconciliation-evidence.md).
