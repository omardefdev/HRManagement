# FUN-004 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | دور hr-core يحاول Details طبي | Forbid / لا تشخيص | |
| U2 | Clinical + Consent → Recommendation | حدث IMedicalRecommendationIssued | |
| U3 | Insurance Draft→Active→Expire | حالات صحيحة؛ لا حذف | |
| U4 | Enroll→Complete→Verify CERT-… | Valid | |
| U5 | شكوى anonymous؛ محقق ≠ مبلغ | FiledBy مخفي لغير المحقق | |
| U6 | تأديب CanIssue | رفض خارج workflow | |
| U7 | مرفق أدلة FILE-002 | ربط أو رفض مسح | |
| U8 | soft-cancel طبي/تأمين/شكوى/تسجيل | IsCancelled؛ السجل موجود | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي ضمن UAT-001.
