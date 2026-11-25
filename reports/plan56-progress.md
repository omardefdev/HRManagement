Started (UTC): 2026-11-25T04:53:25Z

# PLAN-56 — progress (the checkpoint)

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). Branch `feature/plan-56-pay-run-state-machine` from
`origin/main` `a8f6ee32`. Keep-awake: `systemd-inhibit` PID in `artifacts/plan56/keepawake.pid` (2559133).

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 baseline | done | 2026-11-25T05:17Z |
| P1 one net | done | 2026-11-25T05:28:33Z |
| P2 guards | done | 2026-11-25T05:41:08Z |
| P3 reject / cancel / release / restart | in progress | |
| P4 correction | | |
| P5 reversal | | |
| P6 PostgreSQL proofs | | |
| P7 acceptance + review | | |
| P8 main | | |
| P9 demo server | | |
| P10 docs, gates, PR, merge | | |

## P0 — timing, keep-awake, preconditions, baseline

- Started 2026-11-25T04:53:25Z. Preconditions: on `main`, clean tree; `origin/main` = `a8f6ee32` with the merge of `release/plan-55`;
  `gh` signed in as `omardefdev` (`repo`, `workflow`); `ssh hrm-vps` → `hrm`, host key fingerprint as recorded; port 22 open.
- PLAN-55's merge note cherry-picked from `docs/plan-55-merge-note` (`7749072e`); the prompt written from the session's text
  (`5007c3e0`); the note's branch deleted after checking that its content is on this branch.
- **A fact the prompt has differently:** the demo runs `c95179904e05`, not `ea0d97a6ca26` (PLAN-55's merge note: redeployed on the
  merged head at 2026-11-25T02:37:53Z). The rollback target is `c95179904e05`; `ea0d97a6ca26` is the release before it.
- Machine at start: 38 GB free on `/`, 15 GB RAM (11.5 available), swap 2.1 of 4 GB used, load 1.8 on 8 CPUs.
- Durations: baseline build 4 min 41 s; baseline unit filter 51 s (443 passed); the two read-only inventory agents 10 min 49 s and
  15 min 8 s (in parallel, beside the build); the demo's picture 3 ssh calls, about 20 s.
- Baseline and inventories: `reports/plan56-baseline.md`; raw output in `artifacts/plan56/inventory.*` and
  `artifacts/plan56/vps/picture-before.txt`.

### Notes taken while reading (do not re-read the sources)

- Batch, order and decision statuses are stored as text; the run's status is a number. New values: `Voided` = 9, order `Cancelled` = 6,
  decision `Cancelled` = 3.
- `xmin` is the concurrency token of every entity on Npgsql (`ConcurrencyExtensions`); `ConcurrencyConflictMiddleware` answers 409.
- The six callers of `validate` right after a calculation need `MarkValidated` to stay legal from `Calculated`.
- A run may own several disbursement batches, some born `Processed` (arrears, partial disbursement, the demo seed).
- `PaymentOrderService` has a `switch (target)` with no default (a new value gets no timestamp) and allows one order per (run, line).
- `scripts/detect-data001-duplicates.sql:20` leaves out status 5 only: it must leave out 8 and non-regular runs after the index
  changes (P3).
- A new event needs rows in `docs/EVENT_PUBLISHER_INVENTORY.md`, `docs/rel-compat/event-schema-inventory.json` and
  `docs/MESSAGE_EVENT_MATRIX.md`; Dev.Analytics finds consumers by assembly scan.
- The acceptance seed models an approved reversal as an approval row of role `payroll-reverser` (`AcceptancePayrollSeeder.cs:537-548`);
  `acceptance-verify.ps1` expects 57 runs and 72 payment orders.
- `AcQa34PushableBaselineTests.cs:412-420` lists `Pay003`/`Perf001`/`PayrollRunLifecycle` test names; `ci-scope.json:63-69` their files;
  `coverage-floors.json:25-36` has a temporary floor for `Payroll.Application` (73).
- PLAN-57 needs: `PayrollNet` accepting negative deductions; a release that touches the applied markers only.
- The demo's seeded lines: components do not add up to gross − deductions (deduction component positive). No `Adjustment` component.

## P1 — one net (finished 2026-11-25T05:28:33Z)

- **Code:** `PayrollNet` (the one definition: `Unclipped`, `Net` under a rounding, `Shortfall`); `SalaryLine.TotalAdjustments`,
  written by `RoundingRule`; `ValidateLineIntegrity` and `ValidateTotals` on `PayrollNet` (with the pinned framework's rounding);
  `PayrollRunLines` (one loader of the saved lines for the validation and the posting's check); the posting runs `ValidateTotals`
  again, compares the saved sum with the frozen total exactly, refuses with «تغيّرت النتائج بعد التحقق…» and assigns no line;
  `PayrollRun.Post()` keeps the frozen total. Deleted: `ApplyNetFromGrossAndDeductions`, `CalculateNetPay`, `SumNetPay`.
- **Readers:** `SalaryLineDetailsDto` (new last member), the payslip PDF, the register (Excel, column 5), `ExportRunCsv`, the three
  export-job CSVs, the treasury file (`PayrollDisbursementLine` and its CSV), the monthly summary, the ESS payslips list, the run's
  page (box, column, chart), `PayrollDeterminismComparer`. Format changes for clients, each a new last column or member:
  `TotalAdjustments` on the four CSVs with gross/deductions/net; the bulk payslips index gains `TotalDeductions,TotalAdjustments`.
- **Migration `20261125051113_PAY056_NetFormula`:** the column (numeric(18,2), default 0) and the fill from referenced `Adjustment`
  components, with FORCE lifted on `SalaryLineComponents` and `SalaryLines` around it. No byte order mark.
- **Tests:** `Pay056NetFormulaTests` (32 cases), `Pay056NetComputationArchitectureTests` (with planted computations and writers),
  `Pay056PostingTests` (6, SQLite), `Pay056MigrationPostgreSqlTests` (3, as a non-superuser owner under forced row security).
  Changed with their meaning kept: `Phase2Tests.CalculateNetPay_*` and `Qa002…NetFromGross` (the same invariants on `PayrollNet`),
  `run.Post(total)` → `run.Post()` with the total asserted unchanged (2 tests), and two planted runs whose line had no component
  (`Pay003…Post_CreatesPendingBatch_Pay_Processes`, `OutboxAtomicity…PostPayrollRun_PublishesBeforeItsSingleSave`) now plant a
  sound line (`TestDataFactory.ComponentsOf`).
- **Results:** build `-warnaserror` 0 errors (1 min 37 s); unit filter (Pay056, Pay003, Pay001, Pay023, Pay029, Pay004, Pay054,
  Pay055, Gov002, pipeline, determinism, invariants, Frd001, Phase2, treasury, PDF) 547 passed; integration (SQLite) Pay056, Pay003,
  lifecycle, outbox atomicity 28 passed, 3 skipped (Testcontainers); PostgreSQL migration tests (PAY054, PAY055, PAY056) 10 passed
  in 21 s; `has-pending-model-changes`: none; format check of the changed files clean.
- `AddSalaryLine` still exists until P2; its net comes from `PayrollNet` meanwhile.

## P2 — a guard on every transition (finished 2026-11-25T05:41:08Z)

- **Code:** `PayrollRunTransitions` (one table: act → states it may leave → state it reaches → Arabic refusal) and `PayrollRunAct`;
  every method of `PayrollRun` goes through `Move` → `EnsureTransition`; `Status` has an init accessor over the field the methods set
  (nothing assigns it after construction); the calculator's three writes are `MarkCalculationFailed()` and `BeginValidating()`; a
  resumed or continued calculation calls `BeginCalculating()`; the calculator no longer cancels a run that is already cancelled;
  `ValidatePayrollRunHandler` checks the state first (`validate` on an approved run → refusal, 400 at the API, nothing read or written);
  `MarkValidated` only from `Calculating`, `Calculated`, `Validating`.
- **Removed:** `PayrollRunsController.AddSalaryLine` (GET and POST), its view, its link on the run's page, its allowlist row
  (`payroll.txt` 7 → 6 rows; the ceiling lowered 11 → 6).
- **A duplicate bank reference:** `IX_PaymentBatches_Run_BankRef` has its own Arabic message in `UniqueConstraintMessageRegistry`;
  the conflict middleware (already registered for every host) answers 409 with it. The prompt expected a 500 today; the middleware
  already answered 409 with the general message. Proved on PostgreSQL in P6.
- **Tests:** `Pay056TransitionTests` (99 cells of the matrix, written out independently of the table; the table equals the matrix;
  posted leaves only by a reversal; the init-only state; constructions only in the two creators and the two seeders; no write around
  the entity, with planted writes); `Pay056GuardTests` (validate in every state through the handler; a failed validation leaves the
  run «قيد الحساب»). `ForTesting.SetStatus` (`src/Tests/HRManagement.UnitTests/PayrollRunForTesting.cs`, linked into the integration
  project) replaces the 7 assignments. `MassAssignmentHttpTests` → «no action of Payroll binds a salary line»;
  `PayrollRunLifecycleIntegrationTests.AddSalaryLine_…` → «a line cannot be added to an approved run because no action adds a line».
- **Results:** build `-warnaserror` 0 errors; unit filter 365 passed (and 696 on the wider payroll filter before the new class);
  integration (SQLite) 52 passed, 68 skipped (Testcontainers); architecture project 401 passed; PostgreSQL run tests (Pay021,
  Pay022, Pay023, Pay054V2, Pay055, Data001) 26 passed in 1 min 1 s; format check clean.
- **For P3:** `Reject` still returns the run to `Calculated` (frozen) in the table; P3 replaces it with `ReopenAfterRejection`
  (`UnderReview → Draft`) and updates the matrix. `MarkCancelled()` gets its actor and reason in P3.

## Decisions (with reasons)

- **D1 (P1): the net's rounding in the validation.** `PayrollNet.Net(gross, deductions, adjustments, round)`; the validation and the
  posting's check pass the pinned framework's `RoundMoney`, so a framework with 0 decimal places and an adjustment with decimals
  validates exactly. Without a pinned version the parts are taken as rounded.
- **D2 (P1): the demo seeder writes its deduction component negative and its net through `PayrollNet`**, so a newly seeded run
  passes the validation and the posting's check. Existing demo rows are not touched.

## Server changes and their undo

None yet.

## Open issues

None.

## Next step

P3: `PayrollInputRelease`; `PayrollRun.ReopenAfterRejection` + `Revision`; `PayrollRunRevision`; the rejection in one transaction;
`CancelPayrollRunCommand` (D-PAY-18, a run with a funding decision or a partial batch refused); the worker's save checks the job in
its own transaction; `RestartCalculationCommand`; `ApprovalDecision.Cancelled`; the cancel policy; the API (`reject`, `cancel`,
`restart`, antiforgery) and the screens; migration `PAY056_RunLifecycle` (revision, cancel columns, `PayrollRunRevisions`, the live
index limited to regular runs, one open correction per source); tests.
