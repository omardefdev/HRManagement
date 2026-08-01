# PROD-005 — Independent review waiver

| Field | Value |
|---|---|
| Card | PROD-005 |
| Issued | 2026-07-30 |
| Expires | When PROD-005a–e, IAM-002, and COM-001 are Accepted, or 2027-01-31 — whichever first |

## Scope

Live workbox UI (PROD-005a), live notification delivery (PROD-005b), live safe search (PROD-005c), COM-001/WF-002 join (PROD-005d), and NAT-005/PROD-006 join (PROD-005e) are **deferred**. Engineering adopts lab workbox contracts as a startup/CI binding. This waiver does **not** authorize live inbox claims, secret-bearing notifications, existence-leaking search, or closing the NotificationHub COM-001 gap.

## Conditions

1. Matrix soft blockers remain honest: IAM-002=`NotStarted`; live-workbox-ui / live-notification-delivery / live-safe-search=`Blocked`; COM-001/WF-002/NAT-005=`NotStarted`; PROD-006=`Satisfied` (production semantic-layer binding; live BI remains PROD-006c).
2. `LiveWorkboxClaim` remains `false` in Staging/Production.
3. Independent product/security review required before promoting any live workbox claim.
