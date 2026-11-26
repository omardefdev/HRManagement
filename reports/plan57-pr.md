## PLAN-57 — a closed attendance period, corrections that become differences, and a reliable handover to payroll (LVA-C)

**What changes**
- **A real attendance period.** A calendar month of an agency closes for a unit's tree (or the whole agency): one versioned summary per employee — who left inside the month included — written with the closure and one `IAttendanceSummaryPublished` per summary in one save. The employee's days are then locked for every writer. Absence is counted on working days only (weekly rest days and holidays from configuration; no holiday list in code); a working day with no record refuses the close with the list (`RequireComplete`, the default).
- **One writer for an attendance day** (`AttendanceDayWriter`): an edit changes the status and the note only and can no longer move a record to another employee; «في إجازة» comes from an approved leave only, and a leave's cancellation removes only what the leave wrote; the bulk entry fills working days up to today.
- **A change after the close is a correction:** a reason, a central approver who is not the requester, the day and the summary's next version in one save. A leave decision on a locked day is a system correction.
- **Payroll applies each version once:** a receipt per summary, the difference in absent days as one row (`ATT:{summary}:v{n}`; negative days are a refund), in the month's own run when it is not calculated yet, otherwise carried to the next cycle («فرق غياب عن فترة yyyy-MM»); a frozen run never changes.
- **Payroll knows before a calculation who is not closed** (`Payroll:Attendance:Gate`: `Warn` by default with one open audit record, `Block`, `Off`), on the run's page and in `GET api/payroll/runs/{id}/attendance-status`.
- **A handover contract for PLAN-59:** the event, `POST/GET api/attendance/periods/{yyyy-MM}[/close]`, the corrections API, `GET api/payroll/attendance/receipts`.
- Migrations `LVA057_AttendancePeriods` (four tables under forced row security, three columns, the leave-made fill with `FORCE` lifted and restored) and `LVA057_AttendanceHandoff` (the receipts, four columns on the absence rows). The old close, its event's publisher and its consumer are gone.

**Proof**
- AC-LVA-57a..h (prod-like `main`, required), positive and negative, rerunnable on one stack; seven attendance pages among AC-UX-67's key pages.
- PostgreSQL proofs: the close is one save with its outbox rows and leaves nothing on a failure; a close and a writer meet on a lock per agency and month; two approvals at once give two versions; an approval and a rejection of one request do not both win; two versions at once on one receipt; the carry past a frozen month; both migrations as a non-superuser under forced row security; 2,000 employees × 31 days closed in 2.69 s.
- Unit 7,235, architecture 414, the Testcontainers step 325 passed and 1 skipped; `scripts/lva057-validate.ps1 -SelfTest` (44 planted defects).
- No existing payroll number moved: the golden pack, the pipeline, the determinism, the invariants and the PAY-029/054/055/056 tests are green, untouched.
- An independent review before the server build: one high finding (the gate's warning could be closed by one person and then satisfied the posting's «closed audit record») and thirteen more, fixed in code with tests.
- The demo server runs `4eccb1f269a0`: this plan's five server checks pass (one small unit's close of August 2026, two corrections whose differences cancel, the gate read, ten screens under the enforced CSP); the attendance records, the runs and the lines carry the checksums taken before the deploy; the older plans' checks 72 of 73 (the one not passed reads a file of the formatted Windows machine).

**To know**
- **Deviations, none deferring a part of the plan:** the class's months are January–July 2025 (the plan's are taken by the seed). AC-LVA-57e cannot show an empty missing list on the prod-like seed (some seeded pay profiles begin before their employee's hire date): it proves that nobody HR.Core closed is missing and that whoever is left is not in service that month; the empty list is proved in the integration tests. AC-IAM-52a's forged edit now moves nothing instead of answering 404.
- **The gate's scope is who the run gives a line to** (every active pay profile). Raise it to `Block` only when payroll's and HR.Core's views of who is employed agree (PLAN-59).
- **Rollback:** PLAN-56's release has no lock and knows neither the refund nor the carry: no close, no correction and no calculation while it runs (`docs/OPS_VPS_SINGLE_HOST.md`).
- **The server's disk:** the owner allowed a 30-day Seq retention (set; it freed nothing, the store being 26 days old) and then the wipe of the loopback registry's older releases (53.7 GB → 1.9 GB; the running release kept). The move to GHCR follows as its own change.
- **Left for other plans:** the leave's atomic approval (LVA-A), shifts and overtime (LVA-H/PAY-H), the historical wage of a carried difference (PAY-H), two runs of one agency calculating at once on deductions and adjustments (PLAN-58), the run's scope and the gate at `Block` (PLAN-59).
- This session ran on the owner's Linux machine: the CI steps were run natively with `scripts/ci/run-steps.sh` in a fresh clone, and the image builds and the Testcontainers step separately.

Report: `reports/plan57-final-report.md`. Progress: `reports/plan57-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
