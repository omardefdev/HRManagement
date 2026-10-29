# Task: PLAN-34 — Make `main` pushable: one CI step list that runs the same locally (Linux container) and on GitHub, no time bombs, zero red tests, no High vulnerabilities with `-warnaserror`, a base stack that boots without the dead MinIO image (one autonomous session)

## How to run this task — read before anything else

This is a **single prompt for one autonomous session**. Run it as phases **P0–P9** in order.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan34-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan34-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase.

**Context discipline.**
- Use subagents for broad read-only searches.
- Use `git grep` rather than a recursive grep over `bin`/`obj`.
- Keep long test output in files under `artifacts/` and read only the failures.

**Orchestration rules** (ultracode / workflows may be on):
- Parallel agents are allowed for:
  - read-only exploration;
  - root-cause analysis of separate failing tests;
  - writing **separate new test files**;
  - independent review.
- Never let two agents edit the same file concurrently.
- These edits are made **serially by you, the lead**:
  - `.github/workflows/*`, `scripts/ci/*`
  - `Directory.Build.props`, `Directory.Packages.props`, `packages.lock.json`
  - `docker-compose*.yml`
  - `docs/str-governance/*`
  - `ProgramGovernanceGuard`/`LegalControlGuard` and their DI extensions
  - the twelve controllers of §4
- Test runs and Docker runs are serial.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write a `BLOCKED:` entry in the progress file, with the question and the options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET 10 / CI / DevOps engineer:
- GitHub Actions, Docker, PowerShell and bash;
- xUnit, Testcontainers, EF Core with PostgreSQL;
- ASP.NET Core authorization.

You work in the `HRManagement` microservices repository on the owner's Windows machine (PowerShell, Docker Desktop, machine culture **ar-SA**). This plan is the first of wave 1 (PLAN-34..65). Its job is to make the 140 local, never-pushed commits safe to push in PLAN-35.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-34-qa-pushable-baseline.md` — **the plan; it is the specification.** Follow its design tables, rules, acceptance scenarios and definition of done.
2. `docs/PRG_001_QUALITY_GAP_STUDY.md`: G-QA-02, 03, 04, 05, 12 (with their evidence), §5 decisions, and §7.
3. `docs/gap-study-verification/PRG_001_QUALITY_GAP_STUDY-verify.md`: what the independent check corrected.
4. `docs/PROGRAM_COMPLETION_ROADMAP.md` §3–§5: where this plan sits. PLAN-35 pushes; PLAN-52 does full branch scope; PLAN-36/38 own production MinIO; PLAN-59 owns AC-12.
5. `reports/plan25-baseline.md`, `reports/plan25-final-report.md` §7, `reports/plan33-baseline.md`: the "known failures" list this plan must eliminate.
6. Read fully before changing:
   - `.github/workflows/ci.yml`;
   - the STR-005 files: `docs/str-governance/governance-matrix.json`, `decision-log.json`, `scripts/str005-governance-runtime-validate.ps1`;
   - `ProgramGovernanceGuard.cs`, `ProgramGovernanceEvaluator.cs`, `LegalControlGuard.cs`, `LegalControlCatalogEvaluator.cs`;
   - `scripts/san001-validate.ps1`;
   - `src/Tests/HRManagement.TestSupport/RepoScriptRunner.cs`;
   - `BolaArchitectureTests.cs`;
   - `EmployeeOrgScopeGuard.cs`, `OrgScopeControllerExtensions.cs`;
   - `PayrollIntegrationTestSetup.cs`;
   - `TestFixtures.cs`;
   - `docker-compose.yml`, `docker-compose.services.yml`;
   - `Directory.Build.props`, `Directory.Packages.props`.

## Decisions for this implementation (record them in P0)

Adopt the plan's defaults in «قرارات مطلوبة قبل التنفيذ»:

| # | Decision |
|---|----------|
| D-QA-3 | **Close** both STR-005 simulation exceptions with a numbered decision in `decision-log.json`; **never extend a date**. `EX-STR005-SIM-INCIDENT-WINDOW` expired on 2026-09-15, and `EX-STR005-SIM-LAW-INTERIM` **expires today, 2026-10-30**. |
| D-QA-10 | Dated obligations warn at ≤ 30 days and fail at ≤ 7 days or when expired. |
| D-QA-5 | Restrict the **12** `Index(employeeId)` actions for real. No freeze list. |
| D-QA-7 | File.Storage uses the `Local` provider in dev and CI. MinIO goes behind profile `object-storage` with `MINIO_IMAGE`. The Testcontainers category `ObjectStorage` is excluded, as a dated obligation owned by PLAN-38. |
| D-QA-11 | Test culture is `ar-SY` (Gregorian), set by a `[ModuleInitializer]` in every test project. |
| D-QA-12 | **`global.json` is never committed.** The committed version stays `10.0.400`, `rollForward: disable`. The owner's local `latestPatch` edit stays local. SAN-001 reads the committed version (`git show HEAD:global.json`). |
| D-QA-13 | If the coverage gate fails at first measurement: set that assembly's threshold to the measured value rounded down, add an ADR note, and register a 60-day dated obligation. Never lower `globalThreshold`. |
| D-QA-14 | Flaky `Hcm029CandidateAcceptedV4Tests…`: half a day of investigation. If unresolved, mark it `Category=Flaky`, add it to `tests/quarantine.json` with a 30-day review as a dated obligation, and run it non-blocking. |

Prompt-level decisions:

| # | Decision |
|---|----------|
| P-1 | **The program documents are committed first, in P0**, as their own docs commit on this branch. They were written by the planning session on 2026-10-30 and are currently untracked: the 14 `docs/*_GAP_STUDY.md` (not `HCM_001`), `docs/PROGRAM_COMPLETION_ROADMAP.md`, `docs/OWNER_DECISIONS_REGISTER.md`, `docs/gap-study-verification/*`, `docs/acceptance-plans/PLAN-34..65-*.md`, and this prompt. |
| P-2 | **No push.** Pushing is PLAN-35, done by the owner after deciding repository visibility (the GitHub repo is currently **public**). At the end, merge into local `main` with `--no-ff`, as PLAN-25..33 did. |
| P-3 | **Linux parity is the bar.** "Green" means `pwsh scripts/ci-local.ps1` is green on a clean HEAD **and** the Windows/ar-SA unit and integration runs are green. If Testcontainers cannot reach sibling containers from inside the ci-local container on Docker Desktop, use `-SkipTestcontainers`: it prints the step as explicitly «not run». Then run the Testcontainers tests natively on Windows, and record both. |
| P-4 | **Product code changes are minimal and limited to:** the twelve controllers and the two scope helpers (§4); the two seeding bypasses (`Ten002`); the governance guards (clock and date parsing); the dependency bump. Everything else is test code, scripts, CI and docs. Product defects found along the way are **recorded, not fixed**: CON/MOV per §5 rule 2, TRN certificate culture → wave 2. |

## Scope boundaries

- **Out of scope** (from the plan):
  - push, remote CI, branch protection, CodeQL → PLAN-35;
  - full Production container boot and production MinIO → PLAN-36/38;
  - branch scope on Details/exports/POSTs → PLAN-52;
  - the AC-12 rewrite and payroll blocked-path → PLAN-59;
  - the CI acceptance job → PLAN-41;
  - composition-root tests → wave 2.
- **Never commit:**
  - `global.json` — check `git diff --cached --name-only` before **every** commit;
  - `tmp-vendors.html`;
  - `scripts/__pycache__/*`;
  - `docs/thesis_diagrams/source/.$*`;
  - anything under `artifacts/`, which is git-ignored. This includes `artifacts/claude-study/`, a leftover from the planning session: you may delete it;
  - test-rewritten `testdata/*/out/*.json` — run `git checkout -- testdata/*/out` before committing, never a blanket `git checkout -- testdata`, because PLAN-25 lost a persona entry that way;
  - secrets or credentials.
- **Do not change** acceptance personas, the Keycloak realm or seeds in this plan.

## Repository facts (verified 2026-10-30 — re-verify in P0)

- `main` = `f9eab1a` (PLAN-25 merge). It is ~140 commits ahead of `origin/main`, and remote CI has never run on them.
- Working tree:
  - `M global.json` (owner's local edit);
  - `?? tmp-vendors.html`;
  - the untracked program documents listed in P-1.
- **STR-005:**
  - `governance-matrix.json:146-157` holds two `Declared` simulation exceptions, one expired and one expiring today.
  - `str005-governance-runtime-validate.ps1` compares them with `UtcNow`. Its CI step (`ci.yml:71-72`) runs **before Restore**.
  - `ProgramGovernanceStartupValidator` throws at startup with `EnforceOnStartup=true` in Gateway, HR.Core and Payroll for Production and Staging.
  - `ProgramGovernanceEvaluator.cs:42` parses dates with the thread culture, so it fails on ar-SA.
  - The next bombs are `nextDue: 2027-01-31` (`docs/str-legal/legislative-watch-stamp.json`) and six `expiresOn: 2027-01-31` entries (`docs/str-legal/control-catalog.json`).
- **Red tests on main:**
  - Unit: `Syr001ProductionBindingTests`, `Ten002ArchitecturalTests`, `Str005*` ×4, `San001` (reads the working-tree `global.json`), seven `*_validate_script_passes` (no `-ExecutionPolicy Bypass`), `MissingModulesRulesTests` (Hijri calendar on ar-SA), `BolaArchitectureTests` (12 actions), and the flaky `Hcm029`.
  - Integration:
    - 8 separation and contract tests: `IHrCoreActiveRegimeClient` is not registered.
    - 10 SQLite and 4 PostgreSQL payroll tests: `IHrCoreServiceRegimeClient` is not registered in `PayrollIntegrationTestSetup`.
    - `EstablishmentHireIntegrationTests`: `IFundedPositionVersionService` is not registered.
    - 2 SoD tests: `Mock.Of` returns a null list.
    - `Hcm028`: date formatted with the thread culture.

  The plan's §3/§5 tables give every file:line.
- **NU1903:** Testcontainers 4.13.0 pulls in SSH.NET 2025.1.0, which carries two advisories. There is no `TreatWarningsAsErrors` anywhere.
- **MinIO:** the pinned image `minio/minio:RELEASE.2025-04-22T22-12-26Z` can no longer be pulled, and `file-storage-web` depends on it in `docker-compose.services.yml`.
- **Never run:**
  - `dotnet format --verify-no-changes` (35 files lack a final newline);
  - the EF pending-model check for the 15 contexts;
  - the coverage gate.

## Stop (record `BLOCKED:` and end the turn) if

- The EF pending-model check shows a change that **drops a column or changes a type**, or any behavioural schema change (plan §7 step 2).
- No SSH.NET version fixes the advisories **and** no Testcontainers 4.x avoids it. An exception entry needs the owner's decision (plan §6 rule 5).
- Making a test green would require changing **product behaviour** outside P-4. Record it and ask; never weaken an assertion to pass.
- Green CI would require committing `global.json`, extending an expiry date, lowering `globalThreshold`, or adding an unowned exclusion.

---

## P0 — Branch, program docs, baseline, inventory

1. Set up the branch:
   - `git status`: confirm the facts above;
   - `git switch -c feature/plan-34-qa-pushable-baseline` from `main`.
2. **Commit the program documents (P-1)** — only those paths, checking `git diff --cached --name-only` first.
   Commit: `docs(program): gap studies for all remaining areas, independent verification, completion roadmap, owner decisions register, wave-1 plans PLAN-34..65`.
3. Baseline on Windows (plan 0.2–0.4):
   - record `git show HEAD:global.json`;
   - `dotnet build HRManagement.slnx`, then `-warnaserror` (record NU1903);
   - full unit tests;
   - integration tests with `Category!=Testcontainers`;
   - the Testcontainers categories, if Docker is up.

   Record every failure in `reports/plan34-baseline.md` and compare it with the plan's §3/§5 tables. Any **extra** failure is listed with a one-line hypothesis.
4. **Dated-obligations inventory** (plan 0.6): every JSON or config with `expiresOn|nextDue|reviewBy|renewBy|expiryDate|validUntil|validTo`, using `git grep`.
5. Create `reports/plan34-progress.md`.
   Commit: `chore(qa034): baseline and dated-obligations inventory`.

## P1 — Time bombs: dated obligations, injectable clock, STR-005 closure (plan §2)

Do this first: the second exception expires today.

- `docs/qa/dated-obligations.json` (with `obligations[]` and `historical[]`) and `scripts/qa/dated-obligations-check.ps1`:
  - `-AsOfDate`, `-WarnDays 30`, `-FailDays 7`, `-Registry`;
  - parse dates as invariant `yyyy-MM-dd`;
  - `::warning::` / `::error::`;
  - write `artifacts/dated-obligations.json`.
- Add the test registries under `testdata/qa/dated-obligations/`: 31, 30 and 7 days; expired; `Closed`; and the bad date `2026-13-01`.
- **Close both STR-005 simulation exceptions (D-QA-3):** status `Closed` and closure fields, plus a numbered entry in `decision-log.json`. Update `docs/STR_005_EXCEPTION_PATHS.md` and `docs/STR_005_SIMULATION_PACK.md`, including the note that renewing requires an image rebuild or `ProgramGovernance:MatrixPath`.
- Make the clock and date parsing testable:
  - inject `TimeProvider` into `ProgramGovernanceGuard` and `LegalControlGuard` (`TryAddSingleton(TimeProvider.System)`);
  - parse dates invariantly in both evaluators and in the `str005-*`/`str002-*` scripts;
  - add `TestSupport/Time/FixedTimeProvider.cs`.
- Tests:
  - `DatedObligationsTests`: discovery (any new dated file must be registered) and the script logic through `RepoScriptRunner`;
  - `Str005ProductionBindingTests` and `Str005GovernancePackTests` on a fixed clock;
  - `RuntimeGovernanceDocumentsTests`, the canary: the real documents with Production options start **today**.

Commit: `fix(qa034): dated-obligations registry and horizon check, injectable governance clock, invariant date parsing, STR-005 simulation exceptions closed by decision`.

## P2 — One CI step list and a local Linux runner (plan §1)

- Move every `build-and-test` step of `ci.yml` (`:20-323`) into `scripts/ci/steps.json`, keeping their names and order. Insert «Dated obligations horizon (QA-034)» **before** the STR/MIG steps.
- `scripts/ci/run-steps.sh`:
  - `--all`, `--step`, `--continue-on-error`;
  - writes `artifacts/ci-local/summary.json` with `gitSha`, `dirtyTracked`, `knownLocalOverrides`, per-step `exit`/`durationSec`, and trx counts.
- `docker/ci-local/Dockerfile`:
  - SDK `10.0.400` exactly, `python3`, `docker-ce-cli`, pinned `actionlint`, and `powershell`→`pwsh`;
  - pinned in `docker/base-image-pins.json`.
- `scripts/ci-local.ps1`:
  - `-Step`, `-ContinueOnError`, `-Out`, `-SkipTestcontainers`;
  - mounts the repo, the docker socket and the NuGet cache;
  - sets `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`.
- Rewrite `ci.yml`: each `build-and-test` step runs `bash scripts/ci/run-steps.sh --step "<name>"`, except checkout, setup and upload.
- Add `.github/workflows/dated-obligations.yml`: weekly plus manual.
- `CiStepsParityTests`: names and order match, and no other inline `run:` exists in `build-and-test`.
- Run the baseline on Linux: `pwsh scripts/ci-local.ps1 -ContinueOnError -Out artifacts/ci-local/baseline`. Record its result in the baseline report as plan step 0.5.
- If `actionlint` is available, lint the workflows.

Commit: `ci(qa034): single source of CI steps, local Linux runner matching GitHub, parity guard, weekly dated-obligations workflow`.

## P3 — Red unit tests and test culture (plan §3)

- **`Syr001`:** register `HRCoreDbContext` (SQLite) and use `ValidateOnBuild`/`ValidateScopes`.
- **`Ten002`:** in `BenefitPlanSeeder.cs:26` and `AcceptanceCandidateExperiencePatch.cs:42`, replace `IgnoreQueryFilters()` with `IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)`.
- **`San001`:**
  - `san001-validate.ps1 -GlobalJsonSource Committed|WorkingTree`; the default `Committed` reads `git show HEAD:global.json` and falls back to the file when there is no git;
  - the test passes on the owner's machine **with** the local `global.json` edit present.
- **The seven `*_validate_script_passes`:** route them through `RepoScriptRunner`, and add `ScriptInvocationArchitectureTests`.
- **Culture:**
  - `TestCulture.cs` (`[ModuleInitializer]`, `ar-SY`) in UnitTests, IntegrationTests and AcceptanceTests;
  - `TestCultureGuardTests`;
  - `MissingModulesRulesTests` becomes green through the culture; the product fix is recorded as TRN, wave 2.
- **`Hcm029` flaky:** apply D-QA-14.

Commit: `test(qa034): deterministic unit baseline — DI, tenant bypass, committed global.json, script runner, ar-SY test culture`.

## P4 — The twelve `Index(employeeId)` actions (plan §4)

- **Shared helper:** `EmployeeOrgScopeGuard.EmployeeIdsInScope`.
- **Payroll helper:** `Payroll.Web/Services/PayrollEmployeeScope.cs`. Payroll has no employee snapshots, so it uses the active `EmployeePayProfiles.OrgUnitId`.
- **The twelve actions** (6 in HR.Contracts, 6 in Payroll) follow the plan's pattern and rules 1–6:
  - denied scope → 403;
  - out-of-scope `employeeId` → 403;
  - an unfiltered list contains in-scope employees only;
  - unrestricted users are unchanged;
  - a missing snapshot or profile is out of scope (fail-closed);
  - no change to Details, exports or POSTs.
- **Tests:**
  - `Plan034ScopedIndexTests` (SQLite; 4 cases × 12 actions);
  - `Plan034ScopedIndexPostgreSqlTests` (`Category=Testcontainers`; Contracts and PaymentHolds);
  - `BolaArchitectureTests` green **without** any allow-list.

Commit: `fix(qa034): branch scope on the twelve Index(employeeId) actions in HR.Contracts and Payroll`.

## P5 — Integration test hosts (plan §5)

- Add `TestSupport/Integration/FakeHrCoreActiveRegimeClient` (default `law50`; modes `NoRegime` and `Unavailable`) and `FakeHrCoreServiceRegimeClient` (not blocked by default; blocked ids configurable).
- Add `Support/ContractsTestServices.AddContractsApplicationForTests` and use it in the four hosts.
- **Payroll host:** `TryAddScoped<IHrCoreServiceRegimeClient, FakeHrCoreServiceRegimeClient>()`. This is the scope moved from PLAN-59.
- **Establishment host:** `AddHrEstablishmentTemporal()`.
- **SoD tests:** use the fake instead of `Mock.Of`.
- **`Hcm028`:** use an invariant date format.
- **Every shared host:** `ValidateScopes` + `ValidateOnBuild`. Add `IntegrationHostValidationTests`.
- **Test-only changes**, per plan rule 1. Record the CON/MOV product defects in the progress file.

Commit: `test(qa034): integration hosts register the HR.Core regime clients and temporal services; host validation`.

## P6 — NU1903 and warnings as errors (plan §6)

- Upgrade Testcontainers to the newest 4.x without the vulnerable SSH.NET. Otherwise pin SSH.NET directly, with a comment.
- Run `dotnet restore --force-evaluate` and commit the changed lock files.
- `Directory.Build.props`: `WarningsAsErrors` for NU1903/NU1904. The build step in `steps.json` uses `-warnaserror`.
- Check: `dotnet list package --vulnerable --include-transitive` shows no High/Critical.
- Run the full Testcontainers suite after the bump.

Commit: `build(qa034): fix NU1903 (SSH.NET via Testcontainers), warnings-as-errors for vulnerability warnings`.

## P7 — MinIO and the steps that never ran (plan §7)

- **Compose:**
  - `docker-compose.yml`: `minio` goes behind `profiles: ["object-storage"]` with `image: ${MINIO_IMAGE:?...}`;
  - `docker-compose.services.yml`: File.Storage uses `FILE_STORAGE_PROVIDER` (default `Local`), with a local root volume and no `depends_on: minio`;
  - new `docker-compose.object-storage.yml`.
- **Testcontainers:** take the image from `HRM_MINIO_IMAGE`. `Qa003MinioFileScanTests` gets `Category=ObjectStorage`, excluded in `steps.json` and registered as a dated obligation owned by PLAN-38.
- **`dotnet format`:**
  - run `dotnet format HRManagement.slnx whitespace` in its **own commit**: `style(qa034): whitespace only (final newlines)`;
  - then `--verify-no-changes` must pass.
- **EF:** run `bash scripts/check-pending-ef-model-changes.sh` (15 contexts). Non-destructive drift gets `PLAN034_SyncPendingModel`; destructive or behavioural drift → **Stop**.
- **Coverage:** run `bash scripts/check-coverage.sh`. If it fails, apply D-QA-13.
- **Also run:** `verify-sca-fixture.sh`, `bld002-docker-build-validate.ps1`, and the image builds.

Commit: `fix(qa034): base stack without the unavailable MinIO image, format/EF/coverage steps green for the first time`.

## P8 — Full green run and acceptance AC-QA-34a..i

1. **Clean tree:** run `git checkout -- testdata/*/out`, then check `git status --short`. The only tracked change left must be `global.json`. Commit or revert anything else first.
2. **Linux:** `pwsh scripts/ci-local.ps1` on the clean HEAD must be all green. `summary.json` has `gitSha` = HEAD and an empty `dirtyTracked`; `global.json` appears only in `knownLocalOverrides`.
3. **Windows/ar-SA:** the full unit and integration runs (including Testcontainers, except `ObjectStorage`) have **0 failures**. List the tests that were red at baseline and are now green.
4. **Base stack without MinIO:**
   - `bash scripts/prepare-ci-compose-env.sh`;
   - `docker compose -f docker-compose.yml -f docker-compose.services.yml --profile full-stack up -d --wait --wait-timeout 600`;
   - upload: a clean file → `Clean`, EICAR → quarantined;
   - `--profile object-storage` without `MINIO_IMAGE` → `docker compose config` fails with the clear message.
5. **Acceptance:** `src/Tests/HRManagement.AcceptanceTests/AcQa34PushableBaselineTests.cs`, with `[RequiredAcceptance]`, `AcceptanceGate` and `EvidenceRecorder("AC-QA-34", …)`.
   - Run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`, filter `AcQa34`.
   - Every scenario 34a–i, positive and negative, as in the plan's table.
   - The folder `acceptance-evidence/AC-QA-34/` gets its eight items. Regenerate `acceptance-evidence/summary.md`.
6. **Failures:**
   - a test defect → fix the test;
   - a defect inside this plan's scope → fix it, with a test;
   - anything else → record it as `Fail`/`Blocked` with evidence.

   Never hide a failure.

Commit: `test(qa034): full local CI green on Linux and Windows, AC-QA-34a..i live evidence`.

## P9 — Docs, independent review, final report, merge

**Docs:**
- `docs/CI_AND_TESTING.md`: `ci-local`, `steps.json` as the single source, dated obligations, test culture, `ObjectStorage`, `-SkipTestcontainers`.
- STR-005 docs; `ADR-QA-002` if D-QA-13 applied.
- In `docs/PRG_001_QUALITY_GAP_STUDY.md`, update the «الحالة» lines for G-QA-02/03/04/05/12 with evidence and commit SHAs, as PLAN-25 did in `HCM_001_GAP_STUDY.md`.
- Set the plan's `Status` to `Implemented (CI not executed) — <date>` and add «ملاحظات التنفيذ والانحرافات».

**Independent review:** a fresh subagent reads this prompt, the plan and `git diff main...HEAD`. It hunts for:
- any step in `ci.yml` not in `steps.json`, or vice versa;
- any test made green by weakening its assertion;
- any date extended instead of closed;
- `global.json` in any commit (`git log -p --all -- global.json` on this branch);
- scope logic that lets an out-of-scope `employeeId` through, or blanks central users;
- product changes outside P-4.

A refuter agent attacks each finding. Fix what survives, with a regression test each.

**`reports/plan34-final-report.md`** (English), with these sections:
1. Summary per phase.
2. Commits.
3. Baseline vs final: every test red at baseline and how it became green.
4. Dated-obligations table (nearest dates).
5. CI parity evidence: the Linux `summary.json` and the Windows runs.
6. Package changes.
7. Decisions and deviations.
8. Product defects recorded for later plans (CON, MOV, TRN …).
9. Readiness checklist for PLAN-35:
   - what will run on the first push;
   - the owner's visibility decision (public → private recommended);
   - running gitleaks locally before pushing.

**Merge:** `git switch main && git merge --no-ff feature/plan-34-qa-pushable-baseline -m "Merge branch 'feature/plan-34-qa-pushable-baseline' into main (PLAN-34 pushable baseline)"`. **Do not push.**

Commit before the merge: `docs(qa034): CI/testing guide, STR-005 docs, gap status lines, final report`.

## Definition of done

- `pwsh scripts/ci-local.ps1` is green on a clean HEAD (Linux).
- Windows/ar-SA unit and integration runs have 0 failures.
- No «known failures» list exists anywhere; `tests/quarantine.json` is empty or fully dated.
- `BolaArchitectureTests`, `Ten002ArchitecturalTests`, `CiStepsParityTests`, `DatedObligationsTests`, `RuntimeGovernanceDocumentsTests` and `ScriptInvocationArchitectureTests` are green.
- No High/Critical vulnerability; `-warnaserror` is in the build.
- No `Declared` STR-005 exception is expired or within 7 days, and each change has a decision entry.
- AC-QA-34a..i = Pass with evidence.
- `global.json` is in no commit; `main` has the merge commit; nothing is pushed.
- `reports/plan34-final-report.md` exists, with the PLAN-35 readiness checklist.

Then reply with a short summary and the path of the final report.
