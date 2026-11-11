## PLAN-50 — the management approver reaches his approvals, and delegation has a real effect (IAM-A)

**What changes**
- `hr-management-approver` alone reads and approves the five movement types (transfers, secondments, loans, promotions, assignments)
  through the gateway, and reaches HR.Core's `HrCore.Approve` actions and the delegation page; he creates and executes nothing.
  `hr-movements` never approves. `[ModulePolicyOverride]` lets an action name its own policy; one gateway table
  (`GatewayHrCoreActionPolicies`) replaces the `Contains` rules; `GatewayApproverReachTests` keeps every approval action reachable and
  nothing else open.
- One delegation entity (`ApprovalDelegation`; `WorkflowDelegation` removed) with a mandatory delegable role, a chosen employee, closed
  lists, the principal's stored scope and at most 90 days; old rows without a delegable role revoked (`HCM050`).
- Delegation has a real effect: a delegate approves the delegated role's workflow step and the movement approvals on behalf of the
  principal, inside the period, the type and the principal's scope; «on behalf of» is recorded in the step history, the signature and
  the movement approval record (`MOV050`); identity rules run for both; a revocation acts at once in HR.Core and within 60 s in
  HR.Movements (service-only lookup, fail-closed).

**Evidence**
- AC-IAM-50a..e (required, prod-like): this PR's Acceptance artifact is the evidence of record.
- Unit 6170/6170; Testcontainers step 214 passed, 1 skipped; P-11 lab and least-privilege lists green; seeded counts unchanged.
- Demo server on `a504f6e54921`: migrations applied, a delegate approved a transfer and a requisition's step 2 on behalf.

Report: `reports/plan50-final-report.md`. Progress and decisions: `reports/plan50-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
