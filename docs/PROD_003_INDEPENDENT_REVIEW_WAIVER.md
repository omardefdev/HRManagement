# PROD-003 — Independent review waiver

| Field | Value |
|---|---|
| Card | PROD-003 |
| Issued | 2026-07-30 |
| Expires | When PROD-003a–e and IAM-002 are Accepted, or 2027-01-31 — whichever first |

## Scope

Live employee portal (PROD-003a), live manager portal (PROD-003b), live usage telemetry (PROD-003c), ESS-001/MSS-001 join (PROD-003d), and PROD-004/NAT-005 join (PROD-003e) are **deferred**. Engineering adopts lab employee-selfservice contracts as a startup/CI binding. This waiver does **not** authorize live portal claims, live usage KPI, IAM-002 Done, or manager outside-scope access.

## Conditions

1. Matrix soft blockers remain honest: IAM-002=`NotStarted`; live-employee-portal / live-manager-portal / live-usage-telemetry=`Blocked`; ESS-001/MSS-001/PROD-004=`NotStarted`.
2. `LiveEmployeeSelfServiceClaim` remains `false` in Staging/Production.
3. Independent product/privacy review required before promoting any live portal claim.
