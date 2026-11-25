# PLAN-56 — final report

**The payroll run's state machine: one net, a rejection that reopens, a cancellation, and an approved reversal (PAY-C).**
Plan: [PLAN-56](../docs/acceptance-plans/PLAN-56-pay-run-state-machine.md). Prompt:
[PLAN-56-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-56-AGENT-PROMPT.md). Checkpoint with every stamp:
[plan56-progress.md](plan56-progress.md). One session, 2026-11-25, on the owner's Linux notebook; no owner action.

## 1. Summary per phase

| Phase | What was done | Finished (UTC) |
|-------|---------------|----------------|
| P0 | Preconditions, keep-awake, the baseline, three inventories (every computation of a net, every write of a run's status, the readers of batch and order states), the demo's read-only picture | 05:17 |
| P1 | One net: `PayrollNet`, `SalaryLine.TotalAdjustments`, the validation and the posting's check on it, every reader, migration `PAY056_NetFormula` | 05:28:33 |
| P2 | A guard on every transition: `PayrollRunTransitions`, a state with no setter, a validation that revives nothing, the manual salary line removed | 05:41:08 |
| P3 | A rejection that reopens (with its revision), a cancellation with a reason (D-PAY-18), the release of inputs, the restart, migration `PAY056_RunLifecycle` | 06:16:06 |
| P4 | The correction: targeted adjustments only, the validation, no base and no tax (D-PAY-17), the recovery of a negative difference | 06:33:59 |
| P5 | The reversal as a request and a decision by another person (D-PAY-16), the voided batch, `IPayrollReversed` through the outbox, its consumer | 06:46:31 |
| P6 | PostgreSQL proofs (concurrency, the indexes, the outbox, HTTP answers) and the full Testcontainers step | 07:24 |
| P7 | AC-PAY-56a..f on the prod-like stack; the independent review (13 findings, 9 fixed in code, with tests); P-13's lists on prod-like, lab and leastpriv; the validate script | 11:26:59 |
| P8 | `origin/main` had not moved (`a8f6ee32`): nothing to merge | 11:27 |
| P9 | The demo server: backup, build, deploy, this plan's five checks, the older plans' checks (67 of 68), the smoke | 14:26:19 |
| P10 | Documents, this report, the local gates, the pull request and the merge | see the merge note in the progress file |

## 2. Commits

On `feature/plan-56-pay-run-state-machine`, from `origin/main` `a8f6ee32` (after PLAN-55's merge note and the prompt):

| Commit | Subject |
|--------|---------|
| `0ebca9f8` | chore(pay056): baseline, every computation of the net, every write of a run's status, the readers of batch and order statuses, the demo's picture |
| `77602c2a` | feat(pay056): one net for the engine, the validation, the posting, the payslip and the files; the posting checks and writes nothing; migration PAY056_NetFormula fills each line's adjustments |
| `3f76e56c` | feat(pay056): every transition of a payroll run passes a guard; the validation revives nothing; the manual salary line is removed |
| `6b1d3730` | feat(pay056): a rejection reopens the run with its revision; any run not posted can be cancelled with a reason; inputs are released; a failed calculation can be restarted |
| `fc82f704` | feat(pay056): the correction run takes only the adjustments aimed at its source, passes the validation, taxes no difference, and recovers a negative difference in the next cycle |
| `4635260c` | feat(pay056): a reversal is a request with a reason that another person approves; it voids a batch that has not left, refuses one that has, frees the inputs and publishes IPayrollReversed |
| `ceb579ef` | test(pay056): PostgreSQL proofs of a concurrent rejection, a cancellation under a running job, the run indexes, and the reversal's event in its own transaction |
| `93de869e` | test(pay056): AC-PAY-56's class with its CI scope, ownership and personas; the validate script with its self-test; AC-UX-67's key pages |
| `ff7143ff` | fix(pay056): the independent review — a recovery waits for its correction's posting; a cancelled job is never revived; a correction and a reversal check each other; one door to a correction; the poster of an older run |
| `e2f12e1c` | fix(pay056): the payslip API answers the line's stored figures and components (it answered 500 on a serializer cycle); AC-PAY-56a reads it |
| `69570ea4` | test(pay056): AC-PAY-56a..f pass on the prod-like stack; review fixes; CI scope and ownership — **the commit the demo server runs** (`69570ea4e1f6`) |
| `22fdebb0` | test(pay056): demo server with the new run acts, a refused reversal request and the net migration; PLAN-36..55 server checks still green |
| (this commit) | docs(pay056): the documents and this report |

## 3. Decisions applied and deviations

No deviation defers a part of the plan. Where the prompt and the plan differ, the prompt was followed.

**The HTTP answers (P-4).** One rule on every controller (`PayrollRunActHttp`): done 200; a missing note or reason, or a state
the act cannot leave, 400; a caller the run's state does not admit, 403; another agency's run, job or request, 404; a request
that lost to another (a version conflict, a second pending request, a second open correction, a decided request), 409. Every
refusal carries its Arabic message and changes nothing. Every new API write validates an antiforgery token
(`GET api/payroll/run-lifecycle/antiforgery`).

**The cancel policy (D-PAY-18).** A new policy `Payroll.RunCancel` (processor, approver, hr-admin) admits the caller to the
action; the handler then applies D-PAY-18 to the run's state: the processor cancels what was not sent for review, a run under
review or approved needs `Payroll.Approve` (403 otherwise). `POST api/payroll/jobs/{id}/cancel` delegates to the same command.

**A run with a funding decision or a partial batch** is neither cancelled nor reversed (400, with a message that names the
disbursement path). What its arrears rows become is PLAN-58's.

**The fill of `TotalAdjustments`.** From each line's `Adjustment` components that carry a reference; a line with none keeps 0.
No stored amount of an existing line is written. A line posted before this plan keeps the net it was posted with.

**The seeded construction.** `PayrollRun.Status`, `ResultsFrozen` and `ValidatedAtCalculate` have `init` accessors: the two
creators write «draft», the two seeders write a state at construction, and nothing assigns one afterwards. Tests that need a
state the run did not reach by its methods use `ForTesting.SetStatus`, a file of the test projects only. The demo seeder now
writes its deduction component negative and its net through `PayrollNet`; rows seeded before are untouched.

**Other decisions, each in the progress file with its reason:**
- D1: the validation and the posting's check round with the pinned framework's `RoundMoney`.
- D3: the release of inputs and the deletion of lines are bulk statements inside the caller's transaction, with one summarising
  audit row each.
- D4: a cancelled run keeps its lines as its record; a rejection and a restart delete them (the rejected version's fingerprint
  is kept).
- D5: a restart keeps the run's pinned framework version.
- D6: a paused job's packages in hand are not written; the resume recalculates them.
- D7: **D-PAY-16 is checked at the decision, not at the request.** A batch can leave or come back between the two, and the
  request is the record that someone asked. A request on a run whose money has left is accepted and its approval refused.
- An endpoint beyond the prompt's list: `POST api/payroll/runs/{id}/corrections` (the correction had only a screen action).

**Deviations of fact from the prompt:**
- The demo ran `c95179904e05`, not `ea0d97a6ca26` (PLAN-55 redeployed on its merged head). The rollback target is `c95179904e05`.
- A duplicate bank reference at the posting already answered 409 (the conflict middleware), not 500; it now carries its own
  Arabic message.
- **The three new state names are stored as text, and PLAN-55's tag cannot read them** (P-16 expected «numbers it does not
  know»): EF's converter throws on an unknown name. See §11, the rollback note.
- AC-PAY-56e's «its orders cancelled»: no order exists before a batch is sent (orders are made when the batch is processed), so
  the scenario shows no open order; the cancellation of draft orders is proved by `Pay056ReversalTests`.
- The plan's AC-PAY-Cc said «ثم تُعالج دفعة العكس»: no reversal batch is written any more (P-9); a batch that has not left is
  voided.

**From the independent review** (13 findings; none deferred to save time):

| # | Finding | What was done |
|---|---------|---------------|
| 1 (high) | A correction's recovery was taken by a regular run while the correction was only calculated; a later rejection or cancellation left it applied, and a recalculation made a second one | The regular loader takes a free recovery only once its correction is posted (`CorrectionRecoveryGate`). The recovery is still made in the correction's save, as P-6 says |
| 2 | A resume that read its job before a cancellation or a restart committed set the job running again | The job is taken by one conditional statement inside the save's transaction; a pause and a worker's failure are conditional too. A cancelled job is never revived or relabelled |
| 3 | A posted correction whose recovery a later run took could be reversed, freeing its adjustment for a second recovery | The decision is refused (400) |
| 4 | A reversal ignored the run's corrections; a correction's posting ignored its source | A run with a standing correction is not reversed; a correction is not posted beside a source that is no longer posted |
| 5 | Runs seeded before this plan cannot be posted (their components do not add up) | No code change: no stored amount may be touched. The refusal names the cause. Recorded for the owner (§11) and PLAN-58 |
| 6 | A correction silently left out the adjustment of an employee with no pay profile in force | His last profile version carries his line; with none at all the correction is refused whole |
| 7 | `POST api/payroll/runs` with the type «correction» made a correction not pinned to its source | Refused (400), with the way to make one |
| 8 | A run posted before this plan has no `PostedBy` | `KnownPoster`: the last writer of a run that is still posted stands for its poster. A seeded run has neither |
| 9 | The rollback note assumed numbers | Checked: names, and they throw. `Pay056RollbackReadingTests`; §11 |
| 10 | A restart whose new calculation is refused answered 400 after its reset committed | The answer says the reset is done and why the calculation did not start |
| 11–13 (low) | A recovery larger than the month's net is consumed whole; a `FinalSettlement` run has no uniqueness; the second migration's `Down` is destructive | By design here or another plan's: §13 |

## 4. The net

`PayrollNet` (`Payroll.Domain/Services/PayrollNet.cs`) is the one definition:
`net = round(max(0, gross − deductions + adjustments))`. `Unclipped` is the same without the floor; `Shortfall` is what a
negative line could not pay.

- **The engine** (`RoundingRule`) writes `TotalAdjustments` beside the gross and the deductions and takes the net from
  `PayrollNet` under the framework's rounding. A line's components add up to `gross − deductions + adjustments`.
- **The validation** (`ValidateLineIntegrity`, `ValidateTotals`) compares the stored net with `PayrollNet`'s and the components'
  sum with `Unclipped`. A cycle with adjustments validates (before, any non-zero adjustment failed the whole cycle).
- **The posting checks and writes nothing.** It loads the saved lines (`PayrollRunLines`), runs the validation's totals again,
  compares the saved sum with the frozen total exactly, and refuses with «تغيّرت النتائج بعد التحقق؛ لا يمكن الترحيل…» when
  they differ. `ApplyNetFromGrossAndDeductions`, `CalculateNetPay` and `SumNetPay` are deleted. What is posted is what was
  approved.
- **What each reader shows now:**

  | Reader | Change |
  |--------|--------|
  | `GET api/payroll/runs/{id}` (`SalaryLineDetailsDto`) | a new last member `totalAdjustments` |
  | `GET api/payroll/payslips/{line}` | the line's stored gross, deductions, adjustments and net, with its run's period and state, and its components (it answered 500 before: a serializer cycle) |
  | The payslip PDF, the ESS payslips list, the run's page (a box, a column, the chart), the monthly summary | the adjustments beside the gross and the deductions; the stored net, never recomputed |
  | The run's CSV, the register CSV and Excel (column 5), the treasury file | a new last column `TotalAdjustments` |
  | The bulk payslips index | gains `TotalDeductions,TotalAdjustments` |
  | `PayrollDisbursementLine`, `PayslipPdfModel` | a new last member |

- **No other code computes a net.** `Pay056NetComputationArchitectureTests` reads Payroll's code and views and the shared code
  that carries a line's figures as text: a subtraction from a gross, a subtraction of the total deductions, or a new writer of
  `NetPay` fails it (three named writers: the engine's rounding rule, the legacy builder of the oldest tests, the seed).
- **What the migration filled.** `PAY056_NetFormula` adds the column (0 by default) and fills it from the referenced
  `Adjustment` components, with `FORCE` lifted on the components and the lines around the fill (as the migrator it would
  otherwise read no row and say nothing). On the demo there is no adjustment component: **all 2,550 lines keep 0, and no line's
  stored net differs from the formula** (0 historical mismatches, by run status: posted 1,700, approved 425, calculated 425).
  The lines' checksum (id, gross, deductions, net) is the same before and after: `994e497ffbd877d18b4d5eb7c737840e`.

## 5. The run's states

The state is written in one place: `PayrollRun.Move`, through `PayrollRunTransitions` (one table: the act, the states it may
leave, the state it reaches, its Arabic refusal).

| Act | From | To |
|-----|------|----|
| BeginCalculating | Draft, Calculated, Calculating | Calculating |
| BeginValidating | Calculating | Validating |
| MarkCalculationFailed | Calculating, Validating | Calculating |
| MarkCalculated | Draft, Calculated, Calculating, Validating | Calculated |
| MarkValidated | Calculating, Calculated, Validating | Calculated (frozen, validated) |
| SubmitForReview | Calculated | UnderReview |
| Approve | UnderReview | Approved |
| Reject | UnderReview | Draft (`Revision + 1`, unfrozen) |
| Post | Approved | Posted |
| Reverse | Posted | Reversed |
| Cancel | Draft, Calculating, Validating, Calculated, UnderReview, Approved | Cancelled |
| RestartCalculation | Draft, Calculating, Validating, Calculated | Draft |

**Who may do what.** Calculate, validate, submit, restart, create a correction: `Payroll.Process`. Approve, reject, decide a
reversal: `Payroll.Approve`. Post: `Payroll.Post`. Cancel: `Payroll.RunCancel`, then D-PAY-18 by the run's state. Request a
reversal: `Payroll.Reverse`. The `payroll` role alone writes nothing (`PayrollWriteActionsTests`).

**The rejection and its revision.** `RejectPayrollRunCommand`, in one transaction: the run reopens (`ReopenAfterRejection`:
from «قيد المراجعة» only, the note mandatory), a `PayrollRunRevision` keeps the rejected version (its number, line count,
total, a SHA-256 fingerprint of its lines taken in pages, the rejecter and the note), the inputs are released, the lines and
their components are deleted with one audit row. The processor corrects the inputs and calculates the same run again.

**The cancellation.** `CancelPayrollRunCommand`, in one transaction: the run's jobs and their open partitions are stopped, the
inputs released, the run `Cancelled` with `CancelledBy`, `CancelledAt` and `CancellationReason`, and a `PayrollRunApproval` row
with the decision `Cancelled` and the reason. The lines stay as the run's record; the period is free (the live-period index
leaves reversed and cancelled runs out).

**Nothing is written after a cancellation.** The worker's save checks, in its own transaction, that its job is still running
(`FOR SHARE` on the job's row on PostgreSQL); the cancellation updates that row first, so it waits for a save in flight and
every later save finds the job stopped. A resume takes its job by one conditional statement, so a resume that read the job
before the cancellation committed is refused.

**The restart.** `RestartCalculationCommand`, for a failed or paused job on a run that is not frozen: the job is cancelled, the
lines deleted, the inputs released, the run a draft again (same framework version), then a calculation with a new snapshot
through `StartPayrollRunJobHandler` and PLAN-54's interim gate. It is the way out of a fingerprint conflict.

**The release** (`PayrollInputRelease`) frees `DeductionRecord.AppliedInRunId`, `AbsenceDeduction.AppliedInRunId` and
`PayrollAdjustment.PayrollRunId` for the run and deletes the recoveries the run created that no run applied. It touches the
applied markers only, so PLAN-57 adds its own fields without changing it.

**The manual salary line is gone:** `AddSalaryLine` (GET, POST, the view, the link, the allowlist row). A line is written by
the calculation only, with its components.

## 6. The correction

- **Targeted adjustments.** `PayrollAdjustment.TargetSourceRunId`: chosen on the adjustment's form («تصحيح لتشغيل مرحّل»,
  a posted run in which the employee has a line), approved by a second person like any manual input. The regular loader ignores
  a targeted adjustment; the correction takes nothing else.
- **One door.** `CreateCorrectionRunCommand` (`POST api/payroll/runs/{id}/corrections`, or «تصحيح رجعي» on the run's page): a
  posted source, pinned to its framework version, PLAN-54's interim gate and PLAN-55's pinning kept. The general creation of a
  run refuses the type. One correction not yet posted per source run (a filtered unique index; a second one 409).
- **The validation.** The correction's lines pass `ValidateLineIntegrity` before `MarkValidated`, as a regular run's do. Nothing
  freezes it by hand. It then goes through review, approval and posting like every run.
- **D-PAY-17.** A correction line passes `AdjustmentRule` and `RoundingRule` only: no base, no allowance, no statutory
  deduction, no tax. The double count of the difference is gone; no new base is invented.
- **The recovery.** A line whose parts fall below zero pays nothing, and a `DeductionRecord` of type `CorrectionRecovery` is
  made in the same save: system-made, approved, its amount the shortfall, its `SourceReference` the line, due on the first day
  of the period after the corrected one. **A regular run takes it once its correction is posted.** A rejection, a cancellation
  or a recalculation of the correction deletes its free recoveries; a posted correction whose recovery was taken is not
  reversed.

## 7. The reversal

- **The request and the decision.** `RequestPayrollReversalCommand` (`Payroll.Reverse`; reason mandatory; a posted run; one
  pending request per run, by a filtered unique index → 409). `DecidePayrollReversalCommand` (`Payroll.Approve`): the decider is
  neither the requester nor the run's poster (400), for an approval and a rejection alike; a rejection needs its note; a decided
  request answers 409. `ReversePayrollRunCommand` is deleted; the screens' `ReverseRun` is the request.
- **An approval, in one transaction and one save:** every disbursement batch that has not left (`Pending`, `Failed`,
  `Rejected`) → `Voided` through `PaymentBatchTransitions`; its draft orders → `Cancelled`; the inputs released; `run.Reverse()`;
  `IPayrollReversed` staged in the outbox before the save.
- **D-PAY-16.** A batch `Submitted`, `Acked`, `Settled`, `Processed` or `Returned`, or an order that left, refuses the whole
  decision with the message that points to a correction run. Nothing changes.
- **No reversal batch is written.** `PaymentBatchKind.Reversal` stays in the enum for rows that exist; nothing creates one. The
  closed audit record the old reversal wrote with no actor is no longer written: the trace is the request and the audit log.
- **The event and its consumer.** `IPayrollReversed` (the run, its period, `ReversedNetPay`, `ReversalRequestId`,
  `ReversedAt`); rows in `EVENT_PUBLISHER_INVENTORY.md`, `MESSAGE_EVENT_MATRIX.md` and the schema inventory. Dev.Analytics'
  `PayrollReversedConsumer` records `PayrollReversed:<run>` once, with the amount it takes back: with `PayrollProcessed` it
  gives the run's net effect. No other analytics reader needed a change.
- **After an approved reversal** a new regular run of the same period is created and calculated, and takes the released inputs.

## 8. The existing tests that changed, and the numbers that did not move

Each kept its meaning:

| Test | Before | Now |
|------|--------|-----|
| `Phase2Tests.CalculateNetPay_*`, `Qa002…NetFromGross` | the old helper | the same invariants on `PayrollNet` |
| Two tests that called `run.Post(total)` | passed a total | `run.Post(by, at)`, the total asserted unchanged |
| `Pay003…Post_CreatesPendingBatch_Pay_Processes`, `OutboxAtomicity…PostPayrollRun_PublishesBeforeItsSingleSave` | planted a line with no component | plant a sound line (`TestDataFactory.ComponentsOf`): the posting now checks the lines |
| `Pay003…Reverse_CreatesCompensatingBatch` | a reversal wrote a compensating batch | `Reverse_OfAPaidRun_IsRefused_AndWritesNoCompensatingBatch` (D-PAY-16) |
| `MassAssignmentHttpTests`, `PayrollRunLifecycleIntegrationTests.AddSalaryLine_…` | a line could not be added to an approved run | no action of Payroll adds or binds a salary line |
| Seven tests that assigned `run.Status` | direct assignment | `ForTesting.SetStatus` |
| `Pay056CorrectionTests.A_negative_difference…` (this plan's, after the review) | the next run took the recovery of a calculated correction | the correction is posted first |
| AC-PAY-55c's gate | «a run that holds an adjustment fails its own reconciliation» | the same condition; its reason is isolation between classes |
| `OutboxAtomicityIntegrationTests`' repository double, the event publisher doubles | — | the new members, delegating |

**The numbers that did not move** (a run with no adjustment, unit for unit): `Gov002LegalGoldenPackTests`,
`PayrollEnginePipelineTests`, the determinism tests, `PayrollFinancialInvariantsTests`, `Pay029*`, `Pay023FrozenRunIsolationTests`,
`Pay004ArrearsTests`, `Pay054*`, `Pay055*`, `Frd001*` pass unchanged in the full unit suite and the Testcontainers step; AC-21,
AC-23, AC-24 and AC-25 pass on the lab stack; AC-HCM-25a..h and `Hcm025PanelServerParityTests` are untouched and pass. No
ceiling list grew: the entity-binding ceiling for Payroll went down from 11 to 6.

## 9. The guards and their own negative tests

| Guard | What it refuses | Its own negative test |
|-------|-----------------|-----------------------|
| `Pay056NetComputationArchitectureTests` | a net computed beside `PayrollNet`; a new writer of a line's net | eight planted computations and two planted writers are seen; ten legitimate lines are left |
| `Pay056TransitionTests` | a transition outside the matrix (99 cells, written out independently of the table); a state assigned after construction; a run constructed outside the two creators and the two seeders | planted writes; the helper exists in the test projects only |
| `PayrollWriteActionsTests` | a write action of Payroll without its named policy; a role reaching an act it should not | the rows of the six new actions |
| `OutboxPublishOrderingArchitectureTests` (PLAN-40) | an event not staged before the single save | unchanged; the reversal handler passes |
| `scripts/pay056-validate.ps1` | the seven constants of the plan (the net, the state, what was removed, the migrations, the antiforgery token, the event's row, the acceptance class) | `-SelfTest`: 39 planted defects refused |
| `Pay056RollbackReadingTests` | — (it pins two facts the rollback note rests on) | — |

## 10. Results

| Suite | Result |
|-------|--------|
| Build `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors |
| Unit (on the review fixes, 08:22Z) | 7,140 passed, 7 min 33 s |
| Architecture | 414 passed |
| Integration (SQLite), the PLAN-56 classes | 99 passed |
| Integration, CI's Testcontainers filter on the host (final code, 11:06Z) | **303 passed, 1 skipped** (`Ops038BootstrapIdempotencyTests`, which needs the migrator image as CI builds it), 14 min 25 s |
| `has-pending-model-changes` (Payroll) | none |
| `pay056-validate.ps1` and `-SelfTest` | pass; 39 planted defects refused |
| Acceptance, AC-PAY-56 alone on a fresh prod-like stack | **6 of 6** |
| Acceptance, prod-like, fresh stack (the journey, `main`, `isolated`: 15 classes) | **PASS** — main 42 of 42 with **AC-PAY-56a..f**, AC-PAY-55, AC-PAY-54, AC-IAM-51/52/53, AC-HCM-25; the journey 1; the isolated group 47; guard PASS |
| Acceptance, AC-UX-67 (by hand with the job's environment) | 7 of 7; the reversal requests' list among the key pages: 200, one policy, no violation |
| Acceptance, lab (AC-12, AC-21, AC-23, AC-24, AC-25, the catalog, AC-HCM-29, the lab journey) | **PASS** — 16 passed, 1 skipped (AC-12, `Blocked` as in the committed evidence, for the same reason: PLAN-59's), and the journey; guard PASS |
| Acceptance, leastpriv (`AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests`) | **PASS** at the second run, 9 of 9: both PAY056 migrations applied by the migrator's role from empty volumes. The first run lost one test to the environment (HR-core answered 502 six seconds after the stack came up) |
| Seeded counts by psql on a fresh stack | the 32 tables of `acceptance-verify.ps1` as expected |
| Guard scripts of the earlier plans | `pay055`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050`, `pol047`, `ux067` (after a comment was reworded: §13), `lint-compose` (both chains), `int037-wiring-check`: pass. `pol049-validate`: red on `main` before this plan (§13) |
| The demo server | §11 |
| Local gates before the push | the local CI steps in a fresh clone of this report's commit, then `plan035-prepush.ps1`: their result, with every step not run named, is in the merge note of `reports/plan56-progress.md` |

**The evidence of record** is the artifact of the pull request's Acceptance run; its link, each check's result, the merge commit
and the post-merge run are in the merge note of `reports/plan56-progress.md` (this report is committed before the push, as the
local gates need). No local evidence is committed.

**AC-PAY-56, what each scenario proves** (agency A, months of 2028 no other class takes):
*a* — January: adjustments of +50,000 and −20,000, approved by a second person; the run validates; after the posting the total
is the approved total, the sum of the lines' nets by the database and by the API, and the batch's amount; each payslip shows
gross − deductions + adjustments = net. *b* — February: a rejection without a note 400; with a note the run is a draft at
revision 2 with its revision kept and its lines gone; corrected, recalculated, approved; calculating the approved run 400.
*c* — March: a run holding an advance instalment, an absence and an adjustment is cancelled with a reason; the three are free
and a new run of March takes them; cancelling a posted run 400; the processor cancelling an approved run 403; no reason 400.
*d* — validate on February's approved run 400, nothing changed. *e* — April: the reverser's request (no reason 400, a second
409, another agency 404); his own decision 403; the approver approves: reversed, the batch voided with no reversal batch, the
adjustment free, a new April run takes it. May: the batch is sent through the channel; the approval is refused with D-PAY-16's
message; the request is rejected with a note. *f* — a correction of January with −30,000 aimed at it: one line, no base, no tax,
net 0; the untargeted adjustment stays out; a recovery of 30,000 due 2028-02-01; a second correction 409; the general creation
400; the correction is posted; June's run applies the recovery and takes the untargeted adjustment; January's run and line are
as they were.

## 11. The server

**Before** (P0's picture, 05:01Z, confirmed again at 11:31Z): tag `c95179904e05`, 22 containers; six regular runs of 2026
(posted 4, approved 1, calculated 1), 2,550 lines with the checksum `994e497f…`, no adjustment component, batches `Processed` 5
and `Pending` 1, no order, hold or job; 425 pay profiles with no legal regime.

**After:** tag **`69570ea4e1f6`**, 22 containers, healthy at 12:04:55Z; the same six runs in the same states at revision 1, the
same 2,550 lines with the same checksum, the same batches. **No demo run changed state and no amount changed.**

| # | Time (UTC) | Change | Undo |
|---|------------|--------|------|
| 1 | 11:27:42 | The backup `/opt/hrm/backups/pre-plan56-20261125T112742Z` (700; `pg_dumpall.sql` 40,748,552 bytes, `env.backup`, `SHA256SUMS`, each 600), verified: sums OK, 17 `CREATE DATABASE` lines, the dump complete | delete the folder when it is no longer needed |
| 2 | 11:28:28 | The 17 local images of `ea0d97a6ca26` removed (24 → 30 GB free), each found in the loopback registry first | `docker pull 127.0.0.1:5050/hrmanagement/<image>:ea0d97a6ca26` |
| 3 | 11:28:52 | Three untagged images of earlier builds removed (`docker image prune -f`, 1.145 GB; 31 GB free) | none |
| 4 | 11:29:20–12:01:02 | One detached build of `69570ea4e1f6`: upload 63 s, build 1,781 s, push 36 s, 17 of 17 images; disk 31 GB before, 8 GB at the lowest reading, 24 GB after | none needed |
| 5 | 12:01:45–12:05:55 | The deploy, on the first run: pull 4 s, up 136 s, bootstrap 61 s (with both PAY056 migrations), reconcile 13 s, healthy 15 s; the services were away about two and a half minutes at most. Smoke: 50 pass, 0 fail, 1 warning (the listening ports are read from the client's side) | `scripts/vps/deploy.ps1 -Tag c95179904e05` (see the rollback note) |
| 6 | 12:06–12:29 | What this plan's checks left: four runs of September 2028, each cancelled with its reason; three reversal requests on the posted run of January 2026, each rejected with its note | none needed (records) |

**This plan's five checks (`AC_PAY_56_vps_*`), all passed:**
1. *The migrations* — both recorded; every line's `TotalAdjustments` is its adjustment components' sum (0 lines differ); 0
   historical mismatches; the lines' checksum is the picture's; the two new tables and the lines' tables under forced row
   security; the four unique indexes; every demo run at revision 1.
2. *A rejection that reopens and a cancellation* — **the demo refused to calculate the check's run** (400: «لا يمكن إتمام تشغيل
   الراتب — 425 موظفاً لديهم استثناء نظام خدمة مفتوح أو بدون نظام مصنّف», PLAN-55 §13), leaving it «قيد الحساب» with its refused
   job and no line. So, as the prompt provides, the rejection stays proved by AC-PAY-56b, and the cancellation was proved on that
   run: no reason 400; with a reason the run is cancelled with who, when and why, its job cancelled, a `Cancelled` decision row
   and its audit rows written, the free inputs as before (500 deductions, 70 adjustments, 0 absences), and its month free again.
3. *The guards* — validate on the demo's approved run: 400 with the message that a validation revives nothing; its state as it
   was.
4. *A reversal request, refused* — the administrator's request on the posted run of January 2026: no reason 400; accepted with
   a reason; a second 409; **his own decision 400** («…من طلبه…»); the processor 403; the approver's rejection without a note
   400, with a note 200: `Rejected`, decided by another person; a second decision 409; the run, its batch and every line
   unchanged. **No approval of a reversal was sent on the demo; the requester's own decision was sent as a rejection.**
5. *The screens* — seven pages at 1366×768 and 390×844 in a real Chromium without QUIC: the runs' list, a draft run of the
   checks with its cancellation form, the posted run with its request, the adjustment's form, the adjustments' list as the
   processor; the reversal requests' list (all, and waiting) as the approver. Each HTTP 200, one enforced policy, no violation;
   Arabic, right-to-left; the requester named by the registry's span, no identifier printed; the form offers the posted run;
   the payslip of a demo line answers 200 with its adjustments and the formula's net.

**The older plans' checks (PLAN-36..55):** 12:30:05Z to 14:19Z, 109 minutes with nothing heavy beside them
(`AcOps36VpsBootTests` with its partial classes and `AcInt37VpsServerTests`, 36j excepted): **63 of 68 in the run, 67 of 68
after the reruns.**
- `AC_WF_46_vps_the_sweeps_run…` failed in the run and passed alone 16 s after a sweep cycle (the cause PLAN-55 recorded: the
  check reads the last 5,000 log lines of services that write more than that between two sweeps).
- `AC_OPS_39_vps_e_Keycloak…` failed in the run on an empty answer from `kcadm` over ssh, and passed alone.
- `AC_UX_67_vps_the_portal…` (`net::ERR_NETWORK_CHANGED`) and `AC_IAM_53_vps_the_salary_and_reference_change_screens…` (a
  navigation timeout) failed in the run on this machine's network changing under Chromium, and passed alone.
- `AC_POL_47_vps_the_migration_the_trigger…`: **not passed**, for the local cause PLAN-53 recorded (it reads a file of the
  formatted Windows machine).
- `AC_UX_67_vps_every_key_page…` passed, with the reversal requests' list among its pages; `AcInt37VpsServerTests` and the
  AC-OPS-36 checks passed.
- After every suite: this plan's migration check passed again (14:25Z), and `scripts/vps/smoke.ps1 -RequireIntegrationWiring`
  at 14:24:57Z: pass 50, fail 0, warn 1. The files the suites rewrote under the committed `acceptance-evidence/` were restored.

**Two things the owner will meet on the demo, both older than this plan:**
- **The demo cannot calculate a new run** (its 425 pay profiles carry no legal regime). A run whose calculation was refused
  stays «قيد الحساب»; it can now be cancelled with a reason or restarted.
- **The demo's approved run (May 2026) cannot be posted:** no closed audit record is reachable (G-PAY-05, PLAN-58), and its
  seeded lines' components do not add up to their figures (the old seed wrote the deduction component positive), so the
  posting's check refuses it and names the cause. No stored amount is corrected; that run can only be cancelled.

**The rollback note** (in `docs/OPS_VPS_SINGLE_HOST.md`; PLAN-55's code was read with `git show`):
- PLAN-55's tag ignores the new columns and tables; the run's own state «cancelled» (8) is a number it knows.
- **It cannot read a row with a state name it does not know, and the whole query fails** (`Pay056RollbackReadingTests`): a
  `Cancelled` decision (every cancellation writes one) breaks that run's page; a `Voided` batch breaks the reversed run's page
  and the agency's payment batches list; a `Cancelled` order breaks the queries that load it. On the demo now: the four
  cancelled runs of the checks; no voided batch, no cancelled order.
- Its validation fails any non-zero adjustment again, its posting rewrites the net without the adjustments, its reversal button
  reverses directly and writes a reversal batch, its loader takes a targeted adjustment into a regular run, and its cancellation
  releases nothing.
- **So no run is posted, cancelled or reversed while the older release runs; and once this release cancelled a run or approved
  a reversal, the way back is a fix forward.** A rollback is then a degraded state that lasts until this release runs again; it
  damages no data. The second migration's `Down` is never used.

## 12. Changes for API clients and operators

**New API** (all under `api/payroll`, every POST with the antiforgery token of `GET run-lifecycle/antiforgery`):

| Call | Policy | Answers |
|------|--------|---------|
| `POST runs/{id}/reject` `{ notes }` | `Payroll.Approve` | 200; 400 no note or wrong state; 404; 409 |
| `POST runs/{id}/cancel` `{ reason }` | `Payroll.RunCancel` | 200; 400 no reason, posted, or a funding decision; 403 by D-PAY-18; 404; 409 |
| `POST jobs/{jobId}/restart` | `Payroll.Process` | 200 `{ runId, jobId, linesGenerated }`; 400; 404 |
| `POST runs/{id}/reversal-requests` `{ reason }` | `Payroll.Reverse` | 200 `{ id }`; 400; 404; 409 while one waits |
| `POST reversal-requests/{id}/decide` `{ approve, notes }` | `Payroll.Approve` | 200; 400 requester, poster, no note, money left, a standing correction; 404; 409 decided |
| `GET reversal-requests?status=` | the payroll module | the agency's requests, newest first |
| `POST runs/{id}/corrections` | `Payroll.Process` | 200 `{ id, linesGenerated }`; 400 no targeted adjustment or source not posted; 404; 409 an open correction |

**Changed:**
- `POST jobs/{jobId}/cancel` needs a body `{ reason }` (400 without one) and cancels the run.
- `POST runs` refuses `runType` «correction» (400).
- `POST runs/{id}/validate` answers 400 for a run that was sent for review, approved, posted, reversed or cancelled.
- `GET payslips/{line}` answers a flat `{ line, components }` (it answered 500 before).
- **The reversal is a request.** The screen's «عكس» no longer reverses; nothing writes a reversal batch.
- **Removed:** the «إضافة سطر» page and its action.
- **Files:** a new last column `TotalAdjustments` on the run's CSV, the register CSV, the export jobs' CSVs and the treasury
  CSV; the register Excel has it in column 5; the bulk payslips index gains `TotalDeductions,TotalAdjustments`.
- **A new event:** `IPayrollReversed` (schema 1).
- **New states stored by name:** batch `Voided`, order `Cancelled`, decision `Cancelled`. A reader of those tables must know them.
- **For operators:** `scripts/pay056-validate.ps1` (and `-SelfTest`); `scripts/detect-data001-duplicates.sql` follows the new
  indexes (regular runs that are neither reversed nor cancelled; open corrections per source).

## 13. Items other plans own, and defects for later plans

**Other plans own** (the plan's scope boundaries, unchanged): attendance differences after the close and the attendance gate
(PLAN-57); the payroll period with a cut-off date, unpaid leave approved after its period was posted, the retroactive
differences engine, returning a cancelled leave's deduction (wave 2, PAY-H); recovering money paid through the channel (wave 2,
PAY-I); the unified disbursement path, the four eyes on holds, the double approval, the arrears of a run with a funding decision
(PLAN-58); the end-of-service calculation and the key of a `FinalSettlement` run (PLAN-64); carrying a deduction above the
salary (wave 2, PAY-E); rewriting the acceptance scripts that cancel by SQL (PLAN-59).

**Defects and notes for later plans:**
- **A recovery larger than the month's net is consumed whole** (the line pays nothing and the rest is not carried): PAY-E.
- **A `FinalSettlement` run has no uniqueness** since the live-period index covers regular runs only: PLAN-64's key.
- **A stale `ACCOUNT-RETIRED` hold after a recalculation still blocks the batch** until its four-eyes release: PLAN-58.
- **A run posted before this plan has no `PostedBy`.** Its last writer stands for its poster; a seeded run has neither, so only
  the requester is excluded from deciding its reversal.
- **A refused calculation leaves the run «قيد الحساب»** (the service-regime gate refuses once the calculation has begun). It can
  be cancelled or restarted now; whether the gate should refuse before the run moves is PLAN-59's to decide with the full cycle.
- **AC-12 is still `Blocked`** on the lab stack with its committed reason: PLAN-59.
- **`scripts/pol049-validate.ps1` is red on `main`:** PLAN-67 reshaped `PolicyDomainCatalog.cs` and the script's patterns no
  longer find a governed domain. The unit tests that hold the same rules against the compiled catalog pass. Not this plan's.
- **`scripts/ux067-validate.ps1` was red on `main`:** a Razor comment PLAN-53 wrote quoted an inline style attribute. The
  comment is reworded; the script passes.
- **The 403 answer of `/ui/payroll/Ess/Payslips`** to a user without the self-service module raises one `style-src-attr`
  violation in Chromium on the demo (most likely the browser's own page for a bodiless 403 under the enforced policy; not
  chased). No payroll person of the demo holds that module, so the employee's payslips page was not probed there.
- **The workflow sweeps check and the `AC_POL_47_vps_the_migration…` check** behave as PLAN-55 recorded (§11).
- **The demo's approved run cannot be posted** and **the demo cannot calculate** (§11): data of the demo, not code.

## 14. Timing

All times UTC, 2026-11-25. The stamps are in `reports/plan56-progress.md`.

| | |
|-|-|
| Started | 04:53:25Z |
| Context summarised (no work lost) | resumed 07:21:25Z |
| Stop 1 | 09:55:12Z to 10:02:15Z — **7 min 3 s.** The turn ended on a wait for the prod-like acceptance job (which finished by itself at 09:57:41Z); the owner wrote «Try again» |
| Stop 2 | 10:13:01Z to 10:24:56Z — **11 min 55 s.** The same, on the lab job (which finished at 10:23:16Z) |
| This report written | 14:28Z |
| Wall-clock to this report | **9 h 35 min**, of which 19 min stopped (6 min of them after the job the turn was waiting for had ended) |

There was no other stop: no machine restart, no `BLOCKED:`, no owner action.

Phases (finished): P0 05:17Z · P1 05:28:33Z · P2 05:41:08Z · P3 06:16:06Z · P4 06:33:59Z · P5 06:46:31Z · P6 07:24Z ·
P7 11:26:59Z · P8 nothing to merge · P9 14:26:19Z.

Inside the working time, what was waiting rather than work (the documents, the server check class and this report were written
during most of it):

| Waiting for | Time |
|-------------|------|
| Local image builds: 16 images 25 min 51 s; the payroll image twice more (89 s, 115 s); the migrator 21 min 17 s | 51 min |
| Acceptance stacks: prod-like by hand twice (about 14 min each, each on the first try); the prod-like job's 15 min 15 s; lab 14 min 43 s; leastpriv 3 min 6 s and 2 min 49 s | 65 min |
| Acceptance runs: AC-PAY-56 alone twice (3 min 14 s, 3 min 13 s); AC-UX-67 3 min 42 s; the prod-like job's tests 18 min 44 s; lab 3 min 52 s; leastpriv twice (5 min 43 s, 6 min 8 s) | 45 min |
| Full unit runs (7 min 33 s, twice) and the Testcontainers step twice (13 min 58 s, 14 min 25 s) | 43 min |
| The independent review (one subagent, beside the image build) | 14 min 31 s |
| The server: upload 63 s; build and push 30 min 17 s; deploy with its smoke 4 min 10 s | 36 min |
| The server checks: this plan's, three runs and the last confirmation, 22 min; the older plans' 109 min and their reruns 5 min | 136 min |

The local CI steps, the pre-push check, the wait for CI and the Actions minutes come after this report: they are in the merge
note of `reports/plan56-progress.md`.

**`pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`** (at 14:27Z; PLAN-56 is not merged yet, so its row ends at
its last commit):

```
Plan    Commits Started          FirstCommit      End              Merged Span
----    ------- -------          -----------      ---              ------ ---- 
PLAN-25      11                  2026-10-29 20:31 2026-10-29 23:21   True 2:50 
PLAN-26      12                  2026-10-21 13:38 2026-10-21 22:49  False 9:10 
PLAN-27      11                  2026-10-21 22:57 2026-10-22 09:43  False 10:45
PLAN-28      13                  2026-10-22 10:03 2026-10-22 15:03  False 5:00 
PLAN-29      13                  2026-10-22 15:21 2026-10-22 20:36  False 5:15 
PLAN-30      21                  2026-10-23 00:45 2026-10-29 07:45  False 150:…
PLAN-31      14                  2026-10-29 10:24 2026-10-29 15:03  False 4:38 
PLAN-32       9                  2026-10-29 14:12 2026-10-29 16:31   True 2:19 
PLAN-33      11                  2026-10-29 17:49 2026-10-29 20:18   True 2:28 
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31 
PLAN-37      23 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 12:27   True 10:57
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47
PLAN-39      13 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 11:05  False 8:52 
PLAN-40      20 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 11:56  False 48:51
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08
PLAN-43      14 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 19:03  False 8:42 
PLAN-44      14 2026-11-06 21:11 2026-11-06 21:20 2026-11-07 16:25  False 19:05
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15 
PLAN-46      14 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 15:06  False 15:31
PLAN-47      15 2026-11-08 15:06 2026-11-08 15:06 2026-11-09 15:30  False 24:23
PLAN-48      12 2026-11-09 18:27 2026-11-09 18:28 2026-11-10 02:18   True 7:50 
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58
PLAN-50      14 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08
PLAN-51      15 2026-11-12 02:26 2026-11-12 02:30 2026-11-12 20:04   True 17:34
PLAN-52      11 2026-11-12 23:25 2026-11-12 23:26 2026-11-13 12:40   True 13:13
PLAN-53      17 2026-11-21 03:15 2026-11-22 15:34 2026-11-23 07:39  False 16:05
PLAN-54      17 2026-11-23 10:26 2026-11-23 10:27 2026-11-24 06:20  False 19:53
PLAN-55      15 2026-11-24 08:14 2026-11-24 08:14 2026-11-25 05:51  False 21:36
PLAN-56      13 2026-11-25 07:53 2026-11-25 07:58 2026-11-25 17:27  False 9:28 
PLAN-66      12 2026-11-06 20:46 2026-11-06 21:03 2026-11-07 00:36  False 3:33 
PLAN-67      15 2026-11-13 11:06 2026-11-13 18:39 2026-11-22 15:34  False 212:…

Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```

## 15. Readiness note for PLAN-57..59 and PLAN-64

**PLAN-57 (attendance differences).**
- `PayrollInputRelease` touches the applied markers only; it is the one place to free an absence. Add `CarryForward`,
  `OriginalPeriodStart/End` and `SourceVersion` to `AbsenceDeduction` without changing it.
- A reopened run (after a rejection or a restart) calculates again from a new snapshot, so it picks up the attendance
  differences of its own period.
- `PayrollNet` accepts a negative deduction total; a difference that returns money is an adjustment or a negative absence, and
  the validation holds.
- The attendance gate before a calculation belongs before `BeginCalculating` (see the note on the service-regime gate, §13).

**PLAN-58 (disbursement governance).**
- A batch that has not left is `Voided` (from `Pending`, `Failed`, `Rejected`) and its draft orders `Cancelled`; both states are
  stored by name. `PaymentBatchTransitions.HasNotLeft` and `PaymentOrderTransitions.HasLeft` are the domain's line between
  money that left and money that did not.
- `PayrollRun.PostedBy/PostedAt` exist; `KnownPoster` covers older runs.
- **Holds are untouched by every act of this plan** (a rejection, a cancellation, a restart, a reversal). After a reopen a hold
  of the rejected version still stands; decide there whether a reopened run keeps or re-evaluates its holds.
- **A run with a funding decision or a partial batch is refused by the cancellation and the reversal**
  (`PayrollRunLifecycle.HasFundingOrPartialDisbursementAsync`). When PLAN-58 says what its arrears become, lift that refusal there.
- The posting needs a closed audit record no persona can close (G-PAY-05): AC-PAY-56 uses the test-only fixture.

**PLAN-59 (the full cycle).**
- `POST api/payroll/runs/{id}/cancel` with a reason replaces the scripts' SQL (`Cancel-PayrollRunInDb`, `Cancel-TestRun`,
  `Reset-ReservedPeriod`, and the release by SQL in `acceptance-payroll-determinism.ps1`): it frees the period and the inputs.
- A rejection gives `Draft` at `Revision` 2 with a `PayrollRunRevisions` row; the plan's steps 6–7 can rely on it.
- AC-PAY-56 takes January to June 2028 of agency A and leaves no free input with an amount there.

**PLAN-64 (the final settlement).**
- The live-period index covers `RunType = 'Regular'` only; a `FinalSettlement` run needs its own key (per employee and
  separation), and the job path still calculates a full payroll for it.
- A correction line for an employee with no pay profile in force uses his last profile version; a recovery made for someone who
  left has no later run to take it: the settlement should take free `CorrectionRecovery` deductions of the employee.
