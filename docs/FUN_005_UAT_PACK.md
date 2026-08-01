# FUN-005 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | طلب بطاقة → Process → إصدار + checklist | Issued + خطوة Issued مكتملة | |
| U2 | MarkDelivered بلا إثبات | رفض | |
| U3 | MarkDelivered مع مستلم + إيصال | Delivered | |
| U4 | فصل موظف ببطاقة Active | Revoked عبر party | |
| U5 | MarkLost ثم Renew | بديل مرتبط Replacement | |
| U6 | تعيين أصل مرتين مفتوح | رفض الثاني | |
| U7 | أصل Privileged بلا دليل | رفض | |
| U8 | نقل موظف بعهدة | ReturnedAt + InStorage | |
| U9 | فشل Directory sync | Failed + ظهور في FailedProvisioning | |
| U10 | RetryProvision | Succeeded أو Failed محدث | |
| U11 | فصل يفصل أجهزة | AssignedTo=null | |
| U12 | SyncFromKeycloak (Auth) | مسار Retry حساب الدليل | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي ضمن UAT-001.
