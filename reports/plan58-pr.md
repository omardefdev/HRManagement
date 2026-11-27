## PLAN-58 — disbursement governance (PAY-F)

**Money leaves by one path.** The posting makes the batch and its orders; a funding decision comes after it; only the poster's
`process` and `submit-held` send, each with a fraud scan, a gate per employee and one bank file (control row, stored, never
regenerated). `partial-disburse` and the treasury exports are removed; an architecture test holds the path.

**Real four eyes.** Every hold release, critical alert resolution, arrears adjustment, file reissue and channel settlement or rejection
is a request by one person and a decision by another, from their own sessions (inbox «طلبات بانتظار قرارك»). No actor id comes from the
client. Above the threshold a run needs two approvers («اعتماد 1 من 2»), and an auditor's review (opened by one, closed by another)
before the posting, through the gateway.

**Conflicting roles refused.** A token with two conflicting payroll roles passes no financial act (403 naming the pair, audited);
`hr-admin` counts as processor and approver. The realm import refuses such a persona but the one marked fixture.

**Holds per employee.** A hold, a critical alert, a missing or retired account or a service-regime blocker holds that employee's order
only; the others are sent, and he is sent by a supplementary file after his release. The fraud rules read real data.

- Migration `PAY058_DisbursementGovernance` (additive: two tables under forced row security, columns with defaults).
- Tests: unit, architecture, SQLite and PostgreSQL integration (concurrency, atomic posting, migration as a non-superuser owner),
  AC-PAY-58a..h (prod-like, required), AC-24 and AC-25 rewritten.
- Report: `reports/plan58-final-report.md`. Progress and every stop: `reports/plan58-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
