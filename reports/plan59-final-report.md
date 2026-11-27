# PLAN-59 — A full payroll cycle proved with production settings (PAY-D)

Branch `feature/plan-59-pay-full-cycle-production-proof`, released as `release/plan-59`. Started 2026-11-27T05:19:45Z.
Progress, decisions and every stop: `reports/plan59-progress.md`. Baseline: `reports/plan59-baseline.md`.

## 1. Summary per phase

| Phase | What it did |
|---|---|
| P0 | Preconditions; branch from `708d2004`; PLAN-58's merge note; this prompt; the baseline: build, the endpoints by plan, the fourteen integration tests (all green on SQLite and on PostgreSQL), the scripts' SQL (22 lines), the reads (4 missing, P-9's), the months and pickers in use, the demo's read-only picture. |
| P1 | `BlockRun`'s refusal names the blocked employees; `Pay059RegimeBlockerTests`, `Pay059PayrollSodHandlerTests`; `Pay058HeldEmployeeTests` and `Syr007RegimeGovernanceTests` follow the new text. |
| P2 | The run read's `revision`, `approvalsGiven`, `approvalsRequired`, `paymentBatchId`; `GET payment-batches/{id}/orders`; `Pay059CycleReadsHttpTests`. |
| P3 | Both payroll acceptance scripts cancel through the API and take their month from their class; the months' registry and its unit test; `AcceptancePayrollMonths`; AC-21/22/23 required, with their months and clean-up. |
| P4 | AC-12 rewritten on a calculated, validated run of D-PAY-22's month, with PLAN-58's people. |
| P5 | AC-PAY-59 and `PayrollCycleDriver`; the `cycle` group and the `prodlike-cycle` stage job; AC-12/21/23 moved to the prod-like main group; ownership; `scripts/pay059-validate.ps1`. |
| P6 | Images; the cycle stack (AC-PAY-59 three times), the main stack (AC-12, 21, 23, the touched classes, AC-22 alone), the lab main stage; the Testcontainers step; the independent review and its fixes. |
| P7 | The runbook and the documents; `origin/main` had not moved. |
| P8 | Local gates, the release branch, the PR. |
| P9 | The demo server (§12). |
| P10 | This report, the merge, the merge note. |

## 2. Commits

| Commit | Subject |
|---|---|
| `a5d26bb7` | docs(pay058): merge note (cherry-picked from `docs/plan-58-merge-note`, then deleted) |
| `2dfd4548` | docs(pay059): the PLAN-59 agent prompt, written from the session's text |
| `96d94b29` | chore(pay059): baseline |
| `2a6a3d55` | test(pay059): the regime gate's blocked path and the handler's separation of duties proved; BlockRun's refusal names the employees |
| `3fce35f0` | feat(pay059): the reads an SQL-free cycle needs |
| `12f2f353` | test(pay059): the payroll acceptance scripts cancel through the API; AC-21..23 months from one registry |
| `0e105428` | test(pay059): AC-12 on a calculated, validated run |
| `b40795d0` | test(pay059): AC-PAY-59, its CI stage, pay059-validate |
| `8a6ce4fe` | test(pay059): AC-PAY-59a three times and AC-PAY-59b, AC-12, AC-21, AC-22, AC-23 pass; review fixes |
| `d5d21bea` | docs(pay059): the runbook; the pilot's indicators; G-PAY-05's status |
| `f0f5466b` | docs(pay059): the final report's draft and the PR's description |
| `8086ac86` | merge of the feature into `release/plan-59` (the tag deployed) |
| `a8a3b831` | test(pay059): the demo server's checks; the cycle's stage limit by QA-066's rule |
| later | the server's results, this report (see `git log`) |

## 3. Decisions applied and deviations

- **Step 3's date (P-4).** D-53-1 refuses a salary change dated before the current month, and the cycle's month has ended. Z's raise is
  dated the first day of the next month (2026-12-01), decided by two people (the requester's own approval 403, the approver's →
  `Scheduled`), and cancelled in `finally` whatever happens. Z's line in the cycle's month — calculated at step 5 and again at step 7, after
  the approval — is at his salary in force (439,000; the raise was to 500,000). The proof that a run takes an applied change in its own
  month stays AC-IAM-53a's. The plan's «Z's line at the new salary» in a past month needs a retroactive difference (PAY-G); and a change
  applied before a past month is calculated would be paid in full for it (G-PAY-11, PAY-E) — the runbook tells operators to run each month
  before a later change takes effect.
- **Z is the first employee (from the third id on) with no open request and room for a raise in his grade.** Without SQL the grade's
  bounds come from the request's own refusal of a salary of 1; the maximum is asked; a salary already at the maximum is refused as no
  change and the next candidate is tried. (The first candidate of the seed sits at his maximum: an earlier version asked 259,000 from
  260,000, which P-4 does not mean by «raise»; the three executions were run again on the corrected code.)
- **The run's scope and the attendance gate (P-5).** Unchanged: every active pay profile; the gate stays `Warn`. On the cycle's fresh stack
  a whole-agency close leaves nobody missing (`missingAttendance = []` asserted by the test). The prod-like seed's profile dates are not
  changed (PLAN-65). PLAN-56's question (refuse before the run moves?) was answered by PLAN-58: before `BeginCalculating`.
- **The cycle's own CI stage (P-11).** `prodlike-cycle` (group `cycle`, required) on a fresh prod-like stack, because the cycle gives every
  employee of agency A a verified account and AC-PAY-55/58 pick employees with none. It follows `s2s42` in `order[]` (a run without
  `-Stage` gives it a fresh stack too); `prodlike-final` waits for it. Limit 40 min (local ~26 min × 1.5).
- **The review's rule and message are PLAN-58's:** opened by `auditor`, closed by `a-auditor-2`; a posting before it answers
  «يجب أن يفتح مدقق سجل مراجعة للتشغيل ويغلقه مدقق آخر قبل الترحيل.»
- **AC-22 stays out of CI** (`excluded[]`, QA-G: it kills Payroll on a shared stack); proved on the local prod-like stack (§11).
- **Months:** AC-12 and AC-PAY-59 by D-PAY-22 (each on its stack); AC-21 2024-01..06; AC-22 2024-07..12; AC-23 by «current + 2», as a pair;
  one registry, `testdata/acceptance/payroll-months.json`.
- **The cycle's test IBANs** are made from a digest of the whole id (`AcceptancePayrollAccounts.UniqueIban`): the seed's two legacy employees
  share the first 18 hex digits of their ids, and the older `Iban` (mark + the id's first characters) gave them one number, which the fraud
  scan's FR-04 holds. `Iban` is unchanged for the classes that pick a few employees (they exclude those two).
- **The gateway's rate limit:** the cycle's bulk steps meet 2,000 requests a minute per user on the acceptance stacks; the driver waits as
  Retry-After says and sends the request again (4–5 waits per execution, recorded in `timing.json`).
- **No deviation defers part of the plan.**

## 4. The cycle (AC-PAY-59)

Each step is one HTTP call or one screen post; each check an API read written to `db-checks.json`; no SQL.

| # | Person | Call | What it proved |
|---|---|---|---|
| 0 | payroll-processor, central-a | `POST runs` per candidate month; `GET attendance/periods/{M}`; `GET runs/{id}/attendance-status` | M = the first ended month with no live regular run and no closed attendance (2025-11..2026-07 refused live); scope 1,102 |
| 1 | payroll-processor | `GET frameworks?code=LAW_50_2004` | the seed's LAW_50_2004 in force (import path not needed on a fresh stack) |
| 2 | processor, approver | `GET/POST employees/{id}/bank-accounts`, `…/verify`, `…/retire` | 1,101 verified (first execution; then already active); X's account pending |
| 3 | processor, approver | `POST pay-profile-changes`, `…/approve` | Z raised from 2026-12-01: requester 403, approver → Scheduled, salary in force unchanged |
| 4 | central-a | Create screen ×2, BulkEntry screen, `POST attendance/periods/{M}/close`, `GET attendance/receipts` | Y absent two days; 1,138 closed; 1,138 receipts |
| 5 | processor | `calculate`, `validate`, `attendance-status`, payslips, `provenance`, `bank-readiness` | nobody missing; Y's deduction = two days (443,000 × 2 / working days); Z at 439,000; provenance LAW_50_2004; X alone without an account |
| 6 | processor, approver | `submit-for-review`, `reject` | Draft, revision 2 |
| 7 | processor, approver | adjustments Create/Approve screens; calculate, validate, submit | Y's adjustments +25,000; net = gross − deductions + adjustments |
| 8 | approver, approver-2 | `approve` ×2 | approvals 2 of 2 → Approved |
| 9 | auditor, a-auditor-2 | `audit-records`, `…/close` | review closed |
| 10 | a-payroll-poster | `post`; `GET runs/{id}`, `payment-batches/{id}`, `…/orders`, `…/held` | Posted, total as approved, a Pending batch, X Held NO-ACCOUNT, 1,101 Draft; held report = X |
| 11 | a-payroll-poster | `process` ×2; `…/files`, the file's order ids | 1,101 Submitted; one Initial file; X not in it; **59b:** second process 409, still one file, X still Held |
| 12 | payroll-approver | `…/bank-accounts/{id}/verify` | X's order pinned, NO-ACCOUNT lifted; bank-readiness empty |
| 13 | a-payroll-poster | `submit-held` | a HeldRelease file with X's order alone |
| 14 | processor | `GET payslips/{line}` ×1,102 | every payslip net = its order; Σ orders = TotalNetPay; Z's posted base 439,000 |
| 15 | poster, approver | `ack`, `settlement-requests`, `dual-control/{id}/decide` | the requester's decision 403; batch and 1,102 orders Settled |

**The three executions** (final code, one fresh stack, 10:18–10:35Z):

| Month | Lines/orders | TotalNetPay | Approvals | Initial file (count, total) | HeldRelease (count, total) | Time |
|---|---|---|---|---|---|---|
| 2026-08 | 1,102 | 276,109,392.27 | 2 | 1,101 · 276,019,842.27 | 1 · 89,550.00 | 6 min 32 s |
| 2026-09 | 1,102 | 286,251,392.27 | 2 | 1,101 · 286,129,842.27 | 1 · 121,550.00 | 5 min 3 s |
| 2026-10 | 1,102 | 286,249,474.52 | 2 | 1,101 · 286,127,924.52 | 1 · 121,550.00 | 4 min 48 s |

Held employee X `00676e84-…`; Y `00473c33-…` (absence 40,272.73 in August and September, 42,190.48 in October; adjustment 25,000);
Z `00d41fe8-…`. Each settled by two people with the statement's reference. The totals equal those of the earlier series run with the
decrease: Z's scheduled change reaches no part of M.

## 5. AC-12

On D-PAY-22's month of the main stack (2026-08), a calculated, validated run (ResultsFrozen): the processor's approval 403 (no approval
role); `a-payroll-multirole`'s approval 403 «… دورين متعارضين (payroll-approver وpayroll-poster) …»; submitted, approved by
`payroll-approver` and `a-payroll-approver-2` (2 of 2) → Approved; the approver's posting 403 (no poster role); the poster's posting
before the review 400 with `ReviewRequired`'s text; the review opened by `auditor` and closed by `a-auditor-2`; the poster posts → Posted
with its batch. A run not posted is cancelled in `finally`.

## 6. The integration tests

- **The fourteen** (G-PAY-05): green on SQLite (10) and PostgreSQL (4) at the baseline and after every phase.
- **`Pay059RegimeBlockerTests`** (PLAN-34's host): BlockRun refuses by the employee's name and HR.Core's reason, the run as it was (no job,
  no pinned framework, no line, no hold), the lookup asked for that employee only; no client → `ClientMissing`; no client with
  `BlocksFinalPayroll=false` → calculated; ten names in the order of their ids and «و2 آخرون». HoldEmployee: `Pay058HeldEmployeeTests`.
- **`Pay059PayrollSodHandlerTests`**: the creator's approval → `CreatorCannotApprove`, nothing written; an approver's posting with no review
  → `PosterIsApprover` (not `ReviewRequired`), no batch, still Approved; after a review by two auditors → `PosterIsApprover` again; the poster
  then posts.
- **The refusal's text:** «لا يمكن إتمام تشغيل الراتب — {n} موظفاً لديهم استثناء نظام خدمة مفتوح أو بدون نظام مصنّف: {الاسم} ({الرمز})، … و{m}
  آخرون (الإعداد Payroll:Blockers:Mode=BlockRun يمنع الحساب كله).» — names from `IEmployeeLookup` for the first ten (by id), else the id.

## 7. The scripts and AC-21..23

- **The SQL removed:** `Invoke-PayrollSql`, `Cancel-PayrollRunInDb`, `Reset-ReservedPeriod`, `Cancel-TestRun` (22 lines of the plan's
  pattern → 0). Each script cancels through `POST api/payroll/runs/{id}/cancel` with a reason and the antiforgery token, its cookie through
  a session (5.1 drops a Cookie header); the chaos drill waits for Payroll's own read after the restart and cancels on any failure.
- **The months:** `-PeriodStart` (mandatory) and the class's run; AC-21 took 2024-01, AC-22 2024-08 (its first local attempt left 2024-07,
  §11), AC-23 2027-03/04.
- **The clean-up:** AC-21 both runs Cancelled; AC-22 its run Cancelled; AC-23 both runs and its request; AC-12 a run not posted.

## 8. The reads added

| Read | Policy | Stance | Notes |
|---|---|---|---|
| `GET api/payroll/runs/{id}` + `revision`, `approvalsGiven`, `approvalsRequired`, `paymentBatchId` | `Payroll.RunRead` | central-only | the auditor's reach unchanged (`GatewayPayrollReviewPaths`) |
| `GET api/payroll/payment-batches/{id}/orders?page=` | `Module.Payroll` | central-only (the controller's) | 500 a page, page clamped; no account number, `destinationPinned` only |

## 9. Existing tests that changed, and the numbers that did not move

- `Pay058HeldEmployeeTests` (BlockRun: the new text with the employee's id), `Syr007RegimeGovernanceTests` (the id and reason in the refusal).
- AC-12 rewritten; AC-21/22/23 take their months and clean up through the API (every earlier assertion kept); `iam053-validate.ps1`
  accepts AC-23 where it now runs (required, once).
- Every payroll figure unchanged: no engine, rule or migration change; the integration filter and Testcontainers step green.

## 10. The guards and their own negative tests

`scripts/pay059-validate.ps1 -SelfTest` (25 planted defects); `Pay059PayrollMonthsRegistryTests` (a planted overlap found);
`Pay059CycleReadsHttpTests` (no account number, no field named account/iban/beneficiary, another agency 404, the auditor 403 on orders).

## 11. Results

- Builds: Debug and Release 0 warnings. Unit filters and architecture (303) green; integration (SQLite) 214 + Testcontainers step (CI's
  filter) 335 passed, 1 skipped.
- **Acceptance, local:** AC-PAY-59 Pass ×3 on three consecutive months (§4); AC-12, AC-21, AC-23 Pass on the main stack with AC-PAY-58,
  AC-PAY-56, AC-PAY-55, AC-IAM-53, AC-LVA-57 (32/32); **AC-22 Pass** (2024-08: killed mid-partition at 200 of 1,102, resumed, 1,102 lines,
  no duplicate, Cancelled); the lab main stage PASS (159 passed, 1 skipped; AC-24 and AC-25 Pass). Seeded counts at a fresh stack's start:
  Pass (DATA-AUTH 70, DATA-DIRECTORY 61, DATA-PAYPROFILE 1,489, SEED-03).
- **The evidence of record is the PR run's artifact** `acceptance-evidence` (D-QA-20) of
  [run 45994047230](https://github.com/omardefdev/HRManagement/actions/runs/45994047230): AC-PAY-59 Pass (2026-08, 1,102 lines,
  276,109,392.27 — the local figures), AC-12, AC-21, AC-23 Pass; the second run
  [46006396762](https://github.com/omardefdev/HRManagement/actions/runs/46006396762) carries the same on `a8a3b831`.

## 12. The server

- **Before:** tag `25be64cf9fa8`, 21 containers healthy, 54 G free; the picture (P0 and again just before the deploy, unchanged): runs
  `19|f3d7c343…`, lines `2975|cff7e1eb…`, batches `6|31924d45…`, orders `0|`, arrears `15|747ae32f…` (count|md5, no amount).
- **Backup:** `/opt/hrm/backups/pre-plan59-20261127T115333Z` (700/600): `pg_dumpall.sql` 55,555,882 bytes, 17 `CREATE DATABASE`, `env.backup`,
  `SHA256SUMS` verified.
- **Build:** GitHub Actions `deploy.yml action=build` on `release/plan-59` (run 45994097920, 11:53:13–12:07:09Z, 48 job-minutes): 17/17
  images `8086ac8629af`.
- **Deploy:** `scripts/vps/deploy.ps1 -Tag 8086ac8629af` (previous `25be64cf9fa8`), first time, 12:07:45–12:16:02Z (8 min 17 s): pull 107 s,
  up 137, bootstrap 58 (no migration), Keycloak reconcile 14 (0 changes), healthy 15. Smoke 50 pass, 0 fail, 1 warn (the client-side
  port check). Downtime: the services' restart inside «up» (about two minutes).
- **This plan's checks** (`AcPay59VpsChecks`, 12:17–12:24Z): 4/4 — the tag on 16 images, 0 unhealthy, the last payroll migration the
  tree's (PAY058); a posted demo run answers `revision` 1, `approvalsGiven` 0, `approvalsRequired` 2, `paymentBatchId` its batch; the newest
  demo batch answers its orders (0, as the demo) with the fields batchId/total/page/pageSize/orders only; the picture as before. Their
  own first try found its SQL comparing `Source` as text (it is stored as its number): fixed, rerun alone, passed. They write nothing.
- **The older plans' checks** (`AcOps36VpsBootTests` and `AcInt37VpsServerTests` without 36j and this plan's; 12:28:51–21:44:39Z):
  **80 of 83 passed** — 56 in the first pass; a browser tour then hung silently (17:26–18:05Z) and the run was stopped by its process ids;
  the 19 unreported and the 8 failed checks were then run one at a time under a 20-minute limit (20 passed), and five flaky ones once
  more (four passed). Not passed: `AC_POL_47_vps_the_migration…` (its local file was lost with the Windows machine, since PLAN-53);
  `Server_services_log_the_repository_legal_pack_fingerprint` (an earlier boot check restarted `hr-core-web` at 15:27:15Z and its logs
  rotate at 3 × 10 MB, so the start-up line is gone; the two other services carry it); `AC_UX_67_vps_every_key_page…` (the full-page CSP
  tour hung three times from this machine: Chromium meets `net::ERR_NETWORK_CHANGED` while the owner's dev container `hrm-keycloak`
  restarts every minute — 14,070 restarts, not touched). The flakes seen on the way were the same machine's: HTTP 60-second timeouts,
  the ssh helper's «No such container: timeout», «Connection closed by … port 22», sign-ins landing on `chromewebdata/`. This plan
  changed no page, no realm and no migration.
- **After:** tag `8086ac8629af`, every container healthy, 47 G free; final smoke `-RequireIntegrationWiring` 50/0/1 (21:45:46Z). The
  money as before but what the older PLAN-56 checks leave on every deploy: two runs of 2028-09 created and cancelled (one calculation's
  425 `REGIME-BLOCKER` holds on the cancelled run, holding nothing), its gate warning, one rejected approval. Nothing posted, funded,
  processed or settled; no user added.
- **Rollback:** `scripts/vps/deploy.ps1 -Rollback` (`25be64cf9fa8` from GHCR) is safe at any time: this release adds two reads and a
  message, writes nothing PLAN-58's code cannot read, and has no migration.
- **What the demo needs before a cycle:** a poster, a second approver (its runs are above 50,000,000) and two auditors (through
  `scripts/vps/add-demo-user.sh`); its service regimes classified (a data task: without it a calculation holds all 425 employees
  `REGIME-BLOCKER`); verified bank accounts for its employees.

## 13. Changes for API clients and operators

- New fields at the end of `GET api/payroll/runs/{id}`; new `GET api/payroll/payment-batches/{id}/orders`; BlockRun's refusal names the
  employees; the runbook `docs/runbooks/payroll-monthly-cycle.md`; `ACCEPTANCE_PAYROLL_CYCLE_FROM` (`yyyy-MM`) for the acceptance cycle.

## 14. Items other plans own, and defects for later plans

- PAY-E (G-PAY-11): a past month calculated after a later change was applied pays the new salary in full; prorating inside a month.
- PAY-G: retroactive differences (a salary change in a past month). PAY-I: the channel's notice and the bank's statement imported.
- PLAN-64: the final settlement added to this cycle. PLAN-65: wave 1 rerun live; the prod-like seed's profile dates; the SQL guard over
  every acceptance script (`AcceptanceScriptSqlWriteTests`). PAY-J: the gateway's limit against a large agency's calculation (G-PAY-21).
- Data task: the demo's service regimes; its users for a cycle.
- Recorded: `AcceptancePayrollAccounts.Iban` gives one number to ids sharing their first characters (the cycle uses `UniqueIban`);
  AC-12's posted run keeps its held orders on the main stack (a later verification may re-pin them; their holds act on AC-12's batch
  only); `scripts/acceptance-baseline-validate.ps1` and `scripts/pol049-validate.ps1` are red on `main` as before.

## 15. Timing

All UTC. Started 2026-11-27T05:19:45Z (the progress file's first line).

| Stretch | From → to | Duration |
|---|---|---|
| P0 baseline | 05:19:45 → 05:38:47 | 19 min |
| P1–P5 (code, tests, the cycle, CI scope, validator) | 05:38 → 07:08 (P5 committed 06:52; images built 06:44–07:08) | ~1 h 30 min |
| P6 local acceptance and the review | 07:08 → 10:58 | 3 h 50 min |
| P7 documents | 10:58 → 11:03 | 5 min |
| P8 local gates, push, PR | 11:03 → 11:52:48 | 50 min |
| P9 GHCR build | 11:53:13 → 12:07:09 | 14 min |
| P9 deploy | 12:07:45 → 12:16:02 | 8 min 17 s |
| P9 this plan's server checks | 12:17 → 12:24 | 7 min |
| P9 older server checks (first pass; stopped after a silent hang) | 12:28:51 → 18:05:30 | 5 h 37 min |
| P9 older server checks one at a time, and the flakes once more | 18:06 → 21:44:39 | 3 h 39 min |
| P10 report, merge, merge note | 21:45 → (merge note) | — |

- **Local waits:** solution build 4 min 49 s (baseline); integration filter 66 s (SQLite) and 3 min 51 s (Testcontainers); 16 service
  images one at a time 23 min 50 s, payroll-web alone 1 min 58 s; prod-like stacks to ready 12 min 3 s, 10 min 52 s, 15 min 13 s,
  15 min 3 s (main), 14 min 42 s, and 15 min 3 s after one failed start (a segfault under memory pressure: swap full from other
  applications; retried once after the load fell); the lab stack 15 min 20 s inside the lab main stage (30 min 43 s); the Testcontainers
  step 19 min 44 s; the Release build 2 min 24 s; the local CI steps 44 min 14 s; the unit suite 8 min 9 s (`--blame`, after a test
  host crash).
- **AC-PAY-59's executions (final code):** 6 min 32 s, 5 min 3 s, 4 min 48 s locally; 6 min 6 s in the PR's first CI run. Earlier series
  on the way: one failed at step 2 (the gateway's 429), one failed at step 11 (two legacy employees sharing an IBAN), one passed three
  times with a decrease for Z (corrected to a raise).
- **CI:** run 45994047230 on `8086ac86` (11:54–~13:00Z, 16/16 green, 289 job-minutes); run 46006396762 on `a8a3b831` (13:21–~14:30Z,
  16/16 green, 227 job-minutes); the GHCR build 48 job-minutes. **Actions: 564 job-minutes.**
- **Waiting for CI:** no turn ended to wait and no time stood still on CI: both runs overlapped the server work.
- **Stops:** none (no stopped turn, no usage limit, no sleep or restart of the machine, no full disk, no blocked port). One mistake of
  seconds: a clean-up with `pgrep -f` matched my own shell and ended that command (P-2's rule; nothing else was hit).
- `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67` before the merge: PLAN-59, 11 commits, first commit 08:25 Riyadh
  (05:25Z), span 6:27 to the release branch's merge commit (`8086ac86`, matched by the branch name); the merge note records the true
  wall-clock after the PR's merge.

## 16. Readiness note

- **PLAN-64:** the cycle's driver (`PayrollCycleDriver`) is the place to add a final settlement: it posts through the same issuer, and
  `Settled` is «paid».
- **PLAN-65:** AC-PAY-59 runs on its own stack; the months' registry is where a new payroll class takes its months; the seed's profile dates.
- **Wave 2:** PAY-E (G-PAY-11), PAY-G, PAY-I as above.
