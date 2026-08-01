# مسارات الاستثناء — EXT-004

| مسار | السلوك |
|---|---|
| توقيع نموذجي كدليل قانوني | فشل validate |
| شهادة revoked / expired بلا LTV كـ valid | فشل validate |
| تصدير مفتاح خاص من HSM | فشل validate |
| خوارزمية deprecated كـ valid | فشل validate |
| ادعاء CA / HSM / TSA حي Done | مرفوض — soft blockers |
| ادعاء HSM إنتاجي حي أو SYD-011 | مرفوض — hsm-prod/SYD-011 Blocked؛ SOV-003 binding ≠ live HSM |

راجع [ADR-EXT-004](ADR-EXT-004-national-pki-signatures-timestamps.md).
