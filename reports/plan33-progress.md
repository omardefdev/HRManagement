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
| P5 Remove phantom evidence | done | `69ad2e3` |
| P6 Live acceptance run | done (1 scenario Blocked: AC-HCM-26h) | `4467a4e` |
| P7 Docs corrections and CI | done | (P7 commit) |
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

P8: independent review workflow (finder + refuter) over git diff main...HEAD, fix survivors, final report, PLAN-33 doc status, run scripts/hcm033-validate.ps1 with tests, commit.

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

## P6 notes

- All images rebuilt from this branch (`docker compose … build`, exit 0); full stack up incl. `file-storage-web`
  (Local provider) and ClamAV; MinIO not started. Keycloak personas sign in (no re-import needed).
- **Run 1** (prompt filter `AcHcm21|22|AcWf23|AcHcm26..32|AcHcm06`, `ACCEPTANCE_REQUIRE_ALL=true`): **42/46 Pass, 4 Fail**.
  AC-HCM-30c (document scan, File.Storage) and `Ac27_f_g` now **pass** — both failed/skipped in PLAN-32 for lack of
  File.Storage (decision 6 confirmed). The 4 failures are required scenarios that were Blocked (REQUIRE_ALL turns
  them into failures, as designed):
  - 30j, 31a, 31b–g: single-use seed fixtures consumed by earlier PLAN-30/31 runs → `acceptance-reset.ps1` + up.
  - 26h: no second central `hr-core` persona in agency A for ESTABLISHMENT-CHANGE-APPROVAL step 1 (SoD with
    central-a); pre-existing (Blocked in PLAN-26). Not fixable without a persona-catalog change (exactly 60 personas,
    Keycloak re-import, AuthUsers counts) → stays **Blocked** with its reason; follow-up. Coverage on PostgreSQL by
    `Hcm026EstablishmentCascadeIntegrationTests`.
- K10: PLAN-26..32 recorders now set `ScenarioKey` (the tag), so their folder `result.json` aggregates every scenario
  (before, it showed only the last one — AC-HCM-26 displayed 26h's Blocked for the whole folder).
- `acceptance-reset.ps1` + `acceptance-up.ps1 -NoBuild -SkipLiveVerification`: the whole stack came up healthy from
  empty volumes (File.Storage included); only the Keycloak sync step failed on Windows PowerShell 5.1 (BOM on stdin,
  known from the PLAN-30 guide). Fixed in `acceptance-up.ps1` (UTF-8 without BOM console encoding on PS < 6); the fix
  was verified by running the sync step with the same settings (exit 0), not by a second full reset.
- **Run 2** (clean stack, same filter, `ACCEPTANCE_REQUIRE_ALL=true`): **45/46 Pass**; only AC-HCM-26h fails as a
  required scenario Blocked by the missing persona (expected, see above).
- E2E journey on the clean stack: **Pass** (16 s). Prod-like evidence from P4 stands (run once, as required).
- `scripts/acceptance-evidence-summarize.ps1` regenerated `acceptance-evidence/summary.md`; `hcm033-validate.ps1
  -SkipTests`: every folder has the eight items + http.
- AC-HCM-33 folder: 33c from the journey; 33a/33b/33d added from the real runs that proved them (source recorded per
  scenario in result.json).

| Scenario | Result (P6, clean stack) | Evidence |
|---|---|---|
| AC-HCM-21a–d | Pass ×4 | acceptance-evidence/AC-HCM-21 |
| AC-HCM-22a–e | Pass ×5 | acceptance-evidence/AC-HCM-22 |
| AC-WF-23a–f | Pass ×6 | acceptance-evidence/AC-WF-23 |
| AC-HCM-26 a–e, f–g, g (cancel blocked), i | Pass ×4 | acceptance-evidence/AC-HCM-26 |
| AC-HCM-26h | **Blocked** — no second central hr-core persona (SoD); covered by `Hcm026EstablishmentCascadeIntegrationTests` | acceptance-evidence/AC-HCM-26 (folder status Blocked) |
| AC-HCM-27 (4 tests: a/c/d/h, b, e, f–g) | Pass ×4 (f–g failed in PLAN-32 without File.Storage) | acceptance-evidence/AC-HCM-27 |
| AC-HCM-28 (4 tests) | Pass ×4 | acceptance-evidence/AC-HCM-28 |
| AC-HCM-29 (3 tests) | Pass ×3 | acceptance-evidence/AC-HCM-29 |
| AC-HCM-30 (8 tests incl. c, j) | Pass ×8 (30c skipped in PLAN-32 without File.Storage) | acceptance-evidence/AC-HCM-30 |
| AC-HCM-31 (2 tests: a, b–g) | Pass ×2 (after reset) | acceptance-evidence/AC-HCM-31 |
| AC-HCM-32a–d | Pass ×4 | acceptance-evidence/AC-HCM-32 |
| AC-HCM-06 (candidate DSAR) | Pass | acceptance-evidence/HCM-06-DSAR |
| AC-HCM-33c journey | Pass | acceptance-evidence/AC-HCM-33 |
| AC-HCM-33 prod-like | Fail at «1 requisition» (G-02) | acceptance-evidence/AC-HCM-33/prodlike |

## P7 notes

- Doc workflow: one writer + one independent verifier per document (14 agents); every verifier corrected its file
  against code and evidence. Documents: `HCM_001_RECRUITMENT.md` (real HR.Core.Domain model, plan/status/evidence
  matrix PLAN-21..33 from P6, phantom artifacts removed, known gaps), `HIRE_ONBOARDING_GUIDE.md` (single flow via the
  approved offer and its acceptance; no «مختصر → تعيين»; G-02 once as a note; contract type from the regime map),
  `HIRE_RBAC_MATRIX.md` (ESS = `hr-employee` via `Module.Ess`; compliance reviewer, committee chair/member by employee,
  assessor = `hr-core` with no assessor role, exam marker, candidate OTP session, report readers, branch scope, G-04
  marked), `MESSAGE_EVENT_MATRIX.md` (ICandidateAccepted v4, IContractActivated, IRecruitmentRequisitionClosed v1,
  internal events, correlation gap), `DATABASE_SCHEMA_INVENTORY.{json,md}` (generator not run — no Python; checks
  re-implemented by reading DbContexts/snapshots; only gap was Dev.Analytics `RecruitmentRequisitionFacts`, added),
  `completion-backlog/09` HCM-001 card, `THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md`, `HCM_001_GAP_STUDY.md` (status
  per G item, §7 checklist: 1 not met, 2 not met, 3 partly met (26h Blocked), 4 met).
- CI files were changed in P1/P4 (structural check in `hcm033-validate.ps1`; CI not executed).
