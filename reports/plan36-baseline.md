# PLAN-36 baseline (P0)

Measured 2026-10-31 on branch `feature/plan-36-ops-production-boot-single-host` cut from local `main` 76ac25dd.
The raw `config` dumps are under `artifacts/plan36/` (git-ignored); they are rendered with the lint-only env file
`artifacts/plan36/lint.env` (`--env-file`, so the repository's dev `.env` is never read).

## 0.2 Build

`dotnet build HRManagement.slnx -warnaserror`: **0 warnings, 0 errors**, 30 s.

## 0.3 Compose snapshot

Chain: `-f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml -f docker-compose.production.yml`.

| Variant | Result |
|---------|--------|
| production chain, no profile | **fails**: `service "alert-sink" has neither an image nor a build context specified` |
| production chain, `--profile full-stack` | **fails**: `service "seq" has neither an image nor a build context specified` (the plan's expected first error) |
| + `docker-compose.observability.yml`, no profile | passes (but no application starts: they are all behind `full-stack`) |
| + observability, `--profile full-stack` | **fails**: `service "file-storage-web" depends on undefined service "minio"` (MinIO is behind `object-storage` since QA-034; production still depends on it) |
| + observability, `--profile full-stack --profile object-storage`, `MINIO_IMAGE` set | passes (used for the inventory below) |

Unset variables reported as blank by the base file: `KC_DB`, `KC_DB_URL`, `KC_DB_USERNAME`, `KC_DB_PASSWORD`,
`KEYCLOAK_ADMIN`, `KEYCLOAK_ADMIN_PASSWORD` (production sets the `KC_DB*` values itself; the Keycloak admin bootstrap
user is not set by production at all).

Required (`:?`) variables of the chain: `GITHUB_REPOSITORY_OWNER`, `IMAGE_TAG`, `POSTGRES_USER`, `POSTGRES_PASSWORD`,
`RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS`, `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`, `KEYCLOAK_DIRECTORY_SYNC_SECRET`,
`HR_CORE_OTP_HMAC_KEY`, 12 × `SVC_*_CLIENT_SECRET`, `MINIO_ROOT_USER/PASSWORD`, `MINIO_FILESTORAGE_ACCESS/SECRET_KEY`,
and with observability `GRAFANA_ADMIN_USER/PASSWORD`.

## 0.4 Inherited values in production services (rendered config)

| Service | Key | Value |
|---------|-----|-------|
| 14 modules (all except the gateway) | `Keycloak__AdditionalValidIssuers` | `http://localhost:8088/realms/hrmanagement` |
| hr-gateway | `Keycloak__PublicAuthority` | `http://localhost:8088/realms/hrmanagement` |
| hr-gateway | `Gateway__HealthProbe__InternalBaseUrl` | `http://localhost:8080` (loopback inside the container; legitimate) |
| hr-gateway | `Gateway__EnabledModules__0..1` | `hr-core`, `hr-contracts` only (2 of 14) |
| keycloak | `KC_HOSTNAME` | `localhost` (`${KEYCLOAK_HOSTNAME:-localhost}`) |

`ASPNETCORE_ENVIRONMENT: Development` does not survive (production overrides it everywhere), and no rendered value
contains `YOUR_`, `USE_ENV` or `dev-svc-secret-local-only` (production requires the `SVC_*` secrets with `:?`).

Placeholders that remain **inside the images** (`appsettings.Production.json`, not overridden by compose today):

| File(s) | Keys with `YOUR_` / `USE_ENV_*` / `USE_SECRET_MANAGER` |
|---------|---------------------------------------------------------|
| 14 module `appsettings.Production.json` | `AllowedHosts`, `GatewayCallerProof:Secret` |
| gateway `appsettings.Production.json` | `AllowedHosts`, `Keycloak:Authority`, `Keycloak:ClientSecret`, `ConnectionStrings:GatewayAuditDb`, `GatewayCallerProof:Secret` |

`GatewayCallerProof__Secret` is not set by the production chain for any service, so each module would run with its
placeholder (or refuse) and the gateway's proof would not match.

## 0.5 Memory limits (sum of `deploy.resources.limits.memory`)

| Container | Limit |
|-----------|-------|
| 15 applications | 512 MiB each = 7.5 GiB |
| clamav | 2048 MiB |
| postgresql | 1024 MiB |
| keycloak | 768 MiB |
| rabbitmq | 512 MiB |
| minio | 512 MiB |
| **Total without observability** | **12.25 GiB** (matches G-OPS-10) |
| seq | 512 MiB (other observability containers have no limit) |
| Total with observability | 12.75 GiB + unlimited tempo/otel-collector/prometheus/alertmanager/grafana/exporters |

## Local resources (P-7)

Docker Desktop 29.9.2, `MemTotal` 15.48 GiB, `NCPU` 32, buildx v0.38.1, free disk D: 448.7 GB, C: 224.2 GB, port 443 free.

## Server probe summary (read-only, 2026-10-31T13:34Z)

| Item | Measured |
|------|----------|
| Provider / virtualisation | Contabo, KVM (QEMU i440FX), AMD EPYC |
| vCPU | 8 |
| RAM | 25,193,742,336 B = **23.46 GiB** (P-4: >= 15 GiB, plan table applies as written) |
| Disk `/` | 154.99 GB, **152.03 GB free** |
| OS | **Ubuntu 26.04.1 LTS (`resolute`)**, kernel 7.0.0-42 — owner's facts said 24.04.5 |
| Swap | none |
| Time | Europe/Berlin, NTP synchronized |
| Addresses | 161.97.185.71/22, IPv6 2a02:c207:2371:2327::1/64 |
| Listening | sshd :22 (v4/v6); systemd-resolved on loopback |
| sshd effective | `permitrootlogin yes`, `passwordauthentication yes`, `kbdinteractiveauthentication no`, `pubkeyauthentication yes` |
| sshd drop-ins | `50-cloud-init.conf` (600), `60-cloudimg-settings.conf` |
| Docker | not installed (Docker apt repo has `dists/resolute`) |
| ufw | inactive |
| unattended-upgrades | active |
