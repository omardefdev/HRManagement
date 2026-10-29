# PLAN-33 — Progress (checkpoint)

**Branch:** `feature/plan-33-verification-docs` (from `main` @ `4342bb6`; local only, never pushed)
**Prompt:** `docs/acceptance-plans/execution/PLAN-33-AGENT-PROMPT.md` · **Plan:** `docs/acceptance-plans/PLAN-33-hcm-verification-docs.md`
**Resume rule:** read this file and `git log --oneline -20` first; continue from "Next step"; never redo a committed phase.

## Phase status

| Phase | Status | Commit |
|-------|--------|--------|
| P0 Baseline, inventory, notes | done | (this commit) |
| P1 Explicit skips, required scenarios, architecture guard | done | `5d4e4d0
` |
| P2 Deterministic recruitment states | done | `b93ddba` (+ fix `29670a0`) |
| P3 AC-HCM-21/22, AC-WF-23 | done | `95c70e1` (+ stack fix `4588a23`) |
| P4 Requisition-to-hire E2E + prod-like | done | `d91b7b8` |
| P5 Remove phantom evidence | done | (P5 commit) |
| P6 Live acceptance run | pending | — |
| P7 Docs corrections and CI | pending | — |
| P8 Validate script, review, final report | pending | — |

## Decisions

D1–D8 are the prompt's decisions 1–8, recorded (Arabic) in the plan's «تعديلات قبل التنفيذ (2026-10-29)» block.

| # | Decision | Reason |
|---|----------|--------|
| K1 | `AcceptanceGate` lives in the acceptance project; its unit tests (`AcceptanceGateTests`) run there too, like `AcceptanceEvidenceSummaryTests`; `AcceptanceSkipPatternArchitectureTests` lives in `HRManagement.UnitTests/Architecture` (textual scan, no project reference needed). | The unit-test project does not reference the acceptance project. |
| K2 | `[RequiredAcceptance]` is detected by walking the stack to the first xUnit test method (async state machines resolved to their method); the decision core `AcceptanceGate.Evaluate(…, requireAll, required)` is tested directly so no test mutates process-wide environment variables. | Keeps the prompt's `RequireData(recorder, condition, reasonAr, reasonEn)` signature. |
| K3 | `AcceptanceEnv.RequireEnabled()` now delegates to `AcceptanceGate.RequireEnabled()`: not enabled → Skip, or **Fail** for a required scenario under `ACCEPTANCE_REQUIRE_ALL=true`. It writes no evidence (nothing ran; a local `dotnet test` without the stack must not overwrite committed evidence). `RequireEnabled(EvidenceRecorder)` exists for a caller that already has a recorder. | AC-HCM-33a/b. |
| K4 | The guard forbids more than the prompt's two patterns: any `WriteResult(…"Skip"…)` (also the ternary in `AcEst24`), any Pass/Fail/Blocked result followed within 3 lines by `return;`, and `WriteResult("Blocked"` outside `AcceptanceGate.cs`. Asserting helpers allow-list: `Assert.`, `AcceptanceGate.RequireData(`, `E2eHttp.EnsureNotServerError(`, `.EnsureSuccessStatusCode(`, `Should()`. No method needs the no-assert allow-list today. | A Blocked/Fail result followed by `return` is the same silent pass. |
| K5 | No YAML parser on this machine (no Python, Node, `actionlint`, YamlDotNet). YAML changes are validated by a structural check in `scripts/hcm033-validate.ps1` and the report says so. | Decision 7. |

## Files changed

- `docs/acceptance-plans/PLAN-33-hcm-verification-docs.md` — Depends on + pre-implementation block.
- `reports/plan33-baseline.md`, `reports/plan33-progress.md`, `reports/plan33-acceptance-inventory.md`.
- P1: `src/Tests/HRManagement.AcceptanceTests/AcceptanceGate.cs`, `AcceptanceGateTests.cs`, `AcceptanceHarness.cs`
  (`AcceptanceEnv.RequireEnabled`), `AcHcm21…`, `AcHcm22…`, `AcEst24…`, `Ac33…`, `Ac37…`;
  `src/Tests/HRManagement.UnitTests/Architecture/AcceptanceSkipPatternArchitectureTests.cs`; `.github/workflows/ci.yml`.

## Test and live-run results

| When | Run | Result |
|------|-----|--------|
| P0 | `dotnet build HRManagement.slnx` | 0 errors |
| P0 | unit `Hcm0|Acceptance|E2e|Architecture` | 1191/1192 — known baseline failure `BolaArchitectureTests` |
| P1 | `AcceptanceGateTests` | 8/8 |
| P1 | `AcceptanceSkipPatternArchitectureTests` | 7/7 (adds raw `Skip.If` rule) |
| P1 | `AcceptanceGateTests` (+ shared-folder aggregation) | 9/9 |
| P1 | acceptance project without stack | 23 passed (static), 90 skipped, 0 failed — no live test passes without the stack (AC-HCM-33a) |
| P1 | same, `ACCEPTANCE_REQUIRE_ALL=true`, filter `AcHcm2|Ac33` | 18 failed (required), 1 skipped (Ac33, not required) — AC-HCM-33b |

## Open issues

- `file-storage-web` is not running in the acceptance stack (no container) — decision 6, P6.
- Python unavailable → `DATABASE_SCHEMA_INVENTORY` regeneration (P7) may need another route.

## Next step

P6: full serial live run (rebuild every image from this branch, filter AcHcm21|22|AcWf23|AcHcm26..32|AcHcm06 with REQUIRE_ALL, then the journey), summarize evidence, check eight items per folder.

## P5 notes

- `git grep` over code, csproj, props, scripts (`*.ps1`, `*.py`, `*.sh`), JSON and YAML: no consumer of
  `HRManagement.SharedKernel.Recruitment` besides `Hcm001RecruitmentTests` and `hcm001-validate.ps1`; the thesis and
  diagram generators read DbContexts/snapshots only. The three files are **deleted** (no `[Obsolete]` needed).
- `Hcm001RecruitmentTests` (11): behaviour smoke tests on `RequisitionWorkflowRules`, `SelectionWorkflowRules`,
  `OfferWorkflowRules` (incl. `MarkIssued`/expiry), `RecruitmentAppealWorkflowRules` (window, single appeal, assigned
  reviewer), the real entity files, a guard that the SharedKernel folder stays gone, and the validate script.
- `scripts/hcm001-validate.ps1` checks the real HR.Core artifacts (entities, enums, the four rule classes, offer and
  application properties) and fails if `SharedKernel/Recruitment` returns; the doc check now looks for
  `RecruitmentApplication`. Result: PASS; Hcm001 11/11; solution build 0 errors.
