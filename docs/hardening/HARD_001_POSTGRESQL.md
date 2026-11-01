# HARD-001 — PostgreSQL 16 Hardening Baseline

**Control:** HARD-001 | **Asset:** PostgreSQL 16 (Docker / CNPG) | **Owner:** Platform Security

## Scope

All PostgreSQL instances backing HR Management (`hr_*_db`, `keycloak_db`, `gateway_audit_db`, etc.).

## Baseline controls

| Control | Requirement | Implementation |
|---|---|---|
| Version | PostgreSQL 16.x LTS track | `postgres:16-alpine`, CNPG `postgresql:16` |
| Network | No public listener | `docker-compose.production.yml` — `ports: []`; K8s ClusterIP only |
| Authentication | SCRAM-SHA-256 | `password_encryption = scram-sha-256` in init / CNPG spec |
| Least privilege | Per-service DB roles | `deploy/bootstrap/ensure-roles.sql` + `postgres-roles.conf` (`hrm-bootstrap`, PLAN-38) — `*_app`/`*_migrator` roles, no SUPERUSER for apps |
| TLS | Encrypt client connections in prod | `ssl = on` via CNPG / reverse-proxy TLS termination |
| Logging | DDL + auth failures | `log_statement = ddl`, `log_connections = on`, `log_disconnections = on` |
| Extensions | Allow-list only | Document approved extensions; block `plpythonu`, untrusted languages |
| Backup user | Separate `hrm_backup` role | OPS-003 backup scripts; REVOKE write on app schemas from backup where possible |
| Resource limits | Memory cap | Compose `deploy.resources.limits.memory: 1G`; CNPG pod limits in data overlay |
| Container hardening | Mild — PG needs Linux caps | `no-new-privileges`; **do not** `cap_drop: ALL` (breaks init/chown) |

## CIS / local alignment

- Disable `trust` authentication for non-local connections.
- Revoke `PUBLIC` grants on `public` schema where applicable.
- Set `shared_preload_libraries` explicitly; no ad-hoc superuser sessions in production.
- Rotate credentials via External Secrets / vault — not baked into images.

## Drift detection

- Daily: `scripts/hard001-compliance-scan.ps1`
- Pre-release: `.github/workflows/hardening-check.yml`
- Unauthorized superuser login → SIEM alert (OBS-003)

## Exceptions

Temporary deviations require an entry in [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md) with expiry ≤ 90 days.

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
