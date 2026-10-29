# Task: PLAN-33 — Prove the recruitment path with evidence: no silent passes, deterministic recruitment states, complete AC-HCM-21/22 and AC-WF-23, full requisition-to-hire E2E, phantom evidence removed, docs matching the code (one autonomous session)

## How to run this task — read before anything else

This is a **single prompt for one autonomous session**. Run it as phases **P0–P8** in order.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx`, 0 errors.
2. Run that phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan33-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test and live-run results;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan33-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase.

**Context discipline.** Use subagents for broad read-only searches. On the mounted repo, use `git grep`: a recursive grep over `bin`/`obj` is very slow.

**Orchestration rules** (ultracode / workflows may be on):
- Parallel agents are allowed for:
  - read-only exploration;
  - writing **separate new test files**;
  - independent review;
  - doc audits.
- Never let two agents edit the same file concurrently.
- These edits are made **serially by you, the lead**:
  - `AcceptanceDataSeeder` and its patches
  - `testdata/acceptance/*`
  - `personas.json` / Keycloak scripts
  - `docker-compose*.yml`
  - `.github/workflows/*`
  - `HRCoreDbContext`/migrations (none are expected)
- Live acceptance runs are **serial**: one stack, one run at a time.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write a `BLOCKED:` entry in the progress file, with the question and the options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

**The goal of this plan is honesty.** A test that cannot check must show **Skipped/Blocked with a reason**, never Passed. A status in a document must point to evidence.

---

## Role

You are a senior .NET 10 / xUnit / Playwright / Docker engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop, machine culture ar-SA). You own acceptance and E2E quality.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-33-hcm-verification-docs.md` (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md`:
   - **G-23**, **G-24**;
   - **G-02** (still open, PLAN-25);
   - §7 "معيار اكتمال مسار التوظيف".
3. `docs/acceptance-plans/README.md` — the **evidence contract**: eight items per scenario folder (`metadata.json`, `steps.md`, `http/*.json`, `before.json`/`after.json`, `audit.json`, `db-checks.json`, `timing.json`, `result.json` with `Pass` / `Fail` / `Blocked`).
4. Scenario sources:
   - `PLAN-21-hcm-requisition-approval-ui.md` (AC-HCM-21a–d)
   - `PLAN-22-hcm-recruitment-workflows-ui.md` (AC-HCM-22a–e)
   - `PLAN-23-wf-work-inbox-ui.md` (AC-WF-23a–f)
   - the PLAN-26..32 plan docs and final reports (`reports/plan3[0-2]-final-report.md`, `reports/plan31-progress.md`)
   - the execution guides in `docs/acceptance-plans/execution/`
5. Acceptance infrastructure:
   - `src/Tests/HRManagement.AcceptanceTests/{AcceptanceHarness,EvidenceRecorder,AcceptanceEvidenceSummary,AcceptanceMessagingHelper}.cs`
   - every `Ac*.cs` file in that folder
   - `testdata/acceptance/personas.json`
   - `scripts/acceptance-{up,reset,run-all,verify,evidence-summarize}.ps1`
   - `scripts/prepare-keycloak-import.ps1`
   - `docker-compose.acceptance.yml`
6. E2E:
   - `src/Tests/HRManagement.E2E/*` (`E2eHarness`, `PlaywrightFixture`, `HireSeparationJourneyE2ETests`, `Category=Journey`)
   - `.github/workflows/e2e-journeys.yml`, `.github/workflows/ci.yml`
7. Phantom evidence:
   - `src/Shared/HRManagement.SharedKernel/Recruitment/{RecruitmentRequisition,CandidateApplication,OfferLetter}.cs`
   - `src/Tests/HRManagement.UnitTests/Hcm001RecruitmentTests.cs`
   - `scripts/hcm001-validate.ps1`
   - `docs/completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md` (the HCM-001 card)
8. Docs to correct (§6 of the plan):
   - `docs/HCM_001_RECRUITMENT.md`
   - `docs/HIRE_ONBOARDING_GUIDE.md`
   - `docs/HIRE_RBAC_MATRIX.md`
   - `docs/MESSAGE_EVENT_MATRIX.md`
   - `docs/DATABASE_SCHEMA_INVENTORY.*`
   - `docs/THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md`
   - `docs/completion-backlog/09-…`

## Decisions for this implementation (record them in P0)

| # | Decision |
|---|----------|
| 1 | **PLAN-25 is still deferred.** G-02 is open: with `NationalScope:IsLabScale=false` and the policy engine `Off`, requisition, offer and committee approvals do not start (`RecruitmentRequisitionLifecycleService`, `RecruitmentOfferService`, `SelectionCommitteeLifecycleService` gate on `IsLabScale`). The required E2E therefore runs on the **acceptance profile** (lab scale). |
| 2 | A **prod-like variant** of the E2E is written now: a compose override `docker-compose.acceptance-prodlike.yml` with Production-like settings, `Category=JourneyProdLike`, **excluded from CI until PLAN-25**. Run it once and record honestly where it stops (expected: at the first approval, because of G-02). That evidence feeds PLAN-25. Do **not** fix G-02 here. |
| 3 | **Skips are explicit.** A missing precondition goes through one helper that writes `result.json = Blocked` with the reason and then calls `Skip.If`/`Skip.IfNot`. `ACCEPTANCE_REQUIRE_ALL=true` turns a skip of a `[RequiredAcceptance]` scenario into a **failure**. |
| 4 | **Every silent-pass pattern is removed, not only the recruitment ones:** `WriteResult("Skip", …); return;` in `AcHcm21`, `AcHcm22` and `AcEst24`, and `WriteResult("Pass", "<not seeded…>"); return;` in `Ac33AssetLossLiveTests` and `Ac37JourneyRebuildLiveTests`. An architecture test keeps them out. |
| 5 | **Scenarios are adapted to today's design, not to their original text.** AC-HCM-22d follows PLAN-28: HR files → an independent `compliance` reviewer is assigned → the reviewer decides. Its negative: a non-assigned reviewer is refused. AC-HCM-21d/22e "Off + non-lab → warning" is today's G-02 behaviour: prove it with an **integration test**, not by reconfiguring the shared acceptance stack. |
| 6 | **The acceptance stack must be complete:** File.Storage and ClamAV included. PLAN-32 recorded AC-30c skipped and `Ac27_f_g` failing because File.Storage was not running. Fix the stack or its start-up script, not the tests. |
| 7 | **No push, and remote CI cannot be run here.** Workflow YAML is updated and validated locally (parse, plus `actionlint` if available). The report says "CI not executed". |
| 8 | A **product defect** found by a live run is fixed with a regression test **only** if the fix is local and unambiguous. Otherwise it is recorded as a follow-up, and the scenario is marked `Fail` with the evidence — never hidden. |

## Scope boundaries

- **Out of scope:**
  - PLAN-25 work (G-02 approval binding, server-side step-role checks G-04, G-14, G-15, list org-scoping);
  - new product features;
  - pushing;
  - production deployment.
- Keep product code changes to the defect fixes allowed by decision 8.
- **Never commit:**
  - `global.json`
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets or credentials (`.env`, `.acceptance/credentials.json`)
  - test-rewritten `testdata/*/out/*.json` — run `git checkout -- testdata` before each commit, **except** the new `testdata/acceptance/recruitment-states.json`
- Evidence folders under `acceptance-evidence/` **are** committed (as in PLAN-26..32).

## Repository facts (verified on `main` @ `4342bb6`, 2026-10-29 — re-verify)

**Silent passes**
- `WriteResult("Skip"` followed by `return;` is still in `AcHcm21RequisitionApprovalUiTests.cs` (2×), `AcHcm22RecruitmentWorkflowsUiTests.cs` (2×) and `AcEst24EstablishmentWorkflowUiTests.cs` (2×).
- `Ac33AssetLossLiveTests.cs:28` and `Ac37JourneyRebuildLiveTests.cs:32` write `Pass` when data is not seeded.
- `AcceptanceEnv.RequireEnabled()` already uses `Skip.IfNot(RUN_ACCEPTANCE)`.
- `ACCEPTANCE_REQUIRE_ALL` and `[RequiredAcceptance]` do not exist.

**Coverage**
- `AcHcm21` has one test of four (21b only), and `AcHcm22` has one of five (22b only). Both find their data by scraping HTML.
- Evidence folders exist for AC-HCM-26..32 but **not** for AC-HCM-21, AC-HCM-22 or AC-WF-23. An old `HCM-01` folder holds only `steps.md`.

**Phantom evidence**
- `src/Shared/HRManagement.SharedKernel/Recruitment/*` (three files) is referenced only by `Hcm001RecruitmentTests.cs` (reflection and file-existence tests).
- It is listed as an artifact in `HCM_001_RECRUITMENT.md` and as closure evidence in `completion-backlog/09`.

**E2E**
- `HireSeparationJourneyE2ETests` starts at the HireWizard.
- `e2e-journeys.yml` runs `Category=Journey|FullyQualifiedName~E2eSeedPackTests` with Playwright (`RUN_PLAYWRIGHT_E2E=true`).
- `docker-compose.acceptance-prodlike.yml` does not exist.

**Production settings.** `appsettings.Production.json` and `appsettings.Staging.json` set `NationalScope:IsLabScale=false`. No `PolicyEngine` default mode is configured (G-02).

**Docs drift remaining**
- `HIRE_ONBOARDING_GUIDE.md` still has the old «طلب → مختصر → تعيين» model (around lines 295, 413–451, and the footer dated 2026-05-29), contradicting its own §2/OfferAccepted sections.
- `HIRE_RBAC_MATRIX.md` lists ESS candidacy as `hr-core` — verify the real role holding `Module.Ess`.
- `HCM_001_RECRUITMENT.md` still lists `SharedKernel/Recruitment/*` as artifacts and has a "Runtime Status (Plans 1–5)" table.

## Stop (record `BLOCKED:` and end the turn) if

- making a scenario pass would need a PLAN-25 change (G-02/G-04/G-14/G-15) — mark that scenario `Blocked` with the reason and continue with the others; stop only if **every** remaining scenario depends on it;
- the acceptance stack cannot be brought up complete (File.Storage, ClamAV, Keycloak realm) after following the execution guides;
- deleting `SharedKernel.Recruitment` breaks a non-test consumer (thesis generators, docs tooling) that needs a decision;
- a live run reveals a product defect whose fix is not local or not unambiguous (decision 8) and it blocks more than its own scenario.

---

## P0 — Baseline, inventory, notes

1. Run `git checkout -- testdata`. From `main`, create `feature/plan-33-verification-docs`.
2. Run `dotnet build HRManagement.slnx` and the unit tests filtered `Hcm0|Acceptance|E2e|Architecture`. Record the results in `reports/plan33-baseline.md`.
3. **Inventory every acceptance test method** (subagent). Write `reports/plan33-acceptance-inventory.md` with, for each test:
   - the scenario id;
   - the gating mechanism (`Skip.If` / silent pass / none);
   - whether it has assertions;
   - the data source (seed ids / HTML scraping);
   - its evidence folder;
   - the last live result found in `acceptance-evidence/`.
4. **Stack check.** List running containers. Compare the images to `main`, and check whether File.Storage and ClamAV are up. Record what P6 needs (rebuilds, Keycloak re-import).
5. Add a «تعديلات قبل التنفيذ (2026-10-29)» block (Arabic) to the top of the PLAN-33 doc with decisions 1–8. Set `Depends on` to "PLAN-26..32 منفذة؛ PLAN-25 مؤجلة (G-02 مفتوحة)".
6. Create `reports/plan33-progress.md`. If `docs/acceptance-plans/execution/PLAN-33-AGENT-PROMPT.md` (this prompt) is untracked, include it in the commit.

Commit: `docs(hcm033): baseline, acceptance inventory, pre-implementation notes`.

## P1 — Acceptance framework: explicit skips, required scenarios, architecture guard (AC-HCM-33a, 33b)

- **`AcceptanceGate`** (static helper in the acceptance project):
  - `RequireData(EvidenceRecorder recorder, bool condition, string reasonAr, string reasonEn)`: when the condition is false, `recorder.WriteResult("Blocked", reason)`, then:
    - if `ACCEPTANCE_REQUIRE_ALL=true` **and** the test method or class has `[RequiredAcceptance]` → `Assert.Fail("Required acceptance scenario blocked: …")`;
    - otherwise → `Skip.If(true, reason)`.
  - Also use it for the "not enabled" path where a recorder exists.
- **`[RequiredAcceptance]`** attribute (method or class). Mark every recruitment scenario: AC-HCM-21, 22, 26–33 and AC-WF-23.
- **Convert** every silent pattern from decision 4. The `Ac33` and `Ac37` "not seeded" paths become `Blocked`/skip, never `Pass`.
- **`AcceptanceSkipPatternArchitectureTests`** (unit tests, textual scan of `src/Tests/HRManagement.AcceptanceTests/*.cs`):
  - no `WriteResult("Skip"`;
  - no `WriteResult("Pass"` followed within 3 lines by `return;`;
  - every `[Fact]`/`[SkippableFact]`/`[Theory]` method in an `Ac*` class contains at least one `Assert.` (or calls a helper documented as asserting — keep an explicit allow-list with reasons).
- **Tests:**
  - `AcceptanceGateTests` — skip vs. fail under `ACCEPTANCE_REQUIRE_ALL`, `result.json` written as `Blocked` (**33a**, **33b**);
  - the architecture test fails on a fixture string containing each forbidden pattern.
- **CI:** add `ACCEPTANCE_REQUIRE_ALL: "true"` to the acceptance job(s) in `.github/workflows/ci.yml`, and to `e2e-journeys.yml` if acceptance runs there. Validate the YAML locally.

Commit: `test(hcm033): explicit acceptance skips, required scenarios and silent-pass architecture guard`.

## P2 — Deterministic recruitment states

- **Reuse first:** list the existing acceptance patches (`AcceptanceCompetitionPatch`, `AcceptanceCandidateExperiencePatch`, `AcceptanceRecruitmentReportsPatch`, the PLAN-26..29 patches) and the states they already create.
- **Add only what AC-HCM-21/22 and AC-WF-23 still lack:** `AcceptanceRecruitmentStatesPatch.SeedAsync` (deterministic GUIDs, idempotent), for each acceptance agency used by the scenarios:

  | Element | States / contents |
  |---------|-------------------|
  | requisitions | one in each state needed: Draft, PendingApproval (with a live workflow instance), Approved, Published (future closing date) |
  | criteria | active v1 |
  | applications | Submitted, Shortlisted, Assessed (two assessors), Ranked 1/2/3 |
  | offers | an offer `PendingApproval` created by `central-a`, and an `Issued` offer |
  | committee | a session with 3 members and a chair (PLAN-28 rules) |
  | appeal | a rejected application with a **pending** appeal assigned to `a-compliance` (the real `sub` of the acceptance realm, resolved the way PLAN-28 resolves it) |
  | inbox | items covering AC-WF-23a–f, including a legacy `ApprovalRequest` |

- **`testdata/acceptance/recruitment-states.json`** — ids and the business keys (requisition numbers, offer numbers) the tests read instead of scraping HTML.
  - Add a loader in the acceptance project.
  - Add a unit test proving the JSON ids equal the seeder's constants.
- **Reset:** `scripts/acceptance-reset.ps1` must restore these states. Document how.

Commit: `test(hcm033): deterministic recruitment states seed and recruitment-states.json`.

## P3 — Complete AC-HCM-21, AC-HCM-22, AC-WF-23

Rewrite each class on the P2 ids, with full evidence (the eight items) per scenario, both positive and negative:

**AC-HCM-21**
- **21a:** `central-a` submits → `br1-mgr` approves step 1 → `a-management-approver` approves step 2 → `Approved` → publish with a closing date.
- **21b:** the creator sees no approve action on their own requisition, and a direct POST is refused.
- **21c:** reject → `Draft`, with the reason.
- **21d:** lab-scale submit creates a `WorkflowInstanceId`. The non-lab warning is covered by an **integration test** (`Hcm033LabScaleBehaviourTests`) with configuration overrides (decision 5), referenced in the evidence `steps.md`.

**AC-HCM-22**
- **22a:** Ranked application → offer → submit → two-step approval → `Issued` → accepted by the candidate through `/careers/my` with OTP (PLAN-30), or HR on behalf **with** evidence (File.Storage up).
- **22b:** SoD (as today, rewritten on ids).
- **22c:** committee with ≥ 3 members → submit → approvals → `ApprovedAt`. Negative: submit without enough members is refused.
- **22d** (PLAN-28 design): HR files → assigns `a-compliance` → the reviewer decides → the application state follows the outcome. Negative: a non-assigned reviewer is refused.
- **22e:** offer submit on lab scale → `PendingApproval` + `WorkflowInstanceId`. Non-lab: integration test as in 21d.

**AC-WF-23**
- **23a–f** per PLAN-23, on the seeded inbox items: `br1-mgr` lists; the management approver sees only its steps; the creator gets no actionable items; the compliance reviewer sees only their appeal; the stats count equals the actionable rows; the legacy `ApprovalRequest` is visible.

**All of them:** `[RequiredAcceptance]` and `AcceptanceGate`; no HTML scraping for data discovery (HTML assertions on rendered results are fine).

Commit: `test(hcm033): AC-HCM-21a–d, AC-HCM-22a–e and AC-WF-23a–f on seeded states with full evidence`.

## P4 — Full requisition-to-hire E2E (AC-HCM-33c)

**`src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs`** (`Category=Journey`) through the **gateway**. Use Playwright for the UI steps the existing journeys use, and HTTP for the candidate and portal steps where the E2E harness already does so. A unique run suffix keeps it re-runnable.

| # | Actor | Step |
|---|-------|------|
| 1 | `central-a` | creates criteria and a requisition → submits |
| 2 | `br1-mgr` → `a-management-approver` | two-step approval → publish with a closing date |
| 3 | citizen (anonymous) | `/careers` apply with a profile, a PDF attachment and consent |
| 4 | `central-a` | screen → COI → interview scheduled and completed → two assessors score → rank (committee approval when the requisition requires it) |
| 5 | `central-a` | offer → `br1-mgr` → `a-management-approver` → `Issued` |
| 6 | citizen | OTP (read as the PLAN-30 acceptance does) → accepts on `/careers/my` |
| 7 | `central-a` | hire (new employee) |
| 8 | — | **Verify:** an HR.Contracts `Appointment` transaction exists; the contract carries the offer grade and salary; the `CorrelationId` is consistent across services (the `Ac15` pattern) |

**At least one negative per stage:**
- an offer for a non-ranked application;
- a step approved with the wrong role;
- a hire before acceptance;
- apply after the closing date;
- a wrong OTP.

**CI:** include it in `e2e-journeys.yml` (it matches `Category=Journey`; keep the zero-skip behaviour). It must be **green locally** on the acceptance profile.

**Prod-like variant (decision 2):**
- `docker-compose.acceptance-prodlike.yml`: an override setting HR.Core / HR.Contracts to the Production-like values (`NationalScope:IsLabScale=false`, the Production policy-engine settings, no demo seed).
- `RecruitmentToHireJourneyProdLikeE2ETests` (`Category=JourneyProdLike`), excluded from the CI filter. It reuses the journey steps and **asserts the real outcome**.
- Run it once. It is expected to stop at the first approval because of G-02. Record the exact stop point and evidence in `acceptance-evidence/AC-HCM-33/prodlike/` and in the progress file.
- Never mark it Pass if it did not pass.

Commit: `test(hcm033): requisition-to-hire E2E journey (acceptance profile) and prod-like variant`.

## P5 — Remove phantom evidence

1. Prove there is no runtime consumer of `HRManagement.SharedKernel.Recruitment`: `git grep` over code, scripts (including the thesis and diagram generators in `scripts/*.py`) and docs tooling.
2. **Delete** the three files. If a generator or the thesis model truly needs them, mark them `[Obsolete("Not the runtime model — see HR.Core.Domain")]` instead and record why.
3. **Rewrite `Hcm001RecruitmentTests`** as behaviour tests on the real `HR.Core.Domain` rules: `RequisitionWorkflowRules`, `SelectionWorkflowRules`, `OfferWorkflowRules` transitions, `RecruitmentAppealWorkflowRules` window. Keep them small; they are smoke tests of the real model, not duplicates of `Hcm026+`.
4. Update `scripts/hcm001-validate.ps1` to check the real artifacts.

Commit: `test(hcm033): replace SharedKernel recruitment reflection tests with behaviour tests on HR.Core.Domain`.

## P6 — Live acceptance run of the whole recruitment path

1. **Bring up the complete acceptance stack** per the PLAN-30/31/32 execution guides:
   - rebuild the images from this branch;
   - make sure **File.Storage and ClamAV** are running (decision 6 — fix `docker-compose.acceptance.yml` or `scripts/acceptance-up.ps1` if they are not started);
   - re-import the Keycloak realm if personas are missing.
2. **Run serially** with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`:

   ```powershell
   dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcHcm21|AcHcm22|AcWf23|AcHcm26|AcHcm27|AcHcm28|AcHcm29|AcHcm30|AcHcm31|AcHcm32|AcHcm06"
   ```

   Then run the E2E journey (P4).
3. **For each failure:**
   - test defect → fix the test;
   - product defect → decision 8.

   Re-run until every scenario is `Pass`, or `Fail`/`Blocked` with recorded evidence and a reason.
4. Regenerate `acceptance-evidence/summary.md` (`scripts/acceptance-evidence-summarize.ps1`). Verify that every AC-HCM-21, 22, 26–33 and AC-WF-23 folder has the **eight items**.
5. Record a per-scenario results table in the progress file.

Commit: `test(hcm033): live acceptance evidence for AC-HCM-21/22/26–33 and AC-WF-23` (plus any `fix(...)` commits for allowed defect fixes, each with its regression test).

## P7 — Documentation corrections (G-24) and CI

- **`HCM_001_RECRUITMENT.md`:**
  - the Domain Model section describes the real `HR.Core.Domain` entities (requisition, application with `IntakeChannel`, events, offer, appeal/objection, committee, interview, criteria/scores, competitions, documents, notifications);
  - replace "Runtime Status (Plans 1–5)" with a matrix **(plan, status, evidence path, last live result)** for PLAN-21..33;
  - remove the `SharedKernel/Recruitment/*` artifacts;
  - statuses say "Implemented — verified" only where P6 evidence is `Pass`, otherwise "Implemented — verification pending" with the reason.
- **`HIRE_ONBOARDING_GUIDE.md`:**
  - remove the old «مختصر → تعيين» model everywhere (sections, troubleshooting rows, footer);
  - one consistent flow: criteria → interview → assessment → ranking (committee when required) → offer approval → candidate self-acceptance or HR on behalf with evidence → hire → HireWizard;
  - reserve list and recall, competitions pointer, reports pointer;
  - no `IsLabScale` instruction as a user step — state the G-02 limitation once, in a note.
- **`HIRE_RBAC_MATRIX.md`:** verify each row against the policies in code. Fix the ESS row (the real role behind `Module.Ess`). Make sure it includes the compliance reviewer, committee secretary/chair/member, assessor, exam marker, the candidate (OTP session), the report readers (PLAN-32 names rule), and the branch scope.
- **`MESSAGE_EVENT_MATRIX.md`:** `ICandidateAccepted` v4, `IContractActivated` with real values, `IRecruitmentRequisitionClosed` (PLAN-32), and the internal application events and notifications — check each entry against the code.
- **`DATABASE_SCHEMA_INVENTORY.*`:** regenerate with the existing generator (`scripts/extract_database_inventory.py` / `database_schema_parser.py`) so that the PLAN-26..32 tables are present.
- **`completion-backlog/09` (the HCM-001 card):** status and closure evidence point to real entities, tests and `acceptance-evidence` folders, and to the gap study.
- **`THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md`:** the recruitment line, with its proven status and evidence links.
- **`HCM_001_GAP_STUDY.md`:** the status of every G item (closed by which plan and evidence; open: G-01 partially — local commits, CI not run; G-02/G-04/G-14/G-15 → PLAN-25). The §7 completion criteria get a checklist with the honest state of each.
- **CI files:** `ci.yml` / `e2e-journeys.yml` changes from P1/P4, validated locally (decision 7).

Commit: `docs(hcm033): recruitment docs match the code — domain model, onboarding guide, RBAC, events, schema, backlog, truth register, gap status`.

## P8 — Validate script, independent review, final report

- **`scripts/hcm033-validate.ps1`:**
  - the architecture test passes;
  - no `WriteResult("Skip"` anywhere;
  - every recruitment evidence folder has the eight items and a `result.json`;
  - the docs no longer mention `SharedKernel/Recruitment` as runtime;
  - `HIRE_ONBOARDING_GUIDE.md` contains no «مختصر → تعيين» flow;
  - the YAML parses;
  - runs the `Hcm001|Hcm033|Acceptance*Architecture|AcceptanceGate` unit tests.
- **Independent review:** a fresh subagent reads this prompt, the plan and `git diff main...HEAD`. It hunts for:
  - any remaining path where a test can pass without checking;
  - evidence folders whose `result.json` contradicts the test outcome;
  - doc statements not backed by code or evidence;
  - required scenarios not marked.

  Each finding is attacked by a refuter agent. Fix what survives, and record it.
- **`reports/plan33-final-report.md`** (English; quote Arabic as-is):
  1. Summary per phase.
  2. Commits.
  3. The acceptance inventory, before vs. after.
  4. The per-scenario live results table (Pass / Fail / Blocked + reason + evidence path).
  5. E2E results (acceptance profile, and the prod-like stop point).
  6. Product defects found (fixed / follow-up).
  7. Docs corrected.
  8. The §7 completion-criteria checklist with an honest state.
  9. What PLAN-25 must do to make the prod-like journey pass.
  10. Follow-ups.
- Update the PLAN-33 doc status (`Implemented` / `Implemented (CI not executed)`) with implementation notes and deviations.

Commit: `docs(hcm033): plan status, validate script, final report`.

## Definition of done

- No acceptance test can pass without checking: the architecture test enforces it, and `ACCEPTANCE_REQUIRE_ALL` turns required skips into failures.
- AC-HCM-21a–d, AC-HCM-22a–e and AC-WF-23a–f are implemented on deterministic seeded states.
- Every recruitment scenario (21, 22, 26–33, WF-23) has an evidence folder with the eight items and a truthful result from a live run.
- The requisition-to-hire E2E is green on the acceptance profile and wired into the journey CI.
- The prod-like variant exists, with its stop point recorded.
- `SharedKernel.Recruitment` is removed (or marked obsolete with a reason), and HCM-001 is proven by behaviour tests on the real model.
- The listed docs match the code, and every status points to evidence.
- Build has 0 errors; tests pass; `scripts/hcm033-validate.ps1` passes; `reports/plan33-final-report.md` exists.

Then reply with a short summary and the path of the final report.
