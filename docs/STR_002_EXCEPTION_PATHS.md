# مسارات الاستثناء — STR-002

| مسار | السلوك |
|---|---|
| ادّعاء كتالوج قانوني وطني دون حزمة STR-002 | مرفوض |
| `Implemented` مع LEGAL_TBD في evidence/test | فشل validate |
| confidence D بلا Blocked-Legal أو exception | فشل validate |
| نقص أحد المجالات العشرة | فشل validate |
| نسخ قرارات GOV-002 داخل control-catalog | مرفوض — إشارة فقط |
| إغلاق SYR-001 / SYD / MoF داخل STR-002 | مرفوض — تبقى Blocked-Legal |
| استثناء منتهٍ دون تجديد لصف Partial يعتمد عليه | No-Go readiness |

راجع [ADR-STR-002](ADR-STR-002-legal-control-catalog.md).
