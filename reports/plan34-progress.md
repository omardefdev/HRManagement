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
| P2 single CI step list, Linux runner | done | `c7a675c` | 2026-10-30T00:19:43.9485782Z |
| P3 red unit tests, test culture | done | `c6086f2` | 2026-10-30T00:30:35.6291998Z |
| P4 twelve `Index(employeeId)` actions | done | `8caddff` | 2026-10-30T00:35:23.0483667Z |
| P5 integration test hosts | done | `eed6128` | 2026-10-30T00:50:33.7789136Z |
| P6 NU1903, warnings as errors | done | `e422f92` | 2026-10-30T08:51:37.1598965Z |
| P7 MinIO, format, EF, coverage | done | `9130e36` (whitespace), `35c2959` | 2026-10-30T08:55:16.5069583Z |
| P8 full green run, AC-QA-34a..i | done (34g Blocked) | `a748dce`, `4115e9d` (evidence HEAD), P8 evidence commit | 2026-10-30T11:30:47.8369785Z |
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

## P3 — Finished (UTC): 2026-10-30T00:30:35.6291998Z

**Files:** `Syr001ProductionBindingTests` (SQLite `HRCoreDbContext`, `ValidateOnBuild`/`ValidateScopes`, resolve from a scope);
`BenefitPlanSeeder.cs`, `AcceptanceCandidateExperiencePatch.cs` (`IgnoreTenantFilterForSystem(SeedInitialization)`, P-4);
`scripts/san001-validate.ps1` (`-GlobalJsonSource Committed|WorkingTree`, default Committed = `git show HEAD:global.json`, file
fallback without git) + `San001SanctionsTests`; the seven `*_validate_script_passes` via `RepoScriptRunner`;
`Architecture/ScriptInvocationArchitectureTests` (+ self-check); `TestCulture.cs` (`[ModuleInitializer]`, ar-SY) in Unit,
Integration and Acceptance tests, `TestCultureGuardTests` in Unit and Integration, `TestCultureInitializerPresenceTests`;
`AcceptanceMessagingHelper.cs` (`-ExecutionPolicy Bypass`, found by the new guard); `Hcm029CandidateAcceptedV4Tests`.

**D-QA-14 resolved, no quarantine:** the `Hcm029…External_hire_publishes_v4_with_the_offer_terms` flake reproduced in the first P3
full run (failed after 22 s at `RoundTripAsync` `:137`, `Consumed.Any` = false). Root cause: the MassTransit test harness answers
"not consumed" once the bus has been idle for its inactivity window, and in the full unit run the saturated thread pool can
delay the in-memory delivery past that window. Fix (test-only): `TestInactivityTimeout = 30 s`, `TestTimeout = 90 s` on that
harness; the assertion is unchanged. Verified: 3 consecutive full unit runs, `Hcm029` green each time. `tests/quarantine.json`
stays empty.

**Tests (Windows/ar-SA, full unit suite ×3):** 4177 total, **1 failed** each run: `Est004EstablishmentPlanningTests.Csv_import_rejects_invalid_rows`
— a product defect exposed by the ar-SY test culture (Q-2 below), left red. All 16 baseline failures are green on Windows
(`BolaArchitectureTests` turns green with the P4 controller changes in the working tree; it is committed in P4).
`TestCultureGuardTests` (Integration) 2/2.

## P4 — Finished (UTC): 2026-10-30T00:35:23.0483667Z

**Product (P-4):** `EmployeeOrgScopeGuard.EmployeeIdsInScope` (snapshot employees whose primary org unit is in a restricted
scope; denied → none; throws if called for an unrestricted scope); `Payroll.Web/Services/PayrollEmployeeScope.cs`
(`ForbidIfEmployeeOutOfScopeAsync` and `EmployeeIdsInScope` keyed on the active pay profile's `OrgUnitId`). The twelve
`Index` actions: HR.Contracts `SeparationRecords`, `ProbationRecords`, `CommencementRecords` (now inject `ContractsDbContext`),
`ServiceTransactions` (injects `ContractsDbContext`), `Contracts`, `HireWizard`; Payroll `PaymentHolds`, `EndOfService`,
`AllowanceRecords`, `DeductionRecords`, `PayrollAdjustments`, `FraudAlerts`. Pattern: resolve scope → 403 if denied → 403 if
`employeeId` is outside (fail-closed when unplaced) → filter the list to in-scope employees when `MustFilter`; unrestricted
users unchanged. Details/exports/POSTs untouched (PLAN-52).

**Tests:** `Plan034ScopedIndexTests` (SQLite, real controllers + MediatR handlers + repository): 6 cases × 12 actions = 72/72
(own employee → rows; BR2 employee → 403; unplaced employee → 403; unfiltered → BR1 only; central → all, and filtering by a BR2
employee still works; invalid claim → 403). Mutation: the pre-PLAN-34 `PaymentHoldsController` fails 4 of its 6 cases.
`Plan034ScopedIndexPostgreSqlTests` (Testcontainers, migrated schemas): Contracts + PaymentHolds 2/2.
`BolaArchitectureTests` green with no allow-list.

**Recorded for PLAN-52 (not fixed here):** `LeaveBalancesController.Index` applies the list scope filter only in the `else`
branch of `if (leaveType.HasValue)` — with a `leaveType` filter, a branch user lists every employee's balances.

## P5 — Finished (UTC): 2026-10-30T00:50:33.7789136Z

**Files (test-only, plan §5 rule 1):** `TestSupport/Integration/FakeHrCoreActiveRegimeClient.cs` (law50 default; `NoRegime`,
`Unavailable`), `FakeHrCoreServiceRegimeClient.cs` (nobody blocked by default; configurable ids);
`IntegrationTests/Support/ContractsTestServices.cs` (`AddContractsApplicationForTests`: deterministic clients, repository,
MediatR, lifecycle gate, active-regime fake — TryAdd so host registrations win) used by `ContractLifecycle`, `Fun002`,
`Int003`, `EndToEndReleaseWorkflowSmoke`, `InboundEventConsumer`, `ContractSeparationChain`; `PayrollIntegrationTestSetup`
(`TryAddScoped<IHrCoreServiceRegimeClient, FakeHrCoreServiceRegimeClient>`, scope moved from PLAN-59);
`PayrollPostgresTestSupport` (TenantIdStamp + NpgsqlTenantSession interceptors, as Payroll.Web); `EstablishmentHireIntegrationTests`
(`AddHrEstablishmentTemporal`); `SoDForgedApprover`/`SoDPolicy` (fake instead of `Mock.Of`); `Hcm028` (invariant date);
`InternalChannelIntegrationTests`/`RecruitmentHireClosureIntegrationTests` (exact message-type list instead of a stale count);
`IntegrationHostValidationTests` (new); `Fun002` +3 AC-QA-34e cases. `ValidateScopes`+`ValidateOnBuild` on the hosts touched.

**Root causes found beyond the plan:**
- Hire-closure counts: HCM-032 (PLAN-32) added `IRecruitmentRequisitionClosed` when the last vacancy is filled; the unit test
  was updated then, these two integration tests were not. They now assert the exact ordered list of message types.
- Payroll on PostgreSQL (5 tests, root-caused by a subagent, change reviewed): the PG host registers a real `ITenantContext`,
  which turns the TEN-002 query filter on, but had no `TenantIdStampInterceptor`, so seeded rows were saved with
  `TenantId = Guid.Empty` and every read was filtered out ("التشغيل غير موجود." / "Sequence contains no elements"). The
  regime client was not their cause. (Note for later: `Qa003OutboxInboxRealInfraTests` and `RabbitDownOutboxTests` use the
  same pattern; both pass/skip today.)
- `IHrCoreServiceRegimeClient` is resolved with `GetService` (optional), so `ValidateOnBuild` cannot catch its absence;
  `IntegrationHostValidationTests` checks required services by name (negative cases for all three hosts).

**Tests (Windows/ar-SA):** integration `Category!=Testcontainers` **337 passed, 0 failed**, 4 skipped (opt-in scale/live tests:
Hcm031/Hcm032 scale, RabbitDownOutbox, FileScanLiveE2E). Testcontainers **80 passed, 4 failed** (`Qa003MinioFileScanTests` ×3,
`FileStorageInboxIdempotencyTests` — MinIO image, P7). All 24 baseline non-TC failures and the 5 payroll PG + `Hcm028` TC
failures are green.

**Product defects recorded (not fixed, plan §5 rule 2):**
- CON: `PublishSeparationHandler` needs HR.Core live; with HR.Core down the publish throws `HttpRequestException`
  (pinned by `Fun002…Publish_with_hr_core_unavailable_fails_without_an_event`), `HrCoreActiveRegimeClient.cs:32-33`.
- MOV: `TransferRequestsController.cs:453-454` calls `regimes.FirstOrDefault()` on the client result and throws when HR.Core
  is down or returns null.

## Owner decisions received (2026-10-30, in chat)

All four product fixes approved (Q-1, Q-2, Q-4, Q-5) and Q-3 "fix + suppress MVC1004" (recommended option).

## P6 — Finished (UTC): 2026-10-30T08:51:37.1598965Z

- `Testcontainers.PostgreSql`/`RabbitMq` 4.13.0 → **4.16.0**, `Testcontainers.Minio` → **4.15.0** (its latest); the core resolves to
  4.16.0, which depends on **SSH.NET 2026.0.0** — the first patched version for GHSA-mggc-4xg6-vcxf (< 2026.0.0) and
  GHSA-q939-rpr3-3284 (<= 2025.1.0). No direct SSH.NET pin, no `supply-chain-exceptions.json` row.
- Lock files regenerated with `dotnet restore --force-evaluate` **inside the ci-local image (SDK 10.0.400)**, not with the local
  10.0.403: 16 files change in content (IntegrationTests: Testcontainers/SSH.NET; 14 Domain projects + FileAdapters: the
  transitive `System.Memory` entry pruned — the same result with either SDK, the old locks predate .NET 10 pruning).
- `Directory.Build.props`: `WarningsAsErrors` += NU1903;NU1904. Build step in `steps.json`: `-warnaserror`.
- Q-3 (owner decision): CS0108 `new` on `FileReferenceRecord.CreatedAt`; CS8321 unused local function removed
  (`Competitions/Details.cshtml`); CS8604 `where T : notnull` on `ReadEmployeeId`/`ReadEntityId`; CS9113 unread
  `regimeValidator` dropped from `ServiceRegimeExceptionService` (+ 8 call sites; recorded as a possible missing validation);
  MVC1004 suppressed at `AssetsController.CreateLegacy`/`Edit` with a reason (renaming changes binding; recorded). Test code:
  CS9124 in `FileReferenceTenantPipelineFixture`.
- Verified: `dotnet list package --vulnerable --include-transitive` → no vulnerable package in any of the 59 projects;
  `dotnet build HRManagement.slnx -c Release -warnaserror` → **0 errors**.

## P7 — Finished (UTC): 2026-10-30T08:55:16.5069583Z

**Compose (D-QA-7):** `docker-compose.yml`: `minio` behind `profiles: ["object-storage"]` with `image: ${MINIO_IMAGE:-}` —
not `:?`, because Compose interpolates every service whatever its profile (a `:?` there breaks the base stack); with the
profile on and no image, `docker compose config` fails ("service \"minio\" has neither an image nor a build context").
`hr-core-web` and `file-storage-web` use `FileStorage__Provider: ${FILE_STORAGE_PROVIDER:-Local}` (file-storage on volume
`file_storage_data:/var/hrm/file-storage`, hr-core under `/tmp/hrm-file-storage`) and no longer `depends_on: minio` (the plan named
only file-storage-web; hr-core-web had the same dependency). New `docker-compose.object-storage.yml`: enables `minio`
(`profiles: !reset []`), requires `MINIO_IMAGE` with an explicit `:?` message, switches both services to Minio.
Verified with `docker compose config`: base OK; `--profile object-storage` without image → fails; overlay without image →
fails with the explicit message; the acceptance-job file set still resolves.

**Testcontainers:** `RealInfra.MinioImage` comes from `HRM_MINIO_IMAGE` (clear exception when unset);
`Qa003MinioFileScanTests` carries `Category=ObjectStorage`; the Testcontainers step filters `Category!=ObjectStorage`;
`tests/ci-exclusions.json` entry (owner PLAN-38, reviewBy 2026-12-14 = +45 days) — a dated obligation.
**Correction to the baseline:** `FileStorageInboxIdempotencyTests` was not a MinIO failure but the INT002 migration defect (Q-4).

**Steps that never ran:**
- `dotnet format`: whitespace commit `9130e36` (115 files, token-identical except UTF-8 BOMs dropped per `charset = utf-8`);
  analyzer findings in `--verify-no-changes`: xUnit2031 in `Hcm029CandidateAcceptedV4Tests` (test fixed), MVC1004 (Q-3, P6).
  `dotnet format HRManagement.slnx --verify-no-changes --no-restore` → exit 0.
- EF: `check-pending-ef-model-changes.sh` → **"Pending model change gate passed (15 contexts)"** on Linux; no migration needed.
  The baseline's 4 failures were the container DNS guard. `check-migration-discovery.sh` → INT002 fixed (Q-4).
- Coverage: first measurement 90.1% on the gate assemblies (threshold 60%); `Payroll.Application` 73.6% < floor 75% → D-QA-13:
  floor 73, `temporaryFloors` entry (ratchetTo 75, reviewBy 2026-12-29), obligation `QA-COVERAGE-TEMPORARY-FLOORS`, ADR-QA-002 note.
  `globalThreshold` unchanged.

**Owner-approved product fixes:** Q-1 `FileUploadHelper.SanitizeFileName` splits on `\` too; Q-2 `FileFinanceBudgetPort`
parses invariant; Q-4 `[DbContext]`/`[Migration]` on `INT002_IntegrationMessageReceipts`; Q-5 `aria-label`/`title` on the
OfferLetters clear-filter link. Regression tests: `Plan034CultureAndPathDefectTests` (5 path cases, ar-SY CSV numbers);
`Hcm030…Long_file_names…`, `Est004…Csv_import_rejects_invalid_rows`, `FileStorageInboxIdempotencyTests` green.

**ci-local fixes found while running:** `--step` may repeat (and `-Step a,b` in ci-local.ps1, since `-File` cannot pass arrays);
the host's `core.autocrlf` is passed in (Linux git otherwise reports CRLF files of the Windows checkout as modified);
Testcontainers host override pinned to the IPv4 address of `host.docker.internal` (its IPv6 address is unreachable from the
container: `CrossTenantRls…`/`Qa003MigrationUpgrade…` timed out / "Network is unreachable").

**Tests (Windows/ar-SA):** unit **4183/0 failed**; integration non-TC **337 passed, 0 failed** (4 opt-in skips); Testcontainers
minus ObjectStorage **81 passed, 0 failed**. `dotnet build -c Release -warnaserror` 0 errors.

## P8 — Finished (UTC): 2026-10-30T11:30:47.8369785Z

**Evidence HEAD `4115e9d`** (the evidence commit after it adds only the AC-QA-34 test fixes for 34f/34g/34i and the evidence files).

- **Linux** `powershell -File scripts/ci-local.ps1 -SkipTestcontainers -Out artifacts/ci-local/head` (clean clone of HEAD, SDK 10.0.400):
  **85 passed, 0 failed, 2 not-run** (Testcontainers, coverage gate — rule P-3), 820 s; `gitSha` = HEAD, `dirtyTracked` = [],
  host `knownLocalOverrides` = global.json (rollForward only); 142 tracked files rewritten by the steps themselves
  (`testdata/*/out`, `docs/qa` registries) are listed as `rewrittenByRun`. trx: unit 4183/0, integration 341/0.
- **Windows/ar-SA** (same HEAD, Release, coverage): `-warnaserror` build 0 errors; unit 4183/0; integration 337/0 (4 opt-in skips);
  Testcontainers minus ObjectStorage 81/0; coverage gate on these reports **passed** (90.1%; Payroll.Application 73.6% ≥ 73).
- **Rule P-3:** inside the ci-local container on Docker Desktop, Testcontainers works but PostgreSQL connections through the desktop
  port proxy time out intermittently (3 of 81 tests, different ones per run; the bridge gateway was tried and Ryuk is unreachable
  there). GitHub runners are unaffected.
- **Base stack without MinIO:** `prepare-ci-compose-env.sh` (in the image; it needs pwsh and `python`), `docker compose -f docker-compose.yml
  -f docker-compose.services.yml --profile full-stack up -d --wait --wait-timeout 600` → every container healthy, no MinIO container,
  `file-storage-web` `FileStorage__Provider=Local`, gateway `/health/live` 200. `--profile object-storage config` without MINIO_IMAGE →
  exit 1; the overlay without it → exit 1 with the explicit message. Stack torn down (`down -v`), `.env` restored.
- **Finding (34g upload half Blocked):** in the base CI realm, password-grant tokens carry **no realm roles** (no `roles` client
  scope; `profile`/`email` are rejected as invalid scopes), so `/api/files` returns 403 "missing role for this module" for every
  persona (hradmin, e2e-file-ops). The S2S route was not tried (it needs a service-account secret; refused by the session's
  permission policy). → PLAN-41 (the CI E2E/acceptance jobs use the same realm).
- **AC-QA-34 (RUN_ACCEPTANCE=true, ACCEPTANCE_REQUIRE_ALL=true):** 34a, 34b, 34c, 34d, 34e, 34f, 34h, 34i **Pass**; 34g **Blocked**
  (fails under REQUIRE_ALL, as designed). `acceptance-evidence/AC-QA-34/` (metadata, steps, before, after, audit, db-checks, timing,
  result + per-scenario steps/db-checks); `acceptance-evidence/summary.md` regenerated.
- Follow-up noted: the ci-local image has `python3` but no `python` alias (GitHub runners have both); only scripts outside
  build-and-test (prepare-keycloak-import.ps1) need it.

## Owner decisions (all answered 2026-10-30; see "Owner decisions received")

| # | Finding | Why it needs a decision | Proposed |
|---|---------|------------------------|----------|
| Q-1 | `FileUploadHelper.SanitizeFileName` (`src/Shared/HRManagement.FileStorage/FileUploadHelper.cs:55`) uses `Path.GetFileName`, which on Linux does not split `C:akepathcv.pdf`; `Hcm030CandidateDocumentTests.Long_file_names_…` is red on Linux only. Production runs Linux containers, so uploads keep client paths in stored names | product change outside P-4 | `Path.GetFileName(fileName.Replace('\', '/'))` + a unit test per upload service path |
| Q-2 | `FileFinanceBudgetPort.ParseCsvContent` (`HR.Core.Infrastructure/Services/Establishment/FileFinanceBudgetPort.cs:44-46`) parses CSV numbers with the thread culture. Under ar-SY (the production request culture) "-1" does not parse (minus sign is U+061C + "-"), becomes 0 and the invalid row is imported; "1000.50" does not parse either (decimal separator "٫"), so BudgetAmount becomes null. `Est004…Csv_import_rejects_invalid_rows` is red under the D-QA-11 test culture | product change outside P-4; overriding the culture in the test would hide the defect | parse with `CultureInfo.InvariantCulture` (CSV is a machine format) + a test for "-1" and "1000.50" under ar-SY |
| Q-3 | `dotnet build -warnaserror` (Release) fails on five product warnings (the sixth, CS9124, was test code and is fixed): CS0108 `FileReferenceRecord.CreatedAt`, CS8321 unused local function `T` in `Competitions/Details.cshtml`, CS8604 ×2 `PayrollPartitionInputLoader.cs:500,502`, CS9113 unread `regimeValidator` in `ServiceRegimeExceptionService`, MVC1004 ×2 `AssetsController.cs:210,294` (also fails `dotnet format --verify-no-changes`) | product changes outside P-4; renaming the MVC1004 parameter changes model binding | non-behavioural fixes for CS0108/CS8321/CS8604/CS9113 (CS9113 recorded as a possible missing validation); MVC1004 suppressed at the two actions with a reason, recorded for the logistics plan |
| Q-4 | `File.Storage.Infrastructure/Migrations/20260824180000_INT002_IntegrationMessageReceipts.cs` has no `[DbContext]`/`[Migration]` (no Designer file) while the model snapshot contains the table, so EF never creates `IntegrationMessageReceipts`; File.Storage inbox consumers fail on every migrated database (production included). Red: CI step "Check EF migration discoverability", `FileStorageInboxIdempotencyTests` | product (schema) change outside P-4; non-destructive (creates one table) | add the two attributes; EF then applies it on next migrate |
| Q-5 | UX-002-E: `HR.Core.Web/Views/OfferLetters/Index.cshtml:39` icon-only "clear filters" link without an accessible name (the Python check never ran on this machine) | product markup outside P-4; no behaviour change | `aria-label`/`title` = "مسح عوامل التصفية" |

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

P9: independent review (fresh subagent on the prompt, the plan and `git diff main...HEAD`) + refuter; fix survivors with a
regression test; finish `reports/plan34-final-report.md`; commit docs; merge into local `main` with --no-ff; no push.
