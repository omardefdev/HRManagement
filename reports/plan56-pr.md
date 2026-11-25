## PLAN-56 — the payroll run's state machine: one net, a rejection that reopens, a cancellation, and an approved reversal (PAY-C)

**What changes**
- **One net.** `PayrollNet` (gross − deductions + adjustments, floored at zero) is the one definition; the engine, the validation, the posting, the payslip, the reports and the files read it, and `SalaryLine.TotalAdjustments` is stored beside the gross and the deductions. A cycle with adjustments validates (any non-zero adjustment used to fail the whole cycle). The posting checks and writes no line: what is posted is what was approved.
- **A guard on every transition.** A run's state is written in one place (`PayrollRun.Move` through `PayrollRunTransitions`); `Status` has no setter; a validation never brings back a run that was sent for review, approved, posted, reversed or cancelled. The manual «إضافة سطر» page is removed.
- **A rejection reopens the run.** The approver rejects with a mandatory note; the run is a draft at `Revision + 1`, the rejected version is kept (`PayrollRunRevisions`), its lines are deleted and its inputs released.
- **Any run not posted can be cancelled with a reason** (D-PAY-18: the processor before the review, the approver after). The period and the inputs are free again; a worker writes nothing after a cancellation. A failed or paused calculation can be restarted with a new snapshot.
- **A reversal is a request another person approves** (neither the requester nor the poster). An approval voids a batch that has not left, cancels its draft orders, releases the inputs and publishes `IPayrollReversed` through the outbox of the same transaction. A run whose money has left is not reversed (D-PAY-16): it is corrected by a correction run. No reversal batch is written any more.
- **The correction run** takes only the approved adjustments aimed at its posted source (`TargetSourceRunId`), has no base and no tax on the difference (D-PAY-17), passes the validation, and turns a negative difference into a recovery deduction that a regular run takes once the correction is posted.
- Migrations `PAY056_NetFormula` (the column, filled from the adjustment components with `FORCE` lifted and restored; no stored amount changes) and `PAY056_RunLifecycle` (the run's new columns, two tables under forced row security, three filtered unique indexes).

**Proof**
- AC-PAY-56a..f (prod-like `main`, required), positive and negative; the reversal requests' list added to AC-UX-67's key pages.
- No payroll number of a run without an adjustment moved: the golden pack, the pipeline, the determinism, the invariants, PAY-029, PAY-054 and PAY-055 tests are untouched and green.
- PostgreSQL proofs: a rejection concurrent with an approval; a cancellation under a running job; a resume that read its job before a cancellation committed; the run indexes; the reversal's event in the outbox of its own transaction; the fill as a non-superuser under forced row security; the HTTP answers (400/403/404/409) and the antiforgery token on every new write.
- Unit 7,140, architecture 414, the Testcontainers step 303 passed and 1 skipped; `scripts/pay056-validate.ps1 -SelfTest` (39 planted defects).
- An independent review before the server build: one high finding (a correction's recovery was deducted before the correction was posted) and eight more fixed in code, each with a test.
- The demo server runs `69570ea4e1f6`: the five PLAN-56 server checks pass; every line's adjustments are filled (all zero there), the lines' checksum is the one taken before the deploy, no demo run changed state and no amount changed; the older plans' checks 67 of 68 (the one not passed reads a file of the formatted Windows machine); smoke 50 pass and 0 fail.

**To know**
- **For API clients:** `POST jobs/{id}/cancel` needs a body `reason`; `POST runs` refuses the type «correction» (use `POST runs/{id}/corrections`); the reversal is a request (`POST runs/{id}/reversal-requests`, then `POST reversal-requests/{id}/decide`); `GET payslips/{line}` answers a flat line with its components (it answered 500 before); a new last column `TotalAdjustments` on the run, register and treasury files.
- **Rollback:** the new batch, order and decision states are stored by name, and PLAN-55's release cannot read a row that carries one. Once this release cancelled a run or approved a reversal, the way back is a fix forward (`docs/OPS_VPS_SINGLE_HOST.md`).
- **Deviations, none deferring a part of the plan:** D-PAY-16 is checked at the decision, not at the request; a run with a funding decision or a partial batch is neither cancelled nor reversed here (PLAN-58); a recovery waits for its correction's posting.
- **Left for other plans:** attendance differences (PLAN-57), the disbursement path and the holds (PLAN-58), the scripts that cancel by SQL and AC-12 (PLAN-59), the key of a final settlement run (PLAN-64).
- The demo cannot calculate a new run (its pay profiles carry no legal regime) and its approved run cannot be posted (seeded lines, no closed audit record): data of the demo, older than this plan.
- This session ran on the owner's Linux machine: the CI steps were run natively with `scripts/ci/run-steps.sh` in a fresh clone, and the image builds and the Testcontainers step separately.

Report: `reports/plan56-final-report.md`. Progress: `reports/plan56-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
