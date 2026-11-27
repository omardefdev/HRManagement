PLAN-59 (PAY-D): one monthly payroll cycle proved end to end on the production-like profile, by HTTP alone, one person per role.

- **AC-PAY-59** (`AcPay59FullCycleTests`, `PayrollCycleDriver`): the framework in force, verified accounts, a raise decided by two people
  (dated next month under D-53-1), the month's attendance closed for the whole agency, calculation, a rejection, an approved adjustment, two
  approvals, a review by two auditors, the posting with one employee held, the Initial file, his account verified, the HeldRelease file,
  payslips equal to orders, and the bank's confirmation by two people (Settled). No SQL. Its own CI stage job `prodlike-cycle`.
- **AC-12** rewritten on a calculated, validated run, required; **AC-21, AC-23** required in the prod-like main group; **AC-22** proved locally
  (it kills Payroll, stays excluded). The payroll acceptance scripts cancel through the API and write no SQL; months from one registry.
- **Payroll:** BlockRun's refusal names the blocked employees; `GET runs/{id}` answers revision, approvals and batch; `GET
  payment-batches/{id}/orders`. No migration.
- Tests: `Pay059RegimeBlockerTests`, `Pay059PayrollSodHandlerTests`, `Pay059CycleReadsHttpTests`, `Pay059PayrollMonthsRegistryTests`;
  `scripts/pay059-validate.ps1`. Runbook `docs/runbooks/payroll-monthly-cycle.md`.

Report: `reports/plan59-final-report.md`. Progress and timing: `reports/plan59-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
