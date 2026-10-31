Started (UTC): 2026-10-31T13:32:59.0005780Z

# PLAN-36 progress

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-10-31T13:41:37Z |
| P1 | done | 2026-10-31T13:51:21Z |
| P2 | done | 2026-10-31T13:58:09Z |
| P3 | done | 2026-10-31T14:02:43Z |
| P4 | done | 2026-10-31T14:13:06Z |
| P5 | done | 2026-10-31T14:29:19Z |
| P6 | in progress | |
| P7-P8 | not started | |

## P0 — preconditions (checked 2026-10-31T13:33Z)

| # | Precondition | Result |
|---|--------------|--------|
| 1a | `reports/plan35-final-report.md` on `main` | OK |
| 1b | `main` = `origin/main` | **FAIL**: local `main` 76ac25dd is 5 commits ahead of `origin/main` 7855f0a5 (0 behind): c1c38f30 (defect 15, gateway 429 at once), cb618e66 + 190937bb + b70b22ac (defect 9, Playwright traces), 76ac25dd (docs). Merged locally after PR #28, never pushed. |
| 1c | last `push` run on `main` green | OK: run 41199597707 (PR #28 merge), success, 1h7m |
| 1d | no other plan session on this tree | OK: no running session (`list_sessions`) |
| 2 | clean tree | OK: only `global.json` (owner), `tmp-vendors.html`, the prompt file, this file |
| 3 | `ssh -o BatchMode=yes hrm-vps "echo ok"` | OK |
| 4 | host key | OK: ED25519 `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto` matches |
| - | GitHub login | `omardefdev` |

## P0 — server probe (read-only, `artifacts/plan36/server-probe.txt`)

- KVM, AMD EPYC, **8 vCPU**, **RAM 25,193,742,336 B = 23.46 GiB** (>= 15 GiB: plan's memory table applies as written, P-4).
- Disk `/`: 154.99 GB total, **152.03 GB free** (>= 60 GB).
- **OS: Ubuntu 26.04.1 LTS (`resolute`), kernel 7.0.0-42** — not 24.04.5 as the owner's facts say. Same host key, so
  same server; recorded as a deviation. Docker's apt repository has `dists/resolute` (HTTP 200), so host-setup can use it.
- No swap. Timezone Europe/Berlin, NTP synchronized. Listening: only sshd :22 (v4+v6) and systemd-resolved on loopback.
- IPv6 `2a02:c207:2371:2327::1/64` on eth0 (ufw must cover v6 too; ufw default IPV6=yes).
- sshd effective: `permitrootlogin yes`, `passwordauthentication yes`, `kbdinteractiveauthentication no`,
  `pubkeyauthentication yes`. Drop-ins: `50-cloud-init.conf` (600), `60-cloudimg-settings.conf`.
- Docker not installed. ufw inactive. unattended-upgrades active.

## P0 — local resources (P-7)

- Docker Desktop 29.9.2: `MemTotal` 16,621,195,264 B = 15.48 GiB (>= 14 GiB, no owner action), `NCPU` 32.
- buildx v0.38.1. Free disk D: 448.7 GB, C: 224.2 GB. Local port 443 free.

## Notes for later phases

- PLAN-35 §10 #11: Actions minutes ~1,460 of 2,000/month used by PLAN-35 alone, plus PR #27/#28 runs since. The single
  P8 PR run (~67-115 min) must be the only push. Check remaining minutes before P8.
- Defect 15 (local main) already sets `QueueLimit = 0` + `Retry-After` on the gateway limiter: P3 builds on it.
- PLAN-35 §10 #2-#6 are PLAN-36 inputs (pg healthcheck over socket, no app healthchecks, cold-start crashes,
  file-storage volume ownership, verify-staging-stack anonymous health).

## Server state

Nothing changed on the server (read-only probe only).

## BLOCKED (2026-10-31T13:34:39Z)

Precondition 1b fails: local `main` is 5 commits ahead of `origin/main` (defects 9 and 15, merged locally after PR #28).
Options:
- **(a, recommended)** Proceed. Branch from local `main`; the 5 commits reach GitHub inside the single PLAN-36 PR
  (`release/plan-36` is cut from `main`). One CI run instead of two; P3 builds on the defect 15 limiter anyway.
- (b) The owner first pushes them through their own PR (one extra CI run, ~67-115 Actions minutes), then PLAN-36 starts
  from the new `origin/main`.

OWNER ACTION (decisions, asked together with the block):
Waiting for owner (UTC): 2026-10-31T13:34:39Z
Owner action done (UTC): 2026-10-31T13:39:13Z — owner chose **(a) proceed**; the 5 local commits ride in the PLAN-36
PR. Decisions D-OPS-2 / D-OPS-1 / ACME_EMAIL / org name / server hygiene not answered yet: defaults apply unless
answered by P7.

## Next step

P6 (local registry, build-push, -Local deploy, AcOps36VpsBootTests, evidence).

## P0 result

Finished (UTC): 2026-10-31T13:41:37Z
- Baseline in `reports/plan36-baseline.md` (build 0/0 in 30 s; production chain fails on `alert-sink`/`seq` without
  observability and on `minio` with it; inherited localhost values; memory 12.25 GiB without observability).
- Decision: `config` dumps rendered with `--env-file artifacts/plan36/lint.env` (lint-only values) so the dev `.env`
  in the repository root is never read into a dump.
- Finding: the production chain sets no `GatewayCallerProof__Secret`; every Production image carries the
  `USE_ENV_OR_SECRET_MANAGER` placeholder for it (and for `AllowedHosts`).

## P1 result

Finished (UTC): 2026-10-31T13:51:21Z
- Production chain (`yml + services + images + production`, `--profile full-stack`) renders **without** the
  observability file; with `observability.yml + observability.production.yml` too; `--profile minio` without
  `MINIO_IMAGE` fails as intended.
- Files: `docker-compose.production.yml`, new `docker-compose.observability.production.yml`, new
  `File.Storage.Web/appsettings.Production.json`, `.github/workflows/deploy.yml` (`--profile full-stack` + the
  observability production file in all 6 invocations), tests `Ops036ComposeLintTests` (new), `Sec001PortScanTests`,
  `Sec004SecretRotationTests` (updated for the moved block and the MinIO change).
- Tests: build 0/0; `Ops036|Sec001|Sec004|Hard001` 33/33; full unit suite 4236/4236 (66 s).
- Decisions:
  - **volume-init** one-shot (`alpine:3.20.3`, root, `network_mode: none`, caps CHOWN/DAC_OVERRIDE/FOWNER) chowns
    every application volume to 1654 before any app starts (PLAN-35 defect 5; outbox volumes were root-owned too).
    `restart: "no"` is the one exception to P-5 (a one-shot); apps depend on it with
    `service_completed_successfully`.
  - `x-app-runtime` (`restart: unless-stopped`) merged into all 15 apps in the production file (P-5 applied to
    the production chain, not only the single-host file). Gateway now waits for PostgreSQL, RabbitMQ and Keycloak healthy.
  - **MinIO credentials are `${VAR:-}` instead of `:?`** (deviation): compose interpolates inactive services, so
    `:?` would force four unused secrets on every Local-provider host (verified with a throwaway compose file). Kept
    fail-closed by: no default value, lint-compose rule (P4), and the guard refusing File.Storage on Minio with empty or
    placeholder keys (P2). Same reason for `image: ${MINIO_IMAGE:-}` (the plan said `:?`).
  - `minio-backup` moved from profile `backup` to `minio` (it depends on MinIO).
  - OTEL in production: `${HRM_OTEL_ENABLED:-true}` / `${HRM_OTEL_ENDPOINT:-http://otel-collector:4317}` (the
    single-host file turns it off).
  - Gateway keeps its own Data Protection volume `gateway_dataprotection_keys` (explicit path wins over the anchor);
    the 14 modules get `dp_<module>` at `/var/hrm/dpkeys`.
- Git identity: no `~/.gitconfig` on this machine; commits use `-c user.name=omardefdev -c user.email=omar@defdev.sy`
  (the identity of all earlier commits), nothing written to git config.
- Open: module `appsettings.Production.json` files set `Serilog:FilePath logs/...` under root-owned `/app` (non-root
  user cannot write) — check in P6 whether it breaks start or only drops the file sink.

## P2 result

Finished (UTC): 2026-10-31T13:58:09Z
- New `ProductionConfigurationGuard` (+`ProductionConfigurationGuardOptions`), an `IHostedService` registered in
  `AddHrProductionNetworking` (all 15 services). Critical keys `AllowedHosts`, `GatewayCallerProof:Secret`,
  `Keycloak:Authority`, `Organization:Name`; the gateway adds `Keycloak:PublicAuthority` and
  `Keycloak:ClientSecret` (required too). Rules: placeholder markers, required-but-empty, caller-proof secret < 32,
  public authority on localhost (Production only), empty `Gateway:EnabledModules` entry, mojibake in
  `Organization:Name/ShortName`, demo mode (`Deployment:Mode=demo`) with `Seed:Profile` None, and for File.Storage
  only (`ObjectStorageCredentialsRequired`) MinIO keys when the provider is Minio. Messages Arabic + English, key only.
  Non-critical placeholders: one Warning per key.
- Decision: **Production cannot switch the guard off** (`Deployment:EnforceConfigGuard=false` is ignored there); the
  switch only turns it on in Development/Acceptance. Reason: a bypass switch in Production would defeat the guard.
- Decision: the demo-mode key is `Deployment:Mode` (env `Deployment__Mode`), set by `docker-compose.vps-demo.yml` (P4).
- `LicenseValidationMiddleware`: exempts `/health`, `/alive`, `/ready`, `/metrics` (segment match), parses the
  expiry with `InvariantCulture` + `AdjustToUniversal | AssumeUniversal`; the "employee cap" summary is gone.
- Gateway `appsettings.json`: `Organization:Name`/`ShortName` fixed (were double-encoded mojibake).
- Compose: HR.Core no longer mounts `file_storage_data` (deviation from §1: HR.Core never registers the file storage
  service, only File.Storage does, so the mount would only widen access to the file bytes).
- Tests: build 0/0; `Ops036|LicenseValidation|ProductionConfigurationGuard` 62/62 (ar-SA/ar-SY/en-US/fr-FR expiry
  parsing; placeholder fails in Production, passes in Development/Acceptance); full unit suite 4285/4285.

## P3 result

Finished (UTC): 2026-10-31T14:02:43Z
- New `GatewayRateLimitPartitioner`: `user:{sub}` (sub or NameIdentifier of an authenticated principal),
  `ip:{RemoteIpAddress}` otherwise, no limiter for GET/HEAD static assets (`.css .js .woff .woff2 .ttf .png .svg .ico
  .map`) under `/ui/{module}/...` or the gateway root; never under `/api`, `/careers`, `/health`, `/metrics`.
- `GatewayRateLimiting`: `GatewayRateLimitSettings` (anonymous `RateLimiting:PermitLimitPerMinute` default 120,
  `RateLimiting:AuthenticatedPermitLimitPerMinute` default 600, `RateLimiting:ExemptStaticAssets` default true), both
  clamped 30..2000; 429 log names the partition kind (`user`/`ip`) and its limit, never the identity.
  `gateway-ui` policy unchanged (shared window at the anonymous limit).
- Gateway `Program.cs`: `UseRateLimiter()` moved right after `UseAuthentication()`.
- Note: OIDC callbacks (`/signin-oidc`) are now handled by the authentication middleware before the limiter; an invalid
  callback is rejected cheaply, recorded for the review.
- Tests: build 0/0; `GatewayRateLimit*` 36/36 (new `GatewayRateLimitPartitionerTests` + defect-15 tests);
  full unit 4311/4311; gateway integration tests 2/2 (incl. `GatewayRateLimitingIntegrationTests`). The acceptance smoke
  for the gateway runs in P6 step 8 with the acceptance stack.

## P4 result

Finished (UTC): 2026-10-31T14:13:06Z
- New: `docker-compose.vps.yml` (edge `caddy:2.8.4` on 443/tcp+udp at 172.28.0.10, Seq `127.0.0.1:5380`, gateway and
  Keycloak ports closed, memory table, `DOTNET_gcServer=0`, OTEL off, `start_period: 240s`, 300/600 limits,
  `KnownProxies=172.28.0.10`), `docker-compose.vps-demo.yml`, `docker-compose.vps-local.yml`,
  `deploy/vps/Caddyfile`, `deploy/vps/clamav/clamd.conf`, `scripts/vps/lint-compose.ps1`,
  `Web.Shared/Views/Shared/_DeploymentBanner.cshtml` (shown in the gateway and module headers when
  `Deployment:Banner=demo`); gateway header now shows the organization name (AC-OPS-36k).
- Production file: Keycloak `start --import-realm`, `KC_PROXY` removed, `KC_PROXY_HEADERS=xforwarded`,
  `KC_HOSTNAME=${SSO_DOMAIN:?}`, `KC_HOSTNAME_STRICT_BACKCHANNEL=false`, `KC_HEALTH_ENABLED`, bootstrap admin
  required (`KEYCLOAK_ADMIN/_PASSWORD :?`, needed for kcadm in the P-9 domain switch), `restart: unless-stopped`;
  PostgreSQL healthcheck = PID 1 is postgres **and** `pg_isready -h 127.0.0.1` (TCP) (P-5, PLAN-35 defect 2).
- Tests/checks: `lint-compose.ps1` PASS production (20 services) and single-host (22); negative (`IMAGE_TAG=latest`)
  fails as expected; `caddy validate` (caddy:2.8.4) valid for the ACME and the `tls internal` variants;
  `Ops036|Sec001|Sec004|Hard001` 81/81; full unit 4330/4330.
- Decisions / deviations:
  - **Memory sum 13.5 GiB, not 13.1**: the plan's table counts "nine other services" but there are ten at 384 MiB
    (health, movements, training, idcard, records-archive, admin-logistics, it-infra, dev-analytics, compliance,
    auth-directory). 13.5 GiB is 57 % of the measured 23.46 GiB host.
  - Edge network: `ip_range 172.28.0.128/25` for dynamic addresses so no container can take 172.28.0.10 first.
  - Seq joins an extra non-internal `ops` network: Docker cannot publish a port of a container that is only on
    internal networks.
  - ACME email: `CADDY_ACME_EMAIL_OPTION=${ACME_EMAIL:+email ${ACME_EMAIL}}` (Caddy substitutes env before
    parsing, so an empty value removes the line; no address is invented). `ADMIN_ALLOWED_CIDRS` empty becomes
    `192.0.2.255/32` (RFC 5737 documentation address = nobody) because `remote_ip` needs at least one range.
  - Caddy: no `trusted_proxies`, so a client `X-Forwarded-For` is ignored and Caddy sends the connection address.
  - Demo mode is the separate file; `remote-deploy.sh` (P5) adds it only when `HRM_DEPLOYMENT_MODE=demo`.
- Note for P6: Git Bash rewrites absolute container paths in `docker run` arguments; use `MSYS_NO_PATHCONV=1`.

## P5 result

Finished (UTC): 2026-10-31T14:29:19Z
- `docker-compose.images.yml`: `${HRM_IMAGE_REPO:?}/<service>:${IMAGE_TAG:?}` for the 15 services;
  `e2e-journeys.yml` and `deploy.yml` derive `HRM_IMAGE_REPO` lowercase from the repository owner.
- New scripts: `scripts/vps/build-push-images.ps1` (build context = `git archive HEAD`, buildx bake linux/amd64 in
  batches, per-image timed `docker push`, digest + compressed size into `artifacts/vps/images-<tag>.json`),
  `make-deploy-bundle.ps1` (files from the committed tag via `git show`, the VpsDemo realm, RELEASE; refuses any
  `.env`; gitleaks pinned image over the staged bundle), `deploy.ps1` (remote over ssh / `-Local` through Git Bash /
  `-Rollback`; timings JSON), `smoke.ps1` (smoke.sh + outside port checks; IP masked to two octets).
  `deploy/vps/remote-deploy.sh` (deploy/rollback/check-env; key names and lengths only; constant project `hrm`;
  `--profile full-stack`; demo file when `HRM_DEPLOYMENT_MODE=demo`; waits for healthy up to 600 s; previous tag),
  `deploy/vps/smoke.sh` (containers, OOM, TLS incl. `--allow-staging-ca` = LE staging only, redirect to SSO,
  edge 404/403, curl sign-in with form_post handling + 13 module UIs, public listeners, nextDue warning,
  `Deferred(PLAN-37)` list; JSON), `deploy/vps/host-setup.sh` (base / `--harden-ssh` / `--undo-harden-ssh` /
  `--with-registry`), `deploy/vps/.env.vps.example` (Arabic comments, no secret values), `deploy/vps/.env.vps-local`
  (lab-only, `-lab-only` suffix + `.gitleaks.toml` allow-list path AND line), `docs/OPS_VPS_SINGLE_HOST.md` (draft).
- `deploy.yml`: deploy and rollback jobs build the bundle and run `remote-deploy.sh` over plain ssh/scp with
  `StrictHostKeyChecking=yes` (new secret `DEPLOY_SSH_KNOWN_HOSTS`, var `APP_DOMAIN`); health gate = `smoke.sh`
  (closes PLAN-35 defect 6: `verify-staging-stack.ps1` no longer used); `record-deployment.sh` step dropped
  (remote-deploy.sh writes `/opt/hrm/deployments.log`). Not run (workflow_dispatch / tags only).
- `scripts/deploy-organization.ps1`: wrapper (sets `HRM_ORGANIZATION_NAME` in the server .env line or the local
  process env, then `deploy.ps1`); `HR_LEGAL_REGIME`/`HR_LICENSE_*` gone.
- `prepare-keycloak-import.ps1 -Profile VpsDemo -AppDomain`: every user password `${DEMO_USERS_PASSWORD}`, gateway
  secret and 8 service-client secrets as `${...}` placeholders, unused secret clients disabled, redirect/web origin/
  post-logout from APP_DOMAIN, `directAccessGrantsEnabled=false`, `sslRequired=external`, brute-force protection;
  refuses leftover dev values; output `artifacts/vps/import/`. Keycloak gets the referenced variables (vps.yml).
- Checks: `bash -n` OK for the three `.sh`; all `.ps1` parse under PowerShell 5.1 and are ASCII; lint-compose PASS
  both chains (the lint now keeps the example's non-secret defaults, which caught OTEL filler values); gitleaks: the
  lab-only file passes, a real token in it is still found; `Ops036|Sec00|Hard001` 111/111.
- Decisions: images built from `git archive HEAD` (local edits and untracked files cannot leak into an image;
  `global.json` ignored in the dirty check because the owner's local roll-forward edit never enters the context);
  bake + one timed push per image instead of `build --push` (P-? "time every push"); `HRM_HOME` layout per P-11.
