# PLAN-66 baseline: CI durations, failures and inventories

Measured 2026-11-06 from the Actions API (`gh run view <id> --json jobs`, per-step times) and from the `job-timing.json` of run 42146523940. Raw files stay in `artifacts/plan66/` (not committed).

## Repository

- `gh repo view --json visibility`: PUBLIC. Standard hosted runners are free, artifact storage is free.
- Required checks (live): `CI`, `CodeQL`, `Gitleaks`, `E2E`, `Acceptance`, strict.

## Last five green pull request runs of `ci.yml` (minutes)

| Run | Branch | Whole run | CI | E2E | CodeQL | Acceptance | Gitleaks | Runner minutes |
|-----|--------|-----------|----|-----|--------|------------|----------|----------------|
| 42146523940 | release/plan-42 | 98 | 30.3 | 56.2 (Playwright install 2438 s, an outlier) | 17.0 | 97.5 | 1.0 | 202 |
| 41998062595 | release/plan-41 (evidence commit) | 90 | 25.9 | 12.4 | 17.3 | 89.8 | 0.8 | 146 |
| 41962709297 | release/plan-41 | 98 | 32.3 | 14.7 | 15.7 | 97.3 | 1.0 | 161 |
| 41934216395 | release/plan-41 | 104 | 34.7 | 14.9 | 9.6 | 103.9 | 1.0 | 164 |
| 41918380549 | release/plan-41 | 104 | 34.9 | 17.3 | 16.9 | 103.8 | 0.6 | 174 |

## Last five red pull request runs (minutes)

| Run | Branch | Whole run | Red job, after | Cause |
|-----|--------|-----------|----------------|-------|
| 42129768328 | release/plan-42 | 107 | Acceptance, 106.9 | `Ac37d`: a two-day leave starting on a Friday holds no working day. Test data |
| 42095506179 | release/plan-42 | 105 | Acceptance, 104.5 | `Ac39a`: 8 scopes expected, 9 found. Test count |
| 41959564898 | release/plan-41 | 31 | CI, 21.2 (Acceptance cancelled) | `dotnet publish` hung in `Int037PublishOutputTests`. Environment |
| 41951228726 | release/plan-41 | 74 | Acceptance, 73.3 | `mcr.microsoft.com` 503 while the base stack built. Environment |
| 41948564758 | release/plan-41 | 24 | CI, 9.0 (Acceptance cancelled) | Fixture lacked `Acceptance`, dated key in evidence |

Every red Acceptance run cost a full rerun of about 100 minutes, even when one class failed.

## Inside `Acceptance` (run 42146523940, seconds)

| Step | Seconds | Note |
|------|---------|------|
| Free runner disk | 272 | 86 GB were free before it, 108 GB after |
| Prepare | 7 | |
| Build the test projects | 41 | |
| Stack lab | 964 | image build 553, start 45, Keycloak sync 311, verification 50 |
| lab-main | 819 | |
| lab-journey | 11 | |
| lab-isolated | 340 | AcHcm26 101, AcHcm27 239 |
| Stack prodlike | 397 | no build, Keycloak sync 233 |
| prodLike journey + main | 37 | |
| prodLike isolated | 359 | |
| prodLike final | 58 | |
| prodLike s2s42 | 95 | |
| PII scan | 1 | |
| Stack leastpriv | 564 | includes the `hr-migrate` build |
| prodLike leastpriv | 227 | |
| Stack base | 550 | |
| lab-meta | 1054 | |

Stack starts: 2475 s (41 minutes). Tests: 3001 s (50 minutes).

## Inside `CI` (same run, seconds)

Verify Docker builds 545, Testcontainers 299, integration 175, unit 162, format 130, free disk 127, build 108, EF check 88, security scan 43; the other steps about 6 minutes in all.

## Inventories

### Images

The stacks build 15 service images under `hrm-acceptance-*` plus `hr-rabbitmq:local`; `hr-migrate:local` is built for least-privilege. Lab images take 3.08 GB (`docker system df`). The overlays add no `build:` of their own, so the base, lab, prod-like and least-privilege stacks all run the same service images under other project names.

Third-party images: `postgres:16-alpine`, `quay.io/keycloak/keycloak:24.0`, `clamav/clamav:1.4`, `alpine:3.20.3`. Tools by digest (`docs/qa/tool-pins.json`): gitleaks, actionlint, used by the meta classes.

Secrets: `.dockerignore` keeps `.env*`, keys and `docker/**` out of the context. `.acceptance/` was not listed: it reaches only the build stage (`COPY . .`), and the final stage copies `/app/publish` alone, so it never reached an image. PLAN-66 adds it to both ignore files anyway. The Keycloak realm is mounted, not baked.

### Code tree

`Get-AcceptanceCodeTree`: SHA-256 of `git ls-tree -r <commit>` without `acceptance-evidence/`, `reports/`, `docs/`. So `docs/qa/**`, which the meta classes read, is outside the tree: reuse must also compare those inputs (see S2 in the plan document).

### What the meta classes read

`AcQa34PushableBaselineTests`: the `test-results` artifact of the CI job (summary and trx of the same commit), `scripts/qa/dated-obligations-check.ps1`, `docs/qa/dated-obligations.json`, `testdata/qa/dated-obligations/**`, `docs/str-governance/*.json`, `scripts/ci/steps.json`, `Directory.Build.props`, `Directory.Packages.props`, `testdata/e2e/personas.json`, `.env.ci`, the base compose files.

`AcQa35RemoteCiTests`: `scripts/plan035-prepush.ps1`, `.gitleaks.toml`, `docs/qa/tool-pins.json`, `.github/**`, `scripts/scm/**`, `scripts/scm001-branch-protection-validate.ps1`, `testdata/qa/branch-protection/**`, `docs/qa/*`, `scripts/ci/codeql-sarif-gate.ps1`, `testdata/qa/codeql-gate/**`, `scripts/sync-dependabot-alerts-to-issues.sh`, `scripts/check-non-prod-data-policy.py`, and GitHub's own records.

Both need the CI job's results, so the meta stage cannot end before `CI` plus 1054 s.

### `E2E` and `CI`

`E2E` has `needs: [build-and-test]` and takes nothing from it: no artifact, no output. It can run beside `CI`.

### `AcQa41AcceptanceBaselineTests` (`final`)

It needs both: 41a, 41e, 41g read the folders of the lab and prod-like stages, and 41f signs in on a live prod-like stack. So it fits neither a plain stage nor an aggregate without a stack. Decision in the plan document: its own stage job starts its stack while the others run, and takes their results when they are ready.

### Dedupe and concurrency

`dedupe-push-run.sh`: on a push it finds the merged PR, its latest run for the PR head, and skips the heavy jobs when that run succeeded and its `tested-tree` equals `HEAD^{tree}`. Fails open. Only `Acceptance` has a `concurrency` group (`acceptance-<ref>`, cancel-in-progress); `CI`, `E2E` and `CodeQL` of a superseded run keep running.

### Runner disk

86 GB free before any cleanup, and the whole sequential job peaked below 30 GB of Docker data, so a stage job does not need the 272 s of `free-runner-disk.sh`.

## Expected critical path (to be measured)

Build job about 14 minutes; `lab:main` job about 24 minutes after it; aggregate about 3: about 41 minutes without meta. With meta: `CI` 36 + meta 18 + aggregate 3: about 57 minutes.
