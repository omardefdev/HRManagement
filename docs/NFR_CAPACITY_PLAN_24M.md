# NFR-001 — خطة سعة 24 شهراً

القرار: [ADR-NFR-001-capacity-load.md](./ADR-NFR-001-capacity-load.md). النموذج: [NFR_CAPACITY_MODEL.md](./NFR_CAPACITY_MODEL.md).

## افتراضات النمو (مختبري → وطني)

| ربع | موظفون (فعّال نموذجي) | مستأجرون | ذروة HTTP نسبية | ملاحظة |
|---|---:|---:|---:|---|
| Q0 (الآن) | 10k مخبري / طيار | 1–10 | 1.0× | harness NFR-001 |
| Q1–Q2 | 50k–100k | 20–50 | 2× | توسيع staging + HPA |
| Q3–Q4 | 250k–500k | 100–200 | 4× | اقتراب نافذة رواتب جزئية |
| Y2 | 1.25M–5M | 500–1000+ | 8–10× نحو 10k rps | **CAP-001 / وطني** |

القيم وطنية مرجعية تبقى 5M / 1000 مستأجر / 10k rps؛ الجدول أعلاه مسار اقتراب تشغيلي.

## Headroom

- هدف: **≥ 30%** دون ذروة النموذج المخبري قبل ظهور تشبع (P95→2s أو أخطاء↑ أو outbox↑).
- قياس: مقارنة `VUS_BASE` الناجح في load مع أول نقطة فشل في stress/spike.

## نقاط قرار توسيع

| إشارة | إجراء |
|---|---|
| Gateway P95 &gt; 1.5s مستدام | رفع replicas (HPA 3→6) ثم مراجعة rate limit |
| Payroll نافذة أطول من الميزانية | ضبط DOP/PartitionSize بحذر؛ توسيع CPU لـpayroll |
| Outbox عمر &gt; 300s متكرر | زيادة consumers / عزل طوابير |
| DB connections قرب السقف | PgBouncer / زيادة max_connections المدروس |
| MinIO/File bursts | فصل دلو + حصة عرض نطاق |

مرجع HA: [OPS_HA_TOPOLOGY](./OPS_HA_TOPOLOGY.md) إن وُجد — Gateway min 3 / max 6، apps 2→4/6، CPU target 70%.

## تسليم إلى CAP-001

CAP-001 يعيد استخدام:

1. هذا النموذج + `scale_factor`
2. `perf/k6/*` و`nfr-k6-run`
3. seed الاصطناعي (بحجم أكبر)
4. ميزانية الاختناقات

ثم يشغّل 72h@100% / 4h@150% / 15m@200% على المغلف الوطني ويحدّث الشهادات.
