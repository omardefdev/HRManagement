# PROD-004 — Independent review waiver

| Field | Value |
|---|---|
| Card | PROD-004 |
| Issued | 2026-07-30 |
| Expires | When PROD-004a–e and L10N-001/MOB-001 are Accepted, or 2027-01-31 — whichever first |

## Scope

Live remote-branch UX (PROD-004a), live PWA fleet (PROD-004b), live field offline (PROD-004c), L10N-001/MOB-001 join (PROD-004d), and SYR-013/NAT join (PROD-004e) are **deferred**. Engineering adopts lab remote-branch contracts as a startup/CI binding. This waiver does **not** authorize live fleet claims, unencrypted offline cache, or skipping WCAG 2.2 AA.

## Conditions

1. Matrix soft blockers remain honest: live-remote-branch-ux / live-pwa-fleet / live-offline-field=`Blocked`; L10N-001/MOB-001/PROD-005/SYR-013=`NotStarted`.
2. `LiveRemoteBranchClaim` remains `false` in Staging/Production.
3. Independent UX/accessibility/security review required before promoting any live remote-branch claim.
