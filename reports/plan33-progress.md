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
| P3 AC-HCM-21/22, AC-WF-23 | done | (P3 commit) |
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

P4: write `RecruitmentToHireJourneyE2ETests` (HTTP through the gateway on the acceptance stack), the prod-like variant, CI wiring; run both live.

## P3 notes

- `RecruitmentAcceptance.cs`: one shared UI client (`AcceptanceUi`), persona sessions, SQL helpers and recruitment
  steps for AC-HCM-21/22 and AC-WF-23 (AC-HCM-26..32 keep their own copies; not refactored in this plan). `audit.json`
  is now a real `AuditLogs` query for the state-changing scenarios.
- Scenario mapping: 21b, 22b (SoD) and 23a–f (inbox) read the seeded states; 22c negative uses the seeded chair-only
  session and the positive uses the seeded roster on a fresh pool requisition (committee submit needs an assessed
  candidate, so the seeded ready session — on the approved requisition without candidates — provides the roster);
  22d positive files a new appeal on the seeded published requisition, the negative uses the seeded pending appeal
  (a-compliance-2 → 404 and refused Decide; its token page is a second appeal assigned to it, which it then dismisses).
  21a/c/d, 22a/c/e create their requisitions on the pool. 21c/21d/22e clean up (reject/cancel).
- Test-only SQL preconditions (documented in steps): 22a/22e Ranked 1, 22c Assessed — ranking is AC-HCM-26's scope; the
  real path runs in the E2E journey.
- G-04 (open): a step approved by the wrong role is not refused server-side; not asserted as a refusal anywhere.
- Non-lab behaviour (21d/22e, G-02): `src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs` — real
  host, HTTP form posts, 6/6: non-lab requisition and offer → PendingApproval without workflow + warning + approve
  refused (offer never Issued); non-lab committee → locked without workflow and **no** warning (silent stall);
  lab counterparts start the workflows.
- Seed defect found live and fixed (`29670a0`): application ↔ active offer insert cycle crashed HR.Core start-up on
  PostgreSQL; the seed test now runs on SQLite and reproduces it without the fix.
- Decision 6 fixed in `fix(acceptance)` commit: File.Storage on the Local provider (22a's on-behalf acceptance with a
  signed PDF now passes, proving uploads work).

| Live run (acceptance stack, `ACCEPTANCE_REQUIRE_ALL=true`) | Result |
|---|---|
| AcWf23 + 21b + 22b (first run) | 8/8 Pass |
| 21a/c/d, 22a/c/d/e (after fixing the pool SQL: Status stored as text) | 7/7 Pass |
| AcHcm21 + AcHcm22 + AcWf23 second pass (repeatability) | 15/15 Pass |
| `Hcm033LabScaleBehaviourTests` (integration) | 6/6 Pass |
