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
| P4 Requisition-to-hire E2E + prod-like | done | (P4 commit) |
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

Commit P5 (SharedKernel.Recruitment deleted, Hcm001 behaviour tests and hcm001-validate.ps1 already written and green — re-run the git grep proof first), then P6.

## P4 notes

- `src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs` (`Category=Journey`) + `RecruitmentJourneyHarness.cs`
  (self-contained copies of the acceptance helpers: the E2E project cannot reference the acceptance project, which
  references it). All steps are HTTP through the gateway; **Playwright browsers are not used** — they are not installed
  here (installing needs pwsh 7 and a download) and the HR screens are server-rendered forms; the test sends the same
  requests the UI does. Deviation from the prompt, recorded for the final report.
- Real path, no ranking shortcut: criteria → two-step approval → publish → public apply with PDF CV (File.Storage +
  ClamAV) → screen → COI → shortlist → interview → two assessors → Assessed → rank 1 → offer → two-step approval →
  Issued → candidate OTP sign-in (code from FileDrop) → accept on /careers/my → hire → HR.Contracts Appointment +
  contract (grade G5, offer salary, source offer). Test-only SQL: interview slot moved into the past (as AC-HCM-26), the
  closing date moved to yesterday and restored for the late-apply refusal.
- Refusals per stage: approver has no approve form at step 1 (server-side role check = G-04, not asserted as refusal);
  late apply (closed page has no form; POST with an earlier token refused, no citizen row); offer before ranking (no
  offer row); creator approving own offer (state unchanged); hire before acceptance (not hired); wrong OTP (no session).
- Correlation (Ac15 pattern): gateway echo on the hire, gateway_audit_entries rows for the run id including the HR.Core
  hire and HR.Contracts requests; the async consumer side effect is tied by application/offer lineage. **Finding:**
  HR.Contracts applies the message X-Correlation-ID to its execution context but persists/logs it nowhere on the
  success path, and neither service's text logs print it → follow-up (observability), not a product defect.
- Prod-like: `docker-compose.acceptance-prodlike.yml` (IsLabScale=false, HardEnforceCeilings=true, no PolicyEngine →
  Off, CommitteeApprovalDefault=true, Retention.Apply=false, Contracts fallback=false; environment stays Acceptance).
  `RecruitmentToHireJourneyProdLikeE2ETests` (`Category=JourneyProdLike`, skips unless hr-core runs with
  IsLabScale=false). Run once: **stops at stage 1** — after submit `PendingApproval` with no workflow instance (G-02).
  Evidence: `acceptance-evidence/AC-HCM-33/prodlike/` (result Fail, stageReached «1 requisition»). A first attempt hit a
  transient 502 right after the container recreate (gateway upstream not ready) and was re-run.
- CI (not executed): `e2e-journeys.yml` — new `recruitment-journey` job (acceptance-up.ps1 -SkipLiveVerification,
  filter on the lab journey, zero-skip + must-pass check, evidence upload, reset); `journeys` job excludes the recruitment
  journey, gets the zero-skip step, and now passes `--profile full-stack` (without it only infrastructure started — a
  pre-existing defect found by the map). `ci.yml` e2e job (demo stack) excludes `JourneyProdLike` and the recruitment
  journey. Filters checked locally with `dotnet test --filter` (selection counts as intended).

| Live run | Result |
|---|---|
| Journey, acceptance profile (after fixes: closing-date token, correlation evidence) | Pass (15 s), again Pass after restoring the lab profile (22 s) |
| Journey, prod-like | Fail at «1 requisition» — G-02 (expected), evidence recorded |
