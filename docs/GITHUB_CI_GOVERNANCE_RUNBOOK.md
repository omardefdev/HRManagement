# GitHub CI & governance runbook (SCM-001 / G0 / G9)

**Repository:** `omardefdev/HRManagement` (owner account `omardefdev`, plan **free**). Visibility: **public** until the
owner decides D-QA-15 (PLAN-35). A private repository on the free plan cannot have branch protection; the local guard
below replaces it there.  
**Primary CI workflow:** [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)

## Required status checks (branch protection, PLAN-35)

| Check name | Job | Workflow | Required |
|------------|-----|----------|----------|
| `CI` | build-and-test (the 89 steps of `scripts/ci/steps.json`) | `ci.yml` | yes |
| `CodeQL` | codeql + SARIF gate (`scripts/ci/codeql-sarif-gate.ps1`) | `ci.yml` | yes |
| `Gitleaks` | gitleaks over the whole history (`scripts/ci/gitleaks.sh`) | `ci.yml` | yes |
| `E2E` | e2e | `ci.yml` | yes since PLAN-35 P7 (D-QA-2: green twice in a row on main, runs 41130907741 and 41140221205) |
| `Acceptance` | acceptance | `ci.yml` | no: disabled until PLAN-41 (`ENABLE_ACCEPTANCE_JOB`, obligation `CI-ACCEPTANCE-DISABLED`) |

The protection is code: [`docs/scm-governance/branch-protection-main.json`](scm-governance/branch-protection-main.json).
The single-developer relaxations (0 approvals, no code-owner or last-push approval, unsigned commits, merge commits)
carry `exceptionRefs` to dated entries of [`docs/qa/governance-exceptions.json`](qa/governance-exceptions.json); the
validator refuses a relaxation whose entry is closed or past `reviewBy`.

Apply and validate (owner with ADMIN; Windows PowerShell 5.1):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1 -DryRun   # print the API body
powershell -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1           # PUT + signatures endpoint + live check
powershell -ExecutionPolicy Bypass -File scripts/scm001-branch-protection-validate.ps1
powershell -ExecutionPolicy Bypass -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub
```

Fallback when GitHub cannot protect `main` (private on the free plan):
`powershell -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1 -ProtectMain` installs a local `pre-push` guard
that refuses a direct push to `main` and runs gitleaks and the forbidden-file check on every push.

Before any push: `powershell -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

## Repository variables (Settings → Secrets and variables → Actions → Variables)

| Variable | Effect | Set when |
|----------|--------|----------|
| `ENABLE_SCHEDULED` | `true` lets the scheduled runs of e2e-journeys, mutation, codeql (weekly), iac-drift and advisory-to-issue run | after `main` is green. Set back to `false` on 2026-10-31: the repository is private on the free plan (2,000 Actions minutes per month) and a nightly e2e-journeys run alone costs ~60 minutes; e2e-journeys is now weekly (Monday 04:00 UTC). Re-enable when the minutes allow |
| `ENABLE_ACCEPTANCE_JOB` | `true` runs the `Acceptance` job | PLAN-41 |
| `CODE_SCANNING_UPLOAD` | `true` uploads the CodeQL SARIF to code scanning (public repo, or private with Code Security) | optional |
| `DEPENDABOT_ALERTS_TOKEN_CONFIGURED` | `true` lets advisory-to-issue run on its schedule | with the secret below |

## Secrets inventory

| Workflow | Secrets |
|----------|---------|
| `ci.yml`, `e2e-journeys.yml`, `codeql.yml`, `dated-obligations.yml`, `hardening-check.yml` | none (lab values from `.env.ci` only) |
| `deploy.yml` (PLAN-36) | `DEPLOY_SSH_USER`, `DEPLOY_SSH_KEY` and the fifteen service connection strings; environments `staging` / `production` |
| `iac-plan.yml`, `iac-apply.yml`, `iac-drift.yml` | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` |
| `advisory-to-issue.yml` | optional `DEPENDABOT_ALERTS_TOKEN` (fine-grained PAT: Dependabot alerts read, Issues write); GITHUB_TOKEN cannot read alerts (obligation `SUP001-DEPENDABOT-ALERTS-TOKEN`) |
| `mutation.yml` | optional `STRYKER_DASHBOARD_API_KEY` |

Removed in PLAN-35: `gateway-ci.yml` and `gitleaks.yml` (duplicates of `ci.yml` jobs).

**First PR / CI (G0–G9):** https://github.com/omardefdev/HRManagement/pull/1. PLAN-35 run links:
`reports/plan35-final-report.md`.

## G9 external blockers (NOT-CLOSED — outside GitHub Actions)

Tracked in [`docs/syd/SYD-001-018-TRACKER.md`](./syd/SYD-001-018-TRACKER.md) and
[`docs/GO_NO_GO_LIVE_EVIDENCE_CHECKLIST.md`](./GO_NO_GO_LIVE_EVIDENCE_CHECKLIST.md).

Repo-side validators:

```powershell
pwsh -File scripts/gov002-validate.ps1
pwsh -File scripts/nat006-validate.ps1
bash scripts/go-no-go-gate.sh
```

## Optional post-merge workflows

| Workflow | Trigger | Prerequisites |
|----------|---------|---------------|
| [`e2e-journeys.yml`](../.github/workflows/e2e-journeys.yml) | nightly + `workflow_dispatch` | Docker on runner; optional `IMAGE_TAG` |
| [`deploy.yml`](../.github/workflows/deploy.yml) | tag `v*` or manual | GitHub secrets: `DEPLOY_SSH_USER`, `DEPLOY_SSH_KEY`; environments `staging` / `production` |

### Staging deploy (manual)

1. Repo → Settings → Secrets → Actions: add `DEPLOY_SSH_USER`, `DEPLOY_SSH_KEY`.
2. Repo → Settings → Environments: create `staging` (and `production` when ready).
3. Actions → **deploy** → Run workflow → `action=deploy`, `environment=staging`, `run_migrations=true`.
4. Images publish to `ghcr.io/omardefdev/hrmanagement/*` with cosign OIDC signing.
5. Optional QA-004: Actions → **E2E Journeys (QA-004)** → `workflow_dispatch` with `image_tag` = short SHA from deploy summary.
