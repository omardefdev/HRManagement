# مسارات الاستثناء — EXT-003

| مسار | السلوك |
|---|---|
| أمر دفع بلا تشغيل Posted/Approved | فشل validate |
| تكرار submit بنفس idempotency مع أثر ثانٍ | فشل validate |
| totals mismatch كـ reconciled Done | فشل validate |
| ملف دفع يدوي بلا تدقيق | فشل validate |
| cutoff بلا contingency hold كـ Settled | فشل validate |
| ادعاء treasury/bank/postal حي Done | مرفوض — soft blockers |
| ادعاء إغلاق EXT-004 (PKI حي) | مرفوض — NotStarted |

راجع [ADR-EXT-003](ADR-EXT-003-treasury-payment-reconciliation.md).
