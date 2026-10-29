# PLAN-33 — Final report

**Date:** 2026-10-29
**Branch:** `feature/plan-33-verification-docs` (local only, **not pushed**), from `main` @ `4342bb6` (PLAN-26..32 merged).
**Plan:** [`docs/acceptance-plans/PLAN-33-hcm-verification-docs.md`](../docs/acceptance-plans/PLAN-33-hcm-verification-docs.md) —
Status **Implemented (CI not executed)**. Execution prompt:
[`docs/acceptance-plans/execution/PLAN-33-AGENT-PROMPT.md`](../docs/acceptance-plans/execution/PLAN-33-AGENT-PROMPT.md).
Progress log with decisions K1–K10, notes per phase and run tables: [`reports/plan33-progress.md`](plan33-progress.md);
baseline: [`reports/plan33-baseline.md`](plan33-baseline.md); inventory before: [`reports/plan33-acceptance-inventory.md`](plan33-acceptance-inventory.md).

## 1. Summary per phase

| Phase | Scope |
|-------|-------|
| P0 | Baseline (build 0 errors; unit filter 1191/1192, the one failure is the known `BolaArchitectureTests` baseline), acceptance inventory (103 methods, 9 silent-pass occurrences, 4 HTML-scraping data lookups), stack check (File.Storage missing), decisions 1–8 in the plan |
| P1 | `AcceptanceGate` (Blocked + Skip, or Fail for `[RequiredAcceptance]` under `ACCEPTANCE_REQUIRE_ALL=true`); every silent pattern removed (Skip+return in AcHcm21/22/AcEst24, Pass+return in Ac33/Ac37) and all 34 raw `Skip.If` gates routed through the gate; conditional assertion bypasses fixed (Ac12, Ac20, Ac32, AcHcm30c); per-scenario aggregation in `result.json`; `AcceptanceSkipPatternArchitectureTests`; `ACCEPTANCE_REQUIRE_ALL` in the CI acceptance job |
| P2 | `AcceptanceRecruitmentStatesPatch` (deterministic, read-only states in RIF: requisitions Draft/PendingApproval step 1 and 2/Approved/Published, criteria v1, applications Submitted…Ranked 1/2/3, offers PendingApproval and Issued, pending appeal assigned to `a-compliance`, committee sessions, legacy `ApprovalRequest`, 40-position pool for state-changing flows) + `testdata/acceptance/recruitment-states.json` + loader + tests; C# twin of the Keycloak user-id derivation |
| P3 | AC-HCM-21a–d, AC-HCM-22a–e, AC-WF-23a–f rewritten on the seeded ids (no HTML scraping for data), eight evidence items each, real `audit.json`; G-02 behaviour proven by `Hcm033LabScaleBehaviourTests` (integration, real host); File.Storage fixed in the acceptance stack (Local provider) |
| P4 | `RecruitmentToHireJourneyE2ETests` (requisition → hire → HR.Contracts, one refusal per stage) green on the acceptance profile; prod-like variant + `docker-compose.acceptance-prodlike.yml`, run once: stops at the first approval (G-02); CI wiring |
| P5 | `SharedKernel.Recruitment` (3 phantom files) deleted — no runtime consumer; `Hcm001RecruitmentTests` now behaviour tests on `HR.Core.Domain`; `hcm001-validate.ps1` checks the real model |
| P6 | All images rebuilt from the branch; clean reset; full recruitment run with `ACCEPTANCE_REQUIRE_ALL=true`: 45/46 Pass, AC-HCM-26h Blocked; journey Pass; evidence regenerated |
| P7 | Docs corrected (one writer + one independent verifier per document) |
| P8 | `scripts/hcm033-validate.ps1`; independent review (4 hunters, 2 refuters per finding; 20 findings, 18 survived) and fixes; final run on committed code; this report |

## 2. Commits

```
(P8)    docs(hcm033): plan status, validate script, final report
dec3133 fix(hcm033): review fixes — required-ness survives awaits, no stale Pass, journey records Blocked, AC-WF-23c control
c0d7e91 docs(hcm033): recruitment docs match the code — domain model, onboarding guide, RBAC, events, schema, backlog, truth register, gap status
4467a4e test(hcm033): live acceptance evidence for AC-HCM-21/22/26–33 and AC-WF-23
69ad2e3 test(hcm033): replace SharedKernel recruitment reflection tests with behaviour tests on HR.Core.Domain
d91b7b8 test(hcm033): requisition-to-hire E2E journey (acceptance profile) and prod-like variant
95c70e1 test(hcm033): AC-HCM-21a–d, AC-HCM-22a–e and AC-WF-23a–f on seeded states with full evidence
4588a23 fix(acceptance): run File.Storage on the Local provider so the acceptance stack is complete
29670a0 fix(hcm033): save the recruitment-states offers before linking them (relational insert cycle)
b93ddba test(hcm033): deterministic recruitment states seed and recruitment-states.json
5d4e4d0 test(hcm033): explicit acceptance skips, required scenarios and silent-pass architecture guard
7c9dcf2 docs(hcm033): baseline, acceptance inventory, pre-implementation notes
```

Author identity: the repository's existing `omardefdev` (no git config change). Nothing pushed.

## 3. Acceptance inventory, before vs. after

| Measure | Before (`4342bb6`) | After |
|---------|--------------------|-------|
| Test methods in the acceptance project | 103 | 127 (adds AC-HCM-21b–d, 22a/c/d/e, WF-23b–f, gate/aggregation/recruitment-states tests) |
| Silent `WriteResult("Skip"/"Pass") + return` | 9 occurrences in 6 methods | 0 — forbidden by `AcceptanceSkipPatternArchitectureTests` |
| Raw `Skip.If` / `Skip.IfNot` (no Blocked evidence, ignores REQUIRE_ALL) | 34 | 0 (only inside `AcceptanceGate`) — forbidden by the guard |
| Conditional assertion bypasses (inventory §B) | Ac12, Ac20, Ac32, AcHcm30c, AcHcm21/22 OR-assertions, AcEst24e, AcWf23 | fixed (gated, or rewritten with precise assertions) |
| HTML scraping to pick the record under test | 4 (Ac17, AcEst24a, AcHcm21, AcHcm22) | 2 (Ac17, AcEst24a — not recruitment; follow-up) |
| `[RequiredAcceptance]` scenarios | none (attribute did not exist) | AcHcm21, 22, 26–32, AcWf23 (10 classes) |
| `result.json` per shared AC folder | last test wins | per-scenario map, worst status wins; a started scenario counts as Fail until it records its result |
| Evidence folders AC-HCM-21, 22, WF-23, 33 | absent | present, eight items each |
| Acceptance run without the stack | (silent passes possible) | 26 passed (stack-free tests only), 101 skipped, 0 failed |

## 4. Per-scenario live results

Acceptance stack (lab profile: `NationalScope:IsLabScale=true`, policy engine Off), clean reset, all images built from the
branch, `ACCEPTANCE_REQUIRE_ALL=true`, 2026-10-29. Final run on committed code `dec3133`: see §4.1.

| Scenario | Result | Reason / note | Evidence |
|----------|--------|---------------|----------|
| AC-HCM-21a submit → two-step approval → publish | Pass | | `acceptance-evidence/AC-HCM-21/` |
| AC-HCM-21b creator SoD (UI + direct POST) | Pass | seeded PendingApproval requisition | 〃 |
| AC-HCM-21c reject → Draft with reason | Pass | | 〃 |
| AC-HCM-21d lab-scale submit creates the workflow | Pass | non-lab case: `Hcm033LabScaleBehaviourTests` (integration, 6/6) | 〃 |
| AC-HCM-22a offer → two-step approval → Issued → accepted (HR on behalf, signed PDF) | Pass | needs File.Storage (fixed); candidate self-acceptance in AC-HCM-30 and the journey | `acceptance-evidence/AC-HCM-22/` |
| AC-HCM-22b offer creator SoD | Pass | seeded PendingApproval offer | 〃 |
| AC-HCM-22c committee: below quorum refused; chair + 3 → submit → 3 approvals → ApprovedAt | Pass | | 〃 |
| AC-HCM-22d appeal: assigned compliance reviewer decides; non-assigned refused (404 + refused Decide) | Pass | PLAN-28 design | 〃 |
| AC-HCM-22e lab-scale offer submit creates the workflow | Pass | non-lab: integration test | 〃 |
| AC-WF-23a–f work inbox (step roles, scope, creator SoD, assigned reviewer, count = rows, legacy request) | Pass ×6 | 23c has a precondition + positive control since P8 | `acceptance-evidence/AC-WF-23/` |
| AC-HCM-26 (a–e, f–g, g, i) | Pass ×4 | | `acceptance-evidence/AC-HCM-26/` |
| AC-HCM-26h freeze cascade | **Blocked** | no second central `hr-core` persona to approve the establishment step (SoD with central-a); covered on PostgreSQL by `Hcm026EstablishmentCascadeIntegrationTests`; folder status Blocked | 〃 |
| AC-HCM-27 (4 tests) | Pass ×4 | 27f–g failed in PLAN-32 without File.Storage; passes now | `acceptance-evidence/AC-HCM-27/` |
| AC-HCM-28 (4 tests) | Pass ×4 | | `acceptance-evidence/AC-HCM-28/` |
| AC-HCM-29 (3 tests) | Pass ×3 | | `acceptance-evidence/AC-HCM-29/` |
| AC-HCM-30 (8 tests) | Pass ×8 | 30c (document scan, EICAR) skipped in PLAN-32; passes now | `acceptance-evidence/AC-HCM-30/` |
| AC-HCM-31 (2 tests) | Pass ×2 | single-use fixtures: needed `acceptance-reset.ps1` (first run reported them Fail under REQUIRE_ALL) | `acceptance-evidence/AC-HCM-31/` |
| AC-HCM-32a–d | Pass ×4 | | `acceptance-evidence/AC-HCM-32/` |
| AC-HCM-06 candidate DSAR | Pass | | `acceptance-evidence/HCM-06-DSAR/` |
| AC-HCM-33a no data → Skipped, never Passed | Pass | 26 passed (stack-free) / 101 skipped / 0 failed without the stack | `acceptance-evidence/AC-HCM-33/` |
| AC-HCM-33b REQUIRE_ALL turns a blocked required scenario into a failure | Pass | proven for gates in test bodies (26h, 30j, 31 in run 1) and — after the P8 fix — for a gate in an async helper after an await (pool exhausted on purpose, `33b-helper-gate-proof.log`) | 〃 |
| AC-HCM-33c requisition-to-hire journey | Pass | §5 | 〃 |
| AC-HCM-33d eight items in every recruitment folder | Pass | `scripts/hcm033-validate.ps1` | 〃 |

### 4.1 Final run on committed code

After the P8 review fixes were committed (`dec3133`), on a stack reset again with `acceptance-reset.ps1` +
`acceptance-up.ps1 -NoBuild -SkipLiveVerification` (which now completes on Windows PowerShell 5.1 — the Keycloak-sync fix
verified end to end):

| Run | Result |
|-----|--------|
| Recruitment acceptance (AcHcm21, AcHcm22, AcWf23, AcHcm26..32, AcHcm06; `ACCEPTANCE_REQUIRE_ALL=true`) | **45/46 Pass**; AC-HCM-26h Blocked → Fail under REQUIRE_ALL (by design) |
| Journey (lab profile) | **Pass** |
| Journey (prod-like, re-run on committed code) | **Fail at «1 requisition»** — G-02, as expected |
| Helper-level REQUIRE_ALL proof (AC33 pool frozen on purpose, AC-HCM-21a) | **Failed as required** («Required acceptance scenario blocked … AC33-POOL positions are used up»), raised in `FreePoolPositionAsync` after an await; pool restored |
| `scripts/hcm033-validate.ps1` (with tests) | **PASS** |
| Unit regression, P0 filter (Hcm0, Acceptance, E2e, Architecture) | 1197/1198 — the one failure is the known `BolaArchitectureTests` baseline |
| `Hcm033LabScaleBehaviourTests` (integration) | 6/6 |

Note: a run immediately before this one (same code, no reset) reported 28a/c/d, 30j, 31a and 31b–g as failures: their
seed fixtures are single-use and had been consumed by the previous run. REQUIRE_ALL turned those Blocked preconditions
into failures, which is the intended behaviour; after the reset they pass. HTTP captures left by discarded runs (56
run-specific committee/vote files in AC-HCM-22/28) were pruned so every committed capture comes from the final runs.

## 5. E2E results

**Acceptance profile — Pass.** `src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs` (`Category=Journey`), all
HTTP through the gateway (port 6200):

1. central-a: requisition on a free AC33-POOL position, criteria v1, submit → PendingApproval + RECRUITMENT-REQUISITION-APPROVAL.
   Refusal: a-management-approver gets no approve form at step 1 (UI; the server-side role check is G-04).
2. br1-mgr step 1 → a-management-approver step 2 → Approved → publish (closing date +10 days).
3. Refusal: closing date moved to yesterday (test-only SQL, restored) → the posting shows no form and a POST with an earlier
   token is refused (no citizen row). Citizen applies on `/careers` with profile, consent and a PDF CV (File.Storage + ClamAV).
4. Screen → COI → shortlist → interview (moved into the past by test-only SQL, as AC-HCM-26) → attended → scores by two
   assessors → Assessed → rank 1. Refusal: an offer before ranking creates nothing.
5. Offer at the G5 mid salary → submit → two approvals → Issued. Refusals: the creator's own approval; hire before acceptance.
6. Citizen OTP sign-in (code from the FileDrop message). Refusal: a wrong code opens no session. Accepts on `/careers/my`.
7. Hire as a new employee.
8. HR.Contracts: `Appointment` transaction linked to the application and offer; contract with the offer grade and salary
   and the offer as source. Correlation (Ac15 pattern): the gateway echoes the run's `X-Correlation-ID` on the hire, and
   `gateway_audit_entries` hold it for the HR.Core hire and for HR.Contracts requests.

Playwright browsers are **not** used: they are not installed on this machine (installing needs pwsh 7 and a download), and
the HR screens are server-rendered forms whose requests the test makes directly. This deviates from the prompt's "Playwright
for the UI steps"; the rest of the E2E project still uses Playwright.

**Prod-like — stops at the first approval (expected).** `docker-compose.acceptance-prodlike.yml` (IsLabScale=false,
HardEnforceCeilings=true, no PolicyEngine section → Off, CommitteeApprovalDefault=true, Retention.Apply=false, Contracts
fallback off; environment stays `Acceptance` for personas, seed and FileDrop). `RecruitmentToHireJourneyProdLikeE2ETests`
(`Category=JourneyProdLike`, excluded from CI) fails at stage «1 requisition»: after submit the requisition is
`PendingApproval` with **no workflow instance**, so it can never be approved (G-02). Evidence:
`acceptance-evidence/AC-HCM-33/prodlike/`.

## 6. Product defects found

| Defect | Status |
|--------|--------|
| Acceptance stack: File.Storage never started (pinned MinIO image no longer pullable) — every upload failed (AC-HCM-30c, on-behalf evidence in AC-HCM-27/28/29) | **Fixed** (`4588a23`): File.Storage on its Local provider in the acceptance override; MinIO opt-in profile. Proven by 22a, 27f–g, 30c passing |
| `acceptance-up.ps1` Keycloak sync fails on Windows PowerShell 5.1 (BOM on stdin) | **Fixed** (`4467a4e`); verified by running the sync step with the same settings, not by a second full reset |
| PLAN-33's own seed: application ↔ active offer insert cycle crashed HR.Core start-up on PostgreSQL | **Fixed** (`29670a0`) with an SQLite regression test |
| `e2e-journeys.yml` started no application services (no `--profile full-stack`) and had no zero-skip step | **Fixed** (`d91b7b8`); CI not executed |
| HR.Contracts applies the message `X-Correlation-ID` to its execution context but neither persists nor logs it on the success path | Follow-up (observability), not hidden: the journey asserts the gateway-side correlation and the lineage instead |
| G-02 / G-04 / G-14 / G-15 | Open — PLAN-25 (out of scope) |

No product code change was needed for a scenario to pass (decision 8 not triggered).

## 7. Docs corrected

`HCM_001_RECRUITMENT.md` (real domain model, plan/status/evidence matrix PLAN-21..33, phantom artifacts removed, known gaps),
`HIRE_ONBOARDING_GUIDE.md` (one flow through the approved, accepted offer; no «مختصر → تعيين»; G-02 once as a note),
`HIRE_RBAC_MATRIX.md` (ESS = `hr-employee`/`Module.Ess`; compliance reviewer, committee chair/member, assessor, exam
marker, candidate OTP session, report readers, branch scope, G-04), `MESSAGE_EVENT_MATRIX.md` (ICandidateAccepted v4,
IContractActivated, IRecruitmentRequisitionClosed v1, internal events), `DATABASE_SCHEMA_INVENTORY.{json,md}` (generator
not run — no Python; checks re-implemented, Dev.Analytics `RecruitmentRequisitionFacts` added),
`completion-backlog/09` HCM-001 card, `THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md`, `HCM_001_GAP_STUDY.md` (status per G
item, §7 checklist). Each document was edited by one agent and checked against code and evidence by another; the P8
review found eight further doc defects, all fixed.

## 8. §7 completion criteria (gap study)

| Criterion | State |
|-----------|-------|
| 1. All P0/P1 gaps closed | **Not met** — G-02, G-04, G-14, G-15 open (PLAN-25); G-01 partly (local commits, CI never executed); G-05 partly |
| 2. One E2E green with Production settings | **Not met** — the prod-like journey exists and stops at the first approval (G-02); the lab-profile journey is green |
| 3. Evidence for every positive/negative scenario; no test passes without checking | **Partly met** — AC-HCM-21/22/26–33 and AC-WF-23 have truthful evidence; AC-HCM-26h is Blocked; PLAN-25 scenarios do not exist yet; the guard and the gate make silent passes impossible |
| 4. Docs reflect reality | **Met** (P7 + P8 review fixes) |

## 9. What PLAN-25 must do for the prod-like journey to pass

1. **Bind approvals in production (G-02):** with `IsLabScale=false` the three lifecycle services (requisition, offer,
   committee) must start their workflow — either through `PolicyEngine:Core.Recruitment=Enforce|Shadow` with seeded
   bindings, or by starting the workflow regardless of lab scale when the policy engine is Off. The committee path also
   needs a warning (today it locks silently).
2. **Server-side step roles (G-04):** refuse an approval whose actor lacks the step's `RoleRequired` (today only the UI hides it).
3. Configure the production settings the journey relies on: a candidate notification provider other than FileDrop with a
   readable test channel, `Recruitment:Portal:OtpHmacKey`, real Keycloak/AllowedHosts/caller-proof values for a staging run.
4. Then run `RecruitmentToHireJourneyProdLikeE2ETests` and, when green, move it into the CI journey filter.

## 10. Follow-ups

- Add a second central `hr-core` persona (personas catalog + Keycloak) so AC-HCM-26h can run live.
- HR.Contracts: persist or log the message correlation id (so Ac15-style tracing covers the consumer).
- `scripts/acceptance-verify.ps1` expects stale row counts (25 applications, 6 requisitions, 55 AuthUsers) — update it.
- Shared AC folders keep `before/after/audit.json` of the last scenario only (result.json is per scenario); consider per-scenario files for all eight items.
- Ac17 and AcEst24a still pick their record from HTML (outside the recruitment scope).
- Make the single-use fixtures of AC-HCM-28a/c/d, 30j and 31 re-armable (today each live run after the first needs `acceptance-reset.ps1`).
- Run the CI workflows remotely (never executed here): `ci.yml` acceptance job with `ACCEPTANCE_REQUIRE_ALL`, `e2e-journeys.yml` `recruitment-journey` job.
- Install Playwright browsers (pwsh 7) if browser-level UI checks of the journey are wanted.
