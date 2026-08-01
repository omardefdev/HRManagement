# ADR-OBS-001 — Telemetry كاملة لكل الخدمات

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OBS-001 |
| التاريخ | 2026-07-28 |

## السياق

المنصة لديها أساس جزئي: `AddHrOpenTelemetry` على 15 host، Serilog→Seq، Prometheus يScrab 4 خدمات، وcollector يصدّر إلى logging فقط. لا مسار تتبع durable ولا لوحات RED/USE ولا سياسة sampling/PII.

## القرار

1. **Trace backend = Grafana Tempo** مع Grafana الحالية.
2. **Logs = Seq** (لا Loki في هذه البطاقة).
3. **Metrics:** scrape `/metrics` لكل الـ15 host + rabbitmq-exporter + postgres-exporter؛ OTLP اختياري عبر collector.
4. **MassTransit OpenTelemetry** + الإبقاء على `X-Correlation-ID`.
5. **Custom meters** عبر `AddMeter`؛ `ActivitySource` للـjobs داخل الـWeb hosts.
6. **Sampling** قابل للتهيئة؛ **PII scrub** لattributes/headers الحساسة.
7. **Compose** مسار التحقق المحلي؛ **K8s** `deploy/k8s/platform/observability/` + env في overlays.
8. **خارج النطاق:** burn-rate SLO + مستقبل تنبيه (**OBS-002**)؛ SIEM (**OBS-003**).

## البدائل المرفوضة

| بديل | سبب الرفض |
|---|---|
| Jaeger كمظلة أولى | Tempo يتكامل مباشرة مع Grafana الحالية |
| Loki بدل Seq | Seq موجود ومُفعّل في الإنتاج |
| مشروع Worker منفصل لكل job | الوظائف in-process؛ يكفي ActivitySource |

## العواقب

- OBS-002 يبني SLO/alerts على المقاييس واللوحات الناتجة.
- synthetic journey يثبت service map عبر Tempo.
