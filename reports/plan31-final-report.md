# PLAN-31 — Final report

**Date:** 2026-10-29
**Branch:** `feature/plan-31-competitions-exams` (local only, not pushed), from the PLAN-30 tip `35d4efc` (PLAN-30 is not merged to `main`).
**Plan:** [`docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md`](../docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md) —
Status **Implemented and accepted (AC-HCM-31a..g Pass live)**. Execution guide:
[`docs/acceptance-plans/execution/PLAN-31-EXECUTION.md`](../docs/acceptance-plans/execution/PLAN-31-EXECUTION.md).
Progress log with decisions J1–J39, maps, files and test runs: [`reports/plan31-progress.md`](plan31-progress.md); baseline:
[`reports/plan31-baseline.md`](plan31-baseline.md).

## 1. What was built (per phase)

| Phase | Commit(s) | Scope |
|-------|-----------|-------|
| P0 | `a4ae72f` | Baseline, P0 maps (RequisitionId-null assumptions, marker access, legislation sources, PLAN-30 extension points), pre-implementation block in the plan, the prompt committed as-is |
| P1 | `29bbdb0` | `RecruitmentCompetition`, `CompetitionLine`, `CompetitionPreference`; per-competition legal inputs (no Production defaults); attach approved, unpublished requisitions by grade; RECRUITMENT-COMPETITION-APPROVAL (creator excluded); announcement (one posting and/or internal vacancy); HR screens; migration `HCM031_Competitions` |
| P2 | `28169a8` | Public and ESS intake with ordered line preferences (one application per person per competition), per-line eligibility, required-document union, intake close, legacy-hire hole closed (J10), retention trigger at the competition end, DSAR record |
| P3 | `a574ec3`, `f98db66`, `ca661e7` | Exam sessions, seating with random 8-digit exam numbers, admission card PDF (hash + verification code), attendance, held; anonymous double marking with reconciler and lock; role `hr-exam-marker`, policy `HrCoreExamMarking`, gateway rule and module-filter entry (PLAN-28 pattern, J7); three marker personas; migration `HCM031_Exams` |
| P4 | `9988e70`, `1f1d0a7` | Results compute (exam + non-exam points, pass mark, one rank, tie decisions), RECRUITMENT-RESULTS-APPROVAL (committee then HR director, the computer excluded), publication and public list (HTML/PDF by exam number); objections on `RecruitmentAppeal` (Kind ResultObjection) within the window from the first publication, independent reviewer (markers excluded), corrected mark → new version; migration `HCM031_Results` |
| P5 | `5e4c0a4`, `9369a5f` | Finalize: merit-then-preference allocation to line requisitions (replaces the PLAN-28 committee gate for attached requisitions, J4), line reserve lists, the rest rejected (kind CompetitionResult), idempotent and serialized (`FOR UPDATE`); reserve recall on decline/expiry/revoke/withdrawal; reserve expiry; bulk offer drafts into the per-offer approval |
| P6 | `028c434` | `/careers/my` competition section (preferences, admission card, result only after publication, objection or «انتهت مدة الاعتراض في …», allocation/reserve); public status inputs; seven notification templates without score/rank/national id and no duplicate generic messages; retention clears the competition residue; DSAR papers/marks/results/allocation/reserve |
| P7 | `d03db0c` | PostgreSQL integration tests, the 10,000-applicant scale test, AC-HCM-31a..g acceptance tests and their seed patch |
| P8 | `2ffa3a3` | Validate script, execution guide, plan status and deviations, legal binding, RBAC, recruitment/onboarding/gap/event/schema docs, independent review, this report |
| Acceptance | branch `fix/plan-31-acceptance-run` | AC-HCM-31a..g run live and passing; seed insert cycle, results-approval gateway policy and acceptance fixtures fixed (section 4) |

Single-requisition recruitment is unchanged: the PLAN-21..30 unit and integration suites pass at their baseline (section 3).

## 2. Decisions

Prompt decisions 1–9 are adopted as written. Judgement calls J1–J39 are recorded with reasons in the progress log; the ones
that change behaviour visibly:

- **J1** — no `ObjectionWindow` status: the competition stays `ResultsPublished`, the window is computed from the first publication; later versions never reopen it.
- **J2/J13** — one criteria shape per competition, frozen per line at submit.
- **J4/J5** — the results approval's committee step replaces the PLAN-28 committee gate for attached requisitions; manual ranking and committee sessions are refused there.
- **J7/J20** — marker access by role-specific entries (no generic gate change); markers assigned by employee, token bound on first visit; HR-read holders flagged.
- **J12** — reconcile rules `Average` / `ReconcilerDecides` / `AverageOfClosestTwo` as defined in the log.
- **J29/J30/J31** — finalize settles every open application; one recall hook on the common offer-settlement path; line entries drive reserve expiry.
- **J34** — no duplicate candidate messages for competition applications.
- **J37** — AC-HCM-31 was run live on the acceptance stack and passes (see section 4).

## 3. Test results

| Run | Result |
|-----|--------|
| `Hcm031*` unit tests | 143 / 143 pass (`scripts/hcm031-validate.ps1`: PASS) — 18 review fixes, 18 lifecycle, 16 intake, 7 seating, 11 marking, 17 marker access, 3 admission card, 12 results, 7 objection, 6 allocation, 5 reserve recall, 4 bulk offer, 5 portal, 10 notification, 3 candidate data, 1 acceptance seed |
| Full unit suite | 3827 pass / 10–11 fail — exactly the environment-only baseline set (ar-SA/Hijri locale, BOLA/Ten002 lists naming no PLAN-31 file) plus, in some full runs, the known `Hcm029CandidateAcceptedV4Tests…` flake recorded by PLAN-30 |
| `Hcm031CompetitionIntegrationTests` (PostgreSQL 16, Testcontainers) | 5 / 5 pass, three consecutive runs (incl. the concurrent reserve recall, which fails 5/5 with the lock removed) |
| Integration regression (`Hcm026`–`Hcm031`, `PublicCareers`) | 31 pass / 1 skipped / 1 fail: `Hcm028AppealGovernanceMigrationTests` formats a date with the current culture (Hijri year 1448); passes with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` — environment-only |
| Scale (`perf/hcm031-competition-scale.ps1`) | PASS — 10,000 applicants, 4 lines, 20 sessions, 100 vacancies: seating 1,050 ms, results 1,129 ms, allocation 3,762 ms, **total 5,943 ms** (budget 60,000 ms) |
| `hcm006-a11y-scan.ps1` | PASS (12 views, incl. `PublicCareers/Competition`, `PublicCareers/CompetitionResults` and the competition section of `CandidatePortal/Index`) |
| `ux003-no-native-confirm.ps1` | fails on pre-existing views only; no PLAN-31 view uses `confirm()` |
| EF model | `has-pending-model-changes`: none |
| AC-HCM-31a..g (live, hrm-acceptance) | **2 / 2 Pass** (31a; 31b–g journey), 3 min 37 s — section 4 |

## 4. Acceptance — AC-HCM-31a..g Pass (live)

Run on 2026-10-29 at the owner's request, from branch `fix/plan-31-acceptance-run` (merged to `main`), on the
`hrm-acceptance` stack rebuilt from `main` (all fourteen services; MinIO and `file-storage-web` left out as in PLAN-30 —
the MinIO image is no longer pullable; no PLAN-31 step needs File.Storage). Evidence: `acceptance-evidence/AC-HCM-31/`
(steps, 285 HTTP captures, db-checks, timing).

| Scenario | Result |
|----------|--------|
| 31a — competition from three approved requisitions, published requisition refused, creator refused, two approvers, announcement, public page | Pass |
| 31b–g — 120 seated, cards PDF, attendance, three markers (exam numbers only, HR pages closed to them), double marking with a reconciled paper, results with SoD and publication (public list without names), RESULTS_PUBLISHED, HR objection → reviewer assigned → upheld by a-compliance → version 2 published, OBJECTION_DECIDED, finalize (5 allocated by merit and preference, 77 on reserve, idempotent), bulk offers through the per-offer approval, decline → first reserve of the line recalled | Pass |

What it took (decision J37): the three marker personas change the acceptance pack fingerprint, so the acceptance data
was reset (`scripts/acceptance-reset.ps1`, project volumes only; credentials kept) and a fresh realm imported. The live
run found and fixed three defects, each with a test:

- **Seed insert cycle** — `AcceptanceCompetitionPatch` saved competition line → criteria → requisition → line in one batch;
  PostgreSQL refused it and `hr-core-web` crash-looped. Fixed (criteria linked after the first save) and guarded by
  `Hcm031AcceptanceSeedIntegrationTests` (fails on the old code).
- **Gateway policy** — `Competitions/ApproveResultsWorkflow`/`RejectResultsWorkflow` fell into the gateway's
  `HrCoreWrite` rule, so the HR director (step 2) got 403. They are workflow actions → `HrCoreBranchOperation`.
- **Acceptance fixture/test** — offer approval checks the approver's org scope, so the dedicated positions moved to a
  RIF unit; the objection's independent reviewer is assigned before deciding; notification waits are 4 minutes (planning
  takes 100 events per minute; RESULTS_PUBLISHED arrived after 124 s).

## 5. Independent review

An independent read-only review of `35d4efc..HEAD` reported nine findings. Each was then attacked by its own refutation
agent; all nine held and were fixed in this commit, each with a regression test (`Hcm031ReviewFixTests`, 18 tests):

| # | Severity | Finding | Fix |
|---|----------|---------|-----|
| R1 | high | Finalize planned passed candidates who had withdrawn/been rejected after publication: they consumed a vacancy that was never filled, and the next candidate by merit lost it | only applications still open take part (vacancies and reserve ranks) |
| R2 | medium | Under the default request culture ar-SY («٫»), fractional marks, corrected marks, max scores and pass marks posted as «37.5» failed to bind | `InvariantDecimalModelBinder` for every `decimal`/`decimal?` form/query/route value in HR.Core.Web; competition forms render decimals invariantly |
| R3 | medium | An objection upheld while another version awaited approval was left out (recompute refused, notice only) and finalize could allocate from stale results | finalize refuses while an `ObjectionCorrection` mark is newer than the published version's computation |
| R4 | medium | Two simultaneous vacancies on one line could recall the same reserve candidate | recall under a transaction with `FOR UPDATE` on the competition row; PostgreSQL test fails 5/5 without the lock, passes with it |
| R5 | low-medium | The results submitter (if not the computer) and a tie resolver could approve the committee step | both excluded from every step |
| R6 | low | HR rejecting an allocated application left the requisition empty; manual transitions after seating broke result computation | recall after HR reject/withdraw of an allocated application; after seating only reject/withdraw are allowed manually |
| R7 | low | Retention could anonymize an allocated/recalled candidate with an offer in flight or a live reserve | `CompetitionLiveStatuses` never trigger retention |
| R8 | low | An approved version of a cancelled competition could still be published and notified | submit/publish refused for cancelled or finalized competitions |
| R9 | low | Internal-only competitions had an anonymous public results page | the careers endpoints serve only public-channel competitions (HR PDF unchanged); DSAR keeps all versions on purpose (J41) |

A second adversarial pass (three lenses, each finding attacked by a refuter) reviewed the fixes themselves: it confirmed one medium issue — HR rejecting an allocated application whose offer was still open recalled a reserve who could then get no offer — fixed by refusing that transition (the application leaves through its offer, which recalls on settlement) and by treating any open offer as holding the requisition in the recall; and three low UI mismatches (public-results link for internal-only competitions, the results approval panel showing Approve to the submitter/tie resolver, status buttons the new guard refuses), all fixed. Three other reports were refuted.

## 6. Follow-ups (not implemented, documented)

- `RECRUITMENT-OFFER-BATCH-APPROVAL` (decision 7, optional) — each offer keeps its existing approval.
- Reserve recall on non-commencement after hire — `// PLAN-31 follow-up:` marker in `CompetitionReserveService` (decision 8).
- No push channel to HR (since PLAN-30): exhausted reserves are shown on the competition's allocation tab and audited.
- PLAN-25 stays deferred (decision 9).
- Regenerate `docs/DATABASE_SCHEMA_INVENTORY.*` with
  `scripts/extract_database_inventory.py` when Python is available (edited by hand here).
- Legal inputs remain «يُحدَّد من المرجع المعتمد عند اعتماد D-2» ([`docs/syr/legal-source-binding.md`](../docs/syr/legal-source-binding.md)).

## 7. Repository hygiene

Nothing pushed. Never committed: `global.json`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`,
`tmp-vendors.html`, credentials, test-rewritten `testdata/**/out/*.json`. Personas use `passwordEnv` only.
