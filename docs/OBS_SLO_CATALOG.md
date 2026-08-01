# OBS-002 — كتالوج SLI / SLO

القرار: [ADR-OBS-002-slo-alerts.md](./ADR-OBS-002-slo-alerts.md). الطوبولوجيا: [OBS_ALERTING_TOPOLOGY.md](./OBS_ALERTING_TOPOLOGY.md).

## J1 — GatewayAvailability

| الحقل | القيمة |
|---|---|
| SLI | نسبة نجاح black-box `probe_success{job="probe_gateway_ready"}` |
| هدف | 99.5% على 30 يوماً |
| نافذة ميزانية | 30d → ~3.6h downtime مسموح |
| مالك منتج | Platform |
| مالك تشغيل | SRE / Platform |
| مصدر | `probe_success` من blackbox-exporter |

## J2 — ApiLatency (Gateway HTTP)

| الحقل | القيمة |
|---|---|
| SLI availability | 1 − (5xx / total) على job `hr-gateway` |
| SLI latency | نسبة طلبات بمدة &lt; 2s (من histogram buckets) |
| هدف | availability 99.5% / 30d؛ P95 &lt; 2s (نافذة 5m كعرض) |
| مالك | Platform + HR.Core |
| مصدر | `http_server_request_duration_seconds_*` |

## J3 — MessagingFreshness

| الحقل | القيمة |
|---|---|
| SLI | دقائق يكون فيها `outbox_pending_messages &lt; 100` و`outbox_oldest_age_seconds &lt; 300` |
| هدف | ≥ 99% من الدقائق خلال 30d |
| مالك | Platform / Integration |
| مصدر | مقاييس OBS-001 Outbox + RabbitMQ ready (عرض) |

## J4 — PayrollCorrectnessSignal

| الحقل | القيمة |
|---|---|
| SLI | أثناء نشاط: `employees_failed / (processed+failed)` &lt; 1% على نافذة 1h |
| هدف | fail ratio &lt; 1% عند `rate(employees_processed[1h]) &gt; 0` |
| مالك | Payroll |
| مصدر | `hrm.payroll.run.*` meters |

## Error budget

- نافذة تقويمية **30 يوماً**.
- Recording: `sli:j1_availability:ratio_rate5m` … و`slo:j1_burn_rate:*`.
- تنبيهات الحرق: سريع (1h / 5m) severity=`page`؛ بطيء (6h / 30m) severity=`ticket`.
