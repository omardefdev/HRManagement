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

Apply protection (admin):

```powershell
gh api -X PUT repos/omardefdev/HRManagement/branches/main/protection `
  --input docs/scm-governance/branch-protection-main.json
```

Validate:

```powershell
pwsh -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub
```

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

| Workflow | Trigger |
|----------|---------|
| [`e2e-journeys.yml`](../.github/workflows/e2e-journeys.yml) | nightly + `workflow_dispatch` |
| [`deploy.yml`](../.github/workflows/deploy.yml) | tag `v*` or manual → GHCR + staging/production |
