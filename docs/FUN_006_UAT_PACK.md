# FUN-006 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | إنشاء عنصر رقمي بتصنيف ret-archive-docs | checklist Classified/Ingested + custody | |
| U2 | PlaceLegalHold ثم RequestDisposal | رفض | |
| U3 | RequestDisposal ثم Approve بنفس المستخدم | رفض SoD | |
| U4 | Approve بمستخدم مختلف بعد انتهاء الاحتفاظ | DisposalRecord + ApprovedBy | |
| U5 | ربط DigitalFile بدون checksum | رفض | |
| U6 | AttestCompleteness | CompletenessAttestation ثابت | |
| U7 | EmployeeCreated يكتب OrgUnitId | حدث قابل للتصفية بالجهة | |
| U8 | Rebuild مرتين | نفس fingerprint | |
| U9 | مستخدم فرع gap-analysis لجهة أخرى | Forbid | |
| U10 | مستخدم فرع بدون طلب إجمالي وطني | أرقام الجهة فقط | |
| U11 | Export workforce CSV | 202 + تحميل | |
| U12 | Export archive inventory | CSV جرد | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي ضمن UAT-001.
