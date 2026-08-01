# مسارات الاستثناء — MIG-001

| مسار | السلوك |
|---|---|
| مصدر في universe بلا جرد/استثناء | فشل validate |
| Inventoried مع estimateOnly=true | فشل validate |
| Inventoried بلا accessRisks | فشل validate |
| حقل required بلا SoT | فشل validate |
| profiling بلا agencySigned | فشل validate |
| Unsupported بلا استثناء موقع | فشل validate |
| ادعاء MIG-003 Done / استخراج جهة حي | مرفوض — soft blockers |
| تعديل DataImportController كبديل للجرد | خارج النطاق |

راجع [ADR-MIG-001](ADR-MIG-001-legacy-source-inventory-field-sot.md).
