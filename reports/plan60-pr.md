# PLAN-60 — the separation journey unblocked, and its decision governed (SEP-A)

**What a separation goes through now (on the server):** Draft -> PendingApproval -> Approved -> Executing -> Completed, with Rejected,
AwaitingIntervention and Cancelled; one transition table, the linked service transaction follows it.

- **Approval in HR.Core's engine** (PLAN-46): `SEPARATION-APPROVAL` (hr-core, then hr-management-approver) and
  `SEPARATION-APPROVAL-SENSITIVE` (compliance in the middle) for Disciplinary and Judicial; distinct approvers; the creator and the
  submitter never approve. HR.Contracts has no approval button.
- **No execution** before Approved, without a `DEC-SEP-YYYY-#####` decision issued by someone else, without the employee's notification
  (or an exception by two people), before the effective date in Damascus, under a legal hold or with HR.Core down. The execution ends the
  contract at the effective date and starts the saga in one outbox unit; nothing else starts it.
- **The checklist from the UI:** manual items by hand with evidence, party items only by the party's acknowledgement.
- **Record integrity:** no unknown or terminated employee, one open record per employee (a partial unique index), the employee's own
  contract, no change of subject after the submission.
- **Migration `SEP060`** with a deterministic backfill under forced RLS (applied by the migrator), and a start-up reconciler for the
  records an older tag would move.
- **A PLAN-46 defect fixed in HR.Core:** the owners' status read (`GET api/workflow-instances/{id}/external-status`) answered 400
  (the class's antiforgery check); found by AC-SEP-60i.

**Acceptance:** AC-SEP-60a..h (`prodLike` `main`) and AC-SEP-60i (`isolated`), both required, with `AcceptanceLeaverFixture` and the
persona `a-leaver`. Locally on the prod-like profile: a..h 8/8 twice on one stack and again on a second, the rest of `main` 86/86 after
them, i 1/1 twice, AC-WF-46/45, AC-INT-37, the journey, s2s42 (42i needs `kubectl`), leastpriv 9/9; Testcontainers 344/345.

Details: `reports/plan60-progress.md`, `reports/plan60-final-report.md`; the deviations are in the plan's notes.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
