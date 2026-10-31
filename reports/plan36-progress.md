Started (UTC): 2026-10-31T13:32:59.0005780Z

# PLAN-36 progress

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-10-31T13:41:37Z |
| P1 | in progress | |
| P2-P8 | not started | |

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

P1 (production compose chain).

## P0 result

Finished (UTC): 2026-10-31T13:41:37Z
- Baseline in `reports/plan36-baseline.md` (build 0/0 in 30 s; production chain fails on `alert-sink`/`seq` without
  observability and on `minio` with it; inherited localhost values; memory 12.25 GiB without observability).
- Decision: `config` dumps rendered with `--env-file artifacts/plan36/lint.env` (lint-only values) so the dev `.env`
  in the repository root is never read into a dump.
- Finding: the production chain sets no `GatewayCallerProof__Secret`; every Production image carries the
  `USE_ENV_OR_SECRET_MANAGER` placeholder for it (and for `AllowedHosts`).
