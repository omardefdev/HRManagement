# مسارات الاستثناء — MIG-002

| مسار | السلوك |
|---|---|
| قيمة بلا disposition Map/Transform/Quarantine | فشل validate |
| تفريغ جماعي إلى Other | فشل validate |
| mapping بلا توقيع/hash/version | فشل validate |
| EmpNo/NAT_ID كمفتاح auth أو tenant | فشل validate |
| ادعاء MIG-003 ETL Done | مرفوض — خارج النطاق |
| ادعاء إغلاق SYR-003/MDM-001 | مرفوض — soft blockers |

راجع [ADR-MIG-002](ADR-MIG-002-canonical-model-code-maps.md).
