# مسارات الاستثناء — REL-003

| مسار | السلوك |
|---|---|
| خدمة بلا ownerRole / runbookPath / escalationPath | فشل Rel003* catalog |
| onCallTier خارج platform\|payroll\|sre\|shared | فشل schema |
| MTTA/MTTR فوق الهدف بلا waiverId | `rel003-incident-game-day` يفشل |
| نقص acknowledgedAt أو resolvedAt | فشل game day |
| تغيير Normal/Emergency بلا خطة تراجع | مرفوض حسب CHANGE_PROCESS |
| ادعاء إغلاق SUP-002 / SRE-001 / OPS-007 داخل REL-003 | مرفوض — تبقى Blocked/خارج النطاق |
| Postmortem action status خارج Open\|Done | فشل تحقق CSV |

راجع [ADR-REL-003](ADR-REL-003-ops-support-sm.md).
