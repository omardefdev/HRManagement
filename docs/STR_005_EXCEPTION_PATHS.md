# مسارات الاستثناء — STR-005

| مسار | السلوك |
|---|---|
| قرار حرج بلا مالك أو مراجع | فشل validate |
| ownerRole == reviewerRole | فشل validate |
| استثناء بلا expiresOn / حالة معلنة | فشل validate |
| استثناء منتهٍ دون تجديد + مراجعة | فشل validate |
| Go-Live بمقاعد engineering فقط | فشل validate (eng-only fixture يجب أن يُرفض) |
| ادعاء إغلاق SYD-017 بأسماء وهمية في الحزمة | مرفوض — soft blocker يبقى Blocked |
| بناء جدول برنامج كامل داخل STR-005 | خارج النطاق — PGM-001 |
| استبدال سجل GOV-002 القانوني بهذا السجل | مرفوض — نطاقان منفصلان |

استثناءات البذرة في المصفوفة:

| exceptionId | expiresOn | status |
|---|---|---|
| EX-STR005-SIM-LAW-INTERIM | 2026-10-30 | Declared |
| EX-STR005-SIM-INCIDENT-WINDOW | 2026-09-15 | Declared |

راجع [ADR-STR-005](ADR-STR-005-program-governance-boards.md).
