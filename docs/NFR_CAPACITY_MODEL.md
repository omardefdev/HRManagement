# NFR-001 — نموذج الحمل والسعة

القرار: [ADR-NFR-001-capacity-load.md](./ADR-NFR-001-capacity-load.md). الطوبولوجيا: [NFR_LOAD_TOPOLOGY.md](./NFR_LOAD_TOPOLOGY.md).

## مغلف وطني مرجعي (CAP / readiness)

| الكيان | هدف وطني |
|---|---:|
| سجلات أشخاص (نشط+تاريخي) | 5,000,000 |
| مستأجرون مستقلون | ≥ 1,000 |
| وحدات تنظيمية | ≥ 250,000 |
| جلسات مصادقة متزامنة | 100,000 |
| ذروة HTTP (بدون static) | 10,000 req/s |
| نافذة رواتب عامة | 5M / 6h |
| نافذة سورية عادية (مرجع) | 1.25M / ≤4h |

المصدر: [06-NATIONAL-READINESS-MODEL.md](./completion-backlog/06-NATIONAL-READINESS-MODEL.md) وCAP-001.

## عامل التصغير المخبري

| الرمز | افتراضي | المعنى |
|---|---:|---|
| `scale_factor` | 0.002 | موظفون مخبريون ≈ 5M × 0.002 = **10,000** |
| `vus_base` | 50 | وحداتون concurrent عند 100% نموذج NFR |
| `vus_stress` | 75 | 150% من القاعدة |
| `spike_vus` | 150 | نبضة قصيرة |

أحجام قياس البطاقة:

| المستوى | موظفون | ملاحظة |
|---|---:|---|
| إلزامي | 10,000 | seed + payroll-run |
| موسّع | 100,000 | staging/HA إن توفّر |
| وطني | 1.25M–5M | **CAP-001 فقط** |

## المواسم (seasons)

| موسم | نمط الحمل | سيناريوهات |
|---|---|---|
| تسجيل صباحي | ارتفاع جلسات + قوائم | `gateway-mixed`, `lookup-list` |
| نافذة رواتب | دفعة + رسائل | `payroll-run` + مراقبة J3/J4 |
| تصدير تقارير | sync→async | `export-async` |
| تصفح هيكل | شجرة/نطاق | `org-tree` |

## خريطة سيناريو → SLO

| سيناريو k6 | رحلة OBS-002 | PERF |
|---|---|---|
| `gateway-mixed` | J1, J2 | — |
| `lookup-list` | J2 | PERF-002 |
| `payroll-run` | J4 (+ J3) | PERF-001 |
| `export-async` | J2 | PERF-004 |
| `org-tree` | J2 | PERF-003 |

## عتبات القبول (نافذة الاختبار)

| مؤشر | عتبة |
|---|---|
| HTTP failed rate (gateway) | &lt; 1% في load؛ &lt; 5% في stress مع degradation موثّق |
| P95 مدة الطلب (J2) | &lt; 2s على مسارات API المقاسة |
| Payroll fail ratio (J4) | &lt; 1% أثناء النشاط |
| Outbox (J3) | pending &lt; 100 وعمر &lt; 300s لمعظم الدقائق أثناء العاصفة |
| Soak ≥2h | لا نمو خطي غير محدود لـRSS / DB connections / queue depth |
| Headroom | ≥ 30% دون ذروة النموذج المخبري قبل التشبع |

## Degradation الآمن (متوقع)

- Rate limit البوابة → 429 بدل انهيار.
- تجاوز تصدير متزامن 10k → 202 async (PERF-004).
- طابور أقسام الرواتب محدود (PERF-001 DOP/queue) → إطالة الزمن لا فساد.
- HPA يزيد النسخ ضمن سقف OPS قبل فشل صلب.
