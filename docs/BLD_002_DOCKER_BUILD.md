# BLD-002 — Docker build performance

## Problem (before BLD-002)

- Each of 15 services had its own Dockerfile with `COPY . .` before `dotnet restore`.
- Any source change invalidated the restore layer → full NuGet restore + publish.
- Parallel `docker compose build --parallel` caused NuGet timeouts (QuestPDF, MassTransit, SkiaSharp).
- No shared NuGet cache between concurrent builds.

## Solution

| Component | Purpose |
|-----------|---------|
| `docker/DotnetService.Dockerfile` | Unified build: manifest-only restore layer + BuildKit NuGet cache (`sharing=locked`) |
| `scripts/docker-generate-build-manifest.ps1` | Copies `*.csproj` + `packages.lock.json` into `docker/build-manifest/` (gitignored) |
| `docker/service-build-matrix.json` | Per-service `PROJECT_CSPROJ` / `ASSEMBLY_NAME` args for compose and deploy |
| Compose profile `full-stack` | Infrastructure by default; all 15 .NET images only when profile is active |
| Compose profile `dev-logistics` | Infra + 4 apps (hr-core, admin-logistics, file-storage, gateway) — ~40% less RAM |
| `docker-compose.dev-profiles.yml` | Shared profile tags; extensible for `dev-payroll`, `dev-contracts` |
| `docker-compose.dev-logistics.yml` | Gateway module subset + `depends_on: !reset` for slim stack |

## Environment variables

Set in shell or `.env`:

```bash
DOCKER_BUILDKIT=1
COMPOSE_PARALLEL_LIMIT=2   # dev: limit concurrent builds
# CI uses COMPOSE_PARALLEL_LIMIT=4
```

## Recommended dev workflow

**Infrastructure only (fast daily work):**

```powershell
pwsh scripts/dev-infra-up.ps1
pwsh scripts/dev-run-hrcore.ps1
```

**Rebuild one Docker service after code change:**

```powershell
pwsh scripts/dev-build-service.ps1 -Service hr-core-web
```

**Dev profile — Admin Logistics / policies / attachments (~11 containers):**

```powershell
pwsh scripts/dev-profile-up.ps1 -Profile logistics
# Rebuild after code change:
pwsh scripts/dev-profile-up.ps1 -Profile logistics -Build
pwsh scripts/dev-build-service.ps1 -Service admin-logistics-web -Profile logistics
# Stop app services only (keep postgres/keycloak/minio/rabbit/clamav):
pwsh scripts/dev-profile-down.ps1 -Profile logistics
```

| Profile | App services | Use when |
|---------|--------------|----------|
| `dev-logistics` | hr-core, admin-logistics, file-storage, gateway | FUN-005, POLICY-CLASSIFY, custody UAT |
| `full-stack` | All 15 .NET services | Cross-module E2E |

Infra always started with any `docker compose up`: PostgreSQL, RabbitMQ, MinIO, ClamAV, Keycloak.

**Full stack in Docker (integration / E2E):**

```powershell
pwsh scripts/docker-generate-build-manifest.ps1
docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.local.yml -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml --profile full-stack build
docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.local.yml -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml --profile full-stack up -d
```

## CI

1. `scripts/docker-generate-build-manifest.ps1`
2. `scripts/bld002-docker-build-validate.ps1`
3. `docker compose --profile full-stack build` with `DOCKER_BUILDKIT=1`

## Benchmark

```powershell
pwsh scripts/bld002-benchmark-build.ps1 -Service hr-core-web
```

Acceptance: warm rebuild after touching one `.cs` file **< 2 minutes** on a dev machine.

## Validation

```powershell
pwsh scripts/bld002-docker-build-validate.ps1
```

Unit tests: `Bld002DockerBuildTests` in `HRManagement.UnitTests`.
