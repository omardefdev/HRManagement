Started (UTC): 2026-10-31T22:24:14.9258001Z

# PLAN-37 progress

## P0
- status: in progress

### P0 notes (in progress)
- Preconditions:
  1. PLAN-36 merged: PR #30 MERGED 2026-10-31T22:13:29Z (merge 7d602f9c). OK.
  2. HARD-001 follow-up: PR #31 MERGED 2026-10-31T22:17:46Z (merge 19f7172b). Note: at merge time the PR's CI and CodeQL
     checks were still pending (only Gitleaks, HARD-001 scan, acceptance notice green) - the P-6 gap that this plan's
     merge rule closes. The post-merge `push` runs on main are the verdict: hardening-check success; ci in progress.
  3. main = origin/main = 19f7172b. Waiting for the ci push run 41247555134.
  4. Working tree: only the allowed items (global.json, plan36-progress.md, the prompt file, tmp-vendors.html) + this file.
  5. No other plan session on this tree (running containers are the PLAN-36 local single-host rehearsal stack).
  6. `ssh -o BatchMode=yes hrm-vps whoami` = hrm; ED25519 host key SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto (matches).
- Decisions register: artifacts copy differs from docs/ only by 132 filled «القرار المعتمد» cells and one added header line
  (scripted line-by-line check, 0 other differences).
- Tools: kubectl present (Docker Desktop) -> k8s rendered check possible. pwsh absent.
- Server before-picture (smoke --require-integration-wiring on release 00926107a37b): pass 16, fail 0, warn 0, deferred 5:
  hr-core Payroll/FileStorage URLs and scopes; Integration__HrCoreBaseUrl for Contracts/IDCard/Archive/IT; S2S token per
  client; candidate upload to File.Storage (S2S); per-service SVC_* secrets for every client.
- 0.5: /app/seed-packs absent in hr-contracts-web and hr-movements-web images (tag 00926107a37b); present in hr-core-web.
- P0 Finished (UTC): 2026-10-31T22:32:10.8544885Z (main ci push run 41247555134 still in progress at this stamp; re-checked before P1 commit)

## P1
- status: in progress
