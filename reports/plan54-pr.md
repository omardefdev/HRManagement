## PLAN-54 — the payroll legal framework and its reference data in production (PAY-A)

**What changes**
- **An agency with `Seed:Profile=None` reaches its first payroll calculation.** A framework version is created from a regime's template, cloned, or imported from a JSON pack (`payroll-framework-pack.v1`; `docs/syr/payroll-framework-packs/law50-interim.json`), as a draft with its own effective date. The import is one transaction: a refused pack leaves no row and its answer never repeats the file.
- **Every value that governs pay lives inside the version and its fingerprint (v2):** the nineteen fields, the tax brackets, the statutory deductions, the holidays and the priority rules, each with a legal reference, a status and, when interim, an STR-002 exception. A v2 run reads its version and no general table. Every existing version is v1: its fingerprint, its path and its results are unchanged.
- **Three people:** an editor does not approve, the approver does not attest, and only the approver or the attestor publishes. A publication never cuts back in time or into a posted period (D-PAY-13); a superseded version still governs its own months; the version of a period is chosen by date.
- **Interim values need an exception active that day (Damascus).** A run, its resume and its correction stop when an exception the governing version cites has ended.
- **A manual allowance, deduction or adjustment waits for a second person** and enters no calculation, payslip or export before that.
- Migration `PAY054_LegalFrameworkPacks` (three tables with row security; existing rows by column defaults, no data statement). API `api/payroll/frameworks`; operator script `scripts/pay054-import-legal-pack.ps1`. Contract for PLAN-55..59 and PAY-E: `docs/PAYROLL_CONFIG_VERSIONING.md`.

**Proof**
- AC-PAY-54a..f (prod-like `main`, required), positive and negative; three framework and manual-input pages added to AC-UX-67's key pages.
- Identity (no number moves): the pack equals the seeds value by value; the imported pack pays line for line what the seeded version pays, on eight engine cases and on real runs in PostgreSQL; the v1 fingerprints reproduce the demo server's stored values.
- `Pay054EmptyDatabaseFirstRunPostgreSqlTests`: an empty database, employees through Payroll's own consumers, the calculation refused until a framework is imported, approved, attested and published.
- Unit 6791, architecture 388, the Testcontainers step 264 passed and 1 skipped; `scripts/pay054-validate.ps1 -SelfTest` (29 planted defects).
- An independent review before the server build: two high findings (a resumed calculation did not pass the interim gate; two acceptance scenarios recalculated a frozen run) and two medium ones (no row version on a framework version; a version's priority rules stored active), all fixed with tests.
- The demo server runs `83b99786a420`: the four PLAN-54 server checks pass; no published version, decision row or pay datum of the demo changed; smoke 50 pass and 0 fail.

**To know**
- **A dated limit, 2027-01-31:** the two STR-002 exceptions the interim pack cites end that day. From 2027-02-01 no payroll is calculated with interim values, and `AcPay54LegalFrameworkTests` fails at its first check by design, until the owner renews the exceptions or a version of signed values is published (D-PAY-1, PAY-E).
- **The interim values tax income twice** (a 5% statutory deduction and the brackets). The seeds do this today and the pack reproduces it exactly; the correction belongs with the signed values.
- **For PLAN-56:** a run that holds a non-zero adjustment fails its own reconciliation, and no product path cancels or recalculates a completed run. Older than this plan.
- An agency needs two payroll approvers beside the payroll clerk (D-PAY-12).
- This session ran on the owner's Linux machine: the CI steps were run natively with `scripts/ci/run-steps.sh` in a fresh clone, and the image builds and the Testcontainers step separately.

Report: `reports/plan54-final-report.md`. Progress: `reports/plan54-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
