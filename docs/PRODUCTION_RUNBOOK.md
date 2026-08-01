# Production Runbook — HRManagement

Operational guide for startup, deployment, migrations, rollback, backup/restore, and incident triage.

**Related docs:** [GATEWAY_RUNBOOK.md](./GATEWAY_RUNBOOK.md), [DATABASE_MIGRATION_POLICY.md](./DATABASE_MIGRATION_POLICY.md), [BACKUP_POSTGRES.md](./BACKUP_POSTGRES.md), [RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md), [KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md), [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md) (OPS-001), [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md) (OPS-002), [OPS_BACKUP_TOPOLOGY.md](./OPS_BACKUP_TOPOLOGY.md) (OPS-003), [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md) (OPS-004), [OPS_IAC_TOPOLOGY.md](./OPS_IAC_TOPOLOGY.md) (OPS-005), [OBS_TELEMETRY_TOPOLOGY.md](./OBS_TELEMETRY_TOPOLOGY.md) (OBS-001), [OBS_SLO_CATALOG.md](./OBS_SLO_CATALOG.md) (OBS-002)

---

## Compose profiles

| Profile | Command |
|---------|---------|
| Development infra + hr-core | `docker compose up -d` |
| Full module stack | `docker compose -f docker-compose.yml -f docker-compose.services.yml up -d` |
| Production (single-host) | `docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.production.yml up -d` |
| Production + observability | `docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.observability.yml -f docker-compose.production.yml up -d` (production **last** — SEC-001) |
| Strip fixed `container_name` (HA policy check) | Add `-f docker-compose.ha-forbidden.yml` — Compose remains non-HA; production HA = k8s |
| Daily backup sidecar | Add `--profile backup` to production compose |
| **Production HA (multi-AZ) apps** | Kubernetes: `deploy/k8s/overlays/production-ha` — [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md) |
| **Production HA data plane** | Kubernetes: `deploy/k8s/data/overlays/production-data-ha` — [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md) |
| **DR secondary (warm standby stub)** | `deploy/k8s/overlays/dr-secondary` + `data/overlays/dr-secondary-data` — [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md) |

Copy [`.env.example`](../.env.example) to `.env` and fill all secrets before production startup.

---

## 1. Startup sequence

1. **Secrets** — inject via `.env` or secret manager (`POSTGRES_PASSWORD`, `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`, etc.). Never rely on committed `appsettings.json` passwords.
2. **Infrastructure** — start PostgreSQL and RabbitMQ; wait until healthy (`pg_isready`, RabbitMQ ping).
3. **Keycloak** — verify realm `hrmanagement` and clients per [KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md). Production uses `command: start` (not `start-dev`).
4. **Migrations (production)** — run `bash scripts/run-migrations.sh` **before** restarting app containers. Startup migrations are disabled via `Database__RunMigrationsAtStartup=false`.
5. **Module services** — start all module containers; gate on `GET /health/ready` → HTTP 200.
6. **Gateway** — start last; verify `/health/ready`, `/Home/UpstreamHealth`, and OIDC login.
7. **Module rollout** — enable modules incrementally with `Gateway__EnabledModules__N` (pilot with `hr-core` first).

### Health endpoints

| Endpoint | Purpose |
|----------|---------|
| `/health/live` | Process alive (liveness) |
| `/health/ready` | DB + RabbitMQ ready (readiness) |
| `/health` | All checks |
| `/Home/UpstreamHealth` | Gateway aggregated module status |
| `/Home/Operations?correlationId=` | Audit trace + metrics |

---

## 2. Deployment

### Image tagging

- Format: `ghcr.io/{owner}/hrmanagement/{service}:{git-sha}` (12-char SHA; **never** `:latest` for releases)
- CD workflow: [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml)
- Registry overlay: [`docker-compose.images.yml`](../docker-compose.images.yml) — host needs no source tree or build tools

### Deploy steps

1. Tag release (`v*`) or trigger `deploy` workflow with `action=deploy`.
2. **Backup** affected databases (`bash scripts/backup-postgres.sh`).
3. **Migrate** changed services (`bash scripts/run-migrations.sh` or CI migrate job — runs on tag push and when `run_migrations=true`).
4. **Pull** GHCR images on target host using `IMAGE_TAG` + `docker-compose.images.yml`.
5. **Rolling update** via `docker compose ... up -d --remove-orphans`.
6. **Health gate** — CI runs `scripts/verify-staging-stack.ps1` (failure triggers auto-rollback).
7. **Deployment record** — append to `$DEPLOY_PATH/deployments.log`.

### Production compose

```bash
export IMAGE_TAG=<12-char-sha>
export GITHUB_REPOSITORY_OWNER=<github-owner>
docker compose \
  -f docker-compose.yml \
  -f docker-compose.services.yml \
  -f docker-compose.observability.yml \
  -f docker-compose.images.yml \
  -f docker-compose.production.yml \
  pull
docker compose \
  -f docker-compose.yml \
  -f docker-compose.services.yml \
  -f docker-compose.observability.yml \
  -f docker-compose.images.yml \
  -f docker-compose.production.yml \
  up -d --remove-orphans
```

Production overlay must be **last** (SEC-001 closes host ports that observability would otherwise publish). See [NETWORK_MATRIX.md](./NETWORK_MATRIX.md).

Production overlay configures:
- Internal YARP cluster DNS (`http://{service}:8080`)
- `Database__RunMigrationsAtStartup=false`
- Container healthchecks on `/health/ready`
- Keycloak production mode (`start` + `KC_PROXY=edge`)
- Seq + OpenTelemetry + Prometheus (with observability file)

---

## 3. Migration

See [DATABASE_MIGRATION_POLICY.md](./DATABASE_MIGRATION_POLICY.md).

- **Development:** `Database:RunMigrationsAtStartup` defaults to `true`.
- **Production:** set `Database:RunMigrationsAtStartup: false` in `appsettings.Production.json` and env.
- **Apply manually or via CI:**

```bash
dotnet ef database update \
  --project src/Services/HRCore/HR.Core.Infrastructure \
  --startup-project src/Services/HRCore/HR.Core.Web
```

Or run all: `bash scripts/run-migrations.sh`

**Order:** gateway audit DB → HR.Core → HR.Contracts/Payroll → remaining modules.

**Pre-migration:** mandatory `pg_dump` of affected databases.

**Failure in production:** migration errors abort startup (fail-fast).

---

## 4. Rollback

| Scenario | Action |
|----------|--------|
| Bad app release (no schema change) | Auto-rollback on health-gate failure, or manual `workflow_dispatch` with `action=rollback` |
| Bad migration | Forward-fix migration **or** restore DB from pre-migration dump |
| RabbitMQ poison messages | Pause consumer; inspect `{queue}_error`; manual replay |
| Keycloak misconfiguration | Restore realm export; update secrets; restart gateway |

### Automated rollback (DEP-001)

Production deploys use **immutable SHA tags** from GHCR (never `:latest`). Compose overlay: [`docker-compose.images.yml`](../docker-compose.images.yml).

1. **Health-gate failure:** after `docker compose up`, CI runs `scripts/verify-staging-stack.ps1` (or curl fallback). On failure the workflow pulls the last successful tag from `$DEPLOY_PATH/deployments.log` and redeploys it.
2. **Manual rollback:** GitHub → Actions → `deploy` → Run workflow:
   - `action` = `rollback`
   - `rollback_tag` = previous known-good tag (12-char SHA), found in `deployments.log` on the host
   - `environment` = `staging` or `production`
3. **Deployment record:** every successful deploy/rollback appends a line via `scripts/record-deployment.sh`:
   ```
   2026-07-28T10:00:00Z|tag=abc123def456|sha=...|env=production|actor=...
   ```

**Rollback checklist:**
1. Identify failing module via `/Home/UpstreamHealth` or `/health/upstreams/{module}`.
2. Trace `X-Correlation-ID` in gateway audit + Seq logs.
3. Read last known-good tag from `$DEPLOY_PATH/deployments.log`, then trigger `action=rollback` (or wait for auto-rollback).
4. Confirm EF migration history matches app version (schema rollback is **not** automatic — use forward-fix or DB restore).

---

## 5. Backup

### Automated

```bash
# One-shot Postgres (16 DBs incl. file_storage_db) + catalog/checksums
bash scripts/backup-postgres.sh ./backups/postgres

# MinIO object mirror (prefer backup-reader keys)
bash scripts/backup-minio.sh

# Daily sidecars (production compose profile)
docker compose ... --profile backup up -d postgres-backup minio-backup
```

Backs up **all 16 databases** listed in `docker/init-databases.sql` (including `file_storage_db`). Retention: `BACKUP_RETENTION_DAYS` (default 30 / warm tier). See [OPS_BACKUP_TOPOLOGY.md](./OPS_BACKUP_TOPOLOGY.md).

### Manual

```bash
pg_dump -h localhost -U hrmanagement -Fc -f hr_core_db.dump hr_core_db
```

See [BACKUP_POSTGRES.md](./BACKUP_POSTGRES.md).

---

## 6. Restore

1. Stop affected application containers.
2. Create or empty target database.
3. Restore:

```bash
pg_restore -h localhost -U hrmanagement -d hr_core_restore --clean --if-exists hr_core_db.dump
```

4. Run `dotnet ef database update` only if dump predates latest migration.
5. Restart services; verify `/health/ready`.
6. **Quarterly drill:** restore to isolated `*_restore` databases; record result in IT.Infra BackupJobs or ops log.

---

## 7. Disaster recovery — site failover / failback (OPS-004)

Full plan: [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md), owners: [OPS_DR_COMMUNICATION_TREE.md](./OPS_DR_COMMUNICATION_TREE.md), drills: [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md).

### DNS cutover (manual after Go)

1. Confirm IC Go per communication tree.
2. Health-check target (do **not** change DNS yet):

```bash
DR_TARGET_URL=https://hr-dr.example.com bash scripts/dr-dns-cutover-check.sh
```

3. Lower TTL if needed; point `hr.example.com` (or approved record) to secondary ingress / LB.
4. Re-run the health check against the public hostname.
5. Record elapsed RTO in the DR results table.

### Failback

```bash
bash scripts/dr-failback-reconcile.sh
# then DNS back to primary after check against primary URL
DR_TARGET_URL=https://hr.example.com bash scripts/dr-dns-cutover-check.sh
```

Secondary cluster provision = **OPS-005** (`terraform/envs/dr-secondary`). Paper/manual continuity = **BCP-001**.

---

## 8. Incident triage

| Symptom | First checks | Owner |
|---------|--------------|-------|
| 502/503 | `/Home/UpstreamHealth`, module `/health/ready` | Failing module |
| 401/403 | Keycloak issuer, realm roles, gateway auth middleware | Keycloak / gateway |
| Slow requests | Gateway Operations metrics, PG connections, RabbitMQ depth | DB / messaging |
| Missing audit | `gateway_audit_db`, gateway logs | Gateway |
| Duplicate/missing events | `_error` queues, outbox tables (HR.Core, HR.Contracts, Payroll) | Messaging |
| Auth down globally | Keycloak health, realm config | Keycloak |
| Site / region loss | DR tree + `dr-failover-drill.sh` | IC / Platform (OPS-004) |

**Trace path:** `traceparent` / Tempo service graph + `X-Correlation-ID` → `gateway_audit_entries` → Seq logs → MassTransit headers → outbox/inbox tables. See [OBS_TELEMETRY_TOPOLOGY.md](./OBS_TELEMETRY_TOPOLOGY.md).

### Observability URLs (default ports)

| Service | URL |
|---------|-----|
| Seq | http://localhost:5341 |
| Tempo | http://localhost:3200 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 (OBS-001 RED/USE/Messaging + OBS-002 SLO) |
| Alertmanager | http://localhost:9093 |
| Alert sink | http://localhost:8089/last (OBS-002 fire-drill) |
| Blackbox | http://localhost:9115 (metrics; probes via Prometheus) |
| RabbitMQ management | http://localhost:15672 (dev only; not exposed in production overlay) |

### Alert thresholds (OBS-002)

Canonical rules: [`docker/prometheus/alerts.yml`](../docker/prometheus/alerts.yml) + [`slo-burn-alerts.yml`](../docker/prometheus/slo-burn-alerts.yml).

| Alert / journey | Threshold (summary) |
|---|---|
| J1 black-box / burn | `probe_success==0` 2m; multi-window burn vs 99.5% |
| J2 API burn / latency | 5xx budget burn; P95 &gt; 2s 10m |
| J3 freshness | outbox pending &gt;100 or age &gt;300s |
| J4 payroll | fail ratio &gt;1% while active |
| ServiceDown | scrape `up==0` 2m (symptom) |

Runbooks: [OBS_ALERT_RUNBOOKS.md](./OBS_ALERT_RUNBOOKS.md). Catalog: [OBS_SLO_CATALOG.md](./OBS_SLO_CATALOG.md).  
Webhook default → `alert-sink`; production may mount Slack/Teams URL. Maintenance: `scripts/obs-silence-maintenance.sh`.

---

## 9. Hardening checklist (summary)

- [ ] `.env` populated; never commit secrets
- [ ] `Database__RunMigrationsAtStartup=false` in production
- [ ] Keycloak realm/clients/roles configured
- [ ] Gateway YARP clusters point to internal Docker DNS
- [ ] TLS termination at reverse proxy; `UseForwardedHeaders=true`
- [ ] Backup cron or `postgres-backup` profile enabled
- [ ] Seq receiving logs from all services
- [ ] Prometheus scraping `/metrics` on all 15 hosts (+ rabbitmq/postgres exporters)
- [ ] Tempo receiving OTLP traces; Grafana service map usable
- [ ] `scripts/obs-inventory-compare.sh` and `scripts/obs-pii-scan.sh` pass
- [ ] Alertmanager delivers to alert-sink (`scripts/obs-alert-fire-drill.sh`)
- [ ] Grafana dashboard `obs002-slo` provisioned
- [ ] Quarterly restore drill documented
