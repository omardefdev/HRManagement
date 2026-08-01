# FUN-003 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | Happy path نقل: Create→Approve→Execute بعد النفاذ | saga أطراف + ITransferExecuted | |
| U2 | نقل بلا صلاحية مصدر أو هدف | Forbid على Details/Approve/Execute | |
| U3 | انتداب SoD: نفس المنشئ يعتمد | رفض | |
| U4 | إعارة SoD + Close | ILoanAssignmentEnded؛ Ended checklist | |
| U5 | تاريخ نفاذ مستقبلي | Execute مرفوض | |
| U6 | soft-cancel قبل التنفيذ | IsCancelled؛ السجل موجود | |
| U7 | Cancel saga بعد أطراف جزئية | Compensate=true؛ لا final event | |
| U8 | تعارض حركة متزامنة | Execute مرفوض | |
| U9 | إنهاء انتداب | Close يستعيد عبر cancel/compensate | |
| U10 | Intervention Cancel | ICancel*Saga | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي الرسمي ضمن UAT-001.
