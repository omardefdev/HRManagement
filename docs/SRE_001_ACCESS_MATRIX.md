# SRE-001 — Access matrix for on-call

| Tool | Tier-0 | Tier-1 | Tier-2 / IC | Notes |
|---|---|---|---|---|
| Alertmanager / paging | R/W ack | R/W | R/W | Required before first shift |
| Grafana / Prometheus | Read | Read | Read | Dashboards for owned services |
| Seq / logs | Read (redacted) | Read | Read | PII redaction enforced |
| kubectl (prod) | — | Limited | Limited | Break-glass for IC |
| docker compose prod host | — | Limited | Limited | Bastion only |
| PostgreSQL | — | Read-only diagnose | Read-only / DBA | No prod writes without change ticket |
| Keycloak admin | — | Security tier | Security + IAM | Dual control for destructive ops |
| VPN / bastion | Required | Required | Required | SCM-001 offboarding revokes |

## Break-glass

1. IC declares break-glass in incident channel.
2. Time-bounded elevation via approved break-glass account.
3. All actions audited; post-incident review mandatory.
