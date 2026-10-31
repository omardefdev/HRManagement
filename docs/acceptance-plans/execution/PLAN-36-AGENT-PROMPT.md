# Task: PLAN-36 — The production profile boots on one server for demo and testing: images from a registry, a Caddy TLS edge that exposes only 443, a memory budget that fits, a rate limiter that survives a demo room behind one NAT, a production configuration guard, and a real deployment to the owner's VPS (one session, with owner actions)

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

Some steps are **owner actions (O)**: DNS, GitHub token scopes, typing a password, editing the owner's `~/.ssh/config`. At each owner action:
1. Write `OWNER ACTION:` in the progress file, with the exact commands, and stamp `Waiting for owner (UTC): <timestamp>`.
2. Commit what is complete.
3. End the turn with the commands in a short numbered list.

When the owner replies (for example «تم»), stamp `Owner action done (UTC): <timestamp>`, verify the result yourself, and continue.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan36-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results, and the server state (what was installed or changed on the server, and how to undo it);
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan36-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Never re-run a server step without first checking the server's current state (every server script is idempotent, but check anyway).

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan36-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Every owner action records the `Waiting for owner` / `Owner action done` pair.
- Record the duration of every image build, image push (with total pushed size), server pull, and stack start.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**, for broad read-only searches or for root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (compose `config` dumps, container logs, build logs, test runs) to files under `artifacts/plan36/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior platform / DevOps engineer: Docker Compose, Caddy and Let's Encrypt, Keycloak 24 behind a reverse proxy, ASP.NET Core hosting (forwarded headers, rate limiting, hosted services), Ubuntu server setup and SSH hardening, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`**, Docker Desktop, culture ar-SA, Windows OpenSSH client). You also operate **one real server** through `ssh hrm-vps`, using a key the owner set up for this purpose.

PLAN-34 made `main` locally green. PLAN-35 put it on GitHub with a green remote CI. This plan makes the **production** compose profile boot by itself, puts it behind a TLS edge, and deploys it to the owner's VPS as a demo and test environment with **synthetic data only**.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-36-ops-production-boot-single-host.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. `reports/plan35-final-report.md`:
   - the defects recorded for later;
   - the readiness note for PLAN-36;
   - the CI timing and Actions minutes.
3. `reports/plan34-final-report.md` §8: `auth-directory-web` crashes on a cold start (exit 139, no restart policy).
4. `docs/PRG_002_OPERATIONS_GAP_STUDY.md`: G-OPS-01, G-OPS-10, G-OPS-15, and the decisions D-OPS-1, 2, 6, 9.
5. The compose chain and the code the plan cites:
   - `docker-compose*.yml`, `.github/workflows/deploy.yml`;
   - `ProductionHostExtensions.cs`, `ForwardedProxyTrust.cs`, `KeycloakExtensions.cs`, `DatabaseMigrationExtensions.cs`, `LegalControlGuard.cs`, `LicenseValidationMiddleware.cs`;
   - the gateway's `Program.cs` and `appsettings*.json`;
   - `scripts/prepare-keycloak-import.ps1`, `docker/service-build-matrix.json`, `scripts/docker-generate-build-manifest.ps1`, `scripts/deploy-organization.ps1`.
6. `artifacts/plan36-inputs/server-access.md` (git-ignored): the owner's server inputs.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-35 is finished and merged.**
   - `reports/plan35-final-report.md` exists on `main`.
   - `git rev-parse main` = `git rev-parse origin/main`.
   - The last `push` run on `main` is green.
   - No other plan session is running on this working tree.
2. **The working tree is clean** except for the owner's local `global.json` edit, the untracked `tmp-vendors.html`, git-ignored `artifacts/`, and this prompt file. This prompt file is committed in P0.
3. **Key login to the server works without a password prompt:** `ssh -o BatchMode=yes hrm-vps "echo ok"` prints `ok`.
4. **The server's host key is the one the owner accepted.**
   - Run `ssh-keygen -lF 161.97.185.71 -f "$env:USERPROFILE\.ssh\known_hosts"`. The ED25519 fingerprint recorded on 2026-10-31 is `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`.
   - If it differs, stop and ask the owner whether the server was reinstalled or its credentials reset since then.
   - **Never** pass `StrictHostKeyChecking=no` and never delete a `known_hosts` entry yourself.

## Server facts (owner-provided 2026-10-31; re-verify in P0)

| Item | Value |
|------|-------|
| Provider | **Contabo** VPS (not Hostinger as the plan assumes — see P-4). Exact plan unknown: measure it in P0 |
| Public IPv4 | `161.97.185.71` |
| OS | Ubuntu 24.04.5 LTS, hostname `vmi3712327` |
| SSH alias on the owner's machine | `hrm-vps` in `%USERPROFILE%\.ssh\config`: `HostName 161.97.185.71`, `User root`, `IdentityFile ~/.ssh/hrm_vps` |
| Authorized public key | `ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIhnW4EM/WfCOaXHT3Zy5Jb443c5/yJVRzjn5jVnGlNl hrm-vps` |
| Current state | Fresh image. Root login by key works. Password authentication may still be on. Contabo's VNC console was enabled by the owner for recovery |
| GitHub owner | `omardefdev` (repository `omardefdev/HRManagement`, **private**, free plan). Re-check with `gh api user --jq .login` |

**Security notes:**
- The root password was exposed in a chat on 2026-10-31. The owner was told to change it. Confirm in P0, and do not touch the server's password state yourself.
- The private key `%USERPROFILE%\.ssh\hrm_vps` never leaves the owner's machine. Never read it, copy it, print it, mount it into a container, or put it on the server.

## Decisions — ask the owner in P0, then record

Ask these **together in P0**, in one short Arabic message. Then continue with the code phases (P1–P5), which do not depend on the answers, so the session does not stall.

| # | Question to the owner | Default if no answer by P7 |
|---|-----------------------|----------------------------|
| D-OPS-2 | The domain: do you have one? If yes, add two DNS **A** records pointing to `161.97.185.71`, `app.<domain>` and `auth.<domain>`. On Cloudflare, use **DNS only** (grey cloud), not the proxy. | No domain: `APP_DOMAIN=hr.161-97-185-71.sslip.io`, `SSO_DOMAIN=sso.161-97-185-71.sslip.io`. Switching to a real domain later is documented (P-9). |
| D-OPS-1 | The image registry: GHCR private (`ghcr.io/omardefdev/hrmanagement/<service>:<sha12>`)? | **Yes, GHCR private.** On 2026-10-31, GitHub's billing docs said container image storage and bandwidth for the Container registry "is currently free" — «يحتاج تحقق» at execution. It needs two owner actions in P7: a `write:packages` scope for pushing, and a **read-only** token for the server. Fallback (P-10): a loopback-only registry on the server, fed through an SSH tunnel. |
| ACME_EMAIL | An email address for Let's Encrypt expiry notices? | Empty: Caddy registers without an email. **Never** fill in an address the owner did not give for this purpose. |
| HRM_ORGANIZATION_NAME | The organization name shown in the header? | «شعبة التنظيم والموارد البشرية» (the plan's default). |
| Server hygiene | Confirm that (1) the root password was changed with `passwd`, and (2) VNC is disabled in the Contabo panel once it is no longer needed. | If (1) is not confirmed by P7: an **OWNER ACTION** for `ssh -t hrm-vps passwd`, typed in the owner's own terminal. |

All other plan decisions keep their defaults with no question: D-OPS-6 (Seq only, loopback, OTEL off), D-36-1 (demo mode), D-36-2 (300/600 per minute), D-36-3 (Local file storage), D-36-4 (`Seed__Profile=Demo`), D-36-5 (8 GiB swap, `vm.swappiness=10`), and D-OPS-9 (license disabled).

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **You operate the real server yourself** through `ssh hrm-vps`, in P7. The owner configured the key for exactly this. Steps that could lock everyone out (SSH hardening, the firewall) follow the gated order in P7, and each has a verified rollback. Everything you change on the server is listed in the progress file with its undo command. |
| P-2 | **Secrets never enter your context, the repository, the evidence or the transcript.** Generate server secrets **on the server**, without echoing them. Check `.env` by key names only (for example `cut -d= -f1`). Never `cat`, `grep` the value of, or `docker compose config` (unredacted) the server's `.env`. Redact evidence files before committing. The owner reads the demo password themselves (P7). |
| P-3 | **PowerShell 5.1.** Every `.ps1` script is ASCII-only and works under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block with that form. Use the Windows OpenSSH `ssh`/`scp` and the built-in `tar.exe`. Shell scripts stay LF: `.gitattributes` already has `* text=auto eol=lf`. Run them on the server with `bash <script>`, never relying on the executable bit. |
| P-4 | **Server size.** The plan's memory table (≈ 13.1 GiB of caps, usage under 75 % of host memory) assumes 16 GB. Measure the real server in P0. **≥ 15 GiB RAM:** apply the plan's table as written. **Less than 15 GiB:** `BLOCKED:` with the measured numbers and the options: upgrade the plan, or reduced caps. Never silently shrink ClamAV or drop a service. Disk: at least 60 GB free, otherwise ask. |
| P-5 | **Cold start and reboot.** Every container in the single-host chain has `restart: unless-stopped`. Every application `depends_on` PostgreSQL with `condition: service_healthy`, plus RabbitMQ and Keycloak where it uses them. PostgreSQL's healthcheck proves TCP readiness, not just a running process. These are the PLAN-34 §8 `auth-directory-web` exit-139 lesson and the PLAN-35 PostgreSQL readiness fixes. P7 proves the stack survives a server reboot. |
| P-6 | **Let's Encrypt.** Port 80 stays closed, so issue certificates by TLS-ALPN on 443 only, and disable the HTTP challenge explicitly in the Caddyfile. Put the ACME directory behind a variable (`HRM_ACME_CA`, default production). Use the **staging** CA until the first smoke run is green, then switch to production and restart the edge only. Record which CA issued the final certificates. |
| P-7 | **Local resources.** The local single-host run needs Docker Desktop memory for the whole stack. Record `docker info` `MemTotal` and `NCPU` in P0. If `MemTotal` is under 14 GiB, raise it as an **OWNER ACTION** (`%USERPROFILE%\.wslconfig` `memory=`, then `wsl --shutdown`). Also check that local port 443 is free (`Get-NetTCPConnection -LocalPort 443`). |
| P-8 | **Merging and CI minutes.** `main` has no GitHub branch protection (private repository on the free plan); the local pre-push guard refuses direct pushes to `main`. The work reaches `main` **only through one PR at the end** (P8), with green checks. Do not push the feature branch earlier: each CI run on this repository costs about 67 minutes of the limited Actions minutes. Add no scheduled workflows. |
| P-9 | **Domain change later.** Keycloak imports the `VpsDemo` realm only on its first boot. `docs/OPS_VPS_SINGLE_HOST.md` documents the switch from sslip.io to a real domain: update `.env`, update the gateway client's redirect URIs and web origins with `kcadm.sh` inside the Keycloak container (or drop and re-import the demo realm, since its data is synthetic), then redeploy. |
| P-10 | **Registry fallback**, only if GHCR fails (refused push, billing prompt, token problems): a `registry:2` container on the server bound to `127.0.0.1:5050`, created by `host-setup.sh --with-registry` **outside** the application compose project. It has a 128 MiB cap and a volume under `/opt/hrm/registry`. The owner's machine pushes to it through `ssh -N -L 5050:127.0.0.1:5050 hrm-vps`. Docker Desktop may not reach a tunnel on the Windows loopback («يحتاج تحقق»). If it cannot, stream the images instead (`docker save … \| ssh hrm-vps docker load`), then tag and push them into the loopback registry on the server. Either way the server still *pulls*, so AC-OPS-36a holds. `HRM_IMAGE_REPO` selects the registry; no code changes. |
| P-11 | **Where `.env` lives.** Use `/opt/hrm/shared/.env` (mode 600, owner `hrm`), shared by all releases. Each release under `/opt/hrm/releases/<tag>` uses it through `--env-file` or a symlink. `deploy.ps1` refuses to deploy if a key from `.env.vps.example` is missing (key names only). |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-37:** per-service `Integration__*` URLs, `ServiceIdentity__Scope`, S2S secrets, and shipping `service-regimes.json`. The smoke test only reports them as `Deferred(PLAN-37)`.
- **PLAN-38:** least-privilege PostgreSQL and RabbitMQ roles, and the committed secret generator. The one-off server-side generation in P7 is a manual step documented in the runbook, not a committed generator.
- **PLAN-39:** the full production realm, and the Data Protection guard and encryption. Here the volumes are mounted only.
- **PLAN-42:** a gateway proof that cannot be forged.
- **Wave 2:** RLS, scheduled backup, k6 load, k8s/Terraform/DR.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/` (including `artifacts/vps/` and `artifacts/plan36-inputs/`);
- test-rewritten `testdata/*/out/*.json` — use `git checkout -- testdata/*/out`, never a blanket `git checkout -- testdata`;
- any real `.env`, token, password, private key, or unredacted `docker compose config` output.

`deploy/vps/.env.vps-local` holds **lab-only** values. Mark them so the PLAN-35 gitleaks allow-list pattern covers them (for example a `-lab-only` suffix), and confirm with the pre-push check in P8.

**Never:**
- open any port on the server other than 22 and 443;
- use the `latest` tag;
- load real personal data;
- push to `main` or force-push;
- disable host-key checking;
- lower a test floor or delete a CI step to get green;
- leave the server in a half-hardened state at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails**, or the server's host key changed (see Preconditions 4).
- **The server has less than 15 GiB RAM** or less than 60 GB of free disk (P-4).
- **An SSH hardening check fails** (P7.3). Revert through the session that still works first, then stop.
- **You lose SSH access to the server.** Give the owner the recovery steps through the Contabo VNC console: log in as root, remove `/etc/ssh/sshd_config.d/00-hrm-hardening.conf`, run `systemctl reload ssh`, and `ufw allow 22/tcp`.
- **Let's Encrypt refuses both the owner's domain and the sslip.io names**, after the staging attempt.
- **Keycloak 24 cannot work behind the edge** with `KC_PROXY_HEADERS=xforwarded`, nor with the plan's fallback (`KC_PROXY=edge`, with a documented deprecation warning).
- **A required fix belongs to PLAN-37, 38 or 39 scope** and cannot be reported as deferred without hiding a failure of this plan's acceptance.

---

## P0 — Timing, preconditions, baseline, server probe, owner questions

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions. Then:
   - `git status`;
   - `git switch -c feature/plan-36-ops-production-boot-single-host` from `main`;
   - commit this prompt file.
2. **Plan baseline**, the plan's «المرحلة 0» steps 0.2–0.5:
   - the build;
   - the compose `config` snapshot with and without the observability file;
   - the inventory of inherited `localhost`/`Development`/`YOUR_`/`USE_ENV` values;
   - the sum of memory limits.

   Write them to `reports/plan36-baseline.md`. Save the `config` dumps under `artifacts/plan36/`, not in the report: they may contain development secrets.
3. **Local resources** (P-7): `docker info` `MemTotal` and `NCPU`, free disk, `docker buildx version`, whether port 443 is free.
4. **Server probe — read-only, one `ssh` call.** Save the output to `artifacts/plan36/server-probe.txt` and copy a summary into the baseline report. The call runs:
   - `hostnamectl`, `lsb_release -ds`, `nproc`, `free -b`, `swapon --show`, `df -B1 /`;
   - `timedatectl show -p NTPSynchronized -p Timezone`;
   - `ip -br a`, `ss -tlnp`;
   - `ls -l /etc/ssh/sshd_config.d/`;
   - `sshd -T | grep -Ei '^(permitrootlogin|passwordauthentication|kbdinteractiveauthentication|pubkeyauthentication) '`;
   - `command -v docker`, `ufw status verbose`, `systemctl is-active unattended-upgrades`.

   Apply P-4.
5. **Send the owner the decisions message** («Decisions» table, in Arabic, short). Stamp the wait, but **do not end the turn**. Continue to P1 and collect the answers when the owner writes.

Commit: `chore(ops036): baseline, compose snapshot, inherited values, local resources, server probe`.

## P1 — The production chain boots by itself (plan §1, compose parts of §7)

Apply the §1 design table:
- Move the observability overrides to `docker-compose.observability.production.yml`.
- Set the gateway's settings explicitly:
  - `Keycloak__PublicAuthority`;
  - the 14 `Gateway__EnabledModules__N`;
  - `HrAuth__BehindGateway`;
  - `AllowedHosts`.
- Extend the `x-app-production-env` / `x-app-messaging-env` anchors: `RequireHttpsMetadata=false` on the internal authority, `AdditionalValidIssuers`, module `AllowedHosts`, and the `GatewayCallerProof__Secret` requirement.
- Add `File.Storage.Web/appsettings.Production.json`.
- File storage: the `Local` provider on `file_storage_data`, and MinIO moved behind the `minio` profile with `${MINIO_IMAGE:?}`.
- Mount the Data Protection volumes `dp_<service>` (mount only).
- Add `Organization__Name` and `Deployment__RequireLicenseKey` from §7.
- `deploy.yml`: `--profile full-stack`. (It is `workflow_dispatch` only; do not run it.)

**Tests:**
- the production chain's `config` succeeds without the observability file;
- `Ops036ComposeLintTests` (text-based): no observability service without an `image`, the gateway overrides are present, no `localhost:8088`/`Development`/`YOUR_`/`USE_ENV`/`dev-svc-secret-local-only` in production services.

Do not remove any existing hardening (`cap_drop`, `internal` networks).

Commit: `feat(ops036): production compose chain valid without observability, explicit gateway settings, File.Storage production settings, Local file storage, MinIO profile, DP volumes`.

## P2 — Production configuration guard, license middleware, organization name (plan §5, code of §7)

- **`ProductionConfigurationGuard`** and its options:
  - the critical keys and rules from §5;
  - Arabic and English messages that name the key and never print a value;
  - registered through `AddHrProductionNetworking` for the gateway and the 14 modules;
  - a non-blocking `Warning` for non-critical placeholders.
- **The guard is off** in Development and Acceptance unless `Deployment:EnforceConfigGuard=true` is set explicitly. The current acceptance stack must stay green.
- **Demo-mode contradiction:** `HRM_DEPLOYMENT_MODE=demo` with `Seed__Profile=None` fails the start (§6 rule 4).
- **`LicenseValidationMiddleware`:**
  - exempt `/health`, `/alive`, `/ready`, `/metrics`;
  - parse the expiry with `CultureInfo.InvariantCulture` and `AdjustToUniversal | AssumeUniversal`;
  - delete the «employee cap» comment.
- **Fix the encoding** of `Organization:Name` and `ShortName` in the gateway's `appsettings.json`.
- **Tests:** `Ops036ProductionConfigurationGuardTests`, `LicenseValidationMiddlewareTests`. They include ar-SA culture for the date parsing, and a placeholder that must fail in Production but pass in Development.

Commit: `feat(ops036): production configuration guard naming the failing key, license middleware exempts health and parses invariantly, organization name encoding fixed`.

## P3 — Rate limiter for a demo room behind one NAT (plan §4)

- `GatewayRateLimitPartitioner` with these partitions:
  - `user:{sub}` for authenticated callers;
  - `ip:{RemoteIpAddress}` for anonymous ones;
  - `NoLimiter` for static assets.
- The three options, with `Math.Clamp(…, 30, 2000)` kept.
- Move `UseRateLimiter()` after `UseAuthentication()`.
- On 429: return `Retry-After`, and log the partition kind without the identity.
- **Tests:** `GatewayRateLimitPartitionerTests`:
  - two users on one IP have separate quotas;
  - anonymous callers get a 429 at the limit;
  - an asset path is exempt, and a lookalike path such as `/api/x.css` is not;
  - the clamp applies;
  - the order relative to authentication.
- Run the existing gateway tests and the gateway part of the acceptance smoke you can run locally.

Commit: `feat(ops036): gateway rate limiter partitions by user after authentication, exempts static assets, Retry-After`.

## P4 — Edge, memory budget, single-host compose files, compose lint (plan §2, §3, lint part of §8)

**Edge and the single-host chain:**
- `docker-compose.vps.yml`:
  - the `edge` service (Caddy pinned, `443:443` and `443:443/udp` only, fixed address `172.28.0.10` on `172.28.0.0/24`);
  - `ports: !override []` for the gateway and Keycloak;
  - Seq on `127.0.0.1:5380`;
  - `KnownProxies`/`ForwardLimit`;
  - the §3 memory table, `DOTNET_gcServer=0`, OTEL off, `Serilog__SeqUrl`;
  - `start_period: 240s`;
  - the 300/600 limits;
  - P-5's restart policies and health dependencies.
- `deploy/vps/Caddyfile`:
  - the two sites;
  - `auto_https disable_redirects`, the HTTP challenge disabled, ACME CA from `HRM_ACME_CA` (P-6);
  - `request_body max_size 20MB`;
  - `@ops` paths answered 404;
  - `/admin` and `/realms/master` allowed only from `ADMIN_ALLOWED_CIDRS`;
  - security headers as the plan states.
- `deploy/vps/clamav/clamd.conf` with `ConcurrentDatabaseReload no`.
- **Keycloak behind the edge:**
  - `KC_PROXY_HEADERS=xforwarded`, `KC_HOSTNAME`, `KC_HOSTNAME_STRICT_BACKCHANNEL=false`;
  - remove `KC_PROXY`.
  - If 24.0 rejects this, use the plan's fallback and document it.

**Demo mode and the local variant:**
- `docker-compose.vps-demo.yml`: demo mode per D-36-1, excluded automatically unless `HRM_DEPLOYMENT_MODE=demo`.
- `docker-compose.vps-local.yml`:
  - Caddy with `tls internal`, `hr.hrm.test`/`sso.hrm.test`;
  - the `registry:2` image path on `localhost:5001`.

**`scripts/vps/lint-compose.ps1`** (PS 5.1). It fails on any of:
- the §1 rules;
- a container without a memory limit;
- `latest`;
- any `ports` other than the edge's 443 and Seq's loopback;
- a missing restart policy.

Run it on both chains, the production chain and the single-host chain. It must be green.

Commit: `feat(ops036): Caddy TLS edge exposing only 443, Keycloak behind the edge, single-host memory budget, demo mode, local variant, compose lint`.

## P5 — Images, bundle, deployment and host scripts, demo realm, smoke (plan §6, §8)

**Images and deployment scripts:**
- `docker-compose.images.yml`: `${HRM_IMAGE_REPO:?}/<service>:${IMAGE_TAG:?}`. `deploy.yml` exports `HRM_IMAGE_REPO=ghcr.io/${{ github.repository_owner }}/hrmanagement`, lowercase.
- `scripts/vps/build-push-images.ps1`:
  - `-Registry`, the tag from `git rev-parse --short=12 HEAD`;
  - refuses a dirty tree unless `-AllowDirty`, and refuses `latest`;
  - `--platform linux/amd64 --push`;
  - writes `artifacts/vps/images-<tag>.json` (tag, digest, size);
  - times every push.
- `scripts/vps/make-deploy-bundle.ps1`: an archive under `artifacts/vps/`, with no `.env` and no secrets. It fails if the archive contains a file named `.env` or matching the gitleaks rules.
- `scripts/vps/deploy.ps1`:
  - `-Tag`, `-SshHost hrm-vps` (default), `-Local`, `-EnvFile` (local only), `-Rollback`;
  - `scp` the bundle, then `ssh … bash remote-deploy.sh`;
  - **pre-check the server `.env` by key names** (P-11);
  - wait for `healthy`, then run the smoke test;
  - keep the previous tag for `-Rollback`.
- `deploy/vps/remote-deploy.sh`: the same sequence; `deploy.yml` calls it instead of its inline commands.
- `scripts/deploy-organization.ps1`: becomes a wrapper (§6).

**Host setup — `deploy/vps/host-setup.sh`** (idempotent, run as root, and every step prints what it changed):
- Install Docker Engine and the Compose plugin from Docker's official apt repository.
- Swap: 8 GiB and `vm.swappiness=10`.
- `/etc/docker/daemon.json`: log rotation (`max-size=10m`, `max-file=3`).
- `ufw`: `default deny incoming`, `allow 22/tcp`, `allow 443/tcp`, `allow 443/udp`, then `--force enable`. The SSH rule is added **before** enabling.
- `unattended-upgrades` with `Automatic-Reboot "false"`.
- The timezone set to `UTC`, and NTP checked.
- The `hrm` user:
  - created with **no password**;
  - in the `docker` group;
  - `/etc/sudoers.d/90-hrm` with `NOPASSWD`, validated with `visudo -cf`;
  - `~hrm/.ssh/authorized_keys` copied from root's (the owner's public key above);
  - `/opt/hrm` with `releases/` and `shared/`, owned by `hrm`, mode 750.
- Option `--harden-ssh` (separate, gated in P7.3): writes `/etc/ssh/sshd_config.d/00-hrm-hardening.conf`:

  ```
  PermitRootLogin no
  PasswordAuthentication no
  KbdInteractiveAuthentication no
  PubkeyAuthentication yes
  ```

  The name starts with `00-` because sshd uses the **first** value it reads, and the `Include` of `sshd_config.d/*.conf` comes first in Ubuntu's `sshd_config`, so this file must sort before any cloud-init or provider drop-in such as `50-cloud-init.conf`.

  Any other drop-in that enables passwords is moved to `/root/sshd-backup-<ts>/`, not deleted. Then `sshd -t`, then `systemctl reload ssh`, then the effective values are printed with `sshd -T`.
- Option `--with-registry` for P-10.
- Option `--undo-harden-ssh`, which restores the backup.

**Demo realm:** the `VpsDemo` profile in `prepare-keycloak-import.ps1`:
- redirect URIs and web origins from `APP_DOMAIN`;
- output under `artifacts/vps/import/`.
- **Every secret in the generated realm file stays a placeholder.** That covers `${DEMO_USERS_PASSWORD}` and any client secret the gateway must match. Keycloak resolves the placeholders from its own container environment at import, so the demo password never exists on the owner's machine (P-2).
  - Keycloak's import supports environment placeholders, «يحتاج تحقق» for 24.0. Fallback: `remote-deploy.sh` renders the file on the server with `envsubst`, limited to those variable names.
- `DEMO_USERS_PASSWORD` is mandatory and at least 16 characters. The server's deploy pre-check verifies the length without printing the value.

**Smoke and docs:**
- `scripts/vps/smoke.ps1` and `deploy/vps/smoke.sh`, per §8:
  - JSON output for `EvidenceRecorder`;
  - the 30-day warning on `nextDue` (2027-01-31 today);
  - `-RequireIntegrationWiring` reports `Deferred(PLAN-37)`;
  - `-AllowStagingCa` accepts only a Let's Encrypt staging certificate, for the first server run (P-6).
- `deploy/vps/.env.vps.example`: Arabic comments; every key, without values for secrets. `deploy/vps/.env.vps-local`: lab-only values.
- Draft `docs/OPS_VPS_SINGLE_HOST.md`:
  - host setup and hardening, with the recovery path through VNC;
  - DNS, or sslip.io;
  - registry login;
  - generating `.env` on the server;
  - deploy and rollback;
  - tunnels for Seq (`ssh -N -L 5380:127.0.0.1:5380 hrm-vps`) and the Keycloak admin console;
  - the domain switch (P-9).

**Tests:** run `bash -n` on every `.sh` (use the Git for Windows `bash.exe`, or a throwaway `docker run --rm -v …:/w alpine sh -n`), `lint-compose.ps1`, and the unit tests.

Commit: `feat(ops036): registry-based images, deploy bundle without secrets, deploy and rollback over ssh, idempotent host setup with gated SSH hardening, VpsDemo realm, smoke test`.

## P6 — Local single-host acceptance (plan Acceptance, Verification)

1. Start the local registry: `docker run -d -p 5001:5000 --name hrm-registry registry:2`.
2. Build and push: `powershell -File scripts/vps/build-push-images.ps1 -Registry localhost:5001/hrmanagement`.
3. Deploy: `powershell -File scripts/vps/deploy.ps1 -Local -EnvFile deploy/vps/.env.vps-local -Tag <sha12>`.
4. **`AcOps36VpsBootTests`** (`[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-OPS-36", …)`) covers AC-OPS-36a..k, positive and negative.
   - **Design it for both targets.**
     - Without `VPS_SSH_HOST`, the Docker commands run locally, and the evidence goes to `acceptance-evidence/AC-OPS-36/`.
     - With `VPS_SSH_HOST=hrm-vps`, they run as `ssh hrm-vps docker …`, and the evidence goes to `acceptance-evidence/AC-OPS-36/vps/`.
   - **Locally, trust only Caddy's exported internal root CA.** Never disable TLS validation globally. Map the host names with `SocketsHttpHandler.ConnectCallback`.
   - **The negatives that change configuration restore it afterwards and prove the restore:**
     - AC-OPS-36f: placeholder `AllowedHosts`, and a short caller-proof secret;
     - AC-OPS-36k: an expired license.
5. Run with `RUN_ACCEPTANCE=true`, `ACCEPTANCE_REQUIRE_ALL=true`, `VPS_APP_DOMAIN=hr.hrm.test`, `VPS_SSO_DOMAIN=sso.hrm.test`, `VPS_TARGET_IP=127.0.0.1`. All must Pass.
6. Record the memory figures (AC-OPS-36h) and the boot time (AC-OPS-36a).
7. Afterwards: `docker compose … down`, but **keep** the volumes until P8 in case a rerun is needed.
8. Run the existing acceptance stack's smoke once to prove the guard did not break it.

Commit: `test(ops036): AC-OPS-36a..k pass on the local single-host chain, evidence`.

## P7 — The real server (owner actions, then you)

Record every server change and its undo command in the progress file **as you make it**.

1. **OWNER ACTION** (one batch, skipping anything already done):
   1. If there is a domain: confirm that the A records `app` and `auth` → `161.97.185.71` exist (DNS only).
   2. `gh auth refresh -h github.com -s write:packages,read:packages`
   3. Create a **classic** token with only `read:packages` for the server (GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)). Keep it ready for step 4; never paste it into chat.
   4. If not done: `ssh -t hrm-vps passwd` (change the root password in your own terminal).
2. **Host setup as root:**
   - `scp deploy/vps/host-setup.sh hrm-vps:/root/`
   - `ssh hrm-vps "bash /root/host-setup.sh"`
   - Verify:
     - `docker version`;
     - `swapon --show`;
     - `ufw status verbose` shows only 22 and 443;
     - `ss -tlnp` shows only `sshd`.
3. **SSH hardening, in this order only:**
   1. Verify the new user **before** changing sshd:
      `ssh -o BatchMode=yes -o User=hrm hrm-vps "id && sudo -n true && docker ps"`.
      If this fails, fix it and **do not continue**.
   2. Run `ssh hrm-vps "bash /root/host-setup.sh --harden-ssh"`. The script runs `sshd -t` before the reload, and existing sessions survive a reload.
   3. **Verify from Windows, each as a new connection:**
      - `ssh -o BatchMode=yes -o User=hrm hrm-vps "echo ok"` → `ok`;
      - `ssh -o BatchMode=yes -o User=root hrm-vps "echo ok"` → refused;
      - `ssh -o BatchMode=yes -o PubkeyAuthentication=no -o PreferredAuthentications=password,keyboard-interactive -o User=hrm hrm-vps "echo ok"` → `Permission denied (publickey).`, which lists publickey as the only method.
   4. **If any check fails:** run `--undo-harden-ssh` through the `hrm` session, and **Stop**.
   5. **OWNER ACTION:** switch the alias to the new user, in the owner's own PowerShell:
      `(Get-Content "$env:USERPROFILE\.ssh\config") -replace '^(\s*User\s+)root\s*$','${1}hrm' | Set-Content -Encoding ascii "$env:USERPROFILE\.ssh\config"`
      This changes every `User root` line. The file was created with only the `hrm-vps` block; if it has other hosts, edit that block's `User` line by hand instead (`notepad "$env:USERPROFILE\.ssh\config"`).
      Then verify `ssh hrm-vps whoami` = `hrm`.
   6. Remove the setup copy from the server: `ssh hrm-vps "sudo rm -f /root/host-setup.sh"`. The copy in the release bundle is the reference.
4. **Registry:**
   1. On the owner's machine: `gh auth token | docker login ghcr.io -u omardefdev --password-stdin`.
   2. `powershell -File scripts/vps/build-push-images.ps1 -Registry ghcr.io/omardefdev/hrmanagement`. Record the sizes and the push time.
   3. **OWNER ACTION:** `ssh -t hrm-vps "docker login ghcr.io -u omardefdev"`, pasting the read-only token at the prompt in the owner's own terminal.
   4. Verify with a `docker pull` of one image on the server.
   5. If GHCR fails, use P-10.
5. **Server `.env`** (P-2, P-11):
   - Create `/opt/hrm/shared/.env` from `.env.vps.example` with a server-side command:
     - every secret generated by `openssl rand` on the server, never echoed;
     - `GATEWAY_CALLER_PROOF_SECRET` ≥ 32 characters;
     - `DEMO_USERS_PASSWORD` ≥ 16;
     - the non-secret values (domains, `HRM_IMAGE_REPO`, `IMAGE_TAG`, `HRM_DEPLOYMENT_MODE=demo`, `COMPOSE_PROFILES=full-stack`, `HRM_ORGANIZATION_NAME`, `ACME_EMAIL`, `HRM_ACME_CA`=staging, `ADMIN_ALLOWED_CIDRS` empty).
   - Set `chmod 600`.
   - Verify by key names only.
6. **Deploy:**
   1. DNS check: `Resolve-DnsName <APP_DOMAIN> -Server 1.1.1.1`, and the same for SSO, must return `161.97.185.71`.
   2. `powershell -File scripts/vps/deploy.ps1 -Tag <sha12>`. Record the pull time and the time until everything is healthy.
   3. Run the smoke test with `-AllowStagingCa`: it accepts only a certificate issued by the Let's Encrypt **staging** CA, for this one step.
   4. Switch `HRM_ACME_CA` to production and recreate the edge only. Rerun the smoke test without the switch: it requires a publicly trusted certificate for both names.
7. **Acceptance on the real server:**
   - Set `VPS_SSH_HOST=hrm-vps`, `VPS_TARGET_IP=161.97.185.71`, and `VPS_APP_DOMAIN`/`VPS_SSO_DOMAIN` to the real names.
   - Run `AcOps36VpsBootTests`. Evidence goes to `acceptance-evidence/AC-OPS-36/vps/`.
   - Port checks are made from the owner's machine (`Test-NetConnection 161.97.185.71 -Port <p>`) **and** confirmed with `ss -tlnp` on the server, since an ISP can block outbound ports.
   - Mask the owner's public IP in the evidence (keep the first two octets). The check compares the full value in memory only.
   - **The demo password.** For the logins in AC-OPS-36b and AC-OPS-36e, the test reads `DEMO_USERS_PASSWORD` over `ssh` into memory. It never logs it, never writes it to evidence, and you never print it.
8. **Reboot survival** (P-5):
   - `ssh hrm-vps "sudo systemctl reboot"`;
   - wait;
   - every container healthy within 10 minutes, and the smoke test green.
   - Record it.
9. **Redeploy and rollback** (AC-OPS-36j): deploy the next tag, then `-Rollback`; the smoke test is green after each.
10. **Hand-over note** for the owner in `docs/OPS_VPS_SINGLE_HOST.md` and the final report:
    - the URL;
    - that the demo users' password is read **by the owner only** with `ssh hrm-vps "grep ^DEMO_USERS_PASSWORD= /opt/hrm/shared/.env"` (you never run this);
    - the Seq and Keycloak admin tunnels;
    - how to deploy and roll back;
    - the reminder to disable VNC in the Contabo panel.

Commit: `test(ops036): deployed to the VPS, AC-OPS-36a..k on the real server, reboot survival, hand-over notes`.

## P8 — Docs, review, final report, merge through a PR

1. **Docs:**
   - Finish `docs/OPS_VPS_SINGLE_HOST.md`.
   - Update `PRODUCTION_RUNBOOK.md` (line 15), `PILOT_DEPLOYMENT.md`, and `KEYCLOAK_AND_SECRETS.md` (the edge section).
   - Update the «الحالة» lines for G-OPS-01, G-OPS-10 (the covered part) and G-OPS-15 in `docs/PRG_002_OPERATIONS_GAP_STUDY.md`.
   - Set the plan's `Status`, and add «ملاحظات التنفيذ والانحرافات»: Contabo instead of Hostinger, the measured server size, the registry used, sslip.io or the domain, the `hrm` user and SSH hardening, and the CA.
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - any port reachable from the internet besides 443 and 22;
   - a trusted-proxy setting that accepts client-supplied `X-Forwarded-For`;
   - a rate-limit partition a client can choose;
   - a guard that can be bypassed or that prints a secret;
   - a secret in the repository, the evidence, the bundle or the reports;
   - `latest` anywhere;
   - an SSH hardening order that could lock out;
   - a restart or dependency gap.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push:**
   - `powershell -File scripts/ci-local.ps1` on HEAD, as in PLAN-35, then `powershell -File scripts/plan035-prepush.ps1`. Both must be green.
   - A red PR run costs about 67 Actions minutes; a local run costs none.
4. **`reports/plan36-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Owner decisions and when they came.
   4. Server facts as measured.
   5. Every server change with its undo command.
   6. Image sizes and push/pull times.
   7. Memory figures per container, local and on the server.
   8. Boot and reboot times.
   9. The certificates and the CA.
   10. AC-OPS-36a..k, local and on the server.
   11. Deviations from the plan.
   12. Deferred items (PLAN-37/38/39).
   13. Defects for later plans.
   14. **Timing:** total wall-clock; **active time vs owner-wait time** from the stamps; build, push and deploy times; and the output of `powershell -File scripts/plan-timing.ps1 -From 25 -To 36`.
   15. A readiness note for PLAN-37.
5. **Merge (P-8):**
   - Merge the feature branch into a new `release/plan-36` branch created from `main` (`--no-ff`).
   - Write `reports/plan36-pr.md`, a short PR description: the summary, the links to the report and the evidence, and the deviations.
   - **OWNER ACTION:**
     1. `git push -u origin release/plan-36`
     2. `gh pr create --base main --head release/plan-36 --title "PLAN-36: production profile on a single host" --body-file reports/plan36-pr.md`
     3. After the checks are green: `gh pr merge --merge`
   - Verify that `main` = `origin/main` and that the post-merge run is green.
   - Record the PR link and the post-merge run in `reports/plan36-progress.md`. That note is committed with the next plan's first commit. **Do not open a second PR just for it.**

## Definition of done

- The production compose chain is valid without the observability file. `lint-compose.ps1` is green for both chains.
- No service starts in Production with a placeholder value. The guard is proved by unit tests and a live negative.
- The single host boots from the registry with one command, and publishes only 443 (and 22 for SSH).
- Login works over TLS on the public names. The rate limiter splits authenticated users by user and exempts assets.
- AC-OPS-36a..k = Pass locally (`acceptance-evidence/AC-OPS-36/`) **and on the real server** (`acceptance-evidence/AC-OPS-36/vps/`).
- The server runs as user `hrm` with key-only SSH, root login disabled, `ufw` allowing only 22 and 443, and swap on. It survives a reboot.
- No secret in the repository, the evidence, the bundle or the reports.
- The final report is merged through a PR with green checks, with the timing section that separates active time from owner waits.

Then reply with a short summary in Arabic:
- the demo URL;
- how the owner reads the demo password;
- the PR link;
- the path of the final report;
- the remaining owner reminders: disable VNC, and where the demo password lives.
