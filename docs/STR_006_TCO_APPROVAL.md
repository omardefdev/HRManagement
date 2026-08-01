# STR-006 — TCO approval (role stub)

| Field | Value |
|---|---|
| status | RoleApprovedStub |
| approverRoles | sponsor, finance |
| independentFinanceReview | **false** (FIN-001) |
| evidence | budgetLines + this doc |

## Gate

- PASS fixture: `testdata/str006/scenarios/tco-ops-security-backup.json`
- FAIL fixture: `testdata/str006/scenarios/tco-dev-only.json`

This is **not** an independent finance review or vendor-quote reconciliation. Claiming FIN-001 Done inside STR-006 is an exception-path violation.
