## PLAN-55 — the employee's bank account, and pay profile versions that keep the unit and the regime (PAY-B)

**What changes**
- **An employee's pay has a real destination.** `EmployeeBankAccount`: a payroll processor registers it, and a payroll approver **who is not the registrar** verifies it against the bank's reference; the account in force stays in force until its replacement is verified. An IBAN is always accepted after the ISO 13616 check (mod 97); a local number only for a bank defined in `Payroll:Bank:Banks` (empty in Production). The number is masked everywhere but on the approver's verification page, redacted in the audit rows, and written to no log, error, hold reason or URL.
- **A run pins the account at its snapshot** (`PayrollRunInputSnapshot.BankAccountId` → `SalaryLine.BankAccountId`). Payment orders, the treasury file, FR-04 and the DSAR answer read the pinned account or the entity, never the profile. A change of account afterwards moves no money of that run. Retiring a pinned account before disbursement holds the employee in that run, and neither the batch nor the treasury file goes out until a second person releases the hold. A run's page shows its bank readiness.
- **No new pay profile version loses the unit or the regime.** `EmployeePayProfileVersioning.CreateNextVersion` is the only place a profile is made; the contract activation goes through PLAN-53's applier (an `Applied` request row with its event), with every PAY-029 behaviour kept and `Pay029PayProfileTests.cs` untouched. The allowed writers of a pay profile went from seven to six.
- **Old profiles are repaired by a script with a dry run first** (`scripts/pay055-backfill-profile-orgunit.ps1`, policy `Payroll.Repair`): it copies from the employee's own history and invents nothing.
- Migration `PAY055_EmployeeBankAccounts`: one table under forced row security; a number an older build left on a profile becomes one account **waiting for verification** (never verified implicitly); the profile's three bank columns stay, obsolete and unread.

**Proof**
- AC-PAY-55a..e (prod-like `main`, required), positive and negative; three pages added to AC-UX-67's key pages.
- No payroll number moved: the golden pack, the pipeline, the determinism and the PAY-029 tests are untouched and green; the account is in neither the input fingerprint nor the profile version.
- PostgreSQL proofs: the migration as a non-superuser under forced row security in two agencies; concurrent verifications and registrations; both orders of `IEmployeeCreated` and `IContractActivated`; a real run to its payment orders and its treasury file, the retirement hold and its four-eyes release.
- Unit 6,950, architecture 401, the Testcontainers step 282 passed and 1 skipped; `scripts/pay055-validate.ps1 -SelfTest` (27 planted defects).
- An independent review before the server build: two high findings (the treasury file went out while a retirement hold was active; a retirement between the snapshot and the line placed no hold), fixed with tests, and eleven smaller ones.
- The demo server runs `ea0d97a6ca26`: the five PLAN-55 server checks pass; the repair found 425 active profiles with no legal regime and nothing to copy it from, so it changed nothing; no account is in force there and no pay datum changed; smoke 50 pass and 0 fail.

**To know**
- **Deviations, none deferring a part of the plan:** 404 (not 403) outside the caller's branch; one filtered unique index instead of two; reject and retire take the approval policy; the repair script also signs in with a session or a token (the password grant is closed on Production).
- **For PLAN-58:** a run paid by the treasury file alone has no payment order, so a later retirement still holds it; one held employee blocks the whole batch, and releasing the hold pays the retired account the line pinned.
- The demo's 425 pay profiles have no legal regime: a data decision, not a repair.
- This session ran on the owner's Linux machine: the CI steps were run natively with `scripts/ci/run-steps.sh` in a fresh clone, and the image builds and the Testcontainers step separately.

Report: `reports/plan55-final-report.md`. Progress: `reports/plan55-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
