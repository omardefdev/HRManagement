# OBS-001 — سياسة Sampling وPII للتيليمتري

الطوبولوجيا: [OBS_TELEMETRY_TOPOLOGY.md](./OBS_TELEMETRY_TOPOLOGY.md).

## Sampling

| بيئة | `OpenTelemetry:SamplingRatio` | ملاحظات |
|---|---|---|
| Development (OTEL off افتراضياً) | — | فعّل يدوياً عند الحاجة |
| Staging | `0.1` | parent-based + ratio |
| Production | `0.05` | أخطاء تبقى عبر parent/record exception |

الإعداد: `OpenTelemetry__SamplingRatio` أو قسم التكوين `OpenTelemetry:SamplingRatio`.

Sampler: `ParentBased(TraceIdRatioBased)`.

## حقول PII / أسرار ممنوعة في spans

يُحذف أو يُستبدل بـ`[redacted]` عبر:

1. `PiiScrubActivityProcessor` في .NET
2. Collector processor `attributes/pii` في [`docker/otel/otel-collector-config.yaml`](../docker/otel/otel-collector-config.yaml)

قائمة الحد الأدنى: `NationalId` / `national_id`، `password`، `access_token`، `Authorization`، `client_secret`، `http.request.body`، `db.statement`، أي مفتاح يحتوي `secret`/`password`/`token`.

**ممنوع:** أجسام الطلبات الخام، أرقام وطنية، كلمات مرور، JWT كاملة في attributes.

## الاحتفاظ التقريبي والتكلفة

| مخزن | احتفاظ افتراضي | ملاحظة |
|---|---|---|
| Prometheus TSDB | 15 يوم (`--storage.tsdb.retention.time`) | قابل للزيادة بقرص |
| Tempo | 48 ساعة (`block_retention`) | زد للحاجة التحقيق |
| Seq | حجم الـvolume | عرّف retention داخل Seq UI |

الكلفة تُدار عبر خفض نسبة الـsampling وإغلاق statement في EF (لا نص SQL خام في traces).

## تحقق

`scripts/obs-pii-scan.sh` يفشل إذا ظهرت قيم من قائمة الحظر في عيّنة fixture/API.
