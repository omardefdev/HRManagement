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
| P6 | done | 2026-10-31T16:15:45Z |
| P7 | waiting for owner | |
| P8 | not started | |

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

P7 step 1: owner actions (see OWNER ACTION below), then host setup.

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

## P6 notes (in progress)

- Local acceptance stack `hrm-acceptance` (19 containers, left running by PLAN-35) **stopped, not removed**, to free
  Docker Desktop memory (2026-10-31T14:30Z); its volumes are kept. Restart for step 8:
  `docker start $(docker ps -aq --filter label=com.docker.compose.project=hrm-acceptance)`.
- Local registry: `docker run -d -p 5001:5000 --name hrm-registry --restart unless-stopped registry:2`.
- First build attempt failed in 3 s: `git archive` + Windows `tar.exe` cannot extract the repository's non-ASCII file
  names; fixed in fbb5c2ab (build context = detached `git worktree` of HEAD, removed after the build).
- `AcOps36VpsBootTests` written (11 scenarios a..k, local/server targets, one-off `docker compose run` negatives).
- First local boot (tag fbb5c2ab9d3f, 2026-10-31T14:39Z): every container healthy at once (pull 103 s, up 57 s, healthy
  +21 s). Smoke found real defects, fixed in ee853033: Keycloak redirect carried the dev port (`KC_HOSTNAME_PORT=8088`
  inherited from docker-compose.yml -> `-1`); gateway CSP `connect-src` kept `http://localhost:8088` and CORS kept
  `YOUR_PORTAL_HOST` (appsettings arrays cannot be cleared by an empty Production array -> index 0 overridden); no demo
  banner on HR.Core's root, which is the public careers portal (`_PublicLayout`). Smoke-script defects: Schannel
  revocation check on the internal CA (`--ssl-no-revoke` only for Schannel curl), Windows curl and `/tmp` paths,
  Keycloak's upper-case form_post page, and curl's ~8 KB cap on its own Cookie header (the gateway's chunked session
  cookie is larger; browsers are unaffected) -> explicit Cookie header from the jar.
- Tag B ee8530331666: cold start after `down -v` (pull 5 s from the local registry, up 57 s, healthy +21 s), smoke
  17 pass / 0 fail.
- Tag C b2f85bcbff12 deployed over B (pull 7 s, up 52 s, healthy +20 s), smoke green. The first local rollback failed:
  Git Bash copies instead of symlinking, so `current` was never a link and no previous tag was kept. Fixed in
  10a35bef (`current-tag` file on every platform; `current` symlink on Linux only). Rollback proof uses tags D and E.
- Final local sequence: `down -v`, cold deploy D 10a35befc8c9 (pull 5 s, up 58 s, healthy +20 s = **78 s** to all
  healthy), deploy E 205f37dfe1c1 over it (up 45 s, +21 s), `-Rollback` to D (up 27 s, +21 s); smoke 17 pass / 0 fail
  after each step. deployments.log: deploy D, deploy E (previous D), deploy D + rollback from E.
- Build/push to the local registry (per tag): fbb5c2ab9d3f build 329 s / push 30 s; ee8530331666 224 s / 29 s;
  b2f85bcbff12 241 s / 28 s; 10a35befc8c9 30 s (cache) / 24 s; 205f37dfe1c1 236 s / 27 s. 15 images = 2,113.8 MB
  compressed (810.0 MB unique layers); largest hr-core-web 193 MB, payroll-web 190 MB, others 125-135 MB.
- **AC-OPS-36a..k = 11/11 Pass locally in one run** (`acceptance-evidence/AC-OPS-36/`, per-scenario copies
  `<key>-after.json`/`<key>-db-checks.json`). Test fixes on the way: Windows cannot hold an empty process variable (36a
  negative now uses a second `--env-file` that empties `HRM_IMAGE_REPO`), local home path quoting (36j), bounded
  client concurrency (36e: 8 connections; an unbounded burst of TLS handshakes was reset once by Docker Desktop's port
  proxy), HR.Core internal page + careers portal both checked for the banner (36k).
- Memory (AC-OPS-36h, local, after a 10-minute tour): 4,826 MiB used of 15,851 MiB (30.4 %), no OOMKilled; clamav
  953/2048, keycloak 574/1024, seq 427/768, hr-core 286/1024, rabbitmq 215/512, payroll 194/768, contracts 171/640,
  gateway 171/512, the 384 MiB modules 137-168, postgresql 149/2048, file-storage 142/512, edge 24/128.
- Step 7: local single-host stack stopped with `down` (23 volumes kept until P8).
- Step 8 (acceptance stack unaffected): images rebuilt from HEAD with compose (974 s), `acceptance-up.ps1 -NoBuild
  -SkipLiveVerification` ready; AC-01, AC-02, AC-03, AC-04, AC-31 = 5/5 Pass through the acceptance gateway (guard off
  in Acceptance, limiter after authentication). AC-28/AC-30 could not start: they shell out to `pwsh`, which this
  machine does not have (tooling, not a regression). Their evidence files were restored (not part of this plan). The
  acceptance stack is stopped again (containers kept).

## P6 result

Finished (UTC): 2026-10-31T16:15:45Z
- AC-OPS-36a..k 11/11 Pass locally (evidence `acceptance-evidence/AC-OPS-36/`); boot 78 s; memory 30.4 % of host.
- Test runs: `artifacts/plan36/p6-acops36-final2.log` (11/11), `p6-acceptance-smoke.log` (5/5).

## OWNER ACTION (P7 step 1)

Waiting for owner (UTC): 2026-10-31T16:16:01Z

Skip anything already done:
1. Decisions still open from P0 (defaults apply if unanswered): domain (else `hr.161-97-185-71.sslip.io` /
   `sso.161-97-185-71.sslip.io`; with a domain: A records `app` and `auth` -> 161.97.185.71, DNS only), GHCR private
   (default yes), ACME_EMAIL (default empty), organization name (default «شعبة التنظيم والموارد البشرية»).
2. `gh auth refresh -h github.com -s write:packages,read:packages`
3. Create a **classic** token with only `read:packages` for the server (GitHub -> Settings -> Developer settings ->
   Personal access tokens -> Tokens (classic)); keep it for the later `docker login` on the server; never paste it in chat.
4. If not done: `ssh -t hrm-vps passwd` (change the root password in your own terminal).
5. Confirm VNC will be disabled in the Contabo panel once it is no longer needed.

Server state: still unchanged (read-only probe only). Next step after the reply: verify `gh auth status` scopes, then
P7 step 2 (host setup as root).
Owner action done (UTC): 2026-10-31T16:34:35Z — owner replied «تم»; decisions: defaults (sslip.io names, GHCR private, no ACME email, default organization name).

## P7 server changes (each with its undo; full table in docs/OPS_VPS_SINGLE_HOST.md §10)

- 2026-10-31T16:34Z root password last changed **2026-10-30** per `passwd -S root` (read-only check), i.e. before the
  exposure in chat: raised again with the owner (now `ssh -t hrm-vps sudo passwd root`).
- 16:35Z host-setup.sh (31 s), all CHANGED, second run 0 changes:
  - Docker Engine 29.9.2 + Compose 5.6.1 from download.docker.com (resolute) — undo: apt purge + remove docker.list/key
  - /etc/docker/daemon.json log rotation 10m x 3 — undo: delete, restart docker
  - /swapfile 8 GiB + fstab + vm.swappiness=10 (/etc/sysctl.d/90-hrm-swappiness.conf) — undo: swapoff, rm, delete lines
  - ufw enabled: deny incoming; 22/tcp, 443/tcp, 443/udp (v4+v6) — undo: `ufw disable`
  - /etc/apt/apt.conf.d/52-hrm-unattended (Automatic-Reboot false) — undo: delete
  - timezone Europe/Berlin -> UTC — undo: `timedatectl set-timezone Europe/Berlin`
  - user hrm (uid 1001, no password, docker group), /etc/sudoers.d/90-hrm, ~hrm/.ssh/authorized_keys (owner's key),
    /opt/hrm{,/releases,/shared} hrm 750 — undo: `userdel -r hrm; rm /etc/sudoers.d/90-hrm; rm -rf /opt/hrm`
- 16:36Z SSH hardening: `/etc/ssh/sshd_config.d/00-hrm-hardening.conf` written, `50-cloud-init.conf` (enabled password
  login) moved to `/root/sshd-backup-20261031T163600Z/`; effective: permitrootlogin no, passwordauthentication no,
  kbdinteractiveauthentication no, pubkeyauthentication yes. Checks from Windows: hrm key -> ok; root -> Permission
  denied (publickey); hrm password-only -> Permission denied (publickey). Undo:
  `ssh -o User=hrm hrm-vps "sudo bash /root/host-setup.sh --undo-harden-ssh"` (or VNC steps).
  `/root/host-setup.sh` kept until the release bundle (with its own copy) is on the server.
- 16:38Z server `.env` generated on the server (`/opt/hrm/shared/.env`, 600, hrm, 36 keys; secrets by
  `openssl rand`, never printed; checked by key names only). Undo: `rm /opt/hrm/shared/.env`.
- DNS: `hr.161-97-185-71.sslip.io` and `sso.161-97-185-71.sslip.io` -> 161.97.185.71 (1.1.1.1).
- 16:37Z GHCR build+push started for de4ffab786c0 (batch 1 took ~14 min: the acceptance rebuild had evicted the cache).

## OWNER ACTION (P7 steps 3.5 and 4.3)

Waiting for owner (UTC): 2026-10-31T17:12:20Z

1. Switch the SSH alias to `hrm` (root login is now refused), in your own PowerShell:
   `(Get-Content "$env:USERPROFILE\.ssh\config") -replace '^(\s*User\s+)root\s*$','${1}hrm' | Set-Content -Encoding ascii "$env:USERPROFILE\.ssh\config"`
   (only if the file holds just the hrm-vps block; otherwise edit its User line by hand). Then `ssh hrm-vps whoami` = hrm.
2. Log the server in to GHCR with the read-only classic token (paste it at the prompt in your terminal):
   `ssh -t hrm-vps "docker login ghcr.io -u omardefdev"`
3. Root password: `passwd -S root` reports the last change on 2026-10-30, before the exposure. Change it now:
   `ssh -t hrm-vps "sudo passwd root"`
- Owner (17:3xZ, «نفذها انت»): asked me to do the remaining owner steps. Alias: already `User hrm` (verified
  `ssh hrm-vps whoami` = hrm). Root password: **not done by me** (choosing/handling a password is the owner's, and the
  prompt forbids touching the server's password state) — still open. GHCR server login: not possible for me (I cannot
  create a read-only classic token, and the owner's full-scope `gh` token must not go on the server).
- **Registry: P-10 fallback.** The GHCR push failed three times on the network from this machine (connection timeout,
  then DNS lookup failure of ghcr.io inside Docker Desktop; four hr-core layers had uploaded, no refusal or billing
  prompt). With no server token either, the images go to the loopback-only registry on the server instead:
  `host-setup.sh --with-registry` -> `hrm-registry` (registry:2.8.3, 127.0.0.1:5050, 128 MiB, /opt/hrm/registry) —
  undo: `docker rm -f hrm-registry; sudo rm -rf /opt/hrm/registry`. Images streamed with
  `docker save | gzip | ssh hrm-vps docker load`, then pushed to 127.0.0.1:5050 on the server, which the deploy pulls
  from (AC-OPS-36a pull path unchanged). Server `.env`: `HRM_IMAGE_REPO=127.0.0.1:5050/hrmanagement`.
- build-push-images.ps1: `-SkipBuild` and `-PushAttempts` added (push already-built images, retry network failures).
Resumed (UTC): 2026-10-31T18:21:33Z — session restarted during the image transfer to the server; owner chose to retry image by image.
- SSH tunnel test (P-10 «يحتاج تحقق»): the tunnel answers on Windows 127.0.0.1:5050, but Docker Desktop's engine cannot
  reach it (`connection refused` from the daemon) -> tunnel push not usable; streaming is the remaining P-10 path.
- Upload throughput measured: 20 MB over ssh in 125 s = **~0.16 MB/s (~1.3 Mbit/s)**, with connection drops
  (one ssh handshake aborted, GitHub 15 s per request). Nothing has reached the server registry yet.

## BLOCKED (2026-10-31T18:25:39Z)

The owner's upload link is the bottleneck. Options:
- (a) Upload only the unique layers (~810 MB) once, with per-layer resume: GHCR push with retries (needs the owner's
  `ssh -t hrm-vps "docker login ghcr.io -u omardefdev"` with a read-only token), ~1.5 h at the measured rate.
- (b) Stream whole images over ssh (P-10 as written, ~2.1 GB because every image carries the shared base layers),
  ~3.7 h, a drop restarts that image.
- (c) Build the images on the server from a source archive (~tens of MB upload; the server downloads the SDK images
  itself) — a deviation from plan §6 rule 1 ("no source or build tools on the server"); the build context and SDK
  layers would be removed afterwards.
- (d) Wait for a faster network, then (a).
Root password change is still the owner's (`ssh -t hrm-vps "sudo passwd root"`).
Owner action done (UTC): 2026-10-31T18:31:00Z — owner chose option (c): build the images on the server from a source archive (deviation from §6 rule 1, recorded).
- Server build (owner decision c): source archive 29.5 MB uploaded in 372-384 s; first run stopped on buildx's
  `fs.read` entitlement (context outside the working directory) -> `--allow=fs.read=<src>`, archive kept until success
  (1a62e06f). Second run: **build 520 s** on the server (8 vCPU), push to 127.0.0.1:5050 10 s, then source, build
  cache and SDK image removed (build cache 0 B; images 2.85 GB).
- **First server deploy** 1a62e06f28c9 (18:56-18:59Z): env check 36 keys; pull 16 s; up 113 s; healthy +12 s
  (**125 s** to all healthy). Smoke with `-AllowStagingCa`: Let's Encrypt **staging** certificates for both names via
  TLS-ALPN on 443 (issuer "(STAGING) ..."), redirect to SSO, edge 404/403, sign-in, 13/13 module UIs, public listeners
  "22 443", nextDue in 91 days.
- External port check from the owner's machine reported every port open. Diagnosis: a never-used control port (23456)
  also "connects"; tcpdump on the server shows the SYN from the owner's egress (87.249.x.x) arriving and **no reply**
  (ufw INPUT DROP): the client's path (VPN/proxy terminating TCP) answers every port itself. smoke.ps1 and AC-OPS-36c
  now probe the control port; when it connects the client view is a Warn/inconclusive and the server-side evidence
  (`ss`: only 22 and 443 public; ufw active, no other ALLOW) decides.
- 19:02Z HRM_ACME_CA -> production, edge recreated: "certificate obtained successfully" for both names; smoke without
  `-AllowStagingCa`: **publicly trusted** (C=US, O=Let's Encrypt, CN=YE1 / YE2, notAfter 2027-01-29), 17 pass, 0 fail,
  1 warn (client port view), 5 Deferred(PLAN-37). Windows trust store also accepts the certificate.
