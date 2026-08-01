# OBS-002 — طوبولوجيا التنبيه

القرار: [ADR-OBS-002-slo-alerts.md](./ADR-OBS-002-slo-alerts.md). Runbooks: [OBS_ALERT_RUNBOOKS.md](./OBS_ALERT_RUNBOOKS.md).

## التدفق

```text
Blackbox + App metrics
  → Prometheus (recording + burn + symptom alerts)
  → Alertmanager (route by severity / owner)
  → alert-sink webhook  OR  ALERTMANAGER_WEBHOOK_URL (Slack/Teams)
```

## Severity

| Label | معنى | تكرار |
|---|---|---|
| `page` | حرق سريع / توقف رحلة | فوري عبر webhook |
| `ticket` | حرق بطيء / تحذير | مجمّع |
| `warning` / `info` | أعراض / إشارات | لا paging |

## تصعيد

1. Owner على الـalert يستلم عبر القناة.
2. إن لم يُغلق خلال نافذة التكرار → تصعيد لـSRE on-call ([REL_003_ONCALL_ROSTER.md](./REL_003_ONCALL_ROSTER.md) + [REL_003_INCIDENT_PROCESS.md](./REL_003_INCIDENT_PROCESS.md)).
3. Maintenance: silence عبر `amtool` / [`scripts/obs-silence-maintenance.sh`](../scripts/obs-silence-maintenance.sh).

## مكونات Compose

| خدمة | دور |
|---|---|
| `blackbox-exporter` | probes J1 |
| `alert-sink` | استقبال webhook + `GET /last` للـfire drill |
| `alertmanager` | توجيه |
| `prometheus` | تقييم القواعد |

التحقق: [OBS_SLO_VERIFICATION.md](./OBS_SLO_VERIFICATION.md).
