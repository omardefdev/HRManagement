## PLAN-66: a faster CI

The owner asked on 2026-11-06 whether the tests are excessive. They are not: the pipeline around them is slow. This PR keeps the coverage and changes how it runs.

**What changes**

- **Parallel stages (S1).** `Acceptance images` builds every service image once and pulls every third-party image once. `Acceptance stage (<name>)` runs one job per entry of `stageJobs[]` in `tests/acceptance/ci-scope.json`, on those images. `Acceptance` stays the required check: it needs every stage job, always runs, and fails when one did not succeed. It assembles the evidence in the layout of a sequential run, then runs the summary, the validator, the guard and the artifact scan as before.
- **Reuse for an unchanged code tree (S2, D-QA-66-2).** A commit that leaves the code tree unchanged reuses the green full run of the same pull request. `CodeQL`, `Gitleaks` and the committed-evidence check always run.
- **Governance checks on demand (S3, D-QA-66-1).** `AcQa34PushableBaselineTests` and `AcQa35RemoteCiTests` run when a pull request changes what they read, every night on `main`, and on every manual run. Otherwise the evidence records them as not run by rule.
- **E2E beside CI (S4).** It took nothing from `CI` but its success.

**What does not change**

The required check names, the branch protection, the name and layout of `acceptance-evidence`, the meaning of the guard. `acceptance-job.ps1 -Profile Both` without `-Stage` still runs everything in order on one machine. No class is changed, excluded or quarantined.

**This PR changes `.github/`, so its own run includes the governance checks.**

Plan: `docs/acceptance-plans/PLAN-66-qa-ci-fast-pipeline.md`. Baseline: `reports/plan66-baseline.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
