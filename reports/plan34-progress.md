# PLAN-34 — Progress checkpoint

Prompt: `docs/acceptance-plans/execution/PLAN-34-AGENT-PROMPT.md` (copy of the session prompt). Plan:
`docs/acceptance-plans/PLAN-34-qa-pushable-baseline.md`. Branch: `feature/plan-34-qa-pushable-baseline` (from `main` @ `f9eab1a`).

**Resume rule:** read this file and `git log --oneline -20` first; continue from "Next step"; never redo a committed phase.

## Phase status

| Phase | Status | Commit |
|-------|--------|--------|
| P0 branch, program docs, baseline, inventory | done | `02f890b` (docs), this commit (baseline) |
| P1 dated obligations, clock, STR-005 closure | in progress (files drafted, not committed) | — |
| P2 single CI step list, Linux runner | pending | — |
| P3 red unit tests, test culture | pending | — |
| P4 twelve `Index(employeeId)` actions | pending | — |
| P5 integration test hosts | pending | — |
| P6 NU1903, warnings as errors | pending | — |
| P7 MinIO, format, EF, coverage | pending | — |
| P8 full green run, AC-QA-34a..i | pending | — |
| P9 docs, review, final report, merge | pending | — |

## Decisions

| # | Decision | Reason |
|---|----------|--------|
| D-QA-3 | Close both STR-005 simulation exceptions (`Closed`, `closedOn 2026-10-30`, `closedBy architecture`) by decision `PG-EXC-2026-10-001` in `decision-log.json`; no date extended | plan default; second exception expires today |
| D-QA-10 | warn ≤ 30 days, fail ≤ 7 days or expired | plan default |
| D-QA-5 | restrict the 12 `Index(employeeId)` actions for real, no freeze list | plan default |
| D-QA-7 | File.Storage `Local` in dev/CI; MinIO behind profile `object-storage` with `MINIO_IMAGE`; Testcontainers category `ObjectStorage` excluded as a dated obligation owned by PLAN-38 | plan default |
| D-QA-11 | test culture `ar-SY` (Gregorian) via `[ModuleInitializer]` in each test project | plan default |
| D-QA-12 | `global.json` never committed; SAN-001 reads `git show HEAD:global.json` | plan default |
| D-QA-13 | coverage: if the gate fails at first measurement, per-assembly floor = measured (rounded down) + ADR note + 60-day dated obligation; never lower `globalThreshold` | plan default |
| D-QA-14 | flaky `Hcm029…`: half-day investigation, else `Category=Flaky` + `tests/quarantine.json` (30-day review, dated obligation), non-blocking step | plan default |
| P-1..P-4 | docs committed first; no push; Linux parity is the bar; product changes limited to P-4 list | prompt |
| S-1 | Commits use the repository's existing author identity `omardefdev <omar@defdev.sy>` via `git -c user.name/-c user.email` (no git config written) | the machine has no global git identity; all earlier commits carry this author |
| S-2 | The dated-obligation key set is the plan's seven keys **plus** `licenseExpiry` and `eolDate` | the sweep found `vendor-contract-registry.json` dates compared with today by `sup004-license-audit.ps1`, which a unit test runs |
| S-3 | `scripts/qa/dated-obligations-check.ps1` and `scripts/ci-local.ps1` are Windows PowerShell 5.1-compatible, ASCII only | the owner's machine has no `pwsh`; `RepoScriptRunner` uses `powershell` (5.1) on Windows; a UTF-8 dash read in the ANSI code page can parse as a quote |

## Files changed (P1, uncommitted draft)

- `scripts/qa/dated-obligations-check.ps1` (new)
- `src/Shared/HRManagement.ServiceDefaults/GovernanceDateParser.cs` (new), `ProgramGovernance/ProgramGovernanceGuard.cs`, `ProgramGovernanceEvaluator.cs`, `ProgramGovernanceExtensions.cs`, `LegalControl/LegalControlGuard.cs`, `LegalControlCatalogEvaluator.cs`, `LegalControlExtensions.cs`
- `scripts/str005-governance-runtime-validate.ps1`, `scripts/str005-governance-validate.ps1`, `scripts/str002-control-runtime-validate.ps1`
- `docs/str-governance/governance-matrix.json`, `decision-log.json`, `docs/STR_005_EXCEPTION_PATHS.md`, `docs/STR_005_SIMULATION_PACK.md`
- `src/Tests/HRManagement.TestSupport/Time/FixedTimeProvider.cs` (new), `src/Tests/HRManagement.UnitTests/Str005ProductionBindingTests.cs` (partly edited)

## Test results

See `reports/plan34-baseline.md`: unit 16 failed / 4141; integration (non-TC) 24 failed / 258; Testcontainers 10 failed / 82.
Raw logs (git-ignored): `artifacts/plan34/{build,build-warnaserror,unit,int,tc}.log` and `artifacts/plan34/baseline-*/*.trx`.

## Open issues

- Extra integration failures not in the plan: `InternalHireClosureIntegrationTests`, `RecruitmentHireClosureIntegrationTests` (event count off by one) → root-cause in P5.
- `FileStorageInboxIdempotencyTests` needs MinIO → `Category=ObjectStorage` in P7.
- Ten compiler/analyzer warnings in product code will break `-warnaserror` (P6): CS0108, CS8321, CS8604 ×2, CS9113, CS9124, MVC1004 ×2.
- AC-QA-34b expects the nearest obligation to be STR-002 2027-01-31, but the plan also registers the ObjectStorage exclusion at +45 days (2026-12-14), which is nearer → the acceptance test asserts "nearest open obligation printed", recorded as a deviation.

## Next step

Finish P1: `docs/qa/dated-obligations.json` (fold in the sweep results), `testdata/qa/dated-obligations/*`,
`Str005ProductionBindingTests` script test via `RepoScriptRunner -AsOfDate`, `Str005GovernancePackTests` invariant parse,
`DatedObligationsTests`, `RuntimeGovernanceDocumentsTests`, sup004 invariant parse + `-AsOfDate`; build; run the STR/QA tests; commit P1.
