PLAN-43: workflow definition governance, a guarded engine API, and separation of duties for every workflow type.

**What changes**

- Definitions are written and published only by `workflow-admin` or `hr-admin`, from the national tenant. Publishing needs a second person and the validator, a published version never changes, and every start honours the validity window.
- The public start is for the owning service account only and records the real requester from `X-Acting-User`. No service account approves or rejects a step.
- Custody events are schema v2 and carry `WorkflowInstanceId`. Their consumers refuse an unlinked instance.
- Step roles are enforced for every type except five recorded exemptions. The requester, excluded users and the subject employee act on no step, and distinct approvers hold where the definition requires them.

**Proof**

- Local CI 91 of 91: unit 4994, integration 366, Testcontainers 148.
- Acceptance job for both profiles: guard green, AC-WF-43a..f 6 of 6, AC-HCM-25 unchanged.
- The demo server runs `3bd9ee92bcf5`, with the PLAN-36..42 server checks 21 of 21 and 2 new ones.

**Read before merging**

- Two migrations, every new column nullable or with a default. The status backfill is a start-up patch, because the migrator sees no rows under forced row security.
- `keycloak-reconcile` now creates missing realm roles.
- Report: `reports/plan43-final-report.md`. Evidence of record: this PR's run artifact.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
