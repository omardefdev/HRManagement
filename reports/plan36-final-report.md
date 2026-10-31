# PLAN-36 final report — the production profile on one server (demo and testing)

Plan: [PLAN-36](../docs/acceptance-plans/PLAN-36-ops-production-boot-single-host.md) · prompt:
[PLAN-36-AGENT-PROMPT](../docs/acceptance-plans/execution/PLAN-36-AGENT-PROMPT.md) · checkpoint:
[plan36-progress.md](plan36-progress.md) · baseline: [plan36-baseline.md](plan36-baseline.md) · runbook:
[OPS_VPS_SINGLE_HOST.md](../docs/OPS_VPS_SINGLE_HOST.md)

**Result.** The production compose chain boots by itself; one server (Contabo, 8 vCPU, 23.5 GiB, Ubuntu 26.04) runs the
whole system in demo mode at **https://hr.161-97-185-71.sslip.io/** behind a Caddy TLS edge with Let's Encrypt
production certificates, publishing only 443 (and 22 for SSH). **AC-OPS-36a..k = 11/11 Pass locally and 11/11 Pass on
the real server.** The server survives a reboot (all healthy 68 s after the reboot command), and redeploy and rollback
work. Synthetic data only.

## 1. Summary per phase

| Phase | Finished (UTC) | Outcome |
|-------|----------------|---------|
| P0 | 13:41 | Preconditions (main 5 commits ahead of origin: owner chose to carry them in this PR), baseline (production chain failed without observability; 12.25 GiB of limits), server probe (23.46 GiB, 8 vCPU, Ubuntu 26.04) |
| P1 | 13:51 | Production chain valid without observability; explicit gateway settings (public authority, 14 modules, AllowedHosts); caller-proof secret required; File.Storage Production settings and Local provider; MinIO behind profile `minio`; Data Protection volumes; `volume-init`; restart policy on every app; deploy.yml `--profile full-stack` |
| P2 | 13:58 | `ProductionConfigurationGuard` (all 15 services; key named in Arabic and English, never a value; cannot be switched off in Production); license middleware spares health/metrics and parses invariantly; organization name encoding fixed |
| P3 | 14:02 | `GatewayRateLimitPartitioner`: `user:{sub}` / `ip:{addr}` / static assets exempt; limiter after authentication; clamp 30..2000; 429 with Retry-After |
| P4 | 14:13 | Caddy edge (443 tcp+udp only, TLS-ALPN, ACME CA variable, ops paths 404, Keycloak admin 403), Keycloak behind the edge, memory budget, demo mode, local variant, `lint-compose.ps1` (both chains green) |
| P5 | 14:29 | Registry images (`HRM_IMAGE_REPO`), build-push, secret-free bundle (gitleaks), deploy/rollback over ssh, idempotent host setup with gated SSH hardening, VpsDemo realm with placeholders only, smoke test, runbook draft |
| P6 | 16:15 | Local single-host run: 5 tags built, cold boot 78 s, redeploy + rollback, AC-OPS-36 11/11, acceptance stack still green (5/5) |
| P7 | 19:56 | Server: host setup, SSH hardening, `.env` generated on the server, images built on the server (owner decision), staging then production certificates, AC-OPS-36 11/11, reboot survival, redeploy + rollback |
| P8 | 21:0x | Docs; independent review (3 medium, 4 low; 6 fixed with tests, 1 documented); the final tag 00926107a37b rebuilt and redeployed on the server and locally; **AC-OPS-36 11/11 again on both targets with the final code**; local CI and pre-push check on the release head; PR (owner action) |

## 2. Commits

20 commits on `feature/plan-36-ops-production-boot-single-host` (from local `main` 76ac25dd, which itself carries 5
earlier commits not yet on GitHub — defects 9 and 15 of PLAN-35): 5def7d63 (P0), 81cfc1c0 (P1), 0b065219 (P2), e76a054a
(P3), e570a945 (P4), 8227622a (P5), fbb5c2ab, ee853033, b2f85bcb, 10a35bef, 205f37df, c39dfe3d (P6), c5fc56f9, de4ffab7,
e91ab72e, 7493e332, c696efb2, 1a62e06f, 68f2276f, bdac1a34 (P7), then the P8 commits (review fixes, docs, this report).
`git log --oneline main..release/plan-36` lists them.

## 3. Owner decisions and when they came

| Time (UTC) | Decision |
|------------|----------|
| 13:39 | Precondition: carry the 5 unpushed local `main` commits in the PLAN-36 PR (option a) |
| 16:34 | Defaults for D-OPS-2 (sslip.io names), D-OPS-1 (GHCR private), no ACME email, default organization name; `gh` scopes refreshed |
| ~17:30 | «نفذها انت»: asked the agent to do the remaining owner steps (alias was already switched; root password and the server GHCR login could not be done by the agent) |
| 18:31 | Transfer path: build the images on the server from a source archive (option c, a deviation from §6 rule 1) |

## 4. Server facts as measured

Contabo KVM, AMD EPYC, **8 vCPU, 25,193,742,336 B RAM (23.46 GiB)**, 154.99 GB disk (152.03 GB free), **Ubuntu 26.04.1
LTS (resolute)**, kernel 7.0.0-42, IPv4 161.97.185.71 and an IPv6 /64, NTP synchronized. Before PLAN-36: root and
password SSH login allowed, no swap, no Docker, ufw inactive. Host key ED25519 `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto` unchanged.

## 5. Server changes and their undo

Full table in [OPS_VPS_SINGLE_HOST.md §10](../docs/OPS_VPS_SINGLE_HOST.md). In order:

| Change | Undo |
|--------|------|
| Docker Engine 29.9.2 + Compose 5.6.1 (download.docker.com, resolute) | `apt-get purge docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin`, remove `docker.list` and the key |
| `/etc/docker/daemon.json` log rotation 10m × 3 | delete, `systemctl restart docker` |
| `/swapfile` 8 GiB, fstab line, `vm.swappiness=10` | `swapoff /swapfile; rm /swapfile`, delete the fstab line and `/etc/sysctl.d/90-hrm-swappiness.conf` |
| ufw: deny incoming; allow 22/tcp, 443/tcp, 443/udp (v4+v6) | `ufw disable` |
| `/etc/apt/apt.conf.d/52-hrm-unattended` (no automatic reboot) | delete |
| Timezone Europe/Berlin → UTC | `timedatectl set-timezone Europe/Berlin` |
| User `hrm` (no password, docker group), `/etc/sudoers.d/90-hrm`, owner key, `/opt/hrm` 750 | `userdel -r hrm; rm /etc/sudoers.d/90-hrm; rm -rf /opt/hrm` |
| SSH: `00-hrm-hardening.conf`; `50-cloud-init.conf` moved to `/root/sshd-backup-20261031T163600Z/` | `sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --undo-harden-ssh` (or the VNC steps) |
| `/opt/hrm/shared/.env` (600, hrm, generated on the server) | `rm /opt/hrm/shared/.env` |
| Loopback registry `hrm-registry` (registry:2.8.3, 127.0.0.1:5050, `/opt/hrm/registry`) | `docker rm -f hrm-registry; sudo rm -rf /opt/hrm/registry` |
| Compose project `hrm` (releases 1a62e06f28c9, 68f2276ffc7e and the running 00926107a37b; volumes) | `docker compose -p hrm ... down -v`, `rm -rf /opt/hrm/releases` |
| `tcpdump` installed for the port diagnosis | `apt-get purge tcpdump` |
| `/root/host-setup.sh` | removed after the first release (the bundle carries the script) |

## 6. Image sizes and push/pull times

- 15 images, **2,113.8 MB compressed** in total, **810.0 MB of unique layers**; hr-core-web 193 MB, payroll-web 190 MB,
  the others 125–135 MB.
- Local registry (Docker Desktop): build 224–329 s (30 s with a warm cache), push 24–30 s per tag.
- GHCR: build 2,336 s (cold cache after the acceptance rebuild), **push failed three times on the network** (connection
  timeout, then DNS lookup failure of ghcr.io inside Docker Desktop); no refusal or billing prompt seen.
- Upload link measured at **~0.16 MB/s** (20 MB in 125 s), with drops; an SSH tunnel to the server registry is not
  reachable from Docker Desktop's engine.
- **Build on the server** (owner decision): source archive 29.5 MB uploaded in 372–384 s; build **520–545 s**; push to
  the loopback registry 9–10 s; source, build cache and SDK image removed after each build.
- Server pull: 16 s (first deploy, includes infrastructure images), 2 s afterwards.

## 7. Memory per container (AC-OPS-36h, after a 10-minute tour)

| Container | Local (MiB / limit) | Server (MiB / limit) |
|-----------|--------------------|----------------------|
| clamav | 953 / 2048 | 960 / 2048 |
| keycloak | 574 / 1024 | **901 / 1024 (88 %)** |
| seq | 427 / 768 | 283 / 768 |
| hr-core-web | 286 / 1024 | 317 / 1024 |
| rabbitmq | 215 / 512 | 192 / 512 |
| payroll-web | 194 / 768 | 216 / 768 |
| hr-contracts-web | 171 / 640 | 179 / 640 |
| hr-gateway | 171 / 512 | see evidence |
| ten 384 MiB modules | 137–168 | 150–193 |
| postgresql | 149 / 2048 | see evidence |
| file-storage-web | 142 / 512 | see evidence |
| edge | 24 / 128 | see evidence |
| **Total** | **4,826 MiB of 15,851 (30.4 %)** | **5,193 MiB of 24,027 (21.6 %)** |
| **Total, final tag 00926107a37b** | **4,447 MiB of 15,851 (28.1 %)** | **4,516 MiB of 24,027 (18.8 %)**; keycloak 814 / 1024 |

No container OOMKilled in either run. Per-container figures: `acceptance-evidence/AC-OPS-36/36h-after.json` and
`acceptance-evidence/AC-OPS-36/vps/36h-after.json`.

## 8. Boot and reboot times

| Event | pull | up | healthy after up | total to all healthy |
|-------|------|----|------------------|----------------------|
| Local cold boot (tag 10a35befc8c9) | 5 s | 58 s | 20 s | 78 s |
| Server first deploy (1a62e06f28c9) | 16 s | 113 s | 12 s | **125 s** |
| Server redeploy (68f2276ffc7e) | 2 s | 81 s | 12 s | 93 s |
| Server rollback (→ 1a62e06f28c9) | 2 s | 46 s | 12 s | 58 s |
| Server final tag 00926107a37b (build on server 540 s), then old release and rollback back to it | 1–2 s | 46–47 s | 12 s | 58–59 s |
| **Server reboot** | — | — | — | ssh back 28 s, **all healthy 68 s** after the reboot command |

`up -d` already waits for the health-gated dependencies, so "total" is up + the final wait.

## 9. Certificates and CA

TLS-ALPN on 443 only (port 80 never published, HTTP challenge disabled). First run with **Let's Encrypt staging**
(issuers "(STAGING) Baloney Bulgur YE2" / "(STAGING) Artificial Amaranth YE1"), smoke with `-AllowStagingCa`; then
`HRM_ACME_CA` switched to **production** and only the edge recreated: **C=US, O=Let's Encrypt, CN=YE1 / YE2**, valid
until **2027-01-29**, trusted by the server's OpenSSL store and by Windows. Locally Caddy's internal CA ("tls internal")
and the tests trust only its exported root. Caddy renews automatically.

## 10. AC-OPS-36a..k

| ID | Local | Server | Notes |
|----|-------|--------|-------|
| 36a boot from registry, all healthy ≤ 10 min; no repo / `latest` refused | Pass | Pass | 78 s / 125 s |
| 36b sign-in over TLS, 14 modules, 13 UIs 200; no localhost Location, no plain HTTP session | Pass | Pass | server: client path answers TCP for any port → server-side proof (no :80 socket, no HTTP response) |
| 36c 443 valid for both names; other ports closed; /admin 403; /metrics /health 404 | Pass | Pass | server: listeners "22 443", ufw active; client-side probe inconclusive (control port) |
| 36d audit IP = what Caddy saw; forged X-Forwarded-For ignored | Pass | Pass | owner IP masked to 87.249.x.x |
| 36e two users one IP no 429; anonymous 429 after 300 with Retry-After | Pass | Pass | server: 300 of 400 in 40.8 s, Retry-After 60 |
| 36f placeholder AllowedHosts / 10-char secret stop the service, key named | Pass | Pass | one-off containers; live services untouched |
| 36g upload via gateway 201; direct call without proof 403 | Pass | Pass | |
| 36h no OOM, under limits, figures recorded | Pass | Pass | §7 |
| 36i Seq gets all 15 services; no OTLP collector/errors | Pass | Pass | |
| 36j redeploy + gateway recreate keep the session; rollback restores the previous tag | Pass | Pass | |
| 36k organization name and demo banner; expired license 402, /health/live 200 | Pass | Pass | banner also on the public careers portal |

**Final runs on tag 00926107a37b (after the review fixes): local 11/11 and server 11/11, each in one run** (server 36e: 300 of 400 anonymous requests passed in 28.3 s, Retry-After 60). The committed evidence is from these runs.

Evidence: `acceptance-evidence/AC-OPS-36/` (local) and `acceptance-evidence/AC-OPS-36/vps/` (server), with
per-scenario copies `<key>-after.json` / `<key>-db-checks.json`. The acceptance stack still passes with the new guard
and limiter (AC-01..04, AC-31: 5/5; AC-28/30 need `pwsh`, not installed on this machine).

## 11. Deviations from the plan

See «ملاحظات التنفيذ والانحرافات» in the plan. In short: Contabo/23.5 GiB/Ubuntu 26.04 instead of Hostinger/16 GB/24.04;
memory sum 13.5 GiB (ten small services, not nine); **images built on the server** and served from a loopback registry
(GHCR push failed on the network; upload ~0.16 MB/s); sslip.io names; MinIO credentials and image `${VAR:-}` instead of
`:?` (compose interpolates inactive services); `volume-init` one-shot (restart "no"); HR.Core without the file volume;
`KC_HOSTNAME_PORT=-1`; the guard cannot be disabled in Production; external port checks use a control port; 36e uses
HEAD for assets and a parallel anonymous burst; the PS 5.1 scripts replace every `pwsh` of the plan.

## 12. Deferred items

- **PLAN-37:** per-service `Integration__*`, `ServiceIdentity__Scope`, S2S secrets for all clients (the VpsDemo realm
  has no `svc-compliance` and others), `service-regimes.json`. Smoke reports them as `Deferred(PLAN-37)`.
- **PLAN-38:** least-privilege PostgreSQL/RabbitMQ roles (demo mode uses the administrators), committed secret
  generator, connection strings and broker credentials in the guard's critical keys.
- **PLAN-39:** full production realm, Data Protection guard and encryption (volumes are mounted only).
- **PLAN-42:** unforgeable gateway proof. **Wave 2:** RLS, scheduled backup, k6 load, k8s/Terraform/DR.

## 12a. Independent review

One fresh subagent read the prompt, the plan and the diff (read-only). No high findings, so no refuter. Fixed, with
tests: (1, medium) anonymous asset-looking paths escaped the per-IP window and each triggered a pushed authorization
request to Keycloak — the asset exemption now applies to signed-in users only and only under `/ui/{module}/`; (2,
medium) rollback after a failed deploy targeted `previous-tag` and CI never rolled back a failed deploy — `failed-tag`
now sends rollback to the last healthy release and the CI rollback also runs on a failed deploy step; (3, medium)
`--harden-ssh` now refuses unless `hrm` has a non-empty `authorized_keys` and sshd accepts its key, and a repeated run
keeps the original backup pointer; (4, low) the ufw SSH rule uses sshd's port; (5, low) Caddy strips a client
`X-Forwarded-Prefix`; (6, low) smoke.sh passes the session cookie through a 0600 header file. Documented only: (7, low)
IPv6 clients would share one address on the IPv4-only edge network if an AAAA record were published (none today).
Checked clean: published ports, trusted proxy, partition key source, guard output, secrets, `latest`, restart and
dependencies, script quoting.

## 13. Defects for later plans

1. Keycloak uses 79–88 % of its 1 GiB limit on the server after a 10-minute tour (JVM heap defaults); raise to 1.5 GiB or
   set `JAVA_OPTS_APPEND=-XX:MaxRAMPercentage` before real load.
2. Module `appsettings.Production.json` keep `Serilog:FilePath logs/...` under the root-owned `/app` (the non-root user
   cannot write; the file sink is silently lost) — use stdout/Seq only or a writable path.
3. Ten module `appsettings.json` files carry `FileStorage:SecretKey = USE_ENV_OR_USER_SECRETS` (guard warnings) and the
   gateway's arrays (`Cors:AllowedOrigins`, `Security:CspConnectSrcExtra`) cannot be cleared by an empty Production
   array — overridden by index in compose; clean the base files (PLAN-38).
4. The deploy workflow (`deploy.yml`) now needs `vars.APP_DOMAIN` and `secrets.DEPLOY_SSH_KNOWN_HOSTS`; it was not run.
5. GHCR push from the owner's network failed; the documented GHCR path is unproven end to end (a faster link or a CI
   build-and-push job would prove it).
6. `acceptance-up.ps1`/`acceptance-verify.ps1` and AC-28/30 depend on `pwsh`; this machine has only Windows PowerShell 5.1.
7. Root password on the server still dated 2026-10-30 (before its exposure) — owner action.

## 14. Timing

- **Total wall-clock** from `Started (UTC): 2026-10-31T13:32:59Z` to the end of P7 (19:56:37Z): **6 h 24 min**; to the final report (2026-10-31T21:03:58Z) **7 h 30 min**; P8 adds
  the review, the docs, the local CI run on the release head and the PR.
- **Owner-wait time** (stamped pairs, plus two waits that were not stamped exactly): 13:34:39→13:39:13 (5 min),
  16:16:01→16:34:35 (19 min), 17:12:20→~17:30 (~18 min, «نفذها انت»), ~18:05→18:21 (~16 min, explanation and the session
  restart), 18:25:39→18:31:00 (5 min): **about 1 h 03 min**.
- **Active time** ≈ 6 h 24 min − 1 h 03 min ≈ **5 h 21 min**, of which long waits on builds and transfers: local builds
  ~22 min, GHCR attempt ~45 min, server uploads ~19 min, server builds ~18 min, acceptance runs ~55 min (local + server).
- Build/push/deploy times: §6 and §8.
- Actions minutes: **none used** by this plan before the final PR (no push until P8).
- `powershell -File scripts/plan-timing.ps1 -From 25 -To 36` (before the merge):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43
PLAN-36      20 2026-10-31 16:32 2026-10-31 16:41 2026-10-31 22:56  False 6:14   6:23
```

(The script prints local time, UTC+3; PLAN-25..33 rows unchanged from the PLAN-35 report.)

## 15. Readiness note for PLAN-37

- The server is a working target: `scripts/vps/build-on-host.ps1` then `scripts/vps/deploy.ps1 -Tag <sha12>`; smoke
  `-RequireIntegrationWiring` lists the five PLAN-37 checks that turn mandatory.
- The VpsDemo realm already maps 8 service clients to `SVC_*` placeholders; PLAN-37 adds the missing clients and scopes.
- Demo mode (administrator accounts) must stay until PLAN-38; the banner marks it everywhere.
- Before relying on GHCR, prove a push from a faster link or from CI (defect 5).
