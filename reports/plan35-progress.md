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
| P4 | BLOCKED (owner decisions OI-1, OI-3) | |
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

P4: owner decisions (OI-1 protection on main blocks the push, OI-3 SEC-001 fix), then prepush green, first-push description, merge to main, release branch.

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
