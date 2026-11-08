## PLAN-46: cross-service approvals

One contract for decisions another service owns, wired to the ID card and to discipline. HR.Core's side of separation is ready for PLAN-60.

- **Contract** (`ServiceDefaults/Workflow/CrossService/`, `MessageContracts/Events/`):
  - `IWorkflowApprovalClient` with its HTTP client;
  - the outcome event `IWorkflowOutcomeDecided`, published through HR.Core's outbox in the decision's transaction;
  - `WorkflowOutcomeGuard`, the owner-side sweep, the submission helper, and a fake client for tests.
- **HR.Core:**
  - `CardRequest`, `DisciplinaryAction` and `SeparationRecord` registered;
  - `external-status` for the owner only;
  - guardrails for the three types;
  - `idcard` and `compliance` reach the inbox and the approval page only;
  - no type exempt from the step role;
  - card and discipline chains raised at start-up;
  - `HIRE-WIZARD`, `TRANSFER-APPROVAL`, `IT-TICKET` and `SEPARATION-WIZARD` retired.
- **ID card:** a card is issued only from a request approved by another card officer. `Renew` opens a replacement request.
- **Discipline:**
  - an action reaches payroll only after compliance, then the authority, approve it;
  - only the outcome consumer publishes, through the consumer outbox;
  - actions published before stay approved and are never published again.
- **Guard:** consumers are found by reflection. The registry and the guard are kept consistent.
- **Tests:**
  - unit;
  - PostgreSQL (the migration under forced row security in two tenants; the sweeps as the app role);
  - AC-WF-46a..j on the prod-like stack, plus the regressions of PLAN-25 and PLAN-36..45.
- **Demo server:** runs this code. Its PLAN-36..46 server checks pass 31/31.

Details: `reports/plan46-final-report.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
