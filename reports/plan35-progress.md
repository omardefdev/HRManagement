Started (UTC): 2026-10-30T13:38:10.4485293Z

# PLAN-35 progress (checkpoint)

Branch: `feature/plan-35-qa-push-remote-ci` (from `main` = `c2b0d3f`).

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-10-30T14:07:43.5005632Z |
| P1 | done | 2026-10-30T14:07:43.5005632Z |
| P2 | done | 2026-10-30T14:55:03.9999621Z |
| P3 | done | 2026-10-30T15:01:33.3818627Z |
| P4 | done (waiting for owner push) | 2026-10-30T15:32:44.9129751Z |
| P5 | pending | |
| P6 | pending | |
| P7 | pending | |
| P8 | pending | |

## Owner interactions

- Decisions message (D-QA-15, D-QA-1, D-QA-16, P-2, D-QA-2) sent in chat.
  Waiting for owner (UTC): 2026-10-30T13:39:05.0830242Z
- Owner logged `gh` in as `omardefdev` (was `omar2def`, READ only) - reported in chat.
  Owner action done (UTC): 2026-10-30T13:54:07.0169820Z (decisions still open)

## GitHub state (P0, verified as omardefdev)

- `omardefdev/HRManagement`: PUBLIC, viewer ADMIN, default branch `main`, account plan **free**.
- `origin/main` = `9cb3705` (3 commits); local `main` is 160 commits ahead; `origin/main` is an ancestor.
- **Live branch protection on `main` already exists** (the 404 seen as `omar2def` was a permission artefact):
  contexts CI/CodeQL/Gitleaks strict, 2 approving reviews + code-owner + last-push approval, required signatures,
  enforce_admins, required linear history, no force push/deletion. Saved: `artifacts/plan35/github/protection-before.json`.
  Consequence: the planned `git push origin main` fast-forward (unsigned commits, merge commits) is refused by this
  protection -> owner decision needed before P6 (see Open issues).
- No rulesets, no Actions variables, no secrets. Actions enabled (all actions allowed).
- Workflows on GitHub: scheduled ones are `disabled_inactivity` (advisory-to-issue, codeql, e2e-journeys, iac-drift, mutation).
- `@omard` (CODEOWNERS) = user `omarD` id 6695527, not the owner -> CODEOWNERS will name `@omardefdev`.
- No real Python on this Windows machine (Store aliases only); the pre-push script runs the non-prod check in the ci-local image.

## Tool pins

`docs/qa/tool-pins.json`: gitleaks 8.28.0 `sha256:cdbb7c95...`, actionlint 1.7.7 `sha256:887a259a...`.

## Decisions

- gitleaks: the old `.gitleaks.toml` had no rules and no `[extend] useDefault`, so every scan (CI and local) passed
  trivially. Fixed: default rules + two narrow path-AND-match allowlists (qa003 Testcontainers realm secrets, the only
  default-rule findings over the whole history). No allowlist for `-lab-only`/`hrm_ci_`/CI passwords: the default
  rules do not flag them, and an unused allowlist only widens the gate.
- forbidden-files: commit `544ba7a` (PLAN-26 snapshot) touches 4 `testdata/*/out/*.json` reports (counts/dates only);
  allowed by commit+path in the script, listed in the report. global.json per P-3.
- actionlint (with shellcheck) over the current workflows: only the literal `\n` in `e2e-journeys.yml:148` (fixed in P2).

## Files changed

- P0/P1: `reports/plan35-progress.md`, `reports/plan35-baseline.md`, `docs/qa/tool-pins.json`, `.gitleaks.toml`, `scripts/plan035-prepush.ps1`, `scripts/scm/pre-push-guard.ps1`, `scripts/scm/install-hooks.ps1`, `docs/acceptance-plans/execution/PLAN-35-AGENT-PROMPT.md`

## Open issues

- OI-1: live protection blocks the first push of `main` (needs owner decision; asked in chat).
- OI-2: owner decisions D-QA-15/D-QA-1/D-QA-16/P-2/D-QA-2 open (defaults apply at P4).

## Next step

P5: after the owner push: gh run list --branch release/plan-35-first-push; gh pr checks; triage failures.

## P0/P1 results (2026-10-30T14:07:43.5005632Z)

- Baseline: `reports/plan35-baseline.md` (ci-local on `c2b0d3f`: 85 passed, 0 failed, 2 not-run; 160 commits ahead).
- `scripts/plan035-prepush.ps1` on `c2b0d3f` (gitleaks, forbidden, signatures, nonprod): gitleaks history 172 commits
  and range 153 commits clean; forbidden PASS (4 allowed out-files of `544ba7a`, global.json = pin); nonprod PASS;
  signatures N=160. actionlint FAIL (the literal `\n` in e2e-journeys.yml) -> fixed in P2.
- AC-QA-35a negatives (temp repos under %TEMP%\plan35-neg): planted random `ghp_` token -> exit 1,
  `deploy.env.sample:1 rule=github-pat`; `global.json` rollForward latestPatch -> exit 1 naming the commit. (A
  placeholder-looking token is skipped by gitleaks' default stopwords; the test must generate a random one.)
- `scripts/scm/pre-push-guard.ps1` self-test: push to main with -ProtectMain -> exit 1; branch push -> gitleaks+forbidden PASS.
- Build `dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors.

## P2 results (2026-10-30T14:55:03.9999621Z)

- Workflows: e2e-journeys literal `\n` fixed; ENABLE_SCHEDULED guard on every scheduled job except dated-obligations;
  codeql.yml schedule+dispatch only; gitleaks.yml and gateway-ci.yml deleted; mutation.yml global-json-file, Stryker
  tool path, `--locked-mode`; advisory-to-issue schedule needs DEPENDABOT_ALERTS_TOKEN_CONFIGURED; ci.yml: CodeQL
  upload behind CODE_SCANNING_UPLOAD + SARIF gate, Gitleaks on the pinned image (whole history, commit-count guard),
  Acceptance renamed and behind ENABLE_ACCEPTANCE_JOB with a notice job, E2E via compose-up-retry + quarantine filter +
  Keycloak readiness wait, actionlint step (steps.json + ci.yml); hardening-check restore gets `--locked-mode` (found
  by WorkflowYamlLintTests).
- Decision: the CodeQL gate is **PowerShell** (`scripts/ci/codeql-sarif-gate.ps1`), not Python as the plan names it:
  this Windows machine has no Python and the unit tests must run on Windows and Linux (RepoScriptRunner).
- Dependabot script: `GH_TOKEN=bogus` -> exit 1, "refused: Bad credentials (HTTP 401)" + permission message (AC-QA-35g negative).
- CI realm (P-1): the realm template's own `clientScopes` list replaced Keycloak's built-in scopes; added roles
  (realm_access.roles), profile, email, web-origins. AC-QA-34g before: Blocked, tokenRealmRoles [] (kept as
  `acceptance-evidence/AC-QA-34/db-checks-34g-before-plan35.json`); after: **Pass**, roles [offline_access, file-storage],
  clean upload Clean/200, EICAR Infected/409.
- Base stack locally: cold start crashed 5 services with exit 139 (auth-directory, payroll, compliance, dev-analytics:
  PostgreSQL refused first connection; file-storage-web: root-owned fresh volume, "Access to /var/hrm/file-storage/hr-core
  denied"). `scripts/ci/compose-up-retry.sh` (CI path only): build, chown the file-storage volume as root (as
  acceptance-up.ps1 does), up --wait with one retry, fail on any non-zero exited container. Verified: stack healthy.
- Local E2E (CI filter) on the base stack: 53/57. 3 navigation timeouts = transient on this host (re-run 5/5 green);
  1 **product defect SEC-001**: `src/Gateway/HRManagement.Gateway/appsettings.json` sets `AuthorizationPolicy: Anonymous`
  on the 14 `health-*` routes (added by snapshot `544ba7a`), so /health/upstreams/* answers 200 anonymously;
  `GatewayUpstreamHealthE2ETests.Gateway_upstream_health_endpoints_deny_anonymous_access` fails -> required E2E red.
  Quarantine would hide a real security regression -> owner decision (OI-3).
- Unit: WorkflowYamlLint 6, CodeqlSarifGate 7, CiStepsParity, DatedObligations: 32/32. Build -warnaserror 0/0.
  ci-local (WorkingTree) steps "Workflow lint (actionlint)" + dated obligations: passed. scripts/ci/gitleaks.sh locally:
  173 commits, no leaks.
- Playwright Chromium installed locally (project playwright.ps1) for the local E2E run.
- `docker compose down -v` of the local base stack was refused by the session's permission policy; the stack
  (project hrmanagement) is still up; `.env` and the realm import were backed up to artifacts/plan35/stack/.

Open issue OI-3: SEC-001 anonymous /health/upstreams/* (product config). Options: (a) remove the 14 Anonymous policies
+ a unit test pinning it (recommended); (b) quarantine the E2E case (hides a real regression; not recommended).

## P3 results (2026-10-30T15:01:33.3818627Z)

- `docs/scm-governance/branch-protection-main.json`: contexts CI/CodeQL/Gitleaks strict; pendingRequiredChecks E2E
  (D-QA-2) and Acceptance (PLAN-41); 0 approvals/no code-owner/no last-push (SCM-REVIEW-COUNT-0), no signatures
  (SCM-UNSIGNED-COMMITS), no linear history (SCM-MERGE-COMMITS); enforce_admins, no force-push/deletion.
- Validator: policy floors + exceptionRefs -> Open unexpired governance exception; hard rules; contexts = ci.yml job
  names; CODEOWNERS only @omardefdev; -CheckLiveGitHub / -LiveProtectionFile field-by-field. Live today: 5 differences
  (the pre-PLAN-35 protection) - expected until P7.
- CODEOWNERS: 366 x @omard -> @omardefdev. `scripts/scm/apply-branch-protection.ps1` (-DryRun, -Remove for OI-1 (a)).
- Tests: Scm001BranchProtectionExceptionTests 10 + Scm001GovernanceTests updated (relaxation needs exceptionRefs).
- Incident (no effect): a bash command with backticks in double quotes ran fragments as commands; every PowerShell
  script among them was refused by the machine's execution policy (Restricted). Verified afterwards: live protection
  unchanged (2 reviews, signatures, linear), no git hook installed, runbook restored and rewritten with the Edit tool.
- Finding: Windows PowerShell here refuses `powershell -File` without `-ExecutionPolicy Bypass`; owner commands use it.

## BLOCKED (P4) - Waiting for owner (UTC): 2026-10-30T15:01:55.3130038Z

P0-P3 are committed (2809a4e, e3ae8c3, 4f953cb). P4 needs two decisions without a safe default:

- **OI-1 - existing protection on `main`.** It requires 2 reviews, code-owner and last-push approval, signed commits and
  linear history, with enforce_admins. The 160 local commits are unsigned and contain merge commits, so neither
  `git push origin main` (P6) nor a GitHub merge of the first PR can land.
  Options: (a, recommended) the owner removes it just before P6
  (`powershell -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1 -Remove`), fast-forwards `main`,
  then applies the file in P7 - matches the plan's order; (b) apply the PLAN-35 file now (0 reviews, no signatures, no
  linear history) and merge the first PR on GitHub with a merge commit (local `main` then pulls it).
- **OI-3 - SEC-001 regression, product config.** `src/Gateway/HRManagement.Gateway/appsettings.json` puts
  `"AuthorizationPolicy": "Anonymous"` on the 14 `health-*` routes (added by snapshot 544ba7a), so
  `/health/upstreams/*` answers 200 anonymously and the required E2E check
  (`GatewayUpstreamHealthE2ETests.Gateway_upstream_health_endpoints_deny_anonymous_access`) fails.
  Options: (a, recommended) remove the 14 lines (the middleware then requires GatewayDashboard, loopback still allowed;
  the dashboard probes clusters directly since 544ba7a) + a unit test pinning it; (b) quarantine the E2E case with a dated
  obligation - this hides a real security regression (plan Stop condition).
- Still open with defaults at P4: D-QA-15 private (default yes; note: free plan -> no protection when private -> local
  guard), D-QA-1 (yes), D-QA-16 (no signing), P-2 (owner pushes), D-QA-2 (as stated).
Owner action done (UTC): 2026-10-30T15:05:27.2407909Z - owner answered «Yes, both (a)»: OI-1 (a) remove the old protection before pushing main,
apply the file in P7; OI-3 (a) fix the 14 health routes with a unit test; defaults for the rest: D-QA-15 private = yes,
D-QA-1 yes (local guard if the free plan cannot protect a private repo), D-QA-16 no signing, P-2 no (owner pushes),
D-QA-2 as stated.

## P4 results (2026-10-30T15:32:44.9129751Z)

- OI-3 (a): `6e907f5` removes the 14 `Anonymous` policies from the `health-*` gateway routes;
  `GatewayRoutingConfigurationTests.Upstream_health_routes_are_not_anonymous` fails on the old config, passes now (gateway
  routing/authorization tests 32/32). Side effect recorded for PLAN-36: `scripts/verify-staging-stack.ps1` (deploy health
  gate, Acceptance job) calls /health/upstreams/* anonymously and now gets the login redirect (passes without real
  health data).
- `reports/plan35-first-push.md` (PR description) `7c540ac`; merge into local main `0f69f52` (--no-ff);
  `release/plan-35-first-push` = main = `0f69f52`, 168 commits ahead of origin/main.
- ci-local on `0f69f52` (`artifacts/ci-local/summary.json`): 86 passed, 0 failed, 2 not-run (Testcontainers, coverage
  gate; they run on ubuntu-latest), 968 s. The native Windows Testcontainers run was not repeated (deviation; remote CI
  runs both steps for real).
- `scripts/plan035-prepush.ps1` on `0f69f52`: all PASS (gitleaks history 163 / range 160 commits, actionlint, cilocal,
  forbidden, nonprod, signatures N). Report: artifacts/plan35/prepush/report.md (not committed).
- Progress commits after `0f69f52` go to the feature branch only, so the pushed HEAD is exactly the checked commit.

## OWNER ACTION (P4)

Waiting for owner (UTC): 2026-10-30T15:32:44.9129751Z

1. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1 -Remove`
   (OI-1 a; done now, while the repository is still public: a private repository on the free plan may refuse the call)
2. `gh repo edit omardefdev/HRManagement --visibility private --accept-visibility-change-consequences` (D-QA-15)
3. `git push -u origin release/plan-35-first-push`
4. `gh pr create --base main --head release/plan-35-first-push --title "First push: PLAN-25..35" --body-file reports/plan35-first-push.md`
Owner action done (UTC): 2026-10-30T15:47:38.3266427Z - owner replied «تم». Verified: repository PRIVATE; protection API now
403 "Upgrade to GitHub Pro" (private on free: protection unavailable -> D-QA-1 fallback, local guard, in P7);
origin/release/plan-35-first-push = 0f69f52; PR #24 https://github.com/omardefdev/HRManagement/pull/24 opened
2026-10-30T15:47:18Z; runs started 15:47:21Z: ci 41083926373, hardening-check 41083926377, iac-plan 41083926367 (success, 26 s).

## P5 - remote CI triage on PR #24

PR: https://github.com/omardefdev/HRManagement/pull/24. Pushes go through the owner (P-2 = no). Minutes = per-job
durations rounded up (the billing API reports 0 ms for these runs so far).

| Run | Head | Workflow | Start (UTC) | End (UTC) | Result | Jobs (minutes) |
|-----|------|----------|-------------|-----------|--------|----------------|
| [41083926373](https://github.com/omardefdev/HRManagement/actions/runs/41083926373) | 0f69f52 | ci | 15:47:21 | 16:18:20 (31 min) | failure | CI fail 28 (No space left on device in "Verify Docker builds"); CodeQL fail 31 (SARIF gate: 112 results / 108 alerts, all pre-existing); Gitleaks ok 2; notice ok 1; E2E/Acceptance skipped. ~62 min |
| [41083926377](https://github.com/omardefdev/HRManagement/actions/runs/41083926377) | 0f69f52 | hardening-check | 15:47:21 | 15:51:34 | success | 5 |
| [41083926367](https://github.com/omardefdev/HRManagement/actions/runs/41083926367) | 0f69f52 | iac-plan | 15:47:21 | 15:47:47 | success | 5 jobs, ~1 each |

Fixes on release/plan-35-first-push (one commit per failure):
- `5d0b9b6` CodeQL: 108 pre-existing alerts baselined one by one (rule + path + primaryLocationLineHash), owner security
  (log-forging: platform), reviewBy 2027-01-28 (QA-CODEQL-BASELINE). 83 missing-token-validation (8.8), 16 log-forging
  (6.1), 3 cleartext storage (7.5), 3 exposure of sensitive info (6.5), 2 path injection (7.5), 1 cookie Secure (5.0).
  Plan §2 rule 2 (existing alert only with a baseline entry, owner, date). Verified locally: gate OK with the baseline on
  the run's SARIF, FAILED without it. Recorded as a security backlog for a later plan (final report §10).
- `dada03e` CI: `scripts/ci/free-runner-disk.sh` (first step of steps.json/ci.yml and of the E2E job), GitHub-hosted
  Linux only. Parity/lint/gate tests 32/32, actionlint clean.
- Notices from the run (later plans): Node 20 actions forced to Node 24; CodeQL Action v3 deprecated December 2026;
  ubuntu-latest moves to Ubuntu 26.
- On the feature branch (reaches main with the final-report PR): `c8ef2b1` D-QA-1 fallback (protection API 403 on the
  private free repo -> exception SCM-NO-GITHUB-PROTECTION + local guard check in the validator).

OWNER ACTION (P5 round 1) - Waiting for owner (UTC): 2026-10-30T16:25:11.3370154Z
1. `git push origin release/plan-35-first-push`
Owner action done (UTC): 2026-10-30T16:26:01.2359583Z - owner pushed dada03e («تم»).

| Run | Head | Workflow | Start (UTC) | End (UTC) | Result | Jobs (minutes) |
|-----|------|----------|-------------|-----------|--------|----------------|
| [41089826696](https://github.com/omardefdev/HRManagement/actions/runs/41089826696) | dada03e | ci | 16:25:57 | 17:24:28 (58.5 min) | failure | CI **ok** 52 (all steps incl. Testcontainers + coverage gate on ubuntu); CodeQL **ok** 18 (gate with the baseline); Gitleaks **ok** 2; notice ok 1; E2E **fail** 7 ("Start stack": '"/docker/build-manifest": not found'); Acceptance skipped. ~80 min |
| [41089826889](https://github.com/omardefdev/HRManagement/actions/runs/41089826889) | dada03e | hardening-check | 16:25:57 | 16:28:14 | success | 3 |
| [41089826879](https://github.com/omardefdev/HRManagement/actions/runs/41089826879) | dada03e | iac-plan | 16:25:57 | 16:26:25 | success | ~5 |

Round 2 fix (`97020fac` on release): the E2E job never generated the gitignored BLD-002
build manifest that docker/DotnetService.Dockerfile copies (locally it existed from earlier runs). compose-up-retry.sh
and acceptance-up.ps1 generate it first (as dev-profile-up.ps1 does); the journeys job uses compose-up-retry.sh; both
journeys jobs free runner disk. actionlint clean, lint/parity tests 12/12.

OWNER ACTION (P5 round 2) - Waiting for owner (UTC): 2026-10-30T17:26:38.8507310Z
1. `git push origin release/plan-35-first-push`
Owner action done (UTC): 2026-10-30T17:27:47.5851324Z - owner pushed 97020fa («تم»).

| Run | Head | Workflow | Start (UTC) | End (UTC) | Result | Jobs (minutes) |
|-----|------|----------|-------------|-----------|--------|----------------|
| [41099010391](https://github.com/omardefdev/HRManagement/actions/runs/41099010391) | 97020fa | ci | 17:27:38 | 18:25:54 (58 min) | failure | CI **ok** 31; CodeQL **ok** 32; Gitleaks **ok** 2; notice ok 1; E2E **fail** 28: stack healthy on attempt 2 (cold-start retry: auth-directory-web, dev-analytics-web exit 139 on attempt 1), then "Install Playwright browsers": ubuntu-latest is Ubuntu 26.04 and Playwright 1.50.1 "does not support chromium on ubuntu26.04-x64". ~94 min |

(The first watcher stopped on a GitHub API 502; the run itself was unaffected.) Free-disk step: 15 GB -> 37 GB free.

Round 3 fix (`444e8c29` on release): the E2E and Acceptance jobs of ci.yml and both e2e-journeys jobs run on ubuntu-24.04.
Later plan: upgrade Microsoft.Playwright (Dependabot PR #20 offers 1.61.0) and drop the pin (dated obligation added
with the final report). actionlint clean, lint/parity 12/12.

OWNER ACTION (P5 round 3) - Waiting for owner (UTC): 2026-10-30T18:29:02.9968344Z
1. `git push origin release/plan-35-first-push`
Owner action done (UTC): 2026-10-30T18:29:47.2610920Z - owner pushed 444e8c2 («تم»); run 41107410607 queued 18:29:45Z.

| Run | Head | Workflow | Start (UTC) | End (UTC) | Result | Jobs (minutes) |
|-----|------|----------|-------------|-----------|--------|----------------|
| [41107410607](https://github.com/omardefdev/HRManagement/actions/runs/41107410607) | 444e8c2 | ci | 18:29:45 | 19:52:33 (83 min) | **success** | CI **ok** 50.4; CodeQL **ok** 31.3; Gitleaks **ok** 1.8; notice ok 0.1; E2E **ok** 32.3 (57 passed / 0 failed / 0 skipped; stack healthy on attempt 2 - cold-start retry, P-1); Acceptance skipped (disabled, CI-ACCEPTANCE-DISABLED). ~116 min |
| [41107410591](https://github.com/omardefdev/HRManagement/actions/runs/41107410591) | 444e8c2 | hardening-check | 18:29:45 | 18:33:13 | success | 3.5 |
| [41107410610](https://github.com/omardefdev/HRManagement/actions/runs/41107410610) | 444e8c2 | iac-plan | 18:29:45 | 18:30:12 | success | 0.5 |

PR #24 mergeStateStatus CLEAN. First green E2E (D-QA-2: E2E becomes a required check after two consecutive green runs;
the push run on main at the same SHA is the second candidate). main (0f69f52) and origin/main (9cb3705) are both
ancestors of 444e8c2: fast-forward possible.

P5 Finished (UTC): 2026-10-30T19:55:14.0034262Z

OWNER ACTION (P6) - Waiting for owner (UTC): 2026-10-30T19:55:14.0229436Z
1. `git switch main`
2. `git merge --ff-only release/plan-35-first-push`
3. `git push origin main`
Owner action done (UTC): 2026-10-30T19:57:19.2627337Z - owner fast-forwarded main to 444e8c2 and pushed («تم»); main = origin/main = 444e8c2. Push runs ci 41119397846 and hardening-check 41119397886 started 19:57:11Z.
