# Production cutover runbook — HRManagement

**Version:** 1.0  
**Date:** 2026-05-29

## Prerequisites

- [ ] Staging soak completed (see [RELEASE_READINESS_STAGING.md](./RELEASE_READINESS_STAGING.md))
- [ ] [RELEASE_PRODUCT_SCOPE.md](./RELEASE_PRODUCT_SCOPE.md) signed by product owner
- [ ] Secrets configured per [KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md)
- [ ] Compose fixes deployed (`ContractsDb`, `HealthDb`, `MovementsDb`, `TrainingDb` in `docker-compose.services.yml` and production overlays)

## Compose command (production)

```bash
docker compose \
  -f docker-compose.yml \
  -f docker-compose.services.yml \
  -f docker-compose.observability.yml \
  -f docker-compose.production.yml \
  up -d --remove-orphans
```

`docker-compose.production.yml` must be last so SEC-001 port closures override observability host ports.

Optional daily Postgres backups:

```bash
docker compose ... --profile backup up -d postgres-backup
```

## Cutover sequence

### 1. Migrations (before traffic)

`Database__RunMigrationsAtStartup` is **false** in production. Run once per release:

```bash
# GitHub Actions: deploy workflow with run_migrations=true
# Or locally/SSH:
bash scripts/run-migrations.sh
```

Connection string env vars must match `Program.cs` names (see `.github/workflows/deploy.yml` migrate job).

### 2. Deploy images

- Tag push `v*` triggers [deploy.yml](../.github/workflows/deploy.yml) build-and-push to GHCR
- SSH deploy pulls and restarts compose stack

### 3. Verify health

```powershell
pwsh scripts/verify-staging-stack.ps1 -GatewayBaseUrl https://<your-gateway-host>
```

All `/health/upstreams/{slug}` should return **200** (503 acceptable only during rolling restart).

### 4. Keycloak & gateway

- [ ] `KEYCLOAK_AUTHORITY` points to public realm URL
- [ ] `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET` set
- [ ] Realm roles match [KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md)
- [ ] `UseForwardedHeaders=true`, `HrAuth:CookieSecureAlways=true` behind TLS terminator
- [ ] `Cors:AllowedOrigins` set if browser calls APIs directly

### 5. Messaging & observability

- [ ] RabbitMQ reachable from all services; no sustained `_error` queue growth ([RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md))
- [ ] Seq / OTel collector receiving logs and traces (`Serilog__SeqUrl`, `OTEL_EXPORTER_OTLP_ENDPOINT`)
- [ ] Alerting wired to Prometheus rules in `docker/prometheus/alerts.yml` (if observability overlay enabled)

### 6. Smoke after cutover

```powershell
$env:RUN_PLAYWRIGHT_E2E = "true"
$env:GATEWAY_BASE_URL = "https://<your-gateway-host>"
$env:E2E_KEYCLOAK_USER = "<test-user>"
$env:E2E_KEYCLOAK_PASSWORD = "<password>"
pwsh scripts/run-staging-e2e.ps1 -SkipComposeUp
```

Manual: one critical workflow (hire → contract → leave → payroll post → separation) on production-like data.

## Rollback

1. Redeploy previous image tag on all 14 containers (gateway + 13 services).
2. Do **not** roll back database migrations without DBA review ([DATABASE_MIGRATION_POLICY.md](./DATABASE_MIGRATION_POLICY.md)).
3. Drain RabbitMQ `_error` queues before reprocessing.

## Go / no-go (production)

| Check | Required |
|-------|----------|
| All services `/health/ready` green | Yes |
| Gateway SSO + module 403 tested | Yes |
| Migrations applied | Yes |
| Backups scheduled | Yes |
| Product scope signed | Yes |
| No P0 config mismatches in compose | Yes |

**NO-GO** if any service fails DB connection at startup, auth redirect loops, or duplicate payroll adjustments under replay during soak.
