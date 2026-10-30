# PLAN-34 — Final report: pushable baseline

**Plan:** `docs/acceptance-plans/PLAN-34-qa-pushable-baseline.md` · **Prompt:** `docs/acceptance-plans/execution/PLAN-34-AGENT-PROMPT.md`
**Branch:** `feature/plan-34-qa-pushable-baseline` (from `main` @ `f9eab1a`) · **Checkpoint:** `reports/plan34-progress.md`
**Baseline:** `reports/plan34-baseline.md` · **Nothing pushed** (PLAN-35).

## 1. Summary per phase

| Phase | Result |
|-------|--------|
| P0 | Branch from `main` @ `f9eab1a`; program documents committed first (P-1); baseline on Windows/ar-SA: unit 16 failed / 4141, integration 24 / 258, Testcontainers 10 / 82; dated-obligations inventory (plus `licenseExpiry`/`eolDate`, compared with today by a script a unit test runs). |
| P1 | Dated-obligations registry + horizon check (warn ≤ 30, fail ≤ 7 days); STR-005 simulation exceptions `Closed` by decision `PG-EXC-2026-10-001` (no date extended); `TimeProvider` in the governance startup validators; invariant `yyyy-MM-dd` UTC parsing in evaluators and scripts (also fixed a one-day-early shift on UTC+3 hosts); unit tests off the real clock; canary `RuntimeGovernanceDocumentsTests`. |
| P2 | `scripts/ci/steps.json` = single source of the 86 `build-and-test` steps + the horizon step; `ci.yml` calls `run-steps.sh --step`; `ci-local.ps1` + `docker/ci-local/Dockerfile` (SDK 10.0.400 exactly) run them on a clean clone of HEAD; `CiStepsParityTests`; weekly `dated-obligations.yml`; Linux baseline recorded (78/87 passed). |
| P3 | Syr001 (DbContext + ValidateOnBuild), Ten002 (sanctioned bypass), SAN-001 (committed `global.json`), the seven script tests via `RepoScriptRunner` + `ScriptInvocationArchitectureTests`, `TestCulture` (ar-SY) in three projects + guards; `Hcm029` flake root-caused and fixed (no quarantine). |
| P4 | Branch scope on the twelve `Index(employeeId)` actions; `EmployeeIdsInScope` (snapshots) and `PayrollEmployeeScope` (active pay profile); 72 SQLite cases (75 after the review E-1 cases) + 2 PostgreSQL; `BolaArchitectureTests` green without an allow-list. |
| P5 | Regime-client fakes, `ContractsTestServices`, payroll host fake (moved from PLAN-59), establishment temporal services, SoD fakes, `Hcm028` invariant date, `IntegrationHostValidationTests`; root causes beyond the plan: stale HCM-032 message counts; PG payroll host missing the tenant-stamp interceptor. |
| P6 | Testcontainers 4.16.0 / Minio 4.15.0 → SSH.NET 2026.0.0; lock files regenerated with SDK 10.0.400; `WarningsAsErrors` NU1903/NU1904; `-warnaserror` build step; Q-3 warning fixes. |
| P7 | MinIO behind profile `object-storage`, File.Storage on `Local`, `docker-compose.object-storage.yml`; `HRM_MINIO_IMAGE` + `Category=ObjectStorage` (dated obligation, PLAN-38); whitespace commit; EF gate green (15 contexts, no migration); first coverage measurement (D-QA-13); owner-approved product fixes Q-1, Q-2, Q-4, Q-5. |
| P8 | Clean-HEAD Linux run 85/0 (+2 not-run, rule P-3); Windows same HEAD all green incl. Testcontainers and the coverage gate; base stack boots without MinIO; AC-QA-34a–f, h, i Pass, 34g Blocked (CI realm tokens carry no roles). |
| P9 | Independent review + refuter (§7.1): E-1, H-1, G-1, A-1 fixed with regression tests, B-1 hardened, E-2 recorded (owner decision), F-1 refuted; evidence re-run on the final code HEAD `1a3397c` (Linux 85/0 + 2 not-run, Windows all green, AC-QA-34 8 Pass + 34g Blocked); docs, gap-study status lines, this report, `--no-ff` merge into local `main`. |

## 2. Commits (branch `feature/plan-34-qa-pushable-baseline`)

| SHA | UTC | Message |
|-----|-----|---------|
| `02f890b` | 2026-10-29 23:13 | docs(program): gap studies, verification, roadmap, owner register, PLAN-34..65 |
| `206c656` | 23:24 | chore(qa034): baseline and dated-obligations inventory |
| `15592a8` | 23:38 | docs(program): PLAN-34 prompt timing rules and scripts/plan-timing.ps1 (owner update) |
| `396e73f` | 23:40 | fix(qa034): dated-obligations registry …, STR-005 simulation exceptions closed by decision |
| `c7a675c` | 2026-10-30 00:20 | ci(qa034): single source of CI steps, local Linux runner, parity guard, weekly workflow |
| `c6086f2` | 00:30 | test(qa034): deterministic unit baseline |
| `8caddff` | 00:35 | fix(qa034): branch scope on the twelve Index(employeeId) actions |
| `eed6128` | 00:50 | test(qa034): integration hosts …; host validation |
| `9130e36` | 00:57 | style(qa034): whitespace only (final newlines) |
| `e422f92` | 08:51 | build(qa034): fix NU1903, warnings-as-errors |
| `35c2959` | 08:55 | fix(qa034): base stack without the unavailable MinIO image, format/EF/coverage green |
| `a748dce` | 09:38 | test(qa034): AC-QA-34a..i acceptance scenarios; ci-local reports the tree under test |
| `4115e9d` | 09:46 | ci(qa034): -SkipTestcontainers also reports the coverage gate not-run (rule P-3) — P8 evidence HEAD |
| `e4187ed` | 11:30 | test(qa034): full local CI green on Linux and Windows, AC-QA-34a..i live evidence |
| `ffdd35e` | 12:00 | fix(qa034): independent review fixes with regression tests |
| `1a3397c` | 12:35 | test(qa034): AC-QA-34d counts the three page-size-cap cases (review E-1) — **final code / evidence HEAD** |
| (P9) | | docs(qa034): CI/testing guide, STR-005 docs, gap status lines, final report (+ AC-QA-34 evidence of `1a3397c`) |

## 3. Baseline vs final — every test red at baseline

Final = HEAD `1a3397c`: Windows/ar-SA unit **4184/0**, integration **340/0** (4 opt-in skips), Testcontainers (−ObjectStorage)
**81/0**; Linux unit **4184/0**, integration **344/0**. (P8, HEAD `4115e9d`: 4183/0, 337/0, 81/0; Linux 4183/0, 341/0.)

| Baseline failure | How it became green |
|------------------|---------------------|
| `BolaArchitectureTests` | the twelve actions now apply scope (P4) |
| `Ten002ArchitecturalTests` | `IgnoreTenantFilterForSystem(SeedInitialization)` in the two seeders |
| `Syr001ProductionBindingTests` | the test registers `HRCoreDbContext` (SQLite) and validates the container |
| `Str005ProductionBindingTests` ×3 | exceptions closed; fixed clock on the matrix `asOfDate`; invariant parsing (the "expired" test failed because ar-SA read 2020 as a Hijri year) |
| `Str005GovernancePackTests.Exceptions_have_expiry…` | invariant `ParseExact` |
| `LearningCompletionRulesTests.Certificate_Number…` | ar-SY test culture (product fix → TRN, wave 2) |
| `San001SanctionsTests.Validate_script_passes` | checks `git show HEAD:global.json` |
| seven `*_validate_script_passes` | `RepoScriptRunner` (`-ExecutionPolicy Bypass`) |
| (flaky) `Hcm029…External_hire…` | harness inactivity window; 3/3 full runs green |
| 10 × `IHrCoreActiveRegimeClient` (ContractLifecycle ×4, Fun002 ×2, Int003, EndToEnd, ContractSeparationChain, InboundEventConsumer) | `ContractsTestServices` + `FakeHrCoreActiveRegimeClient` |
| 9 × payroll on SQLite (PayrollRunLifecycle, Perf001 ×4, Pay003 ×4) | `FakeHrCoreServiceRegimeClient` in `PayrollIntegrationTestSetup` |
| 5 × payroll on PostgreSQL (Pay021, Pay022 ×2, Pay023, Perf001 PG) | PG host stamps tenants (`TenantIdStampInterceptor`), as Payroll.Web |
| `EstablishmentHireIntegrationTests` ×2 | `AddHrEstablishmentTemporal()` |
| `SoDForgedApproverTests` | fake instead of `Mock.Of` |
| `InternalHireClosure…`, `RecruitmentHireClosure…` | exact message-type list (HCM-032 added `IRecruitmentRequisitionClosed`; the counts were stale) |
| `Hcm028AppealGovernanceMigrationTests` | invariant date format |
| `FileStorageInboxIdempotencyTests` | INT002 migration discoverable (Q-4) |
| `Qa003MinioFileScanTests` ×3 | `Category=ObjectStorage`, excluded by a dated obligation (PLAN-38) — not green, excluded with an owner and a date |
| Linux-only: `Hcm030…Long_file_names…` | Q-1 |
| new under ar-SY: `Est004…Csv_import_rejects_invalid_rows` | Q-2 |

## 4. Dated obligations (nearest first, 2026-10-30)

| Days | Date | Obligation | File | Owner |
|------|------|-----------|------|-------|
| 45 | 2026-12-14 | ObjectStorage CI exclusion review | `tests/ci-exclusions.json` | PLAN-38 |
| 60 | 2026-12-29 | `Payroll.Application` temporary coverage floor 73 → 75 | `coverage-floors.json` | payroll (PLAN-56..59) |
| 93 | 2027-01-31 | STR-002 legislative watch `nextDue` (Production startup fails after it) | `docs/str-legal/legislative-watch-stamp.json` | legal |
| 93 | 2027-01-31 | six STR-002 control exceptions | `docs/str-legal/control-catalog.json` | legal |
| 243 | 2027-06-30 | MIG-001 unsupported-source exception; rabbitmq EOL | `source-inventory.json`, `vendor-contract-registry.json` | migration office, architecture |
| … | 2027-07-31 → 2028-11-09 | vendor licenses / EOL; acceptance seed closing date 2028-08-01 | see `docs/qa/dated-obligations.json` | |

Warnings start 30 days before, failures 7 days before. Closed: both STR-005 simulation exceptions.

## 5. CI parity evidence

- **Linux** (`artifacts/ci-local/head/summary.json`, git-ignored; its content is recorded in `acceptance-evidence/AC-QA-34/db-checks-34a.json`):
  source `head-clone`, `gitSha` `1a3397cc…` = HEAD, `dirtyTracked` [], `knownLocalOverrides` [] (host: `global.json` rollForward
  only), steps **85 passed / 0 failed / 0 skipped / 2 not-run** (Testcontainers, coverage gate — rule P-3), 984 s;
  `rewrittenByRunCount` 142 (testdata/*/out reports and docs/qa registries the steps rewrite themselves).
- **Windows/ar-SA** (`artifacts/plan34/windows/`, same HEAD): `dotnet build -c Release -warnaserror` 0 errors, 0 warnings; unit 4184/0;
  integration 340/0; Testcontainers 81/0; coverage gate passed (gate 90.1%; Payroll.Application 73.6% ≥ floor 73).
- **Rule P-3:** Testcontainers works inside the ci-local container but, through Docker Desktop's port proxy, PostgreSQL
  connections time out intermittently under load (3 of 81 per run, different tests); the bridge gateway does not work (Ryuk
  unreachable). The two dependent steps are reported `not-run`, never dropped. GitHub runners run them normally.


## 6. Package changes

| Package | Before | After | Why |
|---------|--------|-------|-----|
| `Testcontainers.PostgreSql` | 4.13.0 | 4.16.0 | core Testcontainers 4.16.0 → SSH.NET 2026.0.0 |
| `Testcontainers.RabbitMq` | 4.13.0 | 4.16.0 | same |
| `Testcontainers.Minio` | 4.13.0 | 4.15.0 | latest published; resolves core 4.16.0 |
| `SSH.NET` (transitive) | 2025.1.0 | 2026.0.0 | first patched version of GHSA-mggc-4xg6-vcxf (< 2026.0.0) and GHSA-q939-rpr3-3284 (≤ 2025.1.0), both High |
| `System.Memory` (transitive, 14 Domain projects + FileAdapters) | 4.5.3 in lock files | pruned | `dotnet restore --force-evaluate` with SDK 10.0.400 (the old locks predated .NET 10 pruning) |

No direct SSH.NET pin and no `supply-chain-exceptions.json` row were needed. `Directory.Build.props`: `WarningsAsErrors`
+= `NU1903;NU1904`; the CI build step runs with `-warnaserror`.

## 7. Decisions and deviations

**Plan defaults adopted** (D-QA-3, 5, 7, 10, 11, 12, 13, 14) — recorded in `reports/plan34-progress.md`.

| # | Decision / deviation | Reason |
|---|----------------------|--------|
| D-QA-3 | Both STR-005 simulation exceptions `Closed` by decision `PG-EXC-2026-10-001`; no date extended | plan default |
| D-QA-13 | `Payroll.Application` floor 75 → 73 (measured 73.6%), `temporaryFloors` entry, obligation until 2026-12-29, ADR-QA-002 note | first measurement of a gate that never ran |
| D-QA-14 | `Hcm029` flake root-caused (MassTransit harness inactivity window under a saturated thread pool); harness waits longer; no quarantine | reproduced in a full run, fixed, 3/3 full runs green |
| Q-1..Q-5 | Owner-approved product fixes outside P-4 (in chat, 2026-10-30): upload file names (Linux), finance-budget CSV culture, INT002 migration attributes, accessible name on an icon link; Q-3 warning fixes + MVC1004 suppression | each kept CI red; found by the first Linux / ar-SY runs |
| S-1 | Commits authored `omardefdev <omar@defdev.sy>` via `git -c` | no global identity on the machine; matches history |
| S-2 | Dated keys: the plan's seven + `licenseExpiry`, `eolDate`, `applicationClosingDate` | `sup004-license-audit.ps1` (run by a unit test) and the acceptance-seed tests compare them with today |
| S-3 | `dated-obligations-check.ps1` and `ci-local.ps1` run on Windows PowerShell 5.1, ASCII only | the owner's machine has no `pwsh` |
| S-4 | OPS-007 / STR-006 `eolDate` listed as non-enforced (`historical[]`), not obligations | nothing compares them with the clock; the OPS-007 inventory already lists past dates |
| S-5 | Seed-date unit tests keep their real-clock assertion; the date is an obligation | replacing the clock would weaken them |
| S-7 | `ci-local.ps1` runs on a clone of HEAD inside the container by default | exact "clean HEAD"; protects the Windows `obj/` folders; keeps the local `global.json` out |
| S-8 | Container-parity fixes: unset `DOTNET_RUNNING_IN_CONTAINER`, same-path `TMPDIR`, IPv4 Testcontainers host, host `core.autocrlf` | each produced false failures that GitHub runners would not have |
| Dev-1 | AC-QA-34b: the nearest obligation is the ObjectStorage CI exclusion (2026-12-14), not STR-002 (2027-01-31) | the plan itself registers the exclusion at +45 days |
| Dev-2 | `minio` image is `${MINIO_IMAGE:-}` in the base file, `:?` only in `docker-compose.object-storage.yml` | Compose interpolates every service whatever its profile; `:?` in the base file breaks the base stack |
| Dev-3 | `hr-core-web` also loses `depends_on: minio` and uses the Local provider | it had the same dependency as `file-storage-web` |
| Dev-4 | `FileStorageInboxIdempotencyTests` is not `ObjectStorage`; it failed on the INT002 migration defect (Q-4) | baseline attribution corrected |
| Dev-5 | No `PLAN034_SyncPendingModel` migration | the EF gate passes for all 15 contexts once the container artefact is removed |

### 7.1 Independent review and refuter (P9)

An independent reviewer read the branch diff against `main`; a second agent then tried to refute each finding against the committed code (HEAD `e4187ed8`). Each fix that survived has a test that fails without it.

| # | Finding | Refuter | Outcome |
|---|---------|---------|---------|
| E-1 | Separation / Probation / Commencement lists took the newest 100 rows **then** filtered by branch, so newer out-of-scope rows could empty a branch user's list | Real (low–medium; the sibling controllers already push scope into SQL) | Fixed: the three queries take `EmployeeIds` and filter before `Take`. `Plan034ScopedIndexTests.Branch_scope_applies_before_the_page_size_cap` (110 newer BR2 rows): red without the fix, green with it |
| E-2 | Payroll scope uses the **active** pay profile only, so separated employees (profile deactivated with the EOS settlement), dismissed employees and new hires (profile created inactive) are out of every branch scope | Real, **medium** (fails closed: no leak, but the EOS list is effectively empty for branch users) | **Not changed**: fail-closed on an inactive profile is the plan's rule (§4, pinned by the scoped-index seed). Choosing the "last profile" instead is a policy decision → §8, owner decision needed |
| B-1 | Message-list asserts ignore the non-generic `Publish(object)` overload | Partly (nit): every publish on these paths is generic | Kept; total-count asserts added as a backstop |
| H-1 | Acceptance read evidence (trx, native coverage, Windows unit trx) without proving which commit produced it; `ci-local` kept an old `TestResults` when a run failed before tests | Real | Fixed: `RequireHeadEvidence` in StartTrx / 34h / 34i, native `head.txt` must equal HEAD; `ci-local-entry.sh` clears an `artifacts/` destination first. Evidence re-run on the final code HEAD (§5) |
| G-1 | `pwsh` 7 `ConvertFrom-Json` turns `2026-12-14T23:30:00+05:00` into a local `[datetime]`, bypassing the strict `yyyy-MM-dd` parse (CI runs pwsh 7.6.8) | Real (low) | Fixed: the five date scripts pass `-DateKind String` when available; checked in the ci-local container: the date-time string now fails as unparseable. PS 5.1 never converts |
| A-1 | The parity test parsed only `name/uses/run/if`, so `continue-on-error`, `env`, `shell`… on a step (or job-level `defaults`/`env`) would go unnoticed | Real (low) | Fixed: `Ci_build_and_test_steps_carry_no_other_keys` (step keys and job keys) |
| F-1 | Switching `hr-core-web` and `file-storage-web` to the Local provider splits one store into two volumes | **Refuted**: HR.Core never registers a file-storage provider; it reaches File.Storage over HTTP (`Integration:FileStorageBaseUrl`) | No change. The `FileStorage__*` variables on `hr-core-web` are dead configuration (§8) |

## 8. Product defects recorded for later plans (not fixed here)

| Area | Defect | Evidence |
|------|--------|----------|
| CON | `PublishSeparationHandler` needs HR.Core live: with HR.Core down the publish throws `HttpRequestException` (`HrCoreActiveRegimeClient.cs:32-33`) | `Fun002…Publish_with_hr_core_unavailable_fails_without_an_event` pins today's behaviour |
| MOV | `TransferRequestsController.cs:453-454` calls `regimes.FirstOrDefault()` on the client result; throws when HR.Core is down or returns null | plan §5 rule 2 |
| TRN (wave 2) | `LearningCompletionRules.cs:28` formats the certificate number with the thread culture (Hijri year under ar-SA) | green only because of the ar-SY test culture |
| IAM / PLAN-52 | `LeaveBalancesController.Index` applies the list scope only when `leaveType` is not set | read during §4 |
| SYR | `ServiceRegimeExceptionService` received a `ServiceRegimeValidator` it never used (CS9113; parameter removed) — possibly a missing validation | Q-3 |
| ADM (logistics) | MVC1004 in `AssetsController.CreateLegacy`/`Edit`: a view-model property named like the `model` parameter can bind wrongly; suppressed, not renamed | Q-3 |
| OPS | `docs/ops007/component-inventory.json` lists stale EOL data (kubernetes 2025-12-28, dotnet-runtime 9.0.7 2026-11-12) | S-4 |
| GW | `LicenseValidationMiddleware.cs:16-24` parses `Deployment:LicenseExpiresUtc` with the thread culture (inert while the setting is empty) | dated-obligations sweep |
| HARD | `scripts/hard001-compliance-scan.ps1:85` compares a culture-parsed date with the clock once `docs/hardening/hard001-exceptions.json` exists | dated-obligations sweep |
| TEST | `Qa003OutboxInboxRealInfraTests` and `RabbitDownOutboxTests` register an `ITenantContext` without the tenant-stamp interceptor (the pattern that broke the payroll PG host) | pass/skip today |
| QA / PLAN-41 | The base CI realm (`prepare-ci-compose-env.sh`) issues password-grant tokens **without realm roles** (no `roles` client scope; `profile`/`email` rejected as invalid scopes), so `/api/files` (and other module paths) return 403 "missing role for this module" for every persona — AC-QA-34g upload half Blocked; the CI E2E/acceptance jobs use the same realm | `acceptance-evidence/AC-QA-34/db-checks-34g.json` (`tokenRealmRoles: []`) |
| PAY / IAM | Payroll branch scope (`PayrollEmployeeScope`) places an employee by the **active** pay profile only; separated or dismissed employees (profile deactivated by `HandleSeparationCompletedHandler` / dismissal) and new hires (profile created inactive) are out of every branch scope, so their EOS settlements and other payroll rows are hidden from branch users (fail-closed). Owner decision: place by the latest profile? | review E-2, `InboundEventHandlers.cs:213, 612, 647` |
| IAM / OPS | `auth-directory-web` can crash on a cold base-stack start: when PostgreSQL refuses the first connection, `Program.cs` logs "Database migration failed … continuing startup (non-production)" and the demo seeder then throws `42P01 relation "AuthUsers" does not exist` (exit 139, no restart policy). A second `up --wait` starts it cleanly | P9 base-stack run, `artifacts/plan34/p9-stack-up*.log` |
| OPS (compose) | `hr-core-web` carries `FileStorage__*` variables it never reads (HR.Core uses File.Storage over HTTP) | review F-1 (refuted) |
| QA (ci-local) | The ci-local image has `python3` but no `python` alias; `prepare-keycloak-import.ps1` calls `python` (GitHub runners have both). Not used by `build-and-test` | P8 |

## 9. Readiness checklist for PLAN-35 (push)

**What will run on the first push** (`.github/workflows/ci.yml` on `push`/`pull_request` to `main`):
- `build-and-test`: the 87 steps of `scripts/ci/steps.json` — locally 85 passed on Linux; the Testcontainers step and the coverage
  gate passed natively (Windows) and will run normally on `ubuntu-latest` (no Docker Desktop proxy).
- `e2e` and `acceptance` jobs (`needs: build-and-test`): **expect failures** — the base stack now boots without MinIO, but the
  CI realm's tokens carry no roles (finding §8), and the acceptance job's persona/realm issues are PLAN-41's scope.
- `codeql` (analysis with `upload: false`) and `gitleaks` jobs; the weekly `dated-obligations.yml`; the other scheduled workflows
  (`e2e-journeys` nightly, `advisory-to-issue`, `mutation`, CodeQL weekly) become active — see PRG_001 G-QA-11/15.

**Owner decisions before pushing:**
1. **Repository visibility:** the GitHub repository is currently **public**. Recommended: switch to **private** before the
   first push (`main` will be 155 commits ahead of `origin/main` after this merge: HR/payroll domain code, lab credentials in
   `.env.ci`/realm templates, internal plans).
2. Branch protection / required checks (PLAN-35).

**Before `git push`:**
```bash
docker run --rm -v "$PWD:/repo" zricethezav/gitleaks:latest detect --source /repo --log-opts="origin/main..main" --redact
```
(or `gitleaks detect --source . --log-opts="origin/main..main" --redact` with a local binary) — review every finding; lab-only
secrets (`*-lab-only`, `.env.ci`, `testdata/e2e/personas.json` defaults) are known and must be allow-listed in `.gitleaks.toml`
deliberately, not silently.

**Also confirm:** `git log -p origin/main..main -- global.json` is empty (it is on this branch); `git status` shows only the local
`global.json` edit; `artifacts/` stays ignored.
