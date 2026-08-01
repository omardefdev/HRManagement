# مسارات الاستثناء — MIG-004

| مسار | السلوك |
|---|---|
| auto-merge عالي المخاطر | فشل validate |
| merge بلا audit / reversible | فشل validate |
| كشف PII عبر جهات للمراجع | فشل validate |
| thresholds بلا توقيع/hash/أدوار متمايزة | فشل validate |
| ادعاء MDM-002 / FRD-001 Done | مرفوض — خارج النطاق |
| ادعاء إغلاق L10N-002 أو live-labelled-metrics | مرفوض — soft blockers |
| ادعاء محرك مطابقة إنتاجي Done | مرفوض — خارج نطاق gate pack |

راجع [ADR-MIG-004](ADR-MIG-004-identity-match-merge.md).
