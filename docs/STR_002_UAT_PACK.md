# STR-002 — حزمة UAT (ReadyForSign-off)

توقيعات: Legal + Privacy + CISO + HR/Finance.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + domain matrix | 10 domains | |
| U2 | control-catalog vs required-domains | كل domain ≥1 صف | |
| U3 | تشغيل str002-control-catalog-validate | PASS | |
| U4 | Implemented بلا LEGAL_TBD | بوابة الإصدار | |
| U5 | confidence D → Blocked-Legal أو exception | قاعدة محققة | |
| U6 | Conflict register ↔ exceptions[] | متوافق | |
| U7 | Legislative watch ربع سنوي | عملية موثّقة | |
| U8 | ≥3 traces قانون→config→test→report | TRACE_SAMPLES | |
| U9 | GOV-002 غير منسوخ | إشارة register فقط | |
| U10 | Readiness blockedOn SYR/SYD | لا إغلاق وهمي | |

**الحالة:** ReadyForSign-off — كتالوج ضوابط؛ المدونة المصدّقة = SYR-001 لاحقاً.
