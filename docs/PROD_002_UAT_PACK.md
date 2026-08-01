# PROD-002 — UAT pack

| Id | Case | Expected |
|---|---|---|
| U1 | ADR + blockers | IAM/TEN/PROD-001 Satisfied; live-* / cross-tenant Blocked; ONB-001 Blocked; PROD-003 NotStarted |
| U2 | National mutate / cross-tenant / same-role | FAIL |
| U3 | Daily change / four-eyes / rollback | PASS |

**Status:** ReadyForSign-off
