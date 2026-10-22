# Task: PLAN-26 wrap-up — acceptance tests, validate script, execution doc, docs (finish session 2)

## Role

You are a senior .NET 10 engineer in the `HRManagement` repository (Windows, PowerShell, Docker Desktop). PLAN-26 session 2 stopped after the integration-test commit. Finish **only** the missing items below. **Do not change production code**, except to fix a defect that a test proves (report it if you do).

## Current state (verified)

- Branch `feature/plan-26-assessment-interview-requisition` (local, not pushed). Last commit: `05a45bf test(hcm026): integration tests on PostgreSQL (index + freeze cascade)`.
- Done:
  - session 1 (A, B, C, F1) — see `reports/plan26-session1-handoff.md`
  - session 2 commits `3bb4ea5` (D: integrity + cascades), `2ca5a97` (E: closing date + intake job), `303c35f` (F2: seeders), `05a45bf` (integration tests)
- Missing:
  - acceptance tests `AcHcm26AssessmentInterviewTests`
  - `scripts/hcm026-validate.ps1`
  - `docs/acceptance-plans/execution/PLAN-26-EXECUTION.md`
  - doc updates (PLAN-26 status is still "Proposed")
  - a final report
- Known blocker: `src/Tests/HRManagement.AcceptanceTests` does not compile — 3 pre-existing CS1061 errors (`HttpClient.PostAsJsonAsync`) in `AcEst14JobCatalogCodPreviewTests.cs`, `AcEst15JobCatalogBulkImportTests.cs` and `AcEst17JobCatalogMigrationLegacyTests.cs` (missing `using System.Net.Http.Json;`).
- Local-only noise **that must never be committed**:
  - `global.json` (local `rollForward` change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*` temp files
  - `tmp-vendors.html`
  - running the full unit suite rewrites tracked `testdata/*/out/*.json` — restore them with `git checkout -- testdata` before committing

## Read first

1. `reports/plan26-session1-handoff.md`
2. `git log --format='%h %s%n%b' 544ba7a..HEAD` — the session 2 commit bodies describe what was built
3. `docs/acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md` (AC-HCM-26a..i) and `docs/acceptance-plans/README.md` (evidence contract)
4. `src/Tests/HRManagement.AcceptanceTests/AcHcm22RecruitmentWorkflowsUiTests.cs` (harness usage) and `scripts/hcm022-validate.ps1`

## Steps

1. **Verify state.**
   - `git status` (only the local noise above may be dirty).
   - `dotnet build HRManagement.slnx`.
   - `dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm026|Recruitment|Hcm|SelectionWorkflow|RequisitionWorkflow|OfferWorkflow|Est0|Wf02"` — compare with the handoff (280 passed at the end of session 1, plus session 2's new tests). If something that was green is red, stop and report.
2. **Fix the acceptance project build:** add `using System.Net.Http.Json;` to the three files. Commit: `fix(tests): acceptance project compiles (System.Net.Http.Json)`.
3. **Acceptance tests** `src/Tests/HRManagement.AcceptanceTests/AcHcm26AssessmentInterviewTests.cs`:
   - Cover **AC-HCM-26a..i** with `EvidenceRecorder("AC-HCM-26", correlationId)`.
   - Personas: `central-a` and `br1-mgr` (two hr-core assessors), `a-management-approver`.
   - When preconditions are missing use `Skip.If(condition, reason)`. **Never** `recorder.WriteResult("Skip", …); return;`.
   - Prefer deterministic seeded data (acceptance seeder) over scraping the first link in an HTML list.
   - Integration tests: if Docker is available, run `dotnet test src/Tests/HRManagement.IntegrationTests --filter Hcm026` and record the result.
   - If the local stack is available (`pwsh scripts/acceptance-up.ps1`), run with `$env:RUN_ACCEPTANCE="true"` and produce `acceptance-evidence/AC-HCM-26/`. Otherwise state clearly that it was not run.
   - Commit: `test(hcm026): acceptance AC-HCM-26a..i`.
4. **`scripts/hcm026-validate.ps1`**, modelled on `scripts/hcm022-validate.ps1`: checks the PLAN-26 files and the four `HCM026_*` migrations exist (`CriteriaGovernance`, `AssessmentScores`, `InterviewLifecycle`, `RequisitionIntegrity`), then runs the unit tests filtered by `Hcm026` and the `Hcm021|Hcm022` regression.
5. **Docs:**
   - `docs/acceptance-plans/execution/PLAN-26-EXECUTION.md`, modelled on `PLAN-22-EXECUTION.md`: prerequisites, commands, personas, Arabic manual steps for all of PLAN-26, evidence folder.
   - `PLAN-26-hcm-assessment-interview-requisition.md`:
     - Status → `Implemented (acceptance evidence pending)`, or `Implemented` if acceptance ran green
     - add "Implementation notes / deviations" for both sessions (handoff §7 + session 2 commit bodies)
   - `docs/HCM_001_RECRUITMENT.md`: sections for criteria versions, scoring formula, interview lifecycle, requisition integrity (cancel/close/freeze cascade), intake window. Mark G-03/G-08/G-11 as addressed.
   - `docs/HIRE_ONBOARDING_GUIDE.md`: update the selection steps (criteria before publish, scoring sheet, interview completion, closing date and extension, cancelling a requisition). Touch only those sections.
   - `docs/DATABASE_SCHEMA_INVENTORY.*`: run the generator script if one exists under `scripts/`, otherwise update the HR.Core recruitment tables manually.
   - Commit: `docs(hcm026): plan status, execution, guides, validate script`.

## Final report (English)

1. Commits added (sha + message).
2. Test results: unit, integration (run or not), acceptance (run or not, evidence path).
3. Any production-code fix and why.
4. PLAN-26 overall status and anything left open.
5. The exact `git log --oneline 9cb3705..HEAD` output, so the next plan can branch from it.
