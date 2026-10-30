# PLAN-35 baseline (P0)

Recorded 2026-10-30 (UTC). Branch `feature/plan-35-qa-push-remote-ci` from `main` = `c2b0d3f`.

## Local CI on HEAD `c2b0d3f`

`powershell -File scripts/ci-local.ps1 -SkipTestcontainers -ContinueOnError -Out artifacts/plan35/ci-local-baseline`
(Linux container, clean clone of HEAD):

| Result | Count |
|--------|-------|
| passed | 85 |
| failed | 0 |
| not-run | 2 (`Integration tests (Testcontainers)`, `Coverage gate`; Docker Desktop proxy, as in PLAN-34) |

Sum of step durations: 846 s. `summary.json`: `gitSha` = `c2b0d3f452622b2a8a07f0341e677dea497729c8`, `ok` = true.
The two not-run steps passed natively on Windows in PLAN-34 P9 and run normally on `ubuntu-latest`.

## Distance from GitHub

- `git fetch origin`; `git rev-list --count origin/main..main` = **160**.
- `origin/main` = `9cb3705` (3 commits: `e7523eb`, `19a926b` PR #1, `9cb3705`) and is an ancestor of `main`
  (fast-forward possible).

## GitHub state

| Item | Value |
|------|-------|
| Repository | `omardefdev/HRManagement`, **public**, default branch `main` |
| `gh` account | first `omar2def` (READ only on this repository); the owner switched to `omardefdev` (ADMIN) at 13:54 UTC |
| Account plan | `free` (`gh api user`) |
| Branch protection on `main` | **present** (the 404 seen as `omar2def` hid it): contexts `CI`, `CodeQL`, `Gitleaks` strict; 2 approving reviews, code-owner reviews, last-push approval; required signatures; `enforce_admins`; linear history; conversation resolution; no force-push or deletion. Copy: `artifacts/plan35/github/protection-before.json` (not committed) |
| Rulesets | none |
| Actions | enabled, all actions allowed; no repository variables, no secrets |
| Workflows | `ci`, `deploy`, `gateway-ci`, `gitleaks`, `hardening-check`, `iac-apply`, `iac-plan` active; `advisory-to-issue`, `codeql`, `E2E Journeys`, `iac-drift`, `Mutation (Payroll)` **disabled_inactivity**; Dependabot updates active |
| Last runs | 2026-10-12 schedule: `iac-drift` failure, `codeql` success, `Mutation (Payroll)` failure |
| `@omard` (CODEOWNERS, 366 lines) | exists, but it is user `omarD` (id 6695527), not the owner -> replaced by `@omardefdev` (P3) |

## Tools

| Tool | Pin (`docs/qa/tool-pins.json`) |
|------|------|
| gitleaks 8.28.0 | `zricethezav/gitleaks@sha256:cdbb7c955abce02001a9f6c9f602fb195b7fadc1e812065883f695d1eeaba854` |
| actionlint 1.7.7 | `rhysd/actionlint@sha256:887a259a5a534f3c4f36cb02dca341673c6089431057242cdc931e9f133147e9` |

This machine has no real Python (only the Microsoft Store aliases); `scripts/plan035-prepush.ps1` runs the GOV-001-D
check with `python3` inside the ci-local image.

## Working tree (not committed)

`global.json` (`rollForward: latestPatch`, the owner's local edit), `tmp-vendors.html`,
`docs/acceptance-plans/execution/PLAN-35-AGENT-PROMPT.md` (the session prompt).
