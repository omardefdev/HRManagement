# PLAN-45: the custody and loss approval loop

- **No pending request without an instance:** a request is saved first as `ApprovalStarting`, holding the asset. If HR.Core is down, a job starts it later in the right tenant, and the same key returns the same instance.
- **Withdrawal:** the requester or a logistics officer withdraws with a reason, and the asset is free. A late outcome changes nothing, and a race ends in the conflict message (PLAN-44's L3 closed).
- **The D-WF-2 chain, enforced:** `admin-logistics`, then `hr-management-approver`, by different people. `AssetLossReport` is no longer exempt.
- **The approvers see it:** custody and loss are in the work inbox, on a generic approval page in HR.Core that PLAN-46 reuses. `admin-logistics` reaches nothing else in HR.Core.
- **AC-11 and AC-33 really test the approval,** through the pages a person uses.

**Tested:** local CI 91 steps green, AC-WF-45a..h pass, 8 PostgreSQL proofs including the job under forced row security as a non-superuser. The demo server runs `9560b03e51a3`, demo users walked the chain, and the PLAN-36..44 server checks are green.

Report: `reports/plan45-final-report.md`. New acceptance class: `AcWf45AssetApprovalLoopTests`, required, in prod-like isolated. AC-11 and AC-33 are now required.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
