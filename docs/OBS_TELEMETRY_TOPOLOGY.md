# OBS-001 — طوبولوجيا التيليمتري

القرار: [ADR-OBS-001-telemetry.md](./ADR-OBS-001-telemetry.md). الجرد: [OBS_SERVICE_INVENTORY.md](./OBS_SERVICE_INVENTORY.md). التحقق: [OBS_TELEMETRY_VERIFICATION.md](./OBS_TELEMETRY_VERIFICATION.md). PII/sampling: [OBS_TELEMETRY_PII_AND_SAMPLING.md](./OBS_TELEMETRY_PII_AND_SAMPLING.md).

## مسارات الإشارة

| إشارة | مسار | مخزن / UI |
|---|---|---|
| Traces | Apps → OTLP `:4317` → Collector → Tempo | Grafana Explore / Service graph |
| Metrics | Prometheus scrape `/metrics` + exporters | Grafana / Prometheus |
| Logs | Serilog → Seq | Seq UI (+ Grafana datasource إن وُجد) |
| Correlation | `X-Correlation-ID` + W3C `traceparent` على HTTP/MQ | Runbook / audit |

## الخدمات (15 host)

Gateway + HR.Core / Contracts / Health / Movements / Training / Payroll / IDCard / Records.Archive / Admin.Logistics / IT.Infra / Dev.Analytics / Compliance / Auth.Directory / File.Storage.

Jobs وconsumers تعمل **داخل** هذه الـhosts (لا Worker منفصل).

## منافذ Compose (افتراضي)

| خدمة | منفذ |
|---|---|
| Seq | 5341 |
| OTEL Collector | 4317 |
| Tempo | 3200 (query), 4317 داخلي عبر collector |
| Prometheus | 9090 |
| Alertmanager | 9093 |
| Grafana | 3000 |
| rabbitmq-exporter | 9419 |
| postgres-exporter | 9187 |

شبكات الإنتاج: انظر [NETWORK_MATRIX.md](./NETWORK_MATRIX.md) — منافذ observability مغلقة للعامة في overlay الإنتاج.

## K8s

`deploy/k8s/platform/observability/` + env في configmaps (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OpenTelemetry__Enabled`, Seq URL).

## حدود البطاقات التالية

- ~~مستقبل Alertmanager الحقيقي + burn alerts → **OBS-002**~~ **Done** — انظر [OBS_ALERTING_TOPOLOGY.md](./OBS_ALERTING_TOPOLOGY.md)
- SIEM / WORM → **OBS-003**
