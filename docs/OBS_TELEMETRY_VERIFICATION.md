# OBS-001 — تحقق التيليمتري

القرار: [ADR-OBS-001-telemetry.md](./ADR-OBS-001-telemetry.md). الطوبولوجيا: [OBS_TELEMETRY_TOPOLOGY.md](./OBS_TELEMETRY_TOPOLOGY.md).

## بوابة الإغلاق

| تحقق | أمر / دليل |
|---|---|
| Inventory compare | `bash scripts/obs-inventory-compare.sh` |
| PII scan على fixture | `bash scripts/obs-pii-scan.sh` (يفشل على عيّنة سيئة) |
| Synthetic journey | مع stack شغّال: `GATEWAY_URL=… bash scripts/obs-synthetic-journey.sh` |
| Prometheus jobs | كل أهداف [OBS_SERVICE_INVENTORY.md](./OBS_SERVICE_INVENTORY.md) في `prometheus.yml` |
| Grafana | لوحات `obs001-red` / `obs001-use` / `obs001-messaging` |
| Tempo | Explore → Service graph بعد رحلة |

## Compose محلي

```bash
docker compose -f docker-compose.yml -f docker-compose.services.yml \
  -f docker-compose.observability.yml up -d
# فعّل OTEL على الخدمات أو استخدم docker-compose.production.yml overrides الجزئية
```

## K8s

`kubectl apply -k deploy/k8s/platform/observability` ثم overlays مع env OTEL.

## OBS-002

مستقبل Alertmanager الحقيقي + burn-rate SLO alerts = **OBS-002** (غير مطلوب لإغلاق OBS-001).
