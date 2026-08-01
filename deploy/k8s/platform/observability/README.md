# OBS-001 K8s observability platform
#
# kubectl apply -k deploy/k8s/platform/observability
#
# App overlays should envFrom ConfigMap `hr-observability-env` (namespace hrmanagement)
# or set OTEL_EXPORTER_OTLP_ENDPOINT / OpenTelemetry__Enabled explicitly.
#
# Prometheus/Grafana/Seq full HA operators can replace these stubs; Compose remains
# the primary local verification path (docs/OBS_TELEMETRY_VERIFICATION.md).
