# PLAN-44: workflow instance integrity under concurrency and failure

- **No lost or doubled decision:** `xmin` on `WorkflowInstance`. The loser gets an Arabic conflict message and writes nothing.
- **One running instance:** partial unique indexes per entity and per idempotency key, with existing duplicates resolved under forced row security.
- **One transaction:** the instance, the signature, the entity's change and the outbox row commit together. The plan assumed this wrapper existed; it did not.
- **Reconciliation:** a job detects outcomes that slipped, and the operator endpoint repairs them after a dry run.
- **One maker rule:** the gates and the inbox bar the same people. The legacy path checks the step's role, only the requester cancels, and dead seeders and the bridge are gone.

**Tested:** local CI 91 steps green, AC-WF-44a..e pass, the demo server runs `973d68488f09` with PLAN-36..43 server checks green.

Report: `reports/plan44-final-report.md`. New acceptance class: `AcWf44InstanceIntegrityTests`, required, in prod-like isolated.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
