## PLAN-53 — payroll write policies, and a salary change approved by another person (IAM-B)

**What changes**
- **Every payroll write names its policy.** `Payroll.Process`, `Payroll.Approve`, the new `Payroll.MasterData` (`hr-admin`, `payroll-processor`) and `Payroll.ChangeCancel`. The `payroll` role alone writes nothing. Payroll's ceiling list is deleted and `PLAN-53` is out of the guard's owners.
- **A salary or grade change is a request** (`PayProfileChangeRequest`): a processor asks with a reason and a legal reference, from the first day of a month, inside the grade's bounds; a payroll approver who is not the requester approves. One applier creates the new pay profile version with every field of the previous one (unit, regime, bank account) and closes the previous one the day before. A change for a later month is scheduled and applied by a job; a change cannot enter a month whose run is settled. Three outbox events. Branch scope on the requests. `UpsertEmployeePayProfileCommand` is deleted; the PUT alias answers 202 with a request.
- **Salary scales and pay grades change by a request** another person approves; a grade in use or a scale with grades is not deleted (application and database).
- **Payroll runs compute in acceptance:** the seed's employees get a service regime (PLAN-54's step 0.7, done here). AC-23, AC-24 and AC-25 pass and are required.
- Migration `PAY053_SalaryChangeRequests` (two tables with row security, filtered unique indexes, `RESTRICT` relations). Contract for PLAN-54..59: `docs/PAYROLL_SALARY_CHANGE.md`.

**Proof**
- AC-IAM-53a..e (prod-like `main`, required), positive and negative; AC-UX-67's key pages gain four payroll pages, one of them a form.
- Unit 6562, architecture 358, the Testcontainers step 248 passed and 1 skipped; `scripts/iam053-validate.ps1 -SelfTest` (20 planted defects).
- An independent review before the deploy: no high finding; its four medium and six low findings fixed or recorded.
- The demo server runs `27cbf35db9cc`: the four PLAN-53 server checks pass (no pay profile changed, nothing left open), smoke 50 pass and 0 fail.

**To know**
- **AC-12 is still Blocked and not required:** it submits a run for review without calculating it, a defect in the test's design that PLAN-59 rewrites. The regime gate was never its cause.
- The server's browser check found an inline style written by an empty validation summary on the payroll forms; fixed in the twelve payroll forms. The same summary remains in four HR.Contracts views and one HR.Core view.
- This session ran on the owner's Linux machine (small disk, slow link): `scripts/ci-local.ps1`'s container could not be built there, so the CI steps were run natively with `scripts/ci/run-steps.sh`; the image builds and the Testcontainers step were run separately. Two of the 55 older server checks could not be made from that machine (a lost local file; a long browser tour).

Report: `reports/plan53-final-report.md`. Progress: `reports/plan53-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
