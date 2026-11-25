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
| P3 reject / cancel / release / restart | done | 2026-11-25T06:16:06Z |
| P4 correction | done | 2026-11-25T06:33:59Z |
| P5 reversal | done | 2026-11-25T06:46:31Z |
| P6 PostgreSQL proofs | done | 2026-11-25T07:24Z |
| P7 acceptance + review | in progress | |
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

## P3 — a rejection reopens; a cancellation; the release; the restart (finished 2026-11-25T06:16:06Z)

- **Domain:** `PayrollRun.Revision` (1), `PostedBy/PostedAt`, `CancelledBy/CancelledAt/CancellationReason`;
  `ReopenAfterRejection` (UnderReview → Draft, unfrozen, revision + 1, note mandatory), `Cancel(by, at, reason)` (replaces
  `MarkCancelled`), `ResetForRestart` (new act `RestartCalculation`), `Post(by, at)`. `PayrollRunRevision`,
  `PayrollReversalRequest` (entity only; its commands are P5), `ApprovalDecision.Cancelled` = 3, `PaymentOrderStatus.Cancelled` = 6,
  `PaymentBatchStatus.Voided` = 9 with `Pending/Failed/Rejected → Voided`, `PayrollAdjustment.TargetSourceRunId` (used in P4),
  `CorrectionRecovery` (type, source reference, due date).
- **Application:** `PayrollInputRelease.ReleaseAsync` (applied markers only, deletes unapplied recoveries of the run's lines, one
  audit row `InputsReleased`, idempotent); `PayrollRunLifecycle` (lines' fingerprint in pages, the funding/partial check, the
  audited deletion of lines); `RejectPayrollRunCommand` (one transaction: the run's row first, then the revision, the release, the
  deletion); `CancelPayrollRunCommand` (D-PAY-18 by two flags the controller reads from `IAuthorizationService`; a run with a
  funding decision or a partial batch refused); `RestartCalculationCommand`; `CancelPayrollRunJobCommand` delegates. `RunActResult`
  with its outcome (400/403/404/409).
- **The worker:** `IPayrollRepository.SaveEmployeePackagesForJobAsync` checks in the save's transaction that the job is running
  (`FOR SHARE` on the job's row on PostgreSQL) and writes nothing otherwise; `StopRunJobsAsync` takes the row first, so a
  cancellation waits for a save in flight and every later save finds the job stopped. The calculator writes nothing after a stop
  (no run state, no partition), and a checkpoint that loses on the job's version is skipped.
- **Web:** policy `Payroll.RunCancel` (processor, approver, hr-admin); `PayrollRunLifecycleApiController`
  (`[OrgScopeCentralOnly]`, antiforgery on every POST, token at `GET api/payroll/run-lifecycle/antiforgery`): `POST runs/{id}/reject`,
  `POST runs/{id}/cancel`, `POST jobs/{jobId}/restart`; `POST jobs/{jobId}/cancel` (old, no antiforgery) now takes an optional
  body `reason` and answers 400 without one. Screens: reject with a note, cancel with a reason (shown by D-PAY-18), restart, the
  run's version, its cancellation, its rejected versions.
- **Migration `20261125055305_PAY056_RunLifecycle`:** the columns, the two tables with forced row security, the adjustment's target,
  the live index limited to `RunType = 'Regular'`, one open correction per source. A `FinalSettlement` run is left with no
  uniqueness (PLAN-64's key). It already holds P4's and P5's columns and tables, so it is not regenerated.
- **P-10:** `UnpaidBatchStatuses` gains `Voided`; a retirement's snapshot query reads only a job still in hand (a rejected
  version's snapshot no longer holds its run); holds are untouched by every act (tested).
- **Decisions:** D3 the release and the deletion are bulk statements inside the caller's transaction (a run of any size), with one
  summarising audit row each, not one row per line. D4 a cancelled run keeps its lines as its record; a rejection and a restart
  delete them. D5 a restart keeps the run's pinned framework version (a run keeps its framework for its life; cancel to change it).
  D6 a paused job's packages in hand are not written (the save refuses a job that is not running); the resume recalculates them.
- **Tests:** `Pay056LifecycleTests` (36 cases), `Pay056TransitionTests` (matrix with the new act, note/reason/restart rules),
  `Pay056MigrationPostgreSqlTests` (+1: the lifecycle migration as the owner), `PayrollWriteActionsTests` (+ the six actions and
  who reaches them). `scripts/detect-data001-duplicates.sql` follows the new indexes.
- **Results:** build `-warnaserror` 0 errors; **full unit suite 7,128 passed in 7 min 33 s**; architecture 409 passed; integration
  (SQLite) Pay056 59 passed, wider payroll filter 92 passed; PostgreSQL (migrations PAY055/PAY056, Pay021, Pay022, Pay054V2, Pay055,
  Perf001) 30 passed in 56 s; `has-pending-model-changes`: none; no byte order mark; format clean.
- **For P6:** HTTP tests of the new API (statuses, antiforgery, roles) on Payroll.Web over PostgreSQL; the concurrent rejection
  and approval; the cancellation under a running job; the duplicate bank reference as 409.

## P4 — the correction (finished 2026-11-25T06:33:59Z)

- **Engine:** a correction line passes `AdjustmentRule` and `RoundingRule` only (`PayrollEnginePipeline.AppliesToACorrection`): no
  base, no allowance, no statutory deduction, no tax (D-PAY-17). `CorrectionDeltaGross/Net` are gone from the context, the unit and
  the two rules; the «فرق إجمالي» component is no longer written.
- **The correction:** `CreateCorrectionRunCommand` takes only approved, free adjustments whose `TargetSourceRunId` is its source,
  one line per employee who has a line in the source; in one transaction it creates the run, the lines, a `CorrectionRecovery`
  deduction for each line whose parts fall below zero (due the day after the source's period ends, naming its line), runs
  `ValidateLineIntegrity`, and only then `MarkValidated`. Refusals: no approved targeted adjustment → 400 and nothing created; a
  second open correction of the source → 409 (pre-check and the unique index); PLAN-54's interim gate and PLAN-55's pinning kept.
  `CalculateCorrectionRunCommand` recalculates a correction a rejection reopened; `CalculatePayrollRunCommand` routes a correction
  to it, and `StartPayrollRunJobCommand` refuses a correction (it is never a calculation over the pay profiles).
- **The adjustment:** `CreateManualInputCommand.TargetSourceRunId` (a posted run in which the employee has a line; adjustment only),
  the form's list of posted runs, the list's column. The regular loader leaves targeted adjustments out.
- **Added beyond the prompt's API list:** `POST api/payroll/runs/{id}/corrections` (`Payroll.Process`, antiforgery), so that the
  409 of a second open correction is an HTTP answer and the acceptance test creates a correction over the API.
- **Flags:** `ResultsFrozen` and `ValidatedAtCalculate` have init accessors like `Status` (nothing freezes a run by hand);
  `ForTesting.Unfreeze` for the three planted tests.
- **A guard that caught me:** PLAN-54's «only the manual input commands set an approval state or origin» failed on the recovery
  deduction's explicit `Origin`/`ApprovalStatus`; the entity's defaults (system, approved) say the same, so the assignment went.
- **A defect of my own test, not of the product:** `A_correction_takes_only…` compared an unsorted pair of random ids with a sorted
  one and failed about one run in five; fixed, then 4 runs of the PLAN-56 integration classes green.
- **Tests:** `Pay056CorrectionTests` (13), `PayrollEnginePipelineTests.CorrectionRun_*` (the old «uses the delta net» test now says
  what a correction line is), `Pay056TransitionTests` (+ the flags).
- **Results:** build `-warnaserror` 0 errors; unit filter 1,050 (one guard failure, fixed; then 199 on the touched classes);
  architecture 410; integration (SQLite) payroll filter 101 passed and PLAN-56 classes 72; PostgreSQL (Pay054V2, Pay022, PAY056
  migrations) 12 passed; `has-pending-model-changes`: none; format clean.

## P5 — the approved reversal (finished 2026-11-25T06:46:31Z)

- **Commands** (`Features/PayrollRuns/Reversal/PayrollReversalCommands.cs`): `RequestPayrollReversalCommand` (reason mandatory, a
  posted run, no funding decision or partial batch, one pending request per run → 409) and `DecidePayrollReversalCommand` (the
  decider is neither the requester nor `PostedBy`, for an approval and a rejection alike → 400; a rejection needs its note; a
  decided request → 409). An approval, in one transaction and one save: every live disbursement batch `Pending/Failed/Rejected`
  → `Voided` through `PaymentBatchTransitions`, its draft orders → `Cancelled` (`PaymentOrderTransitions`, now the domain's), any
  batch or order that left → the whole decision refused with D-PAY-16's message; `PayrollInputRelease`; `run.Reverse()`;
  `IPayrollReversed` staged before the save. `ReversePayrollRunCommand` and its handler are deleted: no reversal batch, no closed
  audit record without an actor.
- **Decision D7: D-PAY-16 is checked at the decision, not at the request** (the plan's table; a batch can leave or come back
  between the two, and the request is the record that someone asked). So a request on a run whose money has left is accepted and
  its approval refused; it is then rejected with a note. This is also what the demo check of P9 needs (its posted runs' seeded
  batches are `Processed`).
- **Event:** `IPayrollReversed` (run, period, `ReversedNetPay`, `ReversalRequestId`, `ReversedAt`; schema 1);
  `PayrollEventPublisher.PublishPayrollReversedAsync`; `PayrollReversedConsumer` in Dev.Analytics (`PayrollReversed:<run>`,
  idempotent, `NetEffect` = −amount). Rows in `EVENT_PUBLISHER_INVENTORY.md`, `MESSAGE_EVENT_MATRIX.md`,
  `rel-compat/event-schema-inventory.json`. No analytics reader names `PayrollProcessed`, so none changes.
- **Web:** `POST api/payroll/runs/{id}/reversal-requests` (`Payroll.Reverse`), `POST api/payroll/reversal-requests/{id}/decide`
  (`Payroll.Approve`), `GET api/payroll/reversal-requests` (module); `PayrollReversalRequestsController` (`[OrgScopeCentralOnly]`,
  «طلبات العكس», menu link); the run's page asks with a reason and lists the run's requests; `ReverseRun` is the request now.
- **Tests:** `Pay056ReversalSodTests` (unit, SQLite: requester, poster, a third person; the consumer's idempotency; the schema),
  `Pay056ReversalTests` (integration: the approved reversal end to end, every batch status, an order that left, the rejection,
  one pending request, a funded run, a reversed correction), `PayrollWriteActionsTests` (+4 rows; the poster and the reverser reach
  the request only). `Pay003…Reverse_CreatesCompensatingBatch` → `Reverse_OfAPaidRun_IsRefused_AndWritesNoCompensatingBatch`.
- **Results:** build `-warnaserror` 0 errors; unit filter 689 passed; integration (SQLite) payroll filter 122 passed (PLAN-56
  classes 89); architecture 414 passed (one interface finding fixed: a hand-formatted date in the new list);
  `has-pending-model-changes`: none; format clean.

## P6 — PostgreSQL proofs (finished 2026-11-25T07:24Z)

- **`Pay056RunLifecyclePostgreSqlTests`** (`[Collection("PgRabbit")]`, the application role under forced row security, the real
  outbox; 6 tests): a rejection concurrent with an approval of the same run → one wins, the other is a version conflict, and the
  run is in exactly one of the two states; a cancellation while a job runs waits for the save in flight (`FOR SHARE` on the job
  row in the worker's save, the cancellation updates the job row first), frees every input, and the worker writes no line after it;
  a second regular run of a period is refused while the first is live and made after it is reversed; two corrections of one source
  at the same moment → the index lets one in; `IPayrollReversed` is in the outbox of the reversal's own transaction and a reversal
  whose save fails leaves nothing (no event, run posted, batch as it was); a duplicate bank reference of a run's batch is a named
  conflict and the posting changes nothing.
- **`Pay056RunLifecycleHttpTests`** (`[Collection("PostgreSql")]`, Payroll.Web over PostgreSQL; 9 tests): every new write needs the
  antiforgery token; the rejection is the approver's, needs its note and reopens the run; the cancellation follows D-PAY-18 and
  needs its reason; another agency's run → 404 and a branch-restricted user → 403; the reversal is requested by a reverser and
  decided by an approver who is neither the requester nor the poster; a run whose batch was sent is not reversed (400, the answer
  points to a correction run); a correction of targeted adjustments, a second open one → 409; validate on an approved run and a
  restart of what did not fail → 400 and nothing changes; a duplicate bank reference at the posting → 409 with its Arabic message.
- **Results:** the two classes 15/15, three consecutive runs; the full Testcontainers step with CI's filter
  (`Category=Testcontainers&Category!=ObjectStorage`): **301 passed, 0 failed, 1 skipped** (`Ops038BootstrapIdempotencyTests`,
  the known skip) in **13 m 58 s** (07:03:15Z → 07:17:19Z); build `-warnaserror` 0 errors; format clean.
- **Findings while writing the tests:** a branch-restricted user is refused at the lifecycle token endpoint itself
  (`[OrgScopeCentralOnly]`), so the test takes a token from another endpoint to prove the acts answer 403 and not 400.

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

P7: `AcPay56RunStateMachineTests` (AC-PAY-56a..f; written, not yet compiled or run), then `ci-scope.json` (prodLike, required),
`ac-ownership.json`, the personas' scenarios, AC-PAY-55c's gate text, the two pages in `AcUx67ReadableInterfaceTests`,
`scripts/pay056-validate.ps1 -SelfTest`; build the images one service at a time from local base images; prod-like stack;
AC-PAY-56 then P-13's lists; the independent review; the commit.
