# PRV-001 — Subprocessor register

| Subprocessor | Role | Data location requirement | Status |
|---|---|---|---|
| PostgreSQL operator / host | Primary datastore | Sovereignty per SOV-002 | Approved (platform) |
| MinIO / object storage operator | File blobs | Sovereignty per SOV-002 | Approved (platform) |
| Keycloak / IdP operator | Authentication | Sovereignty per SOV-002 | Approved (platform) |
| Observability (Seq/Prometheus/Grafana host) | Logs/metrics/traces | PII-minimized; redaction required | Approved (platform) |
| External treasury channel | Payment execution | MoU / EXT-003 agreement | Conditional |
| Civil registry adapter | Identity verification | EXT-002 agreement | Conditional |

No unlisted subprocessor may receive production personal data.
