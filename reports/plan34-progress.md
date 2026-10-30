Started (UTC): 2026-10-29T23:13:31Z
# PLAN-34 — Progress checkpoint

Timing note: the timing rules were added to the prompt by the owner mid-session (after P0 was committed). The `Started`
line is therefore taken from git, not from a live clock reading: it is the reflog time of `git switch -c
feature/plan-34-qa-pushable-baseline` (`f9eab1a ... branch: Created from HEAD`, 2026-10-30T02:13:31+03:00), the first
state-changing action of the session (only `git status` preceded it). P0's `Finished` is its commit time.

Resumed (UTC): 2026-10-29T23:39:01.5074644Z — the owner interrupted the session to update the prompt (timing rules, `scripts/plan-timing.ps1` in P-1), then said «اكمل»; committed as `15592a8`.

Prompt: `docs/acceptance-plans/execution/PLAN-34-AGENT-PROMPT.md` (copy of the session prompt). Plan:
`docs/acceptance-plans/PLAN-34-qa-pushable-baseline.md`. Branch: `feature/plan-34-qa-pushable-baseline` (from `main` @ `f9eab1a`).

**Resume rule:** read this file and `git log --oneline -20` first; continue from "Next step"; never redo a committed phase.

## Phase status

| Phase | Status | Commit | Finished (UTC) |
|-------|--------|--------|----------------|
| P0 branch, program docs, baseline, inventory | done | `02f890b` (docs), `206c656` (baseline), `15592a8` (owner's prompt update) | 2026-10-29T23:24:33Z |
| P1 dated obligations, clock, STR-005 closure | done | `396e73f` | 2026-10-29T23:39:20.2696362Z |
| P2 single CI step list, Linux runner | done | P2 commit (`ci(qa034): single source of CI steps …`) | 2026-10-30T00:19:43.9485782Z |
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

| S-4 | `ops007/component-inventory.json` and `str-ownership/ownership-matrix.json` `eolDate` are listed as non-enforced (`historical[]` with a reason), not obligations | nothing compares them with the clock; `component-inventory.json` already lists past EOL dates (kubernetes 2025-12-28, .NET 9 2026-11-12) — registering them would turn CI red on facts that stop nothing. The stale inventory is a docs defect for OPS |
| S-5 | Seed-date unit tests (`AcceptanceSeedTests:84,184`, `Hcm033RecruitmentStatesSeedTests:60,100`) keep their real-clock assertion; the seed date (2028-08-01) is registered as `ACCEPTANCE-SEED-CLOSING` (key `applicationClosingDate` added) | the assertion means "the acceptance seed is still usable today"; replacing the clock with the seed reference date would weaken it. The horizon step fails 7 days before the tests would |
| S-6 | `supply-chain-exceptions.json` registered (`SUP001-SCA-EXCEPTIONS`, `allowEmpty`) | CI compares its `expiresOn` with today; empty today, armed by the first row |

## P1 — Finished (UTC): 2026-10-29T23:39:20.2696362Z

**Files:**
- New: `docs/qa/dated-obligations.json`, `scripts/qa/dated-obligations-check.ps1`, `testdata/qa/dated-obligations/*` (12 files), `tests/quarantine.json` (empty), `tests/ci-exclusions.json` (empty until P7), `src/Shared/HRManagement.ServiceDefaults/GovernanceDateParser.cs`, `src/Tests/HRManagement.TestSupport/Time/FixedTimeProvider.cs`, `src/Tests/HRManagement.UnitTests/DatedObligationsTests.cs`, `RuntimeGovernanceDocumentsTests.cs`.
- Product (P-4 "governance guards: clock and date parsing"): `ProgramGovernanceGuard.cs` (metrics + startup validator take `TimeProvider`), `ProgramGovernanceEvaluator.cs`, `ProgramGovernanceExtensions.cs` (`TryAddSingleton(TimeProvider.System)`), `LegalControlGuard.cs` (startup validator takes `TimeProvider`), `LegalControlCatalogEvaluator.cs`, `LegalControlExtensions.cs`. Parsing: `yyyy-MM-dd`, invariant, UTC (also removes a one-day-early shift on UTC+3 hosts from `Parse(...).ToUniversalTime().Date`).
- Scripts: `str005-governance-runtime-validate.ps1` (invariant parse; rooted `-MatrixPath`/`-OutDir`), `str005-governance-validate.ps1`, `str002-control-runtime-validate.ps1` (`-AsOfDate`, invariant), `sup004-license-audit.ps1` + `sup004-validate.ps1` (`-AsOfDate`, invariant).
- Governance: both STR-005 simulation exceptions `Closed` by `PG-EXC-2026-10-001`; `STR_005_EXCEPTION_PATHS.md`, `STR_005_SIMULATION_PACK.md` updated.
- Tests moved off the real clock: `Str005ProductionBindingTests` (fixed clock = matrix `asOfDate`, script via `RepoScriptRunner -AsOfDate`, new negative script test), `Str005GovernancePackTests` (invariant parse), `Str002ProductionBindingTests` and `Sup004ContractTests` (script `-AsOfDate` = the document's `lastReviewed`).

**Tests:** full unit suite 4165 total, **12 failed** (was 16): the four STR-005 failures are green; the 12 left are P3/P4 targets
(BOLA, Ten002, Syr001, San001, the seven scripts, certificate culture). New: `DatedObligationsTests` (discovery, registry
sanity, 10 horizon cases via `RepoScriptRunner`, real registry on a fixed day), `RuntimeGovernanceDocumentsTests` (canary on
Gateway/HR.Core/Payroll × Production/Staging with `TimeProvider.System`; pre-closure matrix 2026-09-15 passes / 2026-09-16
throws "expired"; legislative watch nextDue passes / +1 day throws). On today's date the CI validators STR-005 runtime,
STR-005 pack, STR-002 runtime and SUP-004 all PASS; `dated-obligations-check.ps1` on the real registry exits 0 (nearest
2027-01-31, STR-002).

## P2 — Finished (UTC): 2026-10-30T00:19:43.9485782Z

**Files:** `scripts/ci/steps.json` (87 steps: the 86 `run:` steps of `build-and-test` in order + "Dated obligations horizon
(QA-034)" before STR-002), `scripts/ci/run-steps.sh`, `scripts/ci/ci-local-entry.sh`, `scripts/ci-local.ps1`,
`docker/ci-local/Dockerfile`, `docker/base-image-pins.json` (`ciLocal` pins), `.github/workflows/ci.yml` (`build-and-test`
calls `run-steps.sh --step` only), `.github/workflows/dated-obligations.yml`, `src/Tests/HRManagement.UnitTests/Architecture/CiStepsParityTests.cs`,
`reports/plan34-baseline.md` (§0.5).

**Design decisions:**
- **S-7** `ci-local.ps1` runs the steps on a fresh **clone of HEAD inside the container** (`-Source Head`, default), not in the
  Windows tree: this is exactly "clean HEAD", keeps the owner's local `global.json` out (reported under `knownLocalOverrides`),
  and prevents Linux restores from clobbering the Windows `obj/` folders. `-Source WorkingTree` overlays only the files the
  host's git reports changed (modified/untracked/deleted) and always restores the committed `global.json`.
- **S-8** Parity fixes for running inside a container: `DOTNET_RUNNING_IN_CONTAINER` is unset (GitHub runners are VMs; the web
  hosts otherwise enforce container DNS settings at design time and in WebApplicationFactory tests); the temp folder is a host
  directory mounted at the same path (`TMPDIR=/tmp/hrm-ci-local`) because Testcontainers bind-mounts files from it.
- **S-9** Run arguments reach the container through environment variables, not `bash -c` text: Windows PowerShell 5.1 mangles
  embedded double quotes in native arguments. The entry script re-executes from a private copy (bash reads scripts while running).
- Steps that were `shell: powershell` run the same command from bash (the `powershell`→`pwsh` link exists on Linux).

**Tests:** `CiStepsParityTests` 5/5 (names/order, no inline `run:`, `always` ↔ `if: always()`, well-formed, every `Category!=`
filter registered in `tests/ci-exclusions.json`); a mutation (an inline `run:`) turns it red. `actionlint` 1.7.7 (in the image):
all workflows clean. Linux baseline: 87 steps, 78 passed, 9 failed — see `reports/plan34-baseline.md` §0.5.

## Pending owner decisions (collected; work that does not depend on them continues)

| # | Finding | Why it needs a decision | Proposed |
|---|---------|------------------------|----------|
| Q-1 | `FileUploadHelper.SanitizeFileName` (`src/Shared/HRManagement.FileStorage/FileUploadHelper.cs:55`) uses `Path.GetFileName`, which on Linux does not split `C:akepathcv.pdf`; `Hcm030CandidateDocumentTests.Long_file_names_…` is red on Linux only. Production runs Linux containers, so uploads keep client paths in stored names | product change outside P-4 | `Path.GetFileName(fileName.Replace('\', '/'))` + a unit test per upload service path |

## Test results (baseline)

See `reports/plan34-baseline.md`: unit 16 failed / 4141; integration (non-TC) 24 failed / 258; Testcontainers 10 failed / 82.
Raw logs (git-ignored): `artifacts/plan34/{build,build-warnaserror,unit,int,tc}.log` and `artifacts/plan34/baseline-*/*.trx`.

## Open issues

- Extra integration failures not in the plan: `InternalHireClosureIntegrationTests`, `RecruitmentHireClosureIntegrationTests` (event count off by one) → root-cause in P5.
- `FileStorageInboxIdempotencyTests` needs MinIO → `Category=ObjectStorage` in P7.
- Ten compiler/analyzer warnings in product code will break `-warnaserror` (P6): CS0108, CS8321, CS8604 ×2, CS9113, CS9124, MVC1004 ×2.
- AC-QA-34b expects the nearest obligation to be STR-002 2027-01-31, but the plan also registers the ObjectStorage exclusion at +45 days (2026-12-14), which is nearer → the acceptance test asserts "nearest open obligation printed", recorded as a deviation.
- Recorded, not fixed (product, outside P-4): `LicenseValidationMiddleware.cs:16-24` parses `Deployment:LicenseExpiresUtc` with the thread culture (inert while the setting is empty); `scripts/hard001-compliance-scan.ps1:85` would become a culture-sensitive time bomb once `docs/hardening/hard001-exceptions.json` exists; `docs/ops007/component-inventory.json` lists stale EOL data.
- `git checkout -- testdata/*/out` does not reach nested outputs (`testdata/rel002/payroll-parallel/out`, `testdata/rel003/game-day/out`); restore those paths too before each commit.

## Next step

P3: run the unit suite on Windows with the P3 edits (Syr001, Ten002, San001 `-GlobalJsonSource`, the seven scripts via
`RepoScriptRunner`, `ScriptInvocationArchitectureTests`, `TestCulture` ×3 + guards), investigate `Hcm029` (D-QA-14), commit P3.
P4 (twelve actions + `Plan034ScopedIndex*`) and P5 (fakes, `ContractsTestServices`, hosts, `IntegrationHostValidationTests`,
stale HCM-032 counts) are already drafted in the working tree — run and commit them in order after P3.
