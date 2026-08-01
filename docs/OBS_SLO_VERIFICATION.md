# OBS-002 — تحقق SLI/SLO والتنبيهات

القرار: [ADR-OBS-002-slo-alerts.md](./ADR-OBS-002-slo-alerts.md). الكتالوج: [OBS_SLO_CATALOG.md](./OBS_SLO_CATALOG.md).

## بوابة الإغلاق

| تحقق | أمر / دليل |
|---|---|
| قواعد Prometheus | `promtool check rules docker/prometheus/*.yml` (إن وُجد promtool) أو مراجعة الملفات |
| كل alert له owner + runbook_url | `scripts/obs-alert-labels-check.sh` |
| Black-box job في scrape | `probe_gateway_ready` في prometheus.yml |
| Fire drill | `ALERT_SINK_URL=… bash scripts/obs-alert-fire-drill.sh` |
| تقرير شهري | `bash scripts/obs-slo-monthly-report.sh` (dry-run بدون Prometheus يكتب قالباً) |
| لوحة Grafana | uid `obs002-slo` تحت `docker/grafana/dashboards/` |

## False positives (عتبات أولية)

| تنبيه | خطر FP | ضبط |
|---|---|---|
| InboxConsumerStalled | مرتفع عند الخمول | severity=info فقط |
| J4PayrollFailRatioHigh | يتطلب نشاطاً | شرط rate processed &gt; 0 |
| J1 burn | يحتاج تاريخ probe | فعّل black-box قبل الاعتماد |

## SIEM

خارج النطاق → **OBS-003**.
