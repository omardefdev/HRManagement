# GitHub CI & governance runbook (SCM-001 / G0 / G9)

**Repository:** `omardefdev/HRManagement`  
**Primary CI workflow:** [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)

## Required status checks (branch protection)

| Check name | Job | Workflow |
|------------|-----|----------|
| `CI` | build-and-test | `ci.yml` |
| `E2E` | e2e | `ci.yml` |
| `CodeQL` | codeql | `ci.yml` |
| `Gitleaks` | gitleaks | `ci.yml` |

Apply protection (admin, **requires GitHub Pro or public repo** for private repositories):

```powershell
gh api -X PUT repos/omardefdev/HRManagement/branches/main/protection `
  --input docs/scm-governance/branch-protection-main.json
```

> **Note:** On a free private repo, GitHub returns 403 for branch protection API. Upgrade to Pro, use an organization plan, or make the repo public before applying live protection.

Validate:

```powershell
powershell -File scripts/scm001-branch-protection-validate.ps1
powershell -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub
```

**PR / CI:** https://github.com/omardefdev/HRManagement/pull/1

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
