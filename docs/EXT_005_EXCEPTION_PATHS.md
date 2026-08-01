# مسارات الاستثناء — EXT-005

| مسار | السلوك |
|---|---|
| تكامل بلا مالك / SLA | فشل validate |
| breaking بلا major bump | فشل validate |
| مشاركة DB كتكامل | فشل validate |
| كسر مستهلك معتمد بلا major | فشل validate |
| export بغرض غير مسموح | فشل validate |
| schema غير متوافق كمنشور | فشل validate |
| deprecation أقصر من الحد | فشل validate |
| ادعاء bus / registry حي Done | مرفوض — soft blockers |
| ادعاء إغلاق live bus/registry أو EXT-006 داخل الحزمة | مرفوض — Blocked/NotStarted |

راجع [ADR-EXT-005](ADR-EXT-005-gov-data-exchange-api-contracts.md).
