# PLAN-35 final report — first push, honest remote CI, protection as code (QA-B)

Plan: [`docs/acceptance-plans/PLAN-35-qa-push-remote-ci.md`](../docs/acceptance-plans/PLAN-35-qa-push-remote-ci.md) ·
prompt: [`PLAN-35-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-35-AGENT-PROMPT.md) · checkpoint:
[`reports/plan35-progress.md`](plan35-progress.md) · baseline: [`reports/plan35-baseline.md`](plan35-baseline.md).
Repository `omardefdev/HRManagement` (private since D-QA-15). All times UTC.

## 1. Summary per phase

| Phase | Result |
|-------|--------|
| P0 | Baseline on `c2b0d3f`: ci-local 85/0/2 not-run, 160 commits ahead of `origin/main` (`9cb3705`, fast-forward possible). `gh` was first `omar2def` (READ); the owner switched to `omardefdev` (ADMIN). The live protection on `main` (2 reviews, code owners, signatures, linear history) could not take the unsigned history with merge commits (OI-1). |
| P1 | `scripts/plan035-prepush.ps1`: gitleaks (pinned digest) over the whole history and the push range, actionlint, ci-local summary, forbidden files, GOV-001-D, signature report. The old `.gitleaks.toml` had **no rules**, so every scan had passed; now default rules + two narrow path-and-match allowlists. Optional pre-push guard (`scripts/scm/pre-push-guard.ps1`, `install-hooks.ps1`). |
| P2 | Gates made real: CodeQL SARIF gate (`scripts/ci/codeql-sarif-gate.ps1`), Gitleaks on the pinned image with a commit-count guard, duplicates `gitleaks.yml` and `gateway-ci.yml` deleted, schedules behind `ENABLE_SCHEDULED`, literal `\n` in `e2e-journeys.yml` fixed, `--locked-mode` everywhere, actionlint step, Acceptance job disabled until PLAN-41 with a notice job. CI realm (P-1): tokens carry realm roles (AC-QA-34g Blocked → Pass). |
| P3 | `docs/scm-governance/branch-protection-main.json` with dated exceptions, validator with policy floors and `-CheckLiveGitHub`, CODEOWNERS → `@omardefdev`. |
| P4 | Owner decisions OI-1 (a), OI-3 (a). SEC-001 fix: the 14 `health-*` gateway routes no longer anonymous (`6e907f5`, unit test). Pre-push check green on `0f69f52`. Owner removed the old protection, made the repository private, pushed `release/plan-35-first-push`, opened PR [#24](https://github.com/omardefdev/HRManagement/pull/24). |
| P5 | Four remote rounds on PR #24 (disk space, CodeQL baseline of 108 pre-existing alerts, BLD-002 build manifest, Playwright on Ubuntu 26.04); round 4 fully green on `444e8c2`. |
| P6 | `main` fast-forwarded and pushed (PR #24 merged by the push). Push run red once (PostgreSQL init-window race → HTTP 500), then green on `ea4a256`; a pipefail bug that had skipped the fix was found on the green log and fixed (`052b506`). |
| P7 | GitHub cannot protect a private repository on the free plan (403) → D-QA-1 fallback: local guard with `-ProtectMain` + exception `SCM-NO-GITHUB-PROTECTION`; validator `-CheckLiveGitHub` OK. E2E green twice in a row on `main` → required check. Negative PR [#25](https://github.com/omardefdev/HRManagement/pull/25) red and closed. `e2e-journeys` fixed in four rounds (environment import, readiness wait; manual run 41158446834 green for both profiles on the release branch). The owner squash-merged PR #26 (release at `4d1b472`) into `main` = `152506e` before its checks ran; its push run was green, its PR run had one flaky E2E timeout. Schedules switched off again and `e2e-journeys` made weekly (Actions minutes, owner decision). |
| P8 | AC-QA-35a..g **7/7 Pass** on `897d16b` (`acceptance-evidence/AC-QA-35/`; 35e against the release-branch run, §9), docs (PRG_001 header and status lines, plan status and notes, CI_AND_TESTING, runbook), independent review (no high findings; two medium and three low fixed in `897d16b`, §9), this report, final PR from `docs/plan-35-final-report`. |

## 2. Commits

Feature branch `feature/plan-35-qa-push-remote-ci` (merged into `main` at `0f69f52`, then into this report's branch):

| Commit | Change |
|--------|--------|
| `2809a4e` | pre-push check over the whole history, narrow lab allow-list, optional pre-push hook |
| `e3ae8c3` | workflow gates made real, CI realm tokens carry roles, workflow lint tests |
| `4f953cb` | branch protection as code, dated exceptions, live check, CODEOWNERS owner, governance docs |
| `aff0abc` | progress checkpoint, P4 blocked on owner decisions |
| `6e907f5` | **fix(sec001)**: upstream health routes require the gateway dashboard policy again |
| `7c540ac`, `eb2cce5`, `3e5c347` | first-push PR description, owner decisions, pre-push report |
| `c8ef2b1` | D-QA-1 fallback when GitHub cannot protect `main` |
| `0348627`, `55e691f`, `29afa7c`, `01f2773`, `1ad496a`, `d44c29f`, `925430a`, `783310f`, `f38773b`, `ce2aa72`, `61b4efa` | run logs, triage, owner-action stamps |
| `a6df66a` | `AcQa35RemoteCiTests` (AC-QA-35a..g) |
| `2c96921` | dated obligation for the ubuntu-24.04 pin |

Release branch `release/plan-35-first-push` (one commit per remote failure):

| Commit | Run | Fix |
|--------|-----|-----|
| `5d0b9b6` | 41083926373 | CodeQL: baseline the 108 pre-existing alerts surfaced by the first real SARIF gate |
| `dada03e` | 41083926373 | CI: free runner disk space first (No space left on device) |
| `97020fa` | 41089826696 | E2E: generate the BLD-002 build manifest before `compose build` |
| `444e8c2` | 41099010391 | Playwright jobs on `ubuntu-24.04` (Playwright 1.50.1 does not support Ubuntu 26.04) |
| `d9c09b0` | 41119397846 | E2E: start PostgreSQL first and wait for TCP readiness |
| `ea4a256` | 41119397846 | acceptance-up: the same PostgreSQL wait |
| `052b506` | 41130907741 | read the compose service list once (pipefail + `grep -q` skipped the preparation) |
| `4d1b472` | 41140244680 | recruitment-journey prod-like switch imports the acceptance environment |
| `0ef97be` | 41147863512 | prod-like switch waits until the gateway reaches the recreated upstreams |
| `eacc914` | 41151844485 | prod-like switch waits on each service's own `/health/ready` instead (the gateway health route answered 500) |

Report branch `docs/plan-35-final-report`: `df776ec` (weekly `e2e-journeys`, schedules off), `897d16b` (review fixes), the docs and evidence
commits, and the final commit `docs(qa035): remote CI green, protection applied, gap status lines, final report`.

## 3. Owner decisions and when they came

| When (UTC) | Decision |
|------------|----------|
| 2026-10-30 13:54 | `gh` re-authenticated as `omardefdev` (ADMIN); the first account `omar2def` was READ only and hid the live protection (404). |
| 2026-10-30 15:05 | «Yes, both (a)»: **OI-1 (a)** remove the old protection just before pushing `main`, apply the file in P7; **OI-3 (a)** fix the 14 anonymous health routes with a unit test. Defaults for the rest: D-QA-15 private = yes, D-QA-1 yes (local guard as fallback), D-QA-16 no signing, P-2 no (the owner pushes every fix), D-QA-2 required checks CI/CodeQL/Gitleaks, E2E after two consecutive green runs. |
| 2026-10-30 15:47 | P4 commands done: protection removed, repository private, release branch pushed, PR #24 opened. |
| 2026-10-30 16:26 · 17:27 · 18:29 | P5 fix pushes (`dada03e`, `97020fa`, `444e8c2`). |
| 2026-10-30 19:57 · 21:25 | P6 pushes of `main` (`444e8c2`, `ea4a256`). |
| 2026-10-30 22:46 | «نفذها انت»: the owner told Claude to run the P7 commands itself, including the push of `main` (`052b506`). A one-off exception to "never push to `main` yourself" and P-2, on the owner's explicit instruction; recorded in the progress file. |
| 2026-10-31 00:01 · 00:44 · 02:05 | «Yes, run them»: Claude pushed the release branch and dispatched `e2e-journeys` (rounds 2, 3 and 4). |
| 2026-10-31 00:15 | The owner opened PR #26 (release at `4d1b472`) and squash-merged it at once: `main` = `152506e`, the same tree. Not requested by the plan; recorded, and the report branch merged `main` back. |
| 2026-10-31 00:22 | «نفذ الخياران المقترحان»: `ENABLE_SCHEDULED=false` and `e2e-journeys` weekly instead of nightly (Actions minutes, §10). |

## 4. gitleaks results (redacted) and their handling

- Before PLAN-35 `.gitleaks.toml` had neither rules nor `[extend] useDefault`: every scan, local and CI, passed with zero
  rules. Now gitleaks 8.28.0 (pinned digest, `docs/qa/tool-pins.json`) with the default rules.
- Whole history (172 commits at P1, 163 / range 160 at `0f69f52`, every CI run since): the only default-rule findings are
  the two Testcontainers lab secrets of QA-003 (`qa003-service-secret`, `qa003-wrong-secret`) in
  `realm-qa003-test.json` and `TestFixtures.cs`. They are test-only values of a throwaway realm. Handling: two
  allowlists that need **both** the path and the exact match. No allowlist for other lab or CI values; the default rules
  do not flag them, and an unused allowlist only widens the gate.
- No real secret was found (no stop condition). The unredacted report stays in `artifacts/plan35/` (git-ignored).
- Negative (AC-QA-35a): a planted random `ghp_` token in a temp repository fails the check, rule `github-pat`. A
  placeholder-looking token is skipped by gitleaks' default stopwords, so the test generates a random one.

## 5. Every remote run

Minutes are per-job durations from the API (the billing endpoint returns nothing usable for this account).

| Run | Head | Workflow | Start | End | Result | Jobs (minutes) |
|-----|------|----------|-------|-----|--------|----------------|
| [41083926373](https://github.com/omardefdev/HRManagement/actions/runs/41083926373) | `0f69f52` | ci (PR #24) | 10-30 15:47 | 16:18 | failure | CI fail 28 (disk); CodeQL fail 31 (gate: 108 pre-existing alerts); Gitleaks 2; notice 1 — ~62 |
| [41083926377](https://github.com/omardefdev/HRManagement/actions/runs/41083926377) | `0f69f52` | hardening-check | 15:47 | 15:51 | success | 5 |
| [41083926367](https://github.com/omardefdev/HRManagement/actions/runs/41083926367) | `0f69f52` | iac-plan | 15:47 | 15:47 | success | ~5 |
| [41089826696](https://github.com/omardefdev/HRManagement/actions/runs/41089826696) | `dada03e` | ci (PR #24) | 16:25 | 17:24 | failure | CI 52; CodeQL 18; Gitleaks 2; notice 1; E2E fail 7 (build manifest) — ~80 |
| [41089826889](https://github.com/omardefdev/HRManagement/actions/runs/41089826889) | `dada03e` | hardening-check | 16:25 | 16:28 | success | 3 |
| [41089826879](https://github.com/omardefdev/HRManagement/actions/runs/41089826879) | `dada03e` | iac-plan | 16:25 | 16:26 | success | ~5 |
| [41099010391](https://github.com/omardefdev/HRManagement/actions/runs/41099010391) | `97020fa` | ci (PR #24) | 17:27 | 18:25 | failure | CI 31; CodeQL 32; Gitleaks 2; notice 1; E2E fail 28 (Playwright on Ubuntu 26.04) — ~94 |
| [41099010343](https://github.com/omardefdev/HRManagement/actions/runs/41099010343) | `97020fa` | hardening-check | 17:27 | 17:31 | success | 4 |
| [41099010223](https://github.com/omardefdev/HRManagement/actions/runs/41099010223) | `97020fa` | iac-plan | 17:27 | 17:28 | success | ~5 |
| [41107410607](https://github.com/omardefdev/HRManagement/actions/runs/41107410607) | `444e8c2` | ci (PR #24) | 18:29 | 19:52 | **success** | CI 50.4; CodeQL 31.3; Gitleaks 1.8; notice 0.1; E2E 32.3 (57/0/0) — ~116 |
| [41107410591](https://github.com/omardefdev/HRManagement/actions/runs/41107410591) | `444e8c2` | hardening-check | 18:29 | 18:33 | success | 3.5 |
| [41107410610](https://github.com/omardefdev/HRManagement/actions/runs/41107410610) | `444e8c2` | iac-plan | 18:29 | 18:30 | success | 0.5 |
| [41119397846](https://github.com/omardefdev/HRManagement/actions/runs/41119397846) | `444e8c2` | ci (push `main`) | 19:57 | 21:17 | failure | CI 51.4; CodeQL 32.8; Gitleaks 1.1; E2E fail 29.2 (52/5/0, HTTP 500) — ~115 |
| [41119397886](https://github.com/omardefdev/HRManagement/actions/runs/41119397886) | `444e8c2` | hardening-check | 19:57 | 20:01 | success | 4 |
| [41130907741](https://github.com/omardefdev/HRManagement/actions/runs/41130907741) | `ea4a256` | ci (push `main`) | 21:25 | 22:38 | **success** | CI 50.1; CodeQL 33.3; Gitleaks 1.7; E2E 23.5 (57/0/0) — ~109 |
| [41140221205](https://github.com/omardefdev/HRManagement/actions/runs/41140221205) | `052b506` | ci (push `main`) | 22:45 | 23:52 | **success** | CI 44.9; CodeQL 18.9; Gitleaks 1.8; E2E 21.6 (57/0/0, PostgreSQL TCP wait ran) — ~87 |
| [41140248696](https://github.com/omardefdev/HRManagement/actions/runs/41140248696) | `052b506` | dated-obligations (dispatch) | 22:45 | 22:46 | **success** | <1 |
| [41140244680](https://github.com/omardefdev/HRManagement/actions/runs/41140244680) | `052b506` | e2e-journeys (dispatch) | 22:45 | 23:21 | failure | journeys 25.9; recruitment fail 35.6 (prod-like env) — ~62 |
| [41140318433](https://github.com/omardefdev/HRManagement/actions/runs/41140318433) | `5bff180` | ci (PR #25, negative) | 22:46 | 23:18 | failure (expected) | CI fail 18.2 (only `Plan35NegativeCheckTests`); CodeQL 31.4; Gitleaks 1.7 — ~51 |
| [41147863512](https://github.com/omardefdev/HRManagement/actions/runs/41147863512) | `4d1b472` | e2e-journeys (dispatch, release) | 10-31 00:01 | 00:37 | failure | journeys 29.5; recruitment fail 35.6 (prod-like 502) — ~65 |
| [41149141375](https://github.com/omardefdev/HRManagement/actions/runs/41149141375) | `4d1b472` | ci (PR #26) | 00:15 | 01:29 | failure | CI 44.0; CodeQL 16.6; Gitleaks 1.9; E2E fail 29.9 (56/1/0: `SEP_H1` navigation timeout 30 s, flaky) — ~92 |
| [41149190573](https://github.com/omardefdev/HRManagement/actions/runs/41149190573) | `152506e` | ci (push `main`, PR #26 squash) | 00:15 | 01:30 | **success** | CI 45.0; CodeQL 32.4; Gitleaks 1.8; E2E 29.3 — ~109 |
| [41151844485](https://github.com/omardefdev/HRManagement/actions/runs/41151844485) | `0ef97be` | e2e-journeys (dispatch, release) | 00:45 | 01:17 | failure | journeys 31.0; recruitment fail 32.4 (gateway health route 500) — ~63 |
| [41158446834](https://github.com/omardefdev/HRManagement/actions/runs/41158446834) | `eacc914` | e2e-journeys (dispatch, release) | 02:06 | 02:41 | **success** | journeys 30.9; recruitment 35.4 (lab and prod-like "Passed: 1, skipped: 0") — ~66 |
| (final-report PR and post-merge push run) | | | | | | after this report; links in the closing reply |

## 6. Quarantines with owners and dates

- **E2E quarantine** (`tests/e2e/quarantine.json`): empty. No test was quarantined. The three local navigation timeouts of
  P2 did not recur remotely; the SEC-001 failure was fixed instead (OI-3 (a)); the HTTP 500s of run 41119397846 were a
  startup race, fixed at the cause.
- **Disabled or pinned with a dated obligation** (`docs/qa/governance-exceptions.json`, `docs/qa/dated-obligations.json`):

| Id | What | Owner | Review by |
|----|------|-------|-----------|
| CI-ACCEPTANCE-DISABLED | Acceptance job off until `ENABLE_ACCEPTANCE_JOB` | PLAN-41 | 2026-12-14 |
| QA-CODEQL-BASELINE | 108 pre-existing CodeQL alerts | security (log forging: platform) | 2027-01-28 |
| CI-PLAYWRIGHT-UBUNTU-2404-PIN | Playwright jobs pinned to `ubuntu-24.04` | QA | 2027-01-28 |
| SCM-NO-GITHUB-PROTECTION | no GitHub protection on the private free repository; local guard | owner | 2027-01-28 |
| SCM-REVIEW-COUNT-0 · SCM-UNSIGNED-COMMITS · SCM-MERGE-COMMITS | single developer: 0 reviews, unsigned commits, merge commits | owner | 2027-01-28 |
| SUP001-DEPENDABOT-ALERTS-TOKEN | advisory sync needs `DEPENDABOT_ALERTS_TOKEN` | owner | 2027-01-28 |

## 7. Protection: file vs live

- **File** `docs/scm-governance/branch-protection-main.json`: required contexts `CI`, `CodeQL`, `Gitleaks`, `E2E`
  (strict); pending `Acceptance` (PLAN-41); `enforce_admins`; no force push or deletion; conversation resolution; 0
  reviews, no signatures, no linear history, each with an `exceptionRefs` entry.
- **Live:** none. The repository is private on the free plan, and `GET/PUT …/branches/main/protection` answers 403
  "Upgrade to GitHub Pro". Following D-QA-1, the fallback is the local pre-push guard installed with `-ProtectMain` (it
  refuses a direct push to `main` and scans every pushed ref with gitleaks and the forbidden-file check) plus the open
  exception `SCM-NO-GITHUB-PROTECTION`. `scm001-branch-protection-validate.ps1 -CheckLiveGitHub` reports exactly that
  and passes.
- **What this does not cover:** the guard is local to this clone. GitHub still lets a red PR be merged (PR #25 showed
  `MERGEABLE` with CI failed), and anyone with another clone could push to `main`. Real enforcement needs a public
  repository or GitHub Pro; then `scripts/scm/apply-branch-protection.ps1` applies the file and the validator compares
  it field by field.
- Context names: every required context is a job name in `ci.yml` (validator check), and the E2E job ran green under
  that name on `main` twice before it became required.

## 8. Workflows changed or removed

| Workflow | Change |
|----------|--------|
| `ci.yml` | free-disk step; actionlint step; CodeQL with a SARIF gate (upload only with `CODE_SCANNING_UPLOAD`); Gitleaks on the pinned image over the whole history with a commit-count guard; E2E via `compose-up-retry.sh` (build manifest, file-storage volume, PostgreSQL TCP wait, one cold-start retry), Keycloak readiness wait, quarantine filter, results upload, `ubuntu-24.04`; Acceptance behind `ENABLE_ACCEPTANCE_JOB` with a notice job |
| `e2e-journeys.yml` | real line continuations (literal `\n` removed); schedule guard; free disk; `compose-up-retry.sh`; `ubuntu-24.04`; prod-like switch imports the acceptance environment and waits for the gateway; **weekly** (Monday 04:00) instead of nightly |
| `codeql.yml` | schedule and dispatch only, with the guard and the gate |
| `mutation.yml` | `global-json-file`, `--locked-mode`, Stryker tool path |
| `advisory-to-issue.yml` | schedule needs `ENABLE_SCHEDULED` and `DEPENDABOT_ALERTS_TOKEN_CONFIGURED`; the script fails loudly on a refused request |
| `iac-drift.yml` | schedule guard |
| `hardening-check.yml` | `--locked-mode` |
| `gitleaks.yml`, `gateway-ci.yml` | **deleted** (duplicates of `ci.yml` jobs) |

Repository variables: `ENABLE_SCHEDULED=false` (was `true` from 22:46 to 00:22, §10). `ENABLE_ACCEPTANCE_JOB` and
`CODE_SCANNING_UPLOAD` unset.

## 9. Decisions and deviations

- **CodeQL gate in PowerShell, not Python.** The plan names a Python script; this Windows machine has no Python, and the
  unit tests (`CodeqlSarifGateTests`) must run on Windows and Linux.
- **108 CodeQL alerts baselined, not fixed.** The first real gate surfaced 108 pre-existing alerts (83
  missing-token-validation, 16 log forging, 3 cleartext storage, 3 exposure, 2 path injection, 1 cookie Secure). Plan §2
  rule 2 allows an existing alert only with an entry, an owner and a date; each entry pins rule + path + location hash,
  so a new alert still fails. Fixing them is a security backlog (§10).
- **No GitHub protection** (D-QA-15 private + free plan): D-QA-1 fallback, local guard and exception (§7). The plan's
  "live protection = file" becomes "validator passes on the documented fallback".
- **Claude pushed** `main` (P7) and the release branch (P7 rounds 2–3) on the owner's explicit instruction, against the
  prompt's "never push to `main` yourself" and P-2. Recorded with the owner's words in the progress file.
- **AC-QA-35e on the release branch.** The `e2e-journeys` fixes reach `main` only with this PR (P-5), so the manual run
  that passes both profiles is a dispatch on `release/plan-35-first-push`; the test takes `ACQA35_JOURNEYS_BRANCH` and
  records it. Run 41158446834 (`eacc914`): "Passed: 1, skipped: 0" for the lab and the prod-like profile.
- **Schedules off again, `e2e-journeys` weekly** (owner decision, 2026-10-31): Actions minutes of the private
  repository. G-QA-15's "three green nights in a row" is not reached; it becomes three green weekly runs once the owner
  sets `ENABLE_SCHEDULED=true` again.
- **Cold-start fixes in the CI path only** (P-1): `compose-up-retry.sh` and `acceptance-up.ps1` wait for PostgreSQL
  over TCP and retry once; the product compose healthcheck is unchanged (§10).
- **Playwright pinned to `ubuntu-24.04`** instead of upgrading Playwright inside this plan (dated obligation).
- **ci-local before the push** ran Testcontainers and the coverage gate as `not-run` (Docker Desktop proxy, as in
  PLAN-34); the native Windows re-run of those two steps was not repeated. Remote CI ran both for real, green.
- **Independent review (P8)** — one fresh subagent over the prompt, the plan and `git diff c2b0d3f..`: no high finding,
  so no refuter. Fixed in `897d16b`, with tests where the machine allows:
  - (medium) the pre-push gitleaks scans could pass without scanning (no `safe.directory`, no commit-count check, unlike
    `scripts/ci/gitleaks.sh`) — both added; verified 197/197 history and 26/26 range commits. A failing case (git
    "dubious ownership" inside the container) cannot be produced on Docker Desktop for Windows.
  - (medium) nothing required `E2E` to stay in the protection file — the validator and `Scm001GovernanceTests` now
    require it; fixture `missing-e2e.json` must fail.
  - (low) the guard let `git push --delete main` through, and called `powershell` even on a pwsh-only machine with the
    failure masked — the main check now comes first; the scan runs in the guard's own shell and a scan that did not run
    fails. Self-tests: delete-main refused, branch push scanned and allowed.
  - (low) the quarantine filter used a substring match (`FullyQualifiedName!~`) — now the exact name
    (`FullyQualifiedName!=`) and a non-qualified entry fails the step (checked in the ci-local image: full name → filter,
    `Login` → exit 1, empty list → empty).
  - No allow-list wider than needed, no secret in reports, required-check names match `ci.yml`.
- **E2E promoted on the second green push run** even though the first of the two (`ea4a256`) had skipped the PostgreSQL
  wait by the pipefail bug. Both runs were green for every E2E case; the second ran the fixed preparation.

## 10. Defects for later plans

| # | Defect | Where | Suggested owner |
|---|--------|-------|-----------------|
| 1 | 108 CodeQL alerts (missing token validation on 83 actions — CSRF —, log forging, cleartext storage, exposure, path injection, cookie Secure) | `docs/qa/codeql-baseline.json` | security plan before 2027-01-28 |
| 2 | PostgreSQL healthcheck `pg_isready` over the Unix socket passes during the init scripts; services then crash (exit 139) or run without migrations | `docker-compose.yml` (product/dev path; CI path works around it) | platform |
| 3 | Application services have no compose healthcheck, so `up --wait` means "running", not "ready" | `docker-compose.services.yml` | platform |
| 4 | Cold-start crashes of auth-directory, payroll, compliance, dev-analytics when PostgreSQL refuses the first connection (no retry in the product startup) | services | platform |
| 5 | file-storage named volume root-owned on a clean host (non-root app user 1654) in the dev path | compose | platform |
| 6 | `scripts/verify-staging-stack.ps1` calls `/health/upstreams/*` anonymously; since the SEC-001 fix it receives the login redirect and passes without real health data | deploy health gate, Acceptance job | PLAN-36 |
| 7 | Playwright 1.50.1 does not support Ubuntu 26.04 (`ubuntu-latest`); upgrade (Dependabot PR #20 offers 1.61.0) and drop the pin | `CI-PLAYWRIGHT-UBUNTU-2404-PIN` | QA |
| 8 | Node 20 actions forced to Node 24; CodeQL Action v3 deprecated in December 2026 | workflows | QA |
| 9 | The failed E2E job uploads neither service logs nor Playwright traces (run 41119397846 was diagnosed from code and a local reproduction) | `ci.yml` E2E job | QA |
| 10 | GitHub protection unavailable on the private free repository; a red PR is mergeable on GitHub | §7 | owner (public or Pro) |
| 12 | Flaky E2E navigation timeout (`SEP_H1_Authorized_Opens_Separation_Surface`, 30 s) on the required E2E check (PR #26 run); same kind as the three local timeouts of P2 | `src/Tests/HRManagement.E2E` | QA |
| 13 | The gateway's `/health/upstreams/*` routes answer 500 to a loopback caller (run 41151844485), so the internal probe they were kept for does not work | gateway | platform |
| 14 | Without GitHub protection a PR can be merged before its checks run (PR #26 was squash-merged seconds after it was opened) | process, §7 | owner |
| 11 | Actions minutes: ~1,210 minutes in this plan before the final PR (~1,430 with it and the post-merge run); the free private quota is 2,000 per month | §11 | owner |

## 11. Timing

Times are UTC. `scripts/plan-timing.ps1` prints the machine's local time (UTC+3). Figures are to the opening of the
final-report PR; the PR run and the post-merge run come after this report and are given in the closing reply.

- **Total wall-clock** to 2026-10-31T02:55Z: **13 h 17 min**, from `Started (UTC): 2026-10-30T13:38:10Z`.
- **Owner-wait time** (every `Waiting for owner` → `Owner action done` pair in the progress file): **1 h 30 min** in 12
  waits. The longest: the decisions at the start (15 min), the P4 push commands (15 min), the P7 round 4 go-ahead
  (44 min). The Actions-minutes question (00:16 → 00:22) was asked in chat and not stamped.
- **Waiting on remote CI** (from a push or dispatch to the end of the run it waited for): **about 9 h 13 min** over ten
  rounds (31, 58, 58, 83, 80, 73, 66, 36, 33 and 35 min). Triage, the acceptance tests, the docs and this report were
  worked on during several of these waits.
- **Active time** (wall-clock − owner waits − CI waits not used for other work): about **2 h 35 min** of pure active
  work; the CI waits during which other work went on add about 1 h, so roughly 3 h 30 min of active work in total.
- **Per phase** (`Finished` stamps): P0+P1 14:07 (29 min), P2 14:55 (48 min), P3 15:01 (6 min), P4 15:32 (31 min, incl.
  the 4-min decision wait), P5 19:55 (4 h 23 min, four CI rounds), P6 22:43 (2 h 48 min, two push runs), P7 02:41
  (3 h 58 min, push run, negative PR, four `e2e-journeys` rounds), P8 from 02:41 (review, evidence, report; started
  earlier in the CI waits).
- **Actions minutes** (per-job durations, rounded): about **1,210 minutes** for the 25 runs in §5, before the final PR.
  The private repository's free quota is 2,000 per month; a full `ci.yml` run costs 90–115 minutes, an `e2e-journeys`
  run about 65.
- **Resumes:** none recorded; the session's context was compacted twice and continued straight on.
- **`powershell -File scripts/plan-timing.ps1 -From 25 -To 35`** (before the final merge; PLAN-35's merge is the
  squash of PR #26, hence `Merged True`):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
----    ------- -------          -----------      ---              ------ ----   ---------
PLAN-25      11                  2026-10-29 20:31 2026-10-29 23:21   True 2:50
PLAN-26      12                  2026-10-21 13:38 2026-10-21 22:49  False 9:10
PLAN-27      11                  2026-10-21 22:57 2026-10-22 09:43  False 10:45
PLAN-28      13                  2026-10-22 10:03 2026-10-22 15:03  False 5:00
PLAN-29      13                  2026-10-22 15:21 2026-10-22 20:36  False 5:15
PLAN-30      21                  2026-10-23 00:45 2026-10-29 07:45  False 150:59
PLAN-31      14                  2026-10-29 10:24 2026-10-29 15:03  False 4:38
PLAN-32       9                  2026-10-29 14:12 2026-10-29 16:31   True 2:19
PLAN-33      11                  2026-10-29 17:49 2026-10-29 20:18   True 2:28
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58
PLAN-35      42 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43
```

## 12. Readiness note for PLAN-36

- `main` = `origin/main`; required checks `CI`, `CodeQL`, `Gitleaks`, `E2E` are green on `main` (`152506e`, push run [41149190573](https://github.com/omardefdev/HRManagement/actions/runs/41149190573); this PR moves `main` again).
- Every change reaches `main` through a PR; the local guard refuses a direct push from this clone. Before relying on
  GitHub to block a red PR, decide on public or Pro (§7).
- Schedules are off (`ENABLE_SCHEDULED=false`). Turn them on only with a minutes budget: weekly `e2e-journeys` (~65
  min), weekly CodeQL (~30), weekly mutation, weekly iac-drift, weekly dated obligations, plus ~90–115 min per push/PR
  CI run.
- PLAN-36 inputs: §10 items 2–6 (stack readiness, health gate), the CodeQL backlog (§10 item 1), the Acceptance job
  (PLAN-41, `CI-ACCEPTANCE-DISABLED`).
