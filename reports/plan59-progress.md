Started (UTC): 2026-11-27T05:19:45Z

# PLAN-59 progress (checkpoint)

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). Branch `feature/plan-59-pay-full-cycle-production-proof` from
`origin/main` = `708d2004`. Keep-awake: `systemd-inhibit` PID 3066561 (`artifacts/plan59/keepawake.pid`), stop by that PID at the end.

## Preconditions (P0, 05:19–05:22Z)
1. No other plan session working: five older Claude processes in this folder, all idle (no child processes); worktree list: this folder
   and the owner's `/home/omar/HRManagement-ghcr` (untouched). On `docs/plan-58-merge-note` at start.
2. `git fetch origin`: `origin/main` = `708d2004` holds `Merge pull request #59 from omardefdev/release/plan-58`.
3. Working tree clean (only this progress file, untracked).
4. `ssh -o BatchMode=yes hrm-vps whoami` → `hrm`; ED25519 host key `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto` (known_hosts).
5. `gh auth status`: `omardefdev` active, scopes `repo`, `workflow`.
- Machine at start: `/` 23 G free of 233 G; Docker images 17.7 GB, build cache 13.9 GB; 15.7 GB RAM; load 1.4 on 8 CPUs.

## P0 — baseline
- Commits: `a5d26bb7` (PLAN-58's merge note, cherry-picked), `2dfd4548` (this prompt, written from the session's text); `docs/plan-58-merge-note` deleted.
- Build 4 min 49 s, 0 warnings. Integration filter: SQLite 208/0/91 skipped (66 s); Testcontainers 299/0/0 (3 min 51 s). The fourteen green.
- Plan inventory 0.4: 22 SQL lines in two scripts. Reads: 4 missing (P-9's). `pol049-validate.ps1` FAIL (28), as on main.
- Demo picture 05:37Z: tag 25be64cf9fa8, 54 G free, checksums in `reports/plan59-baseline.md`.
- Durations: inventories ~5 min (grep-generated, no subagent); demo picture one ssh call.
Finished (UTC): 2026-11-27T05:42:00Z

## Decisions
- BlockRunRefusal names the first ten blocked employees **in the order of their ids** (so the name lookup asks HR.Core for ten ids, under
  its per-request limit), by display name, else by id when the lookup answers «غير متاح» (the lookup's placeholder, now a constant).

## Next step
P1: build; Syr007/Pay058 tests follow the new text; write Pay059RegimeBlockerTests and Pay059PayrollSodHandlerTests; run the filters.

Correction (UTC 2026-11-27T05:51:11Z): P0's «Finished» stamp above (05:42:00Z) was typed, not read; P0 actually ended at 05:38:47Z (read
with date -u just after its commit). The stamp is left as written; this line corrects it.

## P1 — the fourteen, the regime refusal, the handler's rules
- `PayrollRegimeGovernance.BlockRunRefusal(blocked, displayNames)`: the first ten blocked employees (by id) by display name, else by id,
  each with HR.Core's reason code, then «و{n} آخرون»; `DecideAsync` asks `IEmployeeLookup` (when registered) for those ten only; still
  decided before `BeginCalculating`. `EmployeeLookupLabels.Unavailable` names the lookup's «غير متاح».
- New: `Pay059RegimeBlockerTests` (4: BlockRun refuses by name, run as it was; no client → ClientMissing; no client + BlocksFinalPayroll=false
  → calculated; ten names and «و2 آخرون»), `Pay059PayrollSodHandlerTests` (2: creator → CreatorCannotApprove, nothing written; approver
  posts → PosterIsApprover before and after a closed review by two auditors, no batch; then the poster posts).
- Changed: `Pay058HeldEmployeeTests` (BlockRun: the new text, and the employee's id in it), `Syr007RegimeGovernanceTests` (the id and reason).
- Tests: plan filter + Pay059 on SQLite 214/0/91 skipped (53 s); with Testcontainers 305/305 (3 min 39 s); unit Syr007|Pay058 30/30;
  Pay059 + Pay058HeldEmployeeTests 13/13; solution build 0 warnings; format clean.
- CI picks the new classes in «Integration tests (MassTransit InMemory)» (`Category!=Testcontainers`); the four PostgreSQL ones carry
  `Category=Testcontainers` (the Testcontainers step).
Finished (UTC): 2026-11-27T05:51:11Z

## Next step
P2: the run read's revision/approvals/batch; GET payment-batches/{id}/orders; tests (shape, 404 across agencies, no account number, stance).

## P2 — the reads an SQL-free cycle needs
- `GET api/payroll/runs/{id}` (Payroll.RunRead, central-only): `revision`, `approvalsGiven` (different approvers of the present version),
  `approvalsRequired` (SeparationOfDutiesService with the run's total, as the approval counts), `paymentBatchId` (the run's disbursement
  batch, the posting's first; null before). Added at the end of `PayrollRunDetailsDto`.
- `GET api/payroll/payment-batches/{id}/orders?page=` (Module.Payroll, central-only by the controller's stance; 500 a page, by employee id):
  id, employeeId, salaryLineId, status, amount, holdReason, destinationPinned, fileSequence. No account number, no field named like one.
- Nothing else missing (the read inventory): no new write, no migration.
- Tests: `Pay059CycleReadsHttpTests` (PostgreSQL: shape, an earlier version's approval not counted, two required above 50,000,000, null
  batch before the posting, the account number absent and no property named account/iban/beneficiary, page 2 empty, unknown 404, another
  agency 404 on both, the auditor reads the run and is 403 on the orders) with Pay058DisbursementHttpTests and Lva057's reads: 4/4.
  Architecture (PayrollWriteActions, WriteActionsHaveExplicitPolicy, OrgScopeCoverage, GatewayAuditorReach, GatewayApproverReach,
  ModuleOverridesAreDeclared, Pay058SinglePaymentPath, InterfaceTextGuard): 303/303. Solution build 0 warnings; format clean.
- Docs: `docs/PAYROLL_RULES.md` lists both reads for API clients, and the BlockRun refusal's names.
Finished (UTC): 2026-11-27T05:59:43Z

## Next step
P3: scripts on the cancel API with their month from the class; testdata/acceptance/payroll-months.json + unit test; AcceptancePayrollMonths;
AC-21/22/23 [RequiredAcceptance], windows, clean-up, classification.

## P3 — the scripts without SQL, AC-21..23, the months
- `acceptance-payroll-determinism.ps1` and `acceptance-payroll-resume-chaos.ps1`: `Invoke-PayrollSql`, `Cancel-PayrollRunInDb`,
  `Cancel-TestRun` and `Reset-ReservedPeriod` gone; `Cancel-PayrollRun` = `POST api/payroll/runs/{id}/cancel` with a reason, the
  antiforgery header and its cookie from `GET api/payroll/run-lifecycle/antiforgery`, in a `finally`; month from `-PeriodStart`
  (mandatory) and the class's run from `-RunAId`/`-RunId`; the chaos script's psql reads stay. ASCII, parse clean. The plan's
  `Select-String` finds 0 lines (was 22).
- `testdata/acceptance/payroll-months.json`: the seed (posted 2025-08..2026-07; scenario runs 2026-08..2027-02), AC-LVA-57 (2025-01..07, its
  file named), AC-IAM-53 (current month), AC-PAY-54 (2031-01..07), 55 (2029-05..06), 56 (2028-01..06), 58 (2030), AC-12 and AC-PAY-59
  (D-PAY-22; main and cycle stacks), AC-21 window 2024-01..06, AC-22 window 2024-07..12, AC-23 (current+2, two months), AC-24, AC-25 (lab).
- `AcceptancePayrollMonths`: the registry, `WindowOf`, `CycleCandidates` (D-PAY-22), `AfterNextCandidates` (current+2, seed included),
  `CreateRunAsync` (201, or skip at the live-period refusal; none left → RequireData asking for acceptance-reset.ps1), `CancelRunAsync`,
  `ReadRunAsync`, `RunStatus`.
- AC-21 and AC-22 `[RequiredAcceptance]`; AC-21/22/23 classify the records added since the seed before their first calculation, take
  their month from the picker, and cancel what they do not keep in `finally` (AC-21 both runs, AC-22 its run, AC-23 both runs and its
  request); AC-21 and AC-22 assert their runs read «Cancelled»; AC-23 picks the first employee of its run with no open request (API).
- Tests: `Pay059PayrollMonthsRegistryTests` 3/4 — the fourth (every class named exists) waits for `AcPay59FullCycleTests.cs` (P5).
  Acceptance project builds; solution build 0 warnings; format clean.
Finished (UTC): 2026-11-27T06:22:05Z

## Next step
P4: rewrite Ac12PayrollSodTests on a calculated, validated run of D-PAY-22's month.

## P4 — AC-12
- `Ac12PayrollSodTests` rewritten, `[RequiredAcceptance]`: D-PAY-22's month on the main stack (`PayrollCycleDriver.CycleMonthAsync`:
  the registry's candidates, the attendance-closed skip read by central-a, created by the processor); classify before calculating;
  calculate + validate (ResultsFrozen); processor approve 403; a-payroll-multirole approve 403 with two payroll roles named; submit,
  approve (+ a-payroll-approver-2 when required) → Approved with approvalsGiven = approvalsRequired (P-9's read); approver post 403;
  poster post before review 400 = ReviewRequired's text; auditor opens, a-auditor-2 closes; poster posts → Posted with its batch.
  `finally`: `CancelUnlessPostedAsync` (processor before review, approver after).
- New `PayrollCycleDriver.cs`: `PayrollApi` (one person's client, captured calls) and the D-PAY-22 picker; extended in P5.
- Acceptance project builds; solution build 0 warnings; format clean. (Run on the local main stack in P6.)
Finished (UTC): 2026-11-27T06:25:51Z

## Next step
P5: AcPay59FullCycleTests + PayrollCycleDriver steps; ci-scope (cycle group, prodlike-cycle job, ci.yml, prodlike-final), the moved
classes, ac-ownership.json; scripts/pay059-validate.ps1 with -SelfTest.

## P5 — AC-PAY-59, its CI stage, its guard
- `AcPay59FullCycleTests` (`[RequiredAcceptance]`, `EvidenceRecorder("AC-PAY-59")`) and `PayrollCycleDriver.Cycle.cs`: the fifteen
  steps by HTTP alone (P-10), every check an API read in db-checks.json; no SQL. Decisions in the driver:
  - the run is created first (D-PAY-22's picker creates it), so its attendance-status before the close gives the scope;
    Y, X, Z = the scope's first ids in order (Z the first from the third on with no open request);
  - Z's salary is never read by SQL: a probe request at salary 1 is refused with the grade's bounds (or, for a grade with none, is
    created, names the salary in force, and is cancelled at once); the real request is inside the bounds, dated next month (P-4);
  - accounts: GET per employee; Active kept, a leftover pending verified, else registered and verified (first and last captured);
    X's Active retired and a stale pending rejected, then a new one registered; the IBAN mark is "59"+yyMM of M (unique per execution);
  - the bank files' content is read only for their order ids (not kept in the evidence; size and row count only);
  - finally: Z's change cancelled whatever happened; a run not posted cancelled by the processor or approver.
- Seed check: the gate holds on alerts/holds of the run itself or holds with no run, so the seed's open critical alert (of a seeded
  run) does not reach the cycle's batch.
- ci-scope.json: group cycle (AcPay59FullCycleTests, required) after s2s42 in order[] (a run without -Stage gives it a fresh stack);
  stage job prodlike-cycle; prodlike-final waits for it; AC-12, AC-21, AC-23 moved from lab main to prodLike main, required; AC-22
  stays excluded. ci.yml matrix: prodlike-cycle, timeout 45 (first guess; set by the local measurement in P6). ac-ownership.json:
  AC-12/21/23/PAY-59 owned by PLAN-59 (provesClaim true), AC-22 QA-G with «proved by PLAN-59 outside CI».
- personas.json: AC-12 and AC-PAY-59 in the scenario lists of their personas (a persona change: reset before the first stack start).
- `scripts/pay059-validate.ps1` (+ -SelfTest): all checks pass but the runbook's (written in P7). `iam053-validate.ps1` adapted:
  the payroll regression (AC-23/24/25) required where it runs; its self-test 20/20.
- Tests: unit Pay059|Qa066|CiStepsParity|Acceptance 221/221 (registry 4/4); acceptance static classes 48 + 1 skipped (needs a stack);
  validators pay054/055/056/058, lva057, ux067, iam050..053 pass. acceptance-baseline-validate red as on main (stale committed
  evidence; 95 → 96 ACs with AC-PAY-59), recorded. Solution build 0 warnings; format clean.
Finished (UTC): 2026-11-27T06:43:15Z

## Next step
P6: disk/memory checks; acceptance-reset (personas changed); build the service images one at a time; cycle stack: AC-PAY-59 x3;
main stack: AC-12/21/23 + touched classes, then AC-22 alone; lab stack; validators; Testcontainers step; independent review.

## P6 — local acceptance (in progress)
- Images: 16 services built one at a time from b40795d0 in 23 min 50 s (07:08:07Z); every hrm-acceptance image new; the 15
  superseded images of this repository removed by id. Disk 22 G free after.
- Independent review (one fresh subagent, read-only, ~29 min, started beside the image build so its fixes land before the stacks):
  1 high, 2 medium, 8 low. Fixed: H1 the bank file's order id is its second-to-last column (column 0 is the run): parsed from the
  right, the employee from column 4, X's employee also checked absent; M2 Y's adjustment checked by its delta from step 5, the
  pending set read before and after, a never-approved one rejected in finally; M3 Z's base on the posted line checked at step 14;
  L4 Y's deduction must be two days of his base (by 30 or by the month's working days) with a reference; L5 the scripts send the
  antiforgery cookie through a WebRequestSession (5.1 drops a Cookie header); L6 AC-23 takes its months as a pair (run B created
  with run A; a refused B cancels A and moves on); L7 step 1 mirrors the engine's rule (Published or Superseded, covering the period's
  first day); L8 evidence of the bulk steps first/last only, distinct names; L10 the orders read clamps its page (no overflow), with a
  test. Recorded, not changed: L9 the per-test hang limit (20 min) is measured below; L11 AC-12's posted run keeps its held orders,
  which a later verification on the main stack may re-pin or hold ACCOUNT-RETIRED — holds of AC-12's run act on AC-12's batch only
  (P-11 accepts AC-12's posted run and held orders in its month).
- Cycle stack 1 (07:08–07:20:44Z, 12 min 3 s incl. reset). AC-PAY-59 first execution FAILED at step 2 after 26 s: the gateway's
  429 (2,000 requests a minute per user on the acceptance stacks) met the bulk account step, and the finally's cancel was itself
  429, leaving the 2026-08 run live. Fix: PayrollApi waits on 429 (Retry-After, else 5 s) and sends the request again (counted in
  timing.json). The stack is reset so the three executions take 2026-08..10 in a row.
- Cycle stack 2 (07:24–07:35:39Z, 10 min 52 s). Execution 1: steps 1–10 passed (month 2026-08; 1,102 in scope, 1,101 orders;
  accounts 3 min 33 s), step 11 failed: two orders Held PAYMENT-HOLD. A debug execution on the same stack (2026-09) showed the same.
  Cause: the two seeded legacy employees (…0201, …0203) share the first 18 hex digits of their ids, and
  AcceptancePayrollAccounts.Iban builds the number from the mark and the id's first characters, so both got one IBAN and the fraud
  scan's FR-04 (shared account, payment-hold) held both. (A first guess — standing holds with no run — was wrong: the holds screen
  showed none, and that step was removed.) Fix: AcceptancePayrollAccounts.UniqueIban (mark + SHA-256 of the whole id), used by the
  cycle; Iban is unchanged for the classes that pick a few employees (they exclude the legacy two). Stack reset again (07:46:51Z).
- Cycle stack 3 (fresh, 15 min 13 s to ready, 08:02:04Z): **AC-PAY-59a Pass three times in a row on 2026-08, 2026-09, 2026-10**
  (5 min 52 s, 4 min 53 s, 4 min 57 s; gateway 429 waits 4 each). Each: 1,102 lines and orders (scope 1,102; HR.Core closed 1,138
  and payroll holds 1,138 receipts), two approvals (≈276–286 M), X held NO-ACCOUNT then sent alone (Initial 1,101 orders; HeldRelease
  1 order), AC-PAY-59b second process 409 with no second file, 1,102 payslips equal their orders, settled by two people; Y's
  deduction = two days by the month's working days (40,272.73 = 443,000×2/22); Z's line at 260,000 (his salary in force).
  Totals: 2026-08 276,109,392.27; 2026-09 286,251,392.27; 2026-10 286,249,474.52. Evidence in artifacts/plan59/ac-pay-59-{1,2,3}.
  Found after: Z's change was a decrease (the probe's mid-of-bounds); P-4 asks for a raise → the grade's maximum now (a fresh cycle
  stack and three executions again after the main and lab stacks). Cycle stack down (reset).
- payroll-web image rebuilt alone with the review's page clamp (118 s); the old payroll image removed by id.
- Main stack (fresh, 15 min 3 s to ready, 08:36:40Z):
  - AC-12 Pass (2026-08; multirole 403 naming payroll-approver/payroll-poster; 2 approvals; posting before review 400 with
    ReviewRequired; posted). AC-23 Pass (2027-03/04, both runs and the request cancelled). AC-21 first FAILED: in PowerShell
    $periodStart is $PeriodStart (names are case-insensitive), so the [string] parameter took the date; renamed $runPeriodStart in
    both scripts; AC-21 Pass (2024-01; 1,102 lines bit for bit; both runs Cancelled through the API).
  - AC-PAY-58, AC-PAY-56, AC-PAY-55, AC-IAM-53, AC-LVA-57: 32/32 (6 min 32 s), beside the moved classes.
  - acceptance-evidence-summarize.ps1 ran on the run's evidence (complete, 8 elements; locally «stale» only because the payroll image
    was built from an uncommitted tree).
  - AC-22 (RUN_CHAOS=true) first FAILED: after the kill and the restart the drill's next call met Payroll still starting and the
    script died without cancelling (its 2024-07 run stayed live on that stack), the class's cancel and read hit the restart (405/502).
    Fix: the drill waits for Payroll's own read of the run after the restart (Wait-PayrollReady), a trap cancels the run on any
    failure, the cancel retries (Stop-TestRun); the class waits for Payroll before its clean-up and writes the script's output first.
    **AC-22 Pass** (2024-08; killed mid-partition at 200 of 1,102, resumed, 1,102 lines, no duplicate, lease conflict 400, Cancelled).
  - acceptance-verify after the tests: counts differ by design (the tests wrote); the seeded counts are compared on the final fresh
    cycle stack before its tests. Main stack down (reset).
- Lab main stage started (08:51:19Z): scripts/ci/acceptance-job.ps1 -Profile Lab -Stage lab:main -NoBuild.
- Lab main stage (fresh lab stack 15 min 20 s; tests 14 min 26 s; whole job 30 min 43 s, 09:21:51Z): **PASS** — 159 passed, 1 skipped
  (AC-09's known skip), acceptance-guard 54 classes; AC-24 and AC-25 Pass; the three moved classes no longer in it.
- Final cycle stack started (fresh) at 09:22:21Z.
- Final cycle stack (fresh, 14 min 42 s, 09:36:51Z): its start-up verification **Pass** (seeded counts: DATA-AUTH 70, DATA-DIRECTORY
  61, DATA-PAYPROFILE 1,489, DATA-PAYRUN 57, SEED-03 one fingerprint); a second verification after it differs only in BASELINE-02
  (the first run's gateway checks wrote audit rows). AC-PAY-59 Pass x3 on 2026-08/09/10 (6 min 33 s, 5 min 4 s, 4 min 48 s), but Z
  (the third id) is at his grade's maximum (260,000), so the «raise» became 259,000. P-4 asks for a raise: Z is now the first
  candidate (third id on, no open request) whose grade has room — the probe's bounds, the maximum asked, «no change» → next
  candidate; the step asserts proposed > current. Earlier passing evidence kept as artifacts/plan59/ac-pay-59-pre-{1,2,3}.
  One more fresh cycle stack for the three executions on this code (started 09:57:03Z).
- Stack start failure 1 (09:57–10:00:54Z): admin-logistics-web exited 139 (segfault) under memory pressure (swap 4,087/4,095 MB full,
  532 MB free, load 16.9; the owner's other applications hold the swap). Stopped this session's compiler servers
  (dotnet build-server shutdown), brought the stack down, waited for the load to fall under 6; retry 1 at 10:02:57Z.
- Retry 1 of the stack start (10:03–10:18:00Z, 15 min 3 s): Pass. **AC-PAY-59a final series Pass x3 on 2026-08, 2026-09, 2026-10**
  (6 min 32 s, 5 min 3 s, 4 min 48 s): Z = the second candidate (the first is at his maximum), raised 439,000 → 500,000 from
  2026-12, Scheduled, his posted line 439,000; totals 276,109,392.27 / 286,251,392.27 / 286,249,474.52 (equal to the earlier series:
  the scheduled raise reaches no part of M); X held then sent alone; AC-PAY-59b 409; settled. Evidence artifacts/plan59/ac-pay-59-{1,2,3}.
  Stack down. ci.yml: prodlike-cycle limit 40 (QA-066: local ~26 min × 1.5).
- Testcontainers step (CI's filter) started 10:35:45Z.
- Testcontainers step on the host (CI's filter Category=Testcontainers&Category!=ObjectStorage, Debug): **335 passed, 1 skipped**
  (Ops038, needs the migrator image), 0 failed — 15 min 42 s of tests, 19 min 44 s with the build (10:55:20Z).
- pay059-validate PASS, -SelfTest 25 planted defects refused. Solution build 0 warnings; format clean. Two reports/tenant-assignment-*
  files a lab test wrote (AC-07's dry run) removed: never committed.
- P7's documents drafted during the waits: the runbook (docs/runbooks/payroll-monthly-cycle.md), PILOT_DEPLOYMENT indicators,
  PAYROLL_RUN_RESUME, CI_AND_TESTING, G-PAY-05's status, the plan's status and notes (committed with P7).
Finished (UTC): 2026-11-27T10:58:32Z

## Next step
P7: commit the documents (runbook etc.), OPS_VPS_SINGLE_HOST PLAN-59 section (pre-server part), check origin/main; then P8 gates.

## P7 — the runbook, the documents, main
- docs/runbooks/payroll-monthly-cycle.md (new, Arabic): the fifteen steps (role, screen, API), the operator's cases (rejection, hold,
  HoldEmployee/BlockRun blocker, no account, missing attendance), and what this plan found (G-PAY-11 order of runs, D-53-1, when Block
  is safe and how to see it, the people a posting needs, G-PAY-21, STR-002 2027-01-31), cancellation by the API.
- PILOT_DEPLOYMENT (each indicator tied to AC-PAY-59; D-PAY-21's «مكتملة»), PAYROLL_RUN_RESUME (scripts cancel by the API),
  PAY_001_GAP_STUDY (G-PAY-05 «الحالة»: closed by PLAN-59; the PR run's link in the final report), CI_AND_TESTING (prodlike-cycle, the
  registry, the moved classes, AC-22, ACCEPTANCE_PAYROLL_CYCLE_FROM), PAYROLL_RULES (the two reads, P2), OPS_VPS_SINGLE_HOST (PLAN-59's
  section; the server's table comes in P9), the plan's Status and «ملاحظات التنفيذ والانحرافات».
- origin/main: unchanged since 708d2004 (git fetch, 10:59:24Z): no merge needed.
Finished (UTC): 2026-11-27T10:59:24Z

## Next step
P8: Release build; local CI steps (run-steps.sh --all in a fresh clone of HEAD, compiler servers off); plan035-prepush; then the release
branch, the push, the PR; start P9 beside the checks.

## P8 — local gates, the release branch, the PR
- Testcontainers step: P6's (10:55Z; nothing it builds or tests changed since — later commits are documents and reports).
- Release build: 0 warnings, 0 errors (2 min 24 s).
- Local CI steps in a fresh clone of f0f5466b (/home/omar/plan59-ci-clone; compiler servers off; run-steps.sh --all, 11:03–11:47:34Z,
  44 min 14 s): **87 passed, 0 failed, 5 not run and named** (Integration tests (Testcontainers): run natively, 335/1; Coverage gate,
  Install ReportGenerator, Generate coverage report: they follow it; Verify Docker builds: images built here one at a time). Unit
  7,253, architecture 444, integration (in-memory) 647 passed and 4 skipped.
- plan035-prepush (11:52Z): gitleaks (history 632, range 11: no leak), actionlint, cilocal (the five not-run steps named by
  -AllowNotRun), forbidden, nonprod, signatures — PASS. (First run read the old artifacts/ci-local summary: the HEAD summary copied there.)
- release/plan-59 = origin/main 708d2004 + the feature merged --no-ff (8086ac86). **One push** 11:52:32Z. PR #60 opened 11:52:48Z
  (https://github.com/omardefdev/HRManagement/pull/60), bound in the app; the owner turned Auto-fix on. Auto-merge off.
Finished (UTC): 2026-11-27T11:54:07Z

## P9 — the demo server (in progress)
| # | UTC | Change | Undo |
|---|---|---|---|
| 1 | 11:53:13 | GHCR build of release/plan-59 (deploy.yml action=build, run 45994097920) | none needed |
| 2 | 11:53:33–11:53:55 | backup /opt/hrm/backups/pre-plan59-20261127T115333Z (pg_dumpall 55,555,882 bytes, 17 CREATE DATABASE, env.backup, SHA256SUMS verified; 700/600); picture before the deploy = P0's (checksums unchanged) | delete the folder when not needed |
| 3 | 11:53:13–12:07:09 | GHCR build success: 17/17 images 8086ac8629af (build-and-push jobs green; deploy/rollback jobs skipped) | none needed |
| 4 | 12:07:45– | deploy scripts/vps/deploy.ps1 -Tag 8086ac8629af (previous 25be64cf9fa8) | scripts/vps/deploy.ps1 -Rollback (25be64cf9fa8 from GHCR; safe: reads and a message only) |
| 4 | 12:07:45–12:16:02 | deployed 8086ac8629af first time (pull 107 s, up 137, bootstrap 58, keycloak reconcile 14 (0 changes), healthy 15); smoke 50 pass, 0 fail, 1 warn | scripts/vps/deploy.ps1 -Rollback |
| 5 | 12:17–12:24 | AC_PAY_59_vps_* 4/4 (the run-read check's own SQL first compared Source as text: fixed to its number, rerun alone): tag 8086ac8629af on 16 images, 0 unhealthy, last payroll migration = the tree's (PAY058); a posted demo run answers revision/approvals/batch; the newest demo batch answers its orders (total 0 = the demo's), fields batchId,total,page,pageSize,orders; the picture unchanged | none (reads) |
- PR #60 first CI run 45994047230 on 8086ac86 (11:54–~13:00Z): **16/16 green**, merge state CLEAN. Stage times: lab-main 20m21s,
  lab-rest 13m33s, prodlike 35m44s, prodlike-cycle 17m25s, prodlike-final 37m16s, prodlike-policy 11m44s, leastpriv 9m10s, meta 40m2s;
  CI 44m22s, CodeQL 19m16s, E2E 15m39s, Acceptance images 22m51s.
- QA-066: prodlike-cycle limit 40 → 27 (1.5 × 17m25s); prodlike 55 kept (1.5 × 35m44s ≈ 54). Unit suite before the push: first run's
  test host crashed (known flake), rerun with --blame 7,253/7,253.
- Pushed AcPay59VpsChecks.cs and the limit as one commit (a second CI run); the older server checks keep running beside it.
- PR #60 second CI run 46006396762 on a8a3b831 (13:21–~14:30Z): **16/16 green**, CLEAN (prodlike-cycle 16m22s under its new 27,
  prodlike 25m48s, prodlike-final 27m22s, lab-main 23m52s, meta 18m49s, CI 30m33s).
- The first run's artifact acceptance-evidence (run 45994047230) downloaded to artifacts/plan59/pr-evidence (58 MB; the first try
  timed out on the link): AC-PAY-59 Pass (2026-08, 1,102 lines, 276,109,392.27 — the same as locally; Z 439,000 → 500,000; 59b 409;
  6 min 6 s, 5 gateway waits), AC-12, AC-21, AC-23 Pass.
- Older server checks (start 12:28:51Z): known local-only failure AC_POL_47_vps_the_migration (FileNotFound artifacts/plan47/vps/
  fingerprint-before.json, lost with the Windows machine), recorded, not chased.
- Older server checks, first pass (12:28:51–18:05:30Z, 5 h 37 min): 64 of 83 reported — 56 passed, 8 failed (AC_POL_47 migration: the
  lost local file; AC_PAY_55 screens, AC_WF_45, AC_OPS_36e: HttpClient 60 s timeouts from this machine's link; AC_IAM_52 branch
  manager: «No such container: timeout» from the ssh helper; AC_LVA_57 small unit: «Connection closed by … port 22»; AC_UX_67 portal
  and AC_IAM_53 screens: sign-in landed on chromewebdata/ (Chromium's error page)). A browser tour then hung silently (17:26–18:05Z);
  the run was stopped by its PIDs (testhost 552475, vstest 552441). **Stop/mistake:** my clean-up of the browser processes used
  pgrep -f, whose pattern matched my own shell and ended that command (the lesson of PLAN-53): no harm (no other process matched), the
  browser processes were gone; from here on, ids only.
- Rerun: the 19 unreported checks and the 8 failed, each alone under a 20-minute limit (artifacts/plan59/tools/rerun-each.sh), from 18:06:12Z.
- Reruns alone (18:06–20:58Z, each under 20 min; the loop's first try stopped after one check because the test read the list from
  stdin — fixed with < /dev/null): **20 of 27 passed**. Not passed: AC_POL_47_vps_the_migration (the lost local file, as since
  PLAN-53); Server_services_log_the_repository_legal_pack_fingerprint (hr-core-web was restarted at 15:27:15Z by an earlier boot check
  and its logs rotate at 3 × 10 MB, so its start-up line is gone; hr-contracts-web and hr-movements-web carry it); and five browser or
  transport flakes from this machine — AC_LVA_57 attendance screens («No such container: timeout»), AC_PAY_58 new screens and AC_IAM_53
  screens (net::ERR_NETWORK_CHANGED: the owner's dev container hrm-keycloak restarts every minute, 14,070 restarts, not touched),
  AC_UX_67 destructive form (sign-in landed on chromewebdata/), AC_UX_67 every key page (hit the 20-min limit: the same tour hung the
  first pass). Those five get one more run each (from 20:58:49Z), then the postings' clean-up again.
- Retries once more (20:59–21:44:39Z): AC_LVA_57 attendance screens, AC_PAY_58 new screens, AC_IAM_53 screens, AC_UX_67 destructive
  form passed; AC_UX_67 every key page hit the limit again (third time). The postings' clean-up passed. **Final: 80 of 83 older server
  checks passed**; the three not passed are environmental (POL-47's lost file, the rotated start-up line, the hanging full-page tour).
- Final smoke -RequireIntegrationWiring (21:45:46Z): 50/0/1. End picture: tag 8086ac8629af, 47 G free; batches, orders and arrears as
  before; runs 21 and lines 3,400 — PLAN-56's checks' two cancelled 2028-09 runs and one calculation (425 lines, 425 REGIME-BLOCKER holds
  on the cancelled run), as on every deploy. Committed evidence and test-rewritten outputs restored; a server replay report moved
  under artifacts/plan59/vps.
Finished (UTC): 2026-11-27T21:48:23Z

## Next step
P10: commit the server's record (P9 message) and the final report; push (docs only: the PR's green run is reused); merge #60 when green;
main pulled; merge note on docs/plan-59-merge-note; stop keep-awake by PID.
