# PLAN-33 — Progress (checkpoint)

**Branch:** `feature/plan-33-verification-docs` (from `main` @ `4342bb6`; local only, never pushed)
**Prompt:** `docs/acceptance-plans/execution/PLAN-33-AGENT-PROMPT.md` · **Plan:** `docs/acceptance-plans/PLAN-33-hcm-verification-docs.md`
**Resume rule:** read this file and `git log --oneline -20` first; continue from "Next step"; never redo a committed phase.

## Phase status

| Phase | Status | Commit |
|-------|--------|--------|
| P0 Baseline, inventory, notes | done | (this commit) |
| P1 Explicit skips, required scenarios, architecture guard | done | (P1 commit) |
| P2 Deterministic recruitment states | pending | — |
| P3 AC-HCM-21/22, AC-WF-23 | pending | — |
| P4 Requisition-to-hire E2E + prod-like | pending | — |
| P5 Remove phantom evidence | pending | — |
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

Commit P2 (seed patch, constants, `recruitment-states.json`, loader and tests are written and green), then P3.

## P1 notes

- K6: `EvidenceRecorder.ScenarioKey` — several tests share one AC folder; `result.json` now keeps a `scenarios` map and its
  top-level status is the worst (Fail > Blocked > Pass), so a later Pass can no longer hide an earlier Fail/Blocked
  (inventory §C). `WriteScenarioSteps` / `WriteScenarioDbChecks` write `steps-<key>.md` / `db-checks-<key>.json`.
- K7: all 34 raw `Skip.If`/`Skip.IfNot` data gates in `Ac*` files now go through `AcceptanceGate.RequireData` (Blocked
  evidence + REQUIRE_ALL); the guard forbids raw skips. Conditional assertion bypasses fixed: Ac12 (submit/approve
  gated, SoD assertions always run), Ac20 (log/cross-db files gated), Ac32 (asset NotFound gated, then 200 asserted),
  AcHcm30c (File.Storage upload failure → Blocked). AcHcm26h logs in before the second-persona check (recorder needed).
- `[RequiredAcceptance]` on AcHcm21, 22, 26–32, AcWf23.
