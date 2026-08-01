# FUN-002 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | Happy path فصل مكتمل checklist + كل الأطراف | ISeparationCompleted؛ لا وصول/أصل/راتب دوري | |
| U2 | تاريخ نفاذ مستقبلي | Publish مرفوض برسالة واضحة | |
| U3 | Cancel قبل النشر | السجل IsCancelled موجود؛ لا حذف | |
| U4 | Cancel بعد saga وقبل النفاذ | Compensate؛ مسودة EOS ملغاة إن وُجدت | |
| U5 | طرف معطل | Intervention؛ لا SeparationCompleted | |
| U6 | Legal Hold | Publish مرفوض | |
| U7 | Rehire بعد فصل مكتمل | تعيين FUN-001 دون حذف SeparationRecord | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي الرسمي ضمن UAT-001.
