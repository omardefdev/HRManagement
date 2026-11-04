# PLAN-41: acceptance job in CI and an honest acceptance baseline

The `Acceptance` job runs the real stack on every PR and push to `main`: lab, then prod-like, under `ACCEPTANCE_REQUIRE_ALL=true`, with realm passwords generated on the runner. A blocked or unlisted required class turns it red.

- One job script for CI and the owner: `scripts/ci/acceptance-job.ps1`, scope in `tests/acceptance/ci-scope.json`, guard in `scripts/ci/acceptance-guard.ps1`, artifact scan before upload.
- Evidence records profile, image sources, code tree and run source. `acceptance-evidence/summary.md` states per AC: status, SHA, image source, profile, freshness, whether the test proves its claim, owning plan. Contractual, stale or incomplete results do not count as Pass.
- Images carry their source revision; `SOURCE_SHA` still defaults to `unknown`, so the demo server's build is unchanged.
- `CI-ACCEPTANCE-DISABLED` closed; `-Committed` validator runs in `CI` when `acceptance-evidence/**` changes.

The evidence of record comes from this PR's first green run (P-3); the second run validates it. Report: `reports/plan41-final-report.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
